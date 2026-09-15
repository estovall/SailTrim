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
            if (__instance.m_mastObject != null && __instance.m_mastObject.GetComponent<MastHold>() == null)
                __instance.m_mastObject.AddComponent<MastHold>().Init(__instance);
        }

        // Register the sheet RPC alongside vanilla's Forward/Backward/Rudder RPCs.
        [HarmonyPatch(typeof(Ship), "Start")]
        [HarmonyPostfix]
        private static void Ship_Start(Ship __instance)
        {
            SailTrimShip.Get(__instance)?.OnShipStart();
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

            // Tell the ship whether this pilot wants manual trim. Opted out = vanilla controls and physics.
            st.PilotSetMode(Plugin.ManualTrim.Value);
            if (!Plugin.ManualTrim.Value) return true;

            // Physics runs on the ship's owner. Make sure that is the pilot, so a passenger without the mod
            // (or with it opted out) never ends up simulating a manual-trim boat vanilla-style.
            st.EnsurePilotOwnsShip();

            // Rudder only: zero z so vanilla never sees a Forward/Backward press.
            ship.ApplyControlls(new Vector3(moveDir.x, 0f, 0f));
            st.PilotTrimInput(moveDir.z, Time.fixedDeltaTime);
            return false;
        }

        // Sail force from the manual sheet angle instead of vanilla auto-trim.
        [HarmonyPatch(typeof(Ship), "GetSailForce")]
        [HarmonyPrefix]
        private static bool Ship_GetSailForce(Ship __instance, float sailSize, float dt, ref Vector3 __result)
        {
            if (!Plugin.Enabled.Value) return true;
            var st = SailTrimShip.Get(__instance);
            if (st == null || !st.ManualMode) return true;
            __result = st.ComputeSailForce(sailSize, dt);
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
            __instance.UpdateSailSize(dt);
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

        // Swallow the vanilla "tap Use = let go of the rudder" while piloting. Plugin.Update
        // re-implements Use as tap = raise sail, hold = release. Gamepad "JoyUse" is untouched.
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
        [HarmonyPrefix]
        private static bool ZInput_GetButtonDown(string name, ref bool __result)
        {
            if (name != "Use" || !Plugin.Enabled.Value || !Plugin.ManualTrim.Value) return true;
            if (!Plugin.IsLocalPlayerPiloting(out _)) return true;
            __result = false;
            return false;
        }
    }
}
