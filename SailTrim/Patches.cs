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
