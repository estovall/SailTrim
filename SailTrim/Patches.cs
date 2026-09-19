using HarmonyLib;
using UnityEngine;

namespace SailTrim
{
    internal static class Patches
    {
        // Attach our per-ship state as soon as the ship exists.
        [HarmonyPatch(typeof(Ship), "Awake")]
        [HarmonyPostfix]
        private static void Ship_Awake(Ship __instance)
        {
            if (__instance.GetComponent<SailTrimShip>() == null)
                __instance.gameObject.AddComponent<SailTrimShip>().Init(__instance);
            if (__instance.m_mastObject != null)
            {
                // Vanilla ships have a "Hold fast" seat at the mast (a Chair somewhere in the ship hierarchy,
                // not necessarily under the mast object). Hook every seat near the mast or named for holding.
                Vector3 mastPos = __instance.m_mastObject.transform.position;
                int hooked = 0;
                var names = new System.Collections.Generic.List<string>();
                foreach (var c in __instance.GetComponentsInChildren<Chair>(true))
                {
                    Vector3 p = c.m_attachPoint != null ? c.m_attachPoint.position : c.transform.position;
                    Vector3 dv = p - mastPos; dv.y = 0f;
                    string nm = c.m_name ?? "";
                    // Only the hold-fast at the mast works the sheet: benches near the mast (Karve) and the second
                    // hold-fast at the bow (longship) are ordinary seats.
                    bool isHold = nm.IndexOf("hold", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    bool near = dv.magnitude < 2.5f;
                    names.Add($"{nm}@{c.gameObject.name} d={dv.magnitude:0.0}{(isHold && near ? " HOOKED" : "")}");
                    if (isHold && near) { MastChairs[c] = __instance; hooked++; }
                }
                Plugin.Log.LogInfo($"SailTrim: {__instance.name} seats: " + (names.Count > 0 ? string.Join(" | ", names) : "none"));
                if (hooked == 0)
                {
                    // The game only routes hover/interact to a component sitting ON the hit collider's own
                    // object (otherwise it falls back to the ship's rigidbody root), so attach to each collider.
                    foreach (var col in __instance.m_mastObject.GetComponentsInChildren<Collider>(true))
                    {
                        if (col.isTrigger || col.gameObject.GetComponent<MastHold>() != null) continue;
                        col.gameObject.AddComponent<MastHold>().Init(__instance);
                    }
                }
            }
        }

        // Register the sheet RPC alongside vanilla's Forward/Backward/Rudder RPCs.
        [HarmonyPatch(typeof(Ship), "Start")]
        [HarmonyPostfix]
        private static void Ship_Start(Ship __instance)
        {
            SailTrimShip.Get(__instance)?.OnShipStart();
            Mooring.OnShipStart(__instance);
        }

        // ---- The cleat: prefab into the world's prefab list and the hammer ----
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void ZNetScene_Awake(ZNetScene __instance)
        {
            try { Cleat.OnZNetScene(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError("SailTrim: cleat registration failed: " + e); }
            try { Buoy.OnZNetScene(__instance, Cleat.EnsureRoot()); }
            catch (System.Exception e) { Plugin.Log.LogError("SailTrim: buoy registration failed: " + e); }
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        private static void ObjectDB_Awake(ObjectDB __instance)
        {
            try { Cleat.OnObjectDb(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError("SailTrim: cleat setup failed: " + e); }
            try { Buoy.OnObjectDb(__instance, Cleat.EnsureRoot()); }
            catch (System.Exception e) { Plugin.Log.LogError("SailTrim: buoy setup failed: " + e); }
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        private static void ObjectDB_CopyOtherDB(ObjectDB __instance)
        {
            try { Cleat.OnObjectDb(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError("SailTrim: cleat setup failed: " + e); }
            try { Buoy.OnObjectDb(__instance, Cleat.EnsureRoot()); }
            catch (System.Exception e) { Plugin.Log.LogError("SailTrim: buoy setup failed: " + e); }
        }

        // A moored boat holds its spot on its owner's client, whatever else is on or off.
        [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
        [HarmonyPostfix]
        private static void Ship_CustomFixedUpdate_Mooring(Ship __instance, float fixedDeltaTime)
        {
            if (__instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;
            Mooring.FixedStep(__instance, fixedDeltaTime);
        }

        // Pilot input. Vanilla: W/S step the sail, A/D steer. Ours: A/D steer, W/S ease/sheet in.
        [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.ApplyControlls))]
        [HarmonyPrefix]
        private static bool ShipControlls_ApplyControlls(ShipControlls __instance, Vector3 moveDir)
        {
            if (!Plugin.Enabled.Value) return true;
            var ship = __instance.m_ship;
            var st = SailTrimShip.Get(ship);
            if (st == null) return true;

            // Tied to a cleat: taking the helm casts off after a second. Until then the rudder turns and nothing else.
            if (Mooring.IsMoored(ship))
            {
                Mooring.PilotAtHelm(ship, Time.fixedDeltaTime);
                if (!Plugin.ManualTrim.Value)
                {
                    ship.ApplyControlls(new Vector3(moveDir.x, 0f, 0f));
                    return false;
                }
                st.PilotSetMode(true);
                st.PilotSetRowing(0);
                if (st.SailAmount > 0.001f) st.PilotSetSail(-1f);
                ship.ApplyControlls(new Vector3(moveDir.x, 0f, 0f));
                st.PilotFixedStep();
                return false;
            }
            // Tell the ship whether this pilot wants manual trim. Opted out = vanilla controls and physics.
            st.PilotSetMode(Plugin.ManualTrim.Value);
            if (!Plugin.ManualTrim.Value) return true;

            // Physics runs on the ship's owner. Make sure that is the pilot, so a passenger without the mod
            // (or with it opted out) never ends up simulating a manual-trim boat vanilla-style.
            st.EnsurePilotOwnsShip();

            // Rudder only: zero z so vanilla never sees a Forward/Backward press.
            ship.ApplyControlls(new Vector3(moveDir.x, 0f, 0f));
            st.PilotTrimInput(moveDir.z, Time.fixedDeltaTime);
            // Sail amount and rowing decide the vanilla speed setting (Full / Slow / Back / Stop).
            st.PilotFixedStep();
            return false;
        }

        // Optional: the rudder drifts back to centre when the pilot is not steering (both trim modes).
        [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.ApplyControlls))]
        [HarmonyPostfix]
        private static void ShipControlls_ApplyControlls_Post(ShipControlls __instance, Vector3 moveDir)
        {
            if (!Plugin.Enabled.Value || !Plugin.RudderSelfCenter.Value) return;
            var ship = __instance.m_ship;
            if (ship == null || Mathf.Abs(moveDir.x) > 0.1f) return;
            ship.m_rudderValue = Mathf.MoveTowards(ship.m_rudderValue, 0f, ship.m_rudderSpeed * 1.5f * Time.fixedDeltaTime);
        }

        // Sail force from the manual sheet angle instead of vanilla auto-trim.
        [HarmonyPatch(typeof(Ship), "GetSailForce")]
        [HarmonyPrefix]
        private static bool Ship_GetSailForce(Ship __instance, float sailSize, float dt, ref Vector3 __result)
        {
            if (!Plugin.Enabled.Value) return true;
            var st = SailTrimShip.Get(__instance);
            if (st == null || !st.ManualMode) return true;
            // Vanilla passes 1 (Full) or 0.5 (Half); in manual mode the sail is set to any amount.
            __result = st.ComputeSailForce(st.ManualSailSize(sailSize), dt);
            return false;
        }

        // Yard rotation + luff flapping. The yard follows the sheet angle in every sail state, so you can
        // pre-trim with the sail furled. Sail size (furled/half/full) is still vanilla's.
        [HarmonyPatch(typeof(Ship), "UpdateSail")]
        [HarmonyPrefix]
        private static bool Ship_UpdateSail(Ship __instance, float dt)
        {
            if (!Plugin.Enabled.Value) return true;
            var st = SailTrimShip.Get(__instance);
            if (st == null || !st.ManualMode) return true;
            st.UpdateSailSizeManual(dt);
            st.UpdateYard(dt);
            return false;
        }

        // Sheet sync piggybacks on the same step vanilla syncs speed/rudder.
        [HarmonyPatch(typeof(Ship), "UpdateControlls")]
        [HarmonyPostfix]
        private static void Ship_UpdateControlls(Ship __instance)
        {
            if (!Plugin.Enabled.Value) return;
            SailTrimShip.Get(__instance)?.SyncControls();
        }

        // Extra heel, weather helm and leeway after all vanilla forces for this step.
        [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
        [HarmonyPostfix]
        private static void Ship_CustomFixedUpdate(Ship __instance, float fixedDeltaTime)
        {
            if (!Plugin.Enabled.Value) return;
            if (__instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;
            var st = SailTrimShip.Get(__instance);
            if (st != null && st.ManualMode) st.ApplyHullEffects(fixedDeltaTime);
        }

        // Camera roll with the ship: blend between "level" and vanilla's tilted result by config.
        [HarmonyPatch(typeof(GameCamera), "ApplyCameraTilt")]
        [HarmonyPrefix]
        private static void GameCamera_ApplyCameraTilt_Prefix(ref Quaternion rot, out Quaternion __state)
        {
            __state = rot;
        }

        [HarmonyPatch(typeof(GameCamera), "ApplyCameraTilt")]
        [HarmonyPostfix]
        private static void GameCamera_ApplyCameraTilt_Postfix(ref Quaternion rot, Quaternion __state)
        {
            if (!Plugin.Enabled.Value) return;
            float f = Plugin.CameraTilt.Value;
            if (f < 1f) rot = Quaternion.Slerp(__state, rot, Mathf.Clamp01(f));
        }

        internal static readonly System.Collections.Generic.Dictionary<Chair, Ship> MastChairs = new System.Collections.Generic.Dictionary<Chair, Ship>();

        // Vanilla hold/seat on the mast: taking it also takes the sheet.
        [HarmonyPatch(typeof(Chair), nameof(Chair.Interact))]
        [HarmonyPostfix]
        private static void Chair_Interact(Chair __instance, Humanoid human, bool hold)
        {
            if (hold || !Plugin.Enabled.Value || !Plugin.CrewCanTrim.Value) return;
            if (!MastChairs.TryGetValue(__instance, out var ship) || ship == null) return;
            var player = human as Player;
            if (player == null || player != Player.m_localPlayer || !player.IsAttached()) return;
            var st = SailTrimShip.Get(ship);
            if (st == null || !st.ManualMode || Plugin.CrewShip == ship) return;
            if (st.SheetHand != 0L && st.SheetHand != player.GetPlayerID())
            {
                player.Message(MessageHud.MessageType.Center, "Someone else has the sheet");
                return;
            }
            Plugin.TakeSheet(ship);
        }

        [HarmonyPatch(typeof(Chair), nameof(Chair.GetHoverText))]
        [HarmonyPostfix]
        private static void Chair_GetHoverText(Chair __instance, ref string __result)
        {
            if (!Plugin.Enabled.Value || !Plugin.CrewCanTrim.Value) return;
            if (!MastChairs.TryGetValue(__instance, out var ship) || ship == null) return;
            var st = SailTrimShip.Get(ship);
            if (st == null || !st.ManualMode) return;
            var lp = Player.m_localPlayer;
            if (st.SheetHand != 0L && (lp == null || st.SheetHand != lp.GetPlayerID())) __result += "\nSomeone has the sheet";
            else __result += "\nTrim the sail with W/S";
        }

        // A passenger holding the sheet stands still: W/S go to the sail, not their feet.
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        [HarmonyPrefix]
        private static void Player_SetControls(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold,
            ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold, ref bool jump,
            ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
        {
            if (!Plugin.CrewActive || __instance != Player.m_localPlayer) return;
            // Hands are on the sheet: no walking, fighting or blocking. Jump is the one thing that lets go.
            movedir = Vector3.zero;
            attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = false;
            run = autoRun = crouch = dodge = false;
        }

        // Whatever detaches the crew member (jump, a hit, the boat breaking up) also drops the sheet.
        [HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
        [HarmonyPrefix]
        private static void Player_AttachStop(Player __instance)
        {
            if (Plugin.CrewActive && __instance == Player.m_localPlayer) Plugin.OnCrewDetached();
        }

        // Show the vanilla ship HUD (wind circle etc.) to passengers of a manual-trim ship, rudder bits hidden.
        [HarmonyPatch(typeof(Hud), "UpdateShipHud")]
        [HarmonyPostfix]
        private static void Hud_UpdateShipHud(Hud __instance, Player player)
        {
            if (player == null) return;
            if (player.GetControlledShip() != null)
            {
                if (__instance.m_shipControlsRoot != null && !__instance.m_shipControlsRoot.activeSelf) __instance.m_shipControlsRoot.SetActive(true);
                return;
            }
            if (!Plugin.Enabled.Value || !Plugin.PassengerHud.Value || !__instance.IsVisible()) return;
            Ship ship = Plugin.GetShipAboard(player);
            var st = ship != null ? SailTrimShip.Get(ship) : null;
            if (st == null || !st.ManualMode) return;

            __instance.m_shipHudRoot.SetActive(true);
            __instance.m_rudderSlow.SetActive(false);
            __instance.m_rudderForward.SetActive(false);
            __instance.m_rudderFastForward.SetActive(false);
            __instance.m_rudderBackward.SetActive(false);
            __instance.m_rudderLeft.SetActive(false);
            __instance.m_rudderRight.SetActive(false);
            __instance.m_rudder.SetActive(false);
            var speed = ship.GetSpeedSetting();
            __instance.m_fullSail.SetActive(speed == Ship.Speed.Full);
            __instance.m_halfSail.SetActive(speed == Ship.Speed.Half);
            if (__instance.m_shipRudderIndicator != null) __instance.m_shipRudderIndicator.gameObject.SetActive(false);
            if (__instance.m_shipControlsRoot != null) __instance.m_shipControlsRoot.SetActive(false);
            __instance.m_shipWindIndicatorRoot.localRotation = Quaternion.Euler(0f, 0f, ship.GetShipYawAngle());
            __instance.m_shipWindIconRoot.localRotation = Quaternion.Euler(0f, 0f, ship.GetWindAngle());
            __instance.m_shipWindIcon.color = Color.Lerp(Hud.s_shipWindIconColor, Color.white, ship.GetWindAngleFactor());
        }

        // ---------------- Server/client handshake and config sync ----------------
        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void ZNet_OnNewConnection(ZNetPeer peer) => SailTrimNet.OnNewConnection(peer);

        [HarmonyPatch(typeof(ZNet), "SendPeerInfo")]
        [HarmonyPrefix]
        private static void ZNet_SendPeerInfo(ZRpc rpc) => SailTrimNet.BeforeSendPeerInfo(rpc);

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        [HarmonyPrefix]
        private static bool ZNet_RPC_PeerInfo(ZRpc rpc) => SailTrimNet.CheckPeer(rpc);

        [HarmonyPatch(typeof(ZNet), "Update")]
        [HarmonyPostfix]
        private static void ZNet_Update() => SailTrimNet.ServerUpdate();

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        [HarmonyPostfix]
        private static void ZNet_Disconnect(ZNetPeer peer) => SailTrimNet.OnPeerDisconnected(peer);

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        [HarmonyPostfix]
        private static void ZNet_OnDestroy() => SailTrimNet.ResetAll();

        [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
        [HarmonyPostfix]
        private static void FejdStartup_ShowConnectError(FejdStartup __instance) => SailTrimNet.OnShowConnectError(__instance);

        // "SailTrim" tab in the vanilla Settings menu: key bindings and key-behaviour toggles only.
        // Runs before Settings reads its tab list, so the game initialises our tab like its own.
        [HarmonyPatch(typeof(Settings), "Awake")]
        [HarmonyPrefix]
        private static void Settings_Awake(Settings __instance)
        {
            try { SettingsTab.Install(__instance); }
            catch (System.Exception e) { Plugin.Log.LogWarning("SailTrim: could not add the settings tab: " + e); }
        }

        // Swallow the vanilla "tap Use = let go of the rudder" while piloting. Plugin.Update
        // re-implements Use as tap = raise sail, hold = release. Gamepad "JoyUse" is untouched.
        private static bool SwallowUse(string name)
        {
            return name == "Use" && Plugin.Enabled.Value && Plugin.ManualTrim.Value && Plugin.IsLocalPlayerPiloting(out _);
        }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
        [HarmonyPrefix]
        private static bool ZInput_GetButtonDown(string name, ref bool __result)
        {
            if (!SwallowUse(name)) return true;
            __result = false;
            return false;
        }

        // Jotunn (a dependency of many mods) postfixes ZInput.GetButtonDown and overwrites __result with a
        // reverse-patched call to the unpatched original, which undoes the prefix above: Player.Update then
        // sees the Use press and calls StopDoodadControl. Re-apply the swallow after every other postfix.
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("com.jotunn.jotunn")]
        private static void ZInput_GetButtonDown_Post(string name, ref bool __result)
        {
            if (__result && SwallowUse(name)) __result = false;
        }
    }
}
