using System.Collections.Generic;
using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// What another mod needs to put a crew of its own on a boat and sail her. DirectionalCombat's vikings use it.
    ///
    /// The game stops a boat with nobody in <c>Ship.m_players</c> (speed and rudder reset, nine tenths of her way
    /// taken every step), and SailTrim furls an empty boat; a crew that is not made of players has to be counted
    /// somewhere, and this is where. <see cref="SetAiCrew"/> tells SailTrim how many such crew are aboard;
    /// <see cref="CrewCount"/> is what the hull physics read instead of <c>m_players.Count</c>.
    /// </summary>
    public static class SailTrimApi
    {
        private static readonly Dictionary<Ship, int> _aiCrew = new Dictionary<Ship, int>();

        /// <summary>How many non-player crew are aboard. Set every so often; 0 clears it.</summary>
        public static void SetAiCrew(Ship ship, int count)
        {
            if (ship == null) return;
            if (count <= 0) _aiCrew.Remove(ship); else _aiCrew[ship] = count;
        }

        public static int AiCrew(Ship ship) => ship != null && _aiCrew.TryGetValue(ship, out int n) ? n : 0;

        /// <summary>Players aboard plus the AI crew: the number the hull physics and the empty-boat rules use.</summary>
        public static int CrewCount(Ship ship)
        {
            if (ship == null) return 0;
            int n = ship.m_players != null ? ship.m_players.Count : 0;
            // Under tow she must not be stopped as an empty boat, whoever is aboard.
            return n + AiCrew(ship) + (Tow.IsTowed(ship) ? 1 : 0);
        }

        /// <summary>Players and AI crew only: what "empty" means for furling the sail.</summary>
        public static int CrewAboard(Ship ship)
        {
            if (ship == null) return 0;
            return (ship.m_players != null ? ship.m_players.Count : 0) + AiCrew(ship);
        }

        // ---- tow line ----
        /// <summary>Give a boat a tow bollard at her stern (persisted in her ZDO); she can then take another boat in tow.</summary>
        public static void SetTowCapable(Ship ship, bool on) => Tow.SetCapable(ship, on);
        public static Ship Towing(Ship tug) => Tow.Towing(tug);
        public static Ship TowedBy(Ship ship) => Tow.TowedBy(ship);
        public static void TakeInTow(Ship tug, Ship towed) => Tow.Request(tug, towed);
        public static void CastOffTow(Ship tug) => Tow.Request(tug, null);

        /// <summary>A readout of the boat's sailing state for an AI helm.</summary>
        public struct Trim
        {
            public bool Valid;
            public float WindFromAngle;   // degrees off the bow the apparent wind comes FROM, + = from starboard
            public float IdealSheet;      // sheet angle with the most drive right now
            public float SheetAngle;
            public float SailAmount;      // 0 furled .. 1 full (-1 = not yet initialised)
            public float SpeedKnots;
            public float HullSpeedKnots;
            public bool Luffing, Backwinded, Stalled, NoseDiving, Tacking;
            public float HeelAngle;
        }

        public static Trim GetTrim(Ship ship)
        {
            var st = SailTrimShip.Get(ship);
            if (st == null) return default(Trim);
            return new Trim
            {
                Valid = true,
                WindFromAngle = st.WindFromAngle,
                IdealSheet = st.IdealSheet,
                SheetAngle = st.SheetAngle,
                SailAmount = st.SailAmount,
                SpeedKnots = st.SpeedKnots,
                HullSpeedKnots = st.HullSpeedKnots,
                Luffing = st.IsLuffing,
                Backwinded = st.IsBackwinded,
                Stalled = st.IsStalled,
                NoseDiving = st.IsNoseDiving,
                Tacking = st.IsTacking,
                HeelAngle = st.HeelAngle,
            };
        }

        /// <summary>
        /// Drive the boat from code, on her owner's client: sheet angle (degrees), sail amount (0..1), rowing
        /// (+1 ahead, -1 astern, 0 none; refused while sail is set) and rudder (-1 port .. +1 starboard, positive
        /// turns the bow to starboard). Puts the boat in manual trim so the sheet is what drives her.
        /// </summary>
        public static bool AiControl(Ship ship, float sheetAngle, float sailAmount, int rowDir, float rudder)
        {
            var st = SailTrimShip.Get(ship);
            if (st == null || ship.m_nview == null || !ship.m_nview.IsValid() || !ship.m_nview.IsOwner()) return false;
            st.AiSet(sheetAngle, sailAmount, rowDir);
            ship.m_rudderValue = Mathf.Clamp(rudder, -1f, 1f);
            return true;
        }

        private struct Boost { public float Accel, MaxKnots; }
        private static readonly Dictionary<Ship, Boost> _rowBoost = new Dictionary<Ship, Boost>();

        /// <summary>
        /// A crew at the oars with the sail still drawing: forward push of <paramref name="accel"/> m/s^2 up to
        /// <paramref name="maxKnots"/>, applied by SailTrim on the owner each physics step. 0 clears it. The game
        /// itself refuses rowing while sail is set; this is the extra a full crew gives a chase.
        /// </summary>
        public static void SetRowBoost(Ship ship, float accel, float maxKnots)
        {
            if (ship == null) return;
            if (accel <= 0f) _rowBoost.Remove(ship); else _rowBoost[ship] = new Boost { Accel = accel, MaxKnots = maxKnots };
        }

        internal static void ApplyRowBoost(Ship ship, float dt)
        {
            if (ship == null || ship.m_body == null || !_rowBoost.TryGetValue(ship, out Boost b)) return;
            Vector3 fwd = ship.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();
            float kn = Vector3.Dot(ship.m_body.linearVelocity, fwd) * 1.94384f;
            if (kn >= b.MaxKnots) return;
            float fade = Mathf.Clamp01((b.MaxKnots - kn) / 2f);
            ship.m_body.AddForce(fwd * (b.Accel * fade * ship.m_body.mass * dt), ForceMode.Impulse);
        }

        /// <summary>Make one hull faster than her length allows: the hull speed the drag builds toward is multiplied.</summary>
        public static void SetSpeedBonus(Ship ship, float factor)
        {
            var st = SailTrimShip.Get(ship);
            if (st != null) st.SpeedBonus = Mathf.Max(0.1f, factor);
        }

        /// <summary>Wind direction the true wind comes from, in the world, flat.</summary>
        public static Vector3 TrueWindFrom()
        {
            if (EnvMan.instance == null) return Vector3.forward;
            Vector3 to = EnvMan.instance.GetWindDir(); to.y = 0f;
            return to.sqrMagnitude < 1e-4f ? Vector3.forward : -to.normalized;
        }

        // ---- gangway ----
        public static bool GangwayFitted(Ship ship, int side) => Gangway.Fitted(ship, side);
        public static bool GangwayDown(Ship ship, int side) => Gangway.Down(ship, side);
        public static bool AnyGangwayDown(Ship ship) => Gangway.AnyDown(ship);
        public static bool LashedAlongside(Ship ship) => Gangway.LashedAlongside(ship);

        /// <summary>Fit a gangway to a rail without a kit (side -1 port, +1 starboard). Runs on the owner through the RPC.</summary>
        public static void FitGangway(Ship ship, int side) => Gangway.Request(ship, side, 0);

        /// <summary>Let the plank down on that side; onto another boat lashes the two together (both hold and stop).</summary>
        public static void LowerGangway(Ship ship, int side, Ship onto)
        {
            var oz = onto != null && onto.m_nview != null && onto.m_nview.IsValid() ? onto.m_nview.GetZDO().m_uid : ZDOID.None;
            Gangway.Request(ship, side, 1, oz);
        }

        public static void RaiseGangways(Ship ship) => Gangway.RaiseAll(ship, false);

        /// <summary>Lock this boat's lash: the boat it is lashed to cannot cast it off from its helm. Owner only.</summary>
        public static void LockLash(Ship ship, bool locked)
        {
            if (ship == null || ship.m_nview == null || !ship.m_nview.IsValid() || !ship.m_nview.IsOwner()) return;
            ship.m_nview.GetZDO().Set(Gangway.LashLockKey, locked);
        }

        /// <summary>The side (-1 port, +1 starboard) whose plank is down, or 0.</summary>
        public static int GangwayDownSide(Ship ship) => Gangway.Down(ship, -1) ? -1 : Gangway.Down(ship, 1) ? 1 : 0;

        /// <summary>The boat this one is lashed to by a plank, from either side, or null (also when it is not loaded here).</summary>
        public static Ship LashPartner(Ship ship)
        {
            if (ship == null) return null;
            var t = Gangway.LashTarget(ship, -1) ?? Gangway.LashTarget(ship, 1);
            if (t != null) return t;
            ZDOID from = Gangway.LashedFrom(ship);
            if (from.IsNone() || ZNetScene.instance == null) return null;
            var go = ZNetScene.instance.FindInstance(from);
            return go != null ? go.GetComponent<Ship>() : null;
        }

        /// <summary>Which boat's plank joins these two: this one's, or the partner's. Null when neither is down.</summary>
        public static Ship PlankOwner(Ship ship, Ship partner)
        {
            if (ship != null && GangwayDownSide(ship) != 0 && (Gangway.LashTarget(ship, -1) == partner || Gangway.LashTarget(ship, 1) == partner)) return ship;
            if (partner != null && GangwayDownSide(partner) != 0) return partner;
            return null;
        }

        /// <summary>The boat a lowered plank's foot actually rests on, or null (in the water, on the shore, or still probing).</summary>
        public static Ship GangwayLandedOn(Ship ship, int side)
        {
            if (ship == null) return null;
            foreach (var m in ship.GetComponentsInChildren<GangwayMount>(true))
                if (m.Side == side && m.IsDown) return m.RestsOnShip;
            return null;
        }

        /// <summary>A held boat's spot (moored or lashed): move it to warp her along, e.g. hauling two hulls together.</summary>
        public static void SetHoldSpot(Ship ship, Vector3 pos, float yaw) => Mooring.SetHold(ship, pos, yaw);
        public static bool GetHoldSpot(Ship ship, out Vector3 pos, out float yaw) => Mooring.GetHold(ship, out pos, out yaw);

        /// <summary>Where a lowered plank starts (the hinge at the rail) and ends (where it rests), for walking it.</summary>
        public static bool GangwayEnds(Ship ship, int side, out Vector3 hinge, out Vector3 foot)
        {
            hinge = foot = Vector3.zero;
            if (ship == null) return false;
            foreach (var m in ship.GetComponentsInChildren<GangwayMount>(true))
            {
                if (m.Side != side || !m.IsDown) continue;
                hinge = m.HingePoint;
                foot = m.FootPoint;
                return true;
            }
            return false;
        }
    }
}
