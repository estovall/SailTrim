using System;
using System.Collections.Generic;
using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// The gangway: a plank hinged at the rail that you walk over between the deck and the dock, so a load of
    /// metal no longer has to be jumped over the side (encumbered, you cannot jump at all).
    ///
    /// It is a fitting, not a build piece: craft a Gangway at the workbench and interact with the rail of any
    /// hull but the raft to fit it, one to a side. Once fitted it stays with the boat. Interact again to lower
    /// it: it swings down and rests on whatever is under it, dock, shore or log, and keeps resting there as the
    /// boat works in the swell. Interact once more, or take the helm and wait GangwayRetractDelay, to raise it.
    ///
    /// While any gangway is down the boat holds its spot the way a moored boat does (a creature or a wave
    /// cannot shove it out from under you), but unlike a cleat it does not mend the hull. Lowering one within
    /// reach of a cleat that has no boat also ties that cleat on, so one press does the whole job at a dock.
    ///
    /// State lives in the boat's ZDO as one int (SailTrim_Gangway): fitted and down, a bit each side. It is
    /// written by the boat's owner on the SailTrim_Gangway RPC, like the mooring.
    ///
    /// Walking across works because the plank is a child of the boat: the game adds the ground body's velocity
    /// at your feet to your own, which is the same thing that makes the deck itself walkable, and it samples
    /// that velocity at the point you stand on, so the swinging outboard end carries you correctly too.
    /// </summary>
    internal static class Gangway
    {
        internal const string ItemName = "SailTrim_GangwayKit";
        internal const string RpcName = "SailTrim_Gangway";
        private static readonly int StateHash = "SailTrim_Gangway".GetStableHashCode();

        internal const int FittedPort = 1, FittedStbd = 2, DownPort = 4, DownStbd = 8;

        internal static int FittedBit(int side) => side < 0 ? FittedPort : FittedStbd;
        internal static int DownBit(int side) => side < 0 ? DownPort : DownStbd;
        internal static string SideName(int side) => side < 0 ? "port" : "starboard";

        // Lashing alongside: a gangway let down onto another boat's deck rather than onto the shore. The record
        // is kept on both boats. The boat the plank belongs to carries, per side, which boat it went down on; the
        // boat underneath carries who laid the plank on it. Two copies of one fact look like a mistake waiting to
        // happen, and they would be if either side trusted its own alone: neither does. The boat underneath reads
        // the other boat's record before it holds, so a plank that has come up lets it go even if the message
        // saying so never arrived.
        internal const string LashRpc = "SailTrim_Lash";
        private const string LashPortKey = "SailTrim_LashPort";
        private const string LashStbdKey = "SailTrim_LashStbd";
        private const string LashedByKey = "SailTrim_LashedBy";
        // And the same three by tag. A ZDOID is reassigned when the world loads, so the ids above mean nothing
        // after a restart and the raft would come apart; see Tag in Cleat.cs.
        private const string LashPortTagKey = "SailTrim_LashPortTag";
        private const string LashStbdTagKey = "SailTrim_LashStbdTag";
        private const string LashedByTagKey = "SailTrim_LashedByTag";

        private static string LashTagKey(int side) => side < 0 ? LashPortTagKey : LashStbdTagKey;

        private static string LashKey(int side) => side < 0 ? LashPortKey : LashStbdKey;

        private static GameObject _itemPrefab;
        private static Sprite _icon;
        private static Material _woodMat;
        private static Recipe _recipe;

        // ------------------------------------------------------------------
        // State on the boat
        // ------------------------------------------------------------------
        internal static int State(Ship ship)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid()) return 0;
            return nv.GetZDO().GetInt(StateHash);
        }

        internal static bool Fitted(Ship ship, int side) => (State(ship) & FittedBit(side)) != 0;
        internal static bool Down(Ship ship, int side) => (State(ship) & DownBit(side)) != 0;
        internal static bool AnyDown(Ship ship) => (State(ship) & (DownPort | DownStbd)) != 0;

        /// <summary>Ask the boat's owner to change a gangway. action: 0 fit, 1 lower, 2 raise. When lowering,
        /// onto is the boat the plank came down on, or none for the shore.</summary>
        internal static void Request(Ship ship, int side, int action, ZDOID onto = default(ZDOID))
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid()) return;
            nv.InvokeRPC(RpcName, side, action, onto);
        }

        internal static void OnShipStart(Ship ship)
        {
            var nv = ship.m_nview;
            if (nv == null || !nv.IsValid()) return;
            nv.Register<int, int, ZDOID>(RpcName, (sender, side, action, onto) => RPC_Gangway(ship, side, action, onto));
            nv.Register<ZDOID, int>(LashRpc, (sender, from, on) => RPC_Lash(ship, from, on));
        }

        /// <summary>Runs on the boat's owner: the boat's ZDO is the one record of what is fitted and what is down.</summary>
        private static void RPC_Gangway(Ship ship, int side, int action, ZDOID onto)
        {
            var nv = ship.m_nview;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var zdo = nv.GetZDO();
            int state = zdo.GetInt(StateHash);
            switch (action)
            {
                case 0: state |= FittedBit(side); break;
                case 1:
                    if ((state & FittedBit(side)) == 0) return;
                    state |= DownBit(side);
                    // The boat holds where it lies from now on, so record the spot, as tying up does.
                    Mooring.HoldHere(ship);
                    ship.m_speed = Ship.Speed.Stop;
                    var st = SailTrimShip.Get(ship);
                    if (st != null) st.OnMoored();
                    SetLash(ship, side, onto);
                    break;
                case 2:
                    state &= ~DownBit(side);
                    SetLash(ship, side, ZDOID.None);
                    break;
                default: return;
            }
            zdo.Set(StateHash, state);
        }

        /// <summary>
        /// Owner of the boat the plank belongs to: write down the boat it went down on, and ask that boat to hold
        /// where it lies. Two hulls with a plank across them are one raft. If either drifted the plank would fall
        /// between them, so both stop, exactly as a gangway onto a dock stops the one boat.
        /// </summary>
        private static void SetLash(Ship ship, int side, ZDOID onto)
        {
            var zdo = ship.m_nview.GetZDO();
            ZDOID was = zdo.GetZDOID(LashKey(side));
            if (was != onto && !was.IsNone()) TellLashed(ship, was, false);
            if (was != onto) zdo.Set(LashKey(side), onto);
            var oz = onto.IsNone() || ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(onto);
            zdo.Set(LashTagKey(side), oz != null ? oz.GetLong(Tag.Key, 0L) : 0L);
            if (!onto.IsNone()) Tag.Of(ship.m_nview);
            if (!onto.IsNone()) TellLashed(ship, onto, true);
        }

        /// <summary>Tell the boat underneath, through its own owner, that a plank is or is no longer across it.</summary>
        private static void TellLashed(Ship ship, ZDOID other, bool on)
        {
            var go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(other) : null;
            var nv = go != null ? go.GetComponent<ZNetView>() : null;
            // Not here to be told. Nothing is lost: that boat reads our record for itself and stops holding when
            // the plank comes up, so an undelivered message costs a moment, never the release.
            if (nv == null || !nv.IsValid()) return;
            // A boat nobody has claimed has nobody to send this to, and the message would be dropped with the
            // raft half made. Take it on ourselves in that case: we are already running the other boat's side.
            if (!nv.GetZDO().HasOwner()) nv.ClaimOwnership();
            nv.InvokeRPC(LashRpc, ship.m_nview.GetZDO().m_uid, on ? 1 : 0);
        }

        /// <summary>Runs on the owner of the boat a plank has come down on: hold where we lie, and stop.</summary>
        private static void RPC_Lash(Ship ship, ZDOID from, int on)
        {
            var nv = ship.m_nview;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var zdo = nv.GetZDO();
            if (on != 0)
            {
                zdo.Set(LashedByKey, from);
                var fz = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(from) : null;
                zdo.Set(LashedByTagKey, fz != null ? fz.GetLong(Tag.Key, 0L) : 0L);
                Tag.Of(nv);
                Mooring.HoldHere(ship);
                ship.m_speed = Ship.Speed.Stop;
                var st = SailTrimShip.Get(ship);
                if (st != null) st.OnMoored();
            }
            else if (zdo.GetZDOID(LashedByKey) == from) { zdo.Set(LashedByKey, ZDOID.None); zdo.Set(LashedByTagKey, 0L); }
        }

        /// <summary>Ships whose lashing partner has not been seen lately, and since when (see LashedFrom).</summary>
        private static readonly Dictionary<Ship, float> _lashMissingSince = new Dictionary<Ship, float>();

        /// <summary>Is another boat's gangway lying across this one?</summary>
        internal static bool LashedAlongside(Ship ship) => !LashedFrom(ship).IsNone();

        /// <summary>
        /// The boat whose gangway is lying across this one, if any. Checked against that boat's own record rather
        /// than taken on trust, so the hold ends when the plank does, however the news arrives.
        /// </summary>
        internal static ZDOID LashedFrom(Ship ship)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid()) return ZDOID.None;
            var zdo = nv.GetZDO();
            // LashedFrom is asked every physics step by the mooring, so the repair is not: it only matters
            // after a world load, and once a second is sooner than anyone will notice.
            if (Time.time >= _relinkAt) { _relinkAt = Time.time + 1f; RelinkLash(ship); }
            ZDOID from = zdo.GetZDOID(LashedByKey);
            if (from.IsNone()) return ZDOID.None;
            var oz = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(from) : null;
            if (oz == null)
            {
                // The other boat's data is not here. Just after a world loads that is normal and the raft must
                // hold; a boat that has really gone never comes back, so give it half a minute, as a mooring does.
                if (!_lashMissingSince.TryGetValue(ship, out float since)) { _lashMissingSince[ship] = Time.time; return from; }
                if (Time.time - since < 30f) return from;
                _lashMissingSince.Remove(ship);
                if (nv.IsOwner()) zdo.Set(LashedByKey, ZDOID.None);
                return ZDOID.None;
            }
            _lashMissingSince.Remove(ship);
            bool down = (oz.GetInt(StateHash) & (DownPort | DownStbd)) != 0;
            long us = zdo.GetLong(Tag.Key, 0L);
            bool ours = oz.GetZDOID(LashPortKey) == zdo.m_uid || oz.GetZDOID(LashStbdKey) == zdo.m_uid
                     || (us != 0L && (oz.GetLong(LashPortTagKey, 0L) == us || oz.GetLong(LashStbdTagKey, 0L) == us));
            if (down && ours) return from;
            if (nv.IsOwner()) zdo.Set(LashedByKey, ZDOID.None);
            return ZDOID.None;
        }

        /// <summary>
        /// Put the raft's ids back after a world load, from the tags. Both ends: the boat underneath finds the
        /// boat whose plank is on it, and that boat's record of which side it lashed from is repaired too.
        /// </summary>
        private static void RelinkLash(Ship ship)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var zdo = nv.GetZDO();
            long want = zdo.GetLong(LashedByTagKey, 0L);
            if (want == 0L) return;
            ZDOID have = zdo.GetZDOID(LashedByKey);
            if (!have.IsNone() && ZDOMan.instance != null)
            {
                var hz = ZDOMan.instance.GetZDO(have);
                if (hz != null && hz.GetLong(Tag.Key, 0L) == want) return;
            }
            long us = Tag.Of(nv);
            foreach (var other in NearbyShips())
            {
                var ov = other.m_nview;
                if (ov == null || !ov.IsValid() || Tag.Read(ov) != want) continue;
                var oz2 = ov.GetZDO();
                zdo.Set(LashedByKey, oz2.m_uid);
                if (ov.IsOwner() && us != 0L)
                    foreach (int side in Sides)
                        if (oz2.GetLong(LashTagKey(side), 0L) == us) oz2.Set(LashKey(side), zdo.m_uid);
                Plugin.Log.LogInfo("SailTrim: " + ship.name + " found the boat lashed to it again after a reload");
                return;
            }
        }

        /// <summary>
        /// Every loaded boat, found once a second and shared. Each mount asking the engine for the list of ships
        /// separately is the same answer fetched many times over, and the answer allocates an array each time.
        /// </summary>
        private static Ship[] _ships = new Ship[0];
        private static float _shipsAt, _relinkAt;

        internal static Ship[] NearbyShips()
        {
            if (Time.time >= _shipsAt)
            {
                _shipsAt = Time.time + 1f;
                _ships = UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None);
            }
            return _ships;
        }

        /// <summary>The boat this gangway is lying on, if it is on a boat and that boat is loaded here.</summary>
        internal static Ship LashTarget(Ship ship, int side)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid()) return null;
            ZDOID id = nv.GetZDO().GetZDOID(LashKey(side));
            if (id.IsNone()) return null;
            var go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
            return go != null ? go.GetComponent<Ship>() : null;
        }

        internal static string ShipName(Ship ship)
        {
            if (ship == null) return "boat";
            var piece = ship.GetComponent<Piece>();
            return piece != null && !string.IsNullOrEmpty(piece.m_name)
                ? Localization.instance.Localize(piece.m_name) : "boat";
        }

        private static readonly Dictionary<Ship, float> _helmTime = new Dictionary<Ship, float>();
        private static readonly Dictionary<Ship, float> _lashTime = new Dictionary<Ship, float>();

        /// <summary>Pilot's client, every physics step at the helm with a gangway down: after the delay, raise it.</summary>
        internal static void PilotAtHelm(Ship ship, float dt)
        {
            if (!AnyDown(ship)) { _helmTime.Remove(ship); return; }
            if (!_helmTime.TryGetValue(ship, out float t)) t = 0f;
            t += dt;
            if (t < Plugin.GangwayRetractDelay.Value) { _helmTime[ship] = t; return; }
            _helmTime.Remove(ship);
            RaiseAll(ship, true);
        }

        /// <summary>
        /// Pilot's client, every physics step at the helm of a boat with someone else's plank across it: after
        /// the same delay, ask for that plank to come up. This boat cannot cast itself off, because the gangway
        /// is not its own, and a crew held alongside with no way to get under way would be caught there.
        /// </summary>
        internal const string LashLockKey = "SailTrim_LashLock";
        /// <summary>A lash the other boat will not let go of (a boarding party's): the helm here cannot raise it.</summary>
        internal static bool LashLocked(Ship other) => other != null && other.m_nview != null && other.m_nview.IsValid() && other.m_nview.GetZDO().GetBool(LashLockKey);

        internal static void LashedAtHelm(Ship ship, float dt)
        {
            ZDOID from = LashedFrom(ship);
            if (from.IsNone()) { _lashTime.Remove(ship); return; }
            {
                var lgo = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(from) : null;
                var lother = lgo != null ? lgo.GetComponent<Ship>() : null;
                if (LashLocked(lother)) { _lashTime.Remove(ship); return; }
            }
            if (!_lashTime.TryGetValue(ship, out float t)) t = 0f;
            t += dt;
            if (t < Plugin.GangwayRetractDelay.Value) { _lashTime[ship] = t; return; }
            _lashTime.Remove(ship);
            var go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(from) : null;
            var other = go != null ? go.GetComponent<Ship>() : null;
            if (other == null || other.m_nview == null || !other.m_nview.IsValid()) return;
            ZDOID us = ship.m_nview.GetZDO().m_uid;
            bool any = false;
            foreach (int side in Sides)
                if (other.m_nview.GetZDO().GetZDOID(LashKey(side)) == us) { Request(other, side, 2); any = true; }
            var p = Player.m_localPlayer;
            if (any && p != null) p.Message(MessageHud.MessageType.TopLeft, "Gangway up: cast off alongside");
        }

        /// <summary>Owner only: every plank up and every lash record gone, written straight into the ZDO. For a
        /// boat that must be free now (a prize under a player's helm), not after an RPC round trip.</summary>
        internal static void ForceClear(Ship ship)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var zdo = nv.GetZDO();
            int state = zdo.GetInt(StateHash);
            state &= ~(DownPort | DownStbd);
            zdo.Set(StateHash, state);
            foreach (int side in Sides)
            {
                ZDOID was = zdo.GetZDOID(LashKey(side));
                if (!was.IsNone()) TellLashed(ship, was, false);
                zdo.Set(LashKey(side), ZDOID.None);
                zdo.Set(LashTagKey(side), 0L);
            }
            zdo.Set(LashedByKey, ZDOID.None);
            zdo.Set(LashedByTagKey, 0L);
            zdo.Set(LashLockKey, false);
            _lashMissingSince.Remove(ship);
        }

        internal static void RaiseAll(Ship ship, bool message)
        {
            bool any = false;
            foreach (int side in Sides)
                if (Down(ship, side)) { Request(ship, side, 2); any = true; }
            var p = Player.m_localPlayer;
            if (any && message && p != null) p.Message(MessageHud.MessageType.TopLeft, "Gangway up");
        }

        internal static readonly int[] Sides = { -1, 1 };

        // ------------------------------------------------------------------
        // The fitting on each boat
        // ------------------------------------------------------------------
        private static bool Eligible(Ship ship)
        {
            if (ship == null || ship.m_floatCollider == null) return false;
            // Every hull but the raft: a raft has no rail to hinge a plank from.
            string n = ship.name ?? "";
            return n.IndexOf("raft", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// Where the hinge sits on a given hull. The rail itself is measured by ray, which gets the plank onto the
        /// timber; these are the corrections on top of that, arrived at by looking at each boat, because the hulls
        /// differ in where the mast, the shrouds and the benches leave room for a plank to lie.
        /// </summary>
        internal struct Placement
        {
            public float Z;       // fraction of the half length aft of the float collider's centre
            public float Inset;   // extra metres inboard of the measured rail edge
            public float Rise;    // extra metres above the measured rail top
            public bool Step;     // build a step from the deck up to the rail
            public float Stow;    // metres inboard of the hinge that the stowed bundle stands
            public float ZOffset; // metres further aft than the boat's own ladder
        }

        internal static Placement PlacementFor(Ship ship)
        {
            var p = new Placement { Z = Plugin.GangwayMountZ.Value, Inset = 0f, Rise = 0f, Step = true, ZOffset = -0.85f, Stow = 0.5f };
            string n = (ship != null ? ship.name : "") ?? "";
            if (n.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                p.Step = false;    // the Drakkar's own hull already climbs to the rail here
                p.Inset = -0.18f;  // and its rail is broad enough to carry the brackets further out
                // Its rail is broad, so half a metre inboard leaves the stowed bundle out in the middle of the
                // side walkway instead of against the edge where it belongs.
                p.Stow = 0.26f;
            }
            else if (n.IndexOf("VikingShip", StringComparison.OrdinalIgnoreCase) >= 0) { } // Longship
            else if (n.IndexOf("Karve", StringComparison.OrdinalIgnoreCase) >= 0) p.Inset = -0.07f;
            return p;
        }

        /// <summary>The boat's own boarding ladder, if it has one: the place it was built to be boarded at.</summary>
        private static Transform FindLadder(Ship ship)
        {
            Transform best = null;
            foreach (var t in ship.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.IndexOf("ladder", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (best == null || t.GetComponentsInChildren<Transform>(true).Length > best.GetComponentsInChildren<Transform>(true).Length)
                    best = t;
            }
            return best;
        }

        /// <summary>Ship.Awake: a mount at each rail, amidships. They show and hide themselves from the boat's state.</summary>
        internal static void OnShipAwake(Ship ship)
        {
            if (!Plugin.GangwayEnabled.Value || !Eligible(ship)) return;
            if (ship.transform.Find("SailTrim_Gangway_P") != null) return;

            var fc = ship.m_floatCollider;
            Vector3 centre = ship.transform.InverseTransformPoint(fc.transform.TransformPoint(fc.center));
            float halfBeam = fc.size.x * 0.5f;
            float halfLen = fc.size.z * 0.5f;
            // Where the boat itself says to come aboard. Every hull but the raft carries a boarding ladder, and
            // the beam it hangs on is the one place the builders left clear of benches, shrouds and the mast.
            // Guessing a fraction of the length aft of amidships put the Drakkar's nowhere near it.
            Placement place = PlacementFor(ship);
            float z = centre.z - halfLen * place.Z;
            Transform ladder = FindLadder(ship);
            if (ladder != null)
            {
                z = ship.transform.InverseTransformPoint(ladder.position).z;
                Plugin.Log.LogInfo($"SailTrim: {ship.name} gangway follows its ladder '{ladder.name}' at z {z:0.00}");
            }
            z += place.ZOffset;

            foreach (int side in Sides)
            {
                var go = new GameObject(side < 0 ? "SailTrim_Gangway_P" : "SailTrim_Gangway_S");
                go.transform.SetParent(ship.transform, false);
                go.AddComponent<GangwayPart>();
                go.transform.localPosition = new Vector3(centre.x + side * halfBeam, centre.y + 1f, z);
                // Port is the same rig mirrored: within a mount, +X is always outboard.
                go.transform.localRotation = side < 0 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;

                var plank = new GameObject("plank");
                plank.transform.SetParent(go.transform, false);
                var mount = plank.AddComponent<GangwayMount>();
                mount.Init(ship, side, go.transform, place);
            }
            Plugin.Log.LogInfo($"SailTrim: {ship.name} gangway mounts at +-{halfBeam:0.0} m, z {z:0.0}");
        }

        // ------------------------------------------------------------------
        // The item and its recipe
        // ------------------------------------------------------------------
        /// <summary>
        /// The plank wears the game's own fine wood, taken off the item it is built from. A material of the
        /// game's is lit the way everything else is, and it is the light timber a gangway should be; a material
        /// we build ourselves has to guess at the shader variants this build actually ships.
        /// </summary>
        private static Material WoodMaterial()
        {
            if (_woodMat != null) return _woodMat;
            var db = ObjectDB.instance;
            if (db != null)
            {
                foreach (var name in new[] { "FineWood", "RoundLog", "Wood" })
                {
                    var p = db.GetItemPrefab(name);
                    if (p == null) continue;
                    foreach (var r in p.GetComponentsInChildren<MeshRenderer>(true))
                        if (r.sharedMaterial != null && r.sharedMaterial.shader != null)
                        {
                            _woodMat = r.sharedMaterial;
                            Plugin.Log.LogInfo($"SailTrim: gangway timber from {name} ({_woodMat.name}, {_woodMat.shader.name})");
                            return _woodMat;
                        }
                }
            }
            if (Models.StandardTemplate == null) return null;
            _woodMat = Models.Standard(Models.NoiseTexture(new Color(0.78f, 0.62f, 0.40f), 0.18f, 4711, 0.5f), 0f, 0.1f);
            return _woodMat;
        }

        internal static Material WoodMaterialPublic() => WoodMaterial();

        private static Material _timber;
        private static bool _loggedProps;

        /// <summary>
        /// The boat's own timber, not a building's. A building piece's material varies itself by where it stands,
        /// so that two walls side by side do not look stamped from one mould; that variation is a function of
        /// world position, which is a constant for a house and a different number every frame for a boat. The
        /// gangway wore a wall's material and re-rolled its shading each frame as the hull moved under it. A
        /// ship's material cannot do that, because ships move, so we take the one the hull itself wears.
        /// </summary>
        internal static Material ShipTimber(Ship ship)
        {
            if (_timber != null) return _timber;
            // The longship's timber on every hull. Taking each hull's own was the tidier idea and it only worked
            // on one of them: our planks carry the wooden floor's uvs, and only the longship's material reads
            // them as timber. One material that looks right on all three beats three that do not.
            var scene = ZNetScene.instance;
            Material best = Timber(scene != null ? scene.GetPrefab("VikingShip") : null)
                         ?? Timber(ship != null ? ship.gameObject : null);
            if (best == null) return null;
            _timber = new Material(best) { name = best.name + " (SailTrim gangway)" };
            Tame(_timber);
            Plugin.Log.LogInfo($"SailTrim: gangway timber {best.name} ({best.shader.name})");
            return _timber;
        }

        /// <summary>The soundest wood material on a hull: not a worn or broken variant, not the water mask.</summary>
        private static Material Timber(GameObject hull)
        {
            if (hull == null) return null;
            Material best = null;
            foreach (var r in hull.GetComponentsInChildren<MeshRenderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null || m.shader == null) continue;
                string n = m.name.ToLowerInvariant();
                if (n.Contains("worn") || n.Contains("broken") || n.Contains("watermask") || n.Contains("default-material")) continue;
                if (n.Contains("wood")) return m;
                if (best == null && n.Contains("ship")) best = m;
            }
            return best;
        }

        /// <summary>
        /// Turn off any world-position variation the shader offers, and say in the log what it offered, so the
        /// next thing to try is a name from a list rather than a guess.
        /// </summary>
        internal static void TameMaterial(Material m) => Tame(m);

        private static void Tame(Material m)
        {
            var sh = m.shader;
            if (sh == null) return;
            var names = new List<string>();
            try
            {
                int n = sh.GetPropertyCount();
                for (int i = 0; i < n; i++)
                {
                    string prop = sh.GetPropertyName(i);
                    names.Add(prop);
                    string low = prop.ToLowerInvariant();
                    // Custom/Piece carries its own flag for this. A piece that stands still may vary itself by
                    // where it stands; one that moves may not, and the game says so with _MoveableObject. It is
                    // why the hull's own timber never flickered and a wall's did. With it set, any material the
                    // game has can be worn by something that moves.
                    // Both: _MoveableObject is the flag the game names for this, and a triplanar sampled in
                    // world space is the same trap by another route. Dropping the second one was a wrong guess --
                    // the beams were the wrong colour because a stale config value was painting over them.
                    if (low.Contains("moveableobject") || low.Contains("movableobject")
                        || low.Contains("triplanarlocal") || low.Contains("localpos"))
                    {
                        m.SetFloat(prop, 1f);
                        Plugin.Log.LogInfo($"SailTrim: {m.name} {prop} set for a moving object");
                    }
                    // The triplanar is not the shader's only way of varying itself by where a thing is. Custom/Piece
                    // also carries a value noise, and _MoveableObject evidently does not reach it: the planking is
                    // steady and the iron beams, whose material leans on it, are not. Off, and the old value logged,
                    // so it is on record whether this was the difference.
                    else if (low.Contains("valuenoise") || low.Contains("ripplefreq") || low.Contains("rippledistance"))
                    {
                        float had = 0f;
                        try { had = m.GetFloat(prop); } catch { }
                        if (Mathf.Abs(had) > 0.0001f)
                        {
                            m.SetFloat(prop, 0f);
                            Plugin.Log.LogInfo($"SailTrim: {m.name} {prop} was {had:0.###}, turned off for a moving object");
                        }
                    }
                }
            }
            catch { }
            if (!_loggedProps && names.Count > 0)
            {
                _loggedProps = true;
                Plugin.Log.LogInfo("SailTrim: gangway shader " + sh.name + " properties: " + string.Join(", ", names.ToArray()));
            }
        }

        /// <summary>Planking is anything cut as a deck tile; the rest is ironwork and keeps its own finish.</summary>
        private static bool IsPlanking(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.name.StartsWith("deck", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Settle the materials on everything we built. Copies already carry our own instances, so telling them
        /// they are on a moving object is safe and is all most of them need. Only if the hull's plain timber is
        /// asked for does anything get replaced.
        /// </summary>
        internal static void SettleMaterials(Transform root, Ship ship)
        {
            if (root == null) return;
            // The planking takes the hull's timber; the ironwork keeps its own. There used to be a switch to
            // put the hull's timber over everything, and it painted out the iron beams the moment they started
            // working -- not because the default was wrong but because a value already written to a config file
            // does not change when the default does. A setting nobody needs is a setting that can only go stale.
            Material plain = ShipTimber(ship);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var arr = r.sharedMaterials;
                if (arr == null) continue;
                bool planking = IsPlanking(r.transform);
                bool changed = false;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (planking && plain != null) { arr[i] = plain; changed = true; }
                    else if (arr[i] != null) Tame(arr[i]);
                }
                if (changed) r.sharedMaterials = arr;
            }
        }

        /// <summary>
        /// The walkway, laid from the game's own wooden floor pieces end to end with a beam down each edge, the way
        /// the buoy is put together from a barrel and a banner. Real parts rather than a mesh of our own: a game
        /// material expects the UVs of the mesh it ships with, and ours sampled it into muddy darkness.
        /// </summary>
        internal static GameObject BuildWalkway(Transform parent, string name, float length, float width, int layer)
        {
            if (Models.Headless || parent == null) return null;
            var existing = parent.Find(name);
            if (existing != null) return existing.gameObject;
            var db = ObjectDB.instance;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            bool built = false;
            string beamName = null;
            // The walking surface is plain planking, finished in the hull's own timber. The ironwork is the Wood
            // Iron Beam, and it belongs on the edges, where a real gangway is strapped.
            var floorSrc = TimberSource(out string floorName);
            if (floorSrc != null)
            {
                // Lay the piece out at its own size in both directions rather than stretching one of it to fit.
                // A beam stretched to the width of a walkway is a beam with its ironwork smeared across it.
                var first = Models.CopyVisual(floorSrc, root.transform, "deck0_0", out var b0, true);
                if (first != null && b0.size.x > 0.05f && b0.size.z > 0.02f)
                {
                    int nx = Mathf.Max(1, Mathf.RoundToInt(length / b0.size.x));
                    int nz = Mathf.Max(1, Mathf.RoundToInt(width / b0.size.z));
                    float sx = length / nx, sz = width / nz;
                    var scale = new Vector3(sx / b0.size.x, 1f, sz / b0.size.z);
                    for (int i = 0; i < nx; i++)
                        for (int j = 0; j < nz; j++)
                        {
                            GameObject part;
                            Bounds b;
                            if (i == 0 && j == 0) { part = first; b = b0; }
                            else part = Models.CopyVisual(floorSrc, root.transform, "deck" + i + "_" + j, out b, true);
                            if (part == null) continue;
                            bool fitted0 = Models.ScaleInto(part, scale, ref b);
                            Models.Place(part, b, new Vector3(0f, 1f, 0.5f),
                                         new Vector3(i * sx, 0f, (j + 0.5f) * sz - width * 0.5f),
                                         fitted0 ? Vector3.one : scale, Quaternion.identity);
                            built = true;
                        }
                }
                else if (first != null) UnityEngine.Object.Destroy(first);
            }
            var beamSrc = built ? IronBeamSource(out beamName) : null;
            if (beamSrc != null)
            {
                const float MaxEdge = 0.11f;  // an edge on a walkway, not a balk of timber laid along it
                foreach (float sz in new[] { -1f, 1f })
                {
                    var part = Models.CopyVisual(beamSrc, root.transform, sz < 0f ? "edgeL" : "edgeR", out var b, true);
                    if (part == null) continue;
                    // Lay the beam along the walkway whichever way round it was modelled. Taking the longest
                    // dimension and then always stretching X meant that a beam modelled along Z was squashed flat
                    // and blown out sideways: two fat slabs lying in the surface of the deck, fighting with it for
                    // every pixel.
                    Vector3 sz3 = b.size;
                    int longAxis = (sz3.x >= sz3.y && sz3.x >= sz3.z) ? 0 : (sz3.y >= sz3.z ? 1 : 2);
                    if (sz3[longAxis] < 0.05f) continue;
                    // Stretch it along the walkway, and take its cross-section down the same amount in both of
                    // the other two. An iron beam squashed to a tenth of its depth on one axis and not the other
                    // is an iron beam with its straps and rivets smeared into stripes.
                    int a1 = (longAxis + 1) % 3, a2 = (longAxis + 2) % 3;
                    float thick = Mathf.Max(sz3[a1], sz3[a2]);
                    float shrink = thick > MaxEdge ? MaxEdge / thick : 1f;
                    Vector3 scale = new Vector3(shrink, shrink, shrink);
                    scale[longAxis] = length / sz3[longAxis];
                    float edge = thick * shrink;
                    Quaternion rot = longAxis == 0 ? Quaternion.identity
                                   : longAxis == 1 ? Quaternion.Euler(0f, 0f, -90f)   // +Y along the walkway
                                                   : Quaternion.Euler(0f, 90f, 0f);   // +Z along the walkway
                    // Anchored at the near end of its long axis and centred on the other two, so it stands proud
                    // of the deck rather than lying in it.
                    Vector3 anchor = new Vector3(0.5f, 0.5f, 0.5f);
                    anchor[longAxis] = 0f;
                    // Set in by half its own width. Flush with the edge of the deck, the kerb's outer face and the
                    // deck's were the same plane for the whole two metres, and the renderer had no way to choose
                    // between them: that is the flicker down the length of the plank.
                    bool fittedE = Models.ScaleInto(part, scale, ref b);
                    Models.Place(part, b, anchor, new Vector3(0f, edge * 0.5f - 0.02f, sz * (width * 0.5f - edge * 0.5f)), fittedE ? Vector3.one : scale, rot);
                }
            }
            if (built && beamSrc != null && !_loggedBeamMats)
            {
                _loggedBeamMats = true;
                var srcNames = new List<string>();
                foreach (var r in beamSrc.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var m in r.sharedMaterials)
                        srcNames.Add((r.gameObject.name ?? "?") + ":" + (m == null ? "null" : m.name));
                var ourNames = new List<string>();
                var edge = root.transform.Find("edgeR");
                if (edge != null)
                    foreach (var r in edge.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        int subs = mf != null && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : -1;
                        var names = new List<string>();
                        foreach (var m in r.sharedMaterials) names.Add(m == null ? "null" : m.name);
                        ourNames.Add((r.gameObject.name ?? "?") + ":" + string.Join("+", names.ToArray()) + " submeshes=" + subs);
                    }
                Plugin.Log.LogInfo("SailTrim: beam source wears " + string.Join(", ", srcNames.ToArray())
                                 + " | our copy wears " + string.Join(", ", ourNames.ToArray()));
            }
            if (!built)
            {
                var mat = WoodMaterial();
                if (mat == null) { UnityEngine.Object.Destroy(root); return null; }
                var mb = new Models.MeshBuilder();
                mb.Box(new Vector3(length * 0.5f, -0.05f, 0f), new Vector3(length, 0.1f, width));
                Models.MeshPart(root.transform, "plain", mb.Build("SailTrim_GangwayPlain"), mat);
            }
            SetLayer(root.transform, layer);
            Plugin.Log.LogInfo($"SailTrim: gangway {name} built from {(built ? floorName + " + " + (beamName ?? "no beam") : "a plain plank")}, layer {LayerMask.LayerToName(layer)}");
            return root;
        }

        /// <summary>One tread of the steps, cut from the same floor piece the walkway is laid from.</summary>
        /// <summary>
        /// The planking. Plain boards, because that is what you walk on; the hull's own timber goes over them
        /// afterwards, which is the finish that looked right on the water.
        /// </summary>
        internal static GameObject TimberSource(out string name)
        {
            name = "plain";
            if (Plugin.GangwayPlainTimber.Value) return null;
            var src = Models.FindFirst(ObjectDB.instance, out name,
                "wood_floor", "wood_floor_1x1", "piece_woodfloor");
            if (src != null && !_loggedTimber)
            {
                _loggedTimber = true;
                Plugin.Log.LogInfo("SailTrim: gangway planking cut from " + name);
            }
            return src;
        }

        /// <summary>
        /// The Wood Iron Beam, for the edges and the fittings. A material is painted for the mesh it ships with --
        /// the straps and rivets are where they are because that beam's uvs put them there -- so the piece and its
        /// material are taken together and kept near the beam's own cross-section.
        /// </summary>
        internal static GameObject IronBeamSource(out string name)
        {
            name = "plain";
            if (Plugin.GangwayPlainTimber.Value) return null;
            // woodiron_beam. piece_woodironbeam is the localisation token -- what the build menu calls it --
            // and not what the prefab is called; both appear in the bundles side by side and the first is the
            // wrong one to ask ZNetScene for.
            var src = Models.FindFirst(ObjectDB.instance, out name,
                "woodiron_beam", "woodiron_beam_26", "woodiron_beam_45", "woodiron_pole",
                "piece_woodironbeam", "wood_ibeam", "iron_beam");
            if (src == null)
            {
                var scene = ZNetScene.instance;
                if (scene != null && scene.m_prefabs != null)
                    foreach (var p in scene.m_prefabs)
                    {
                        if (p == null || p.GetComponentInChildren<MeshRenderer>(true) == null) continue;
                        string pn = (p.name ?? "").ToLowerInvariant();
                        if (pn.IndexOf("woodiron", StringComparison.Ordinal) < 0
                            && pn.IndexOf("ironbeam", StringComparison.Ordinal) < 0
                            && pn.IndexOf("iron_beam", StringComparison.Ordinal) < 0
                            && pn.IndexOf("ibeam", StringComparison.Ordinal) < 0) continue;
                        name = p.name; src = p; break;
                    }
                // Whatever happens, say what there was to choose from, so a wrong name is a one-line fix and not
                // another round of looking at it.
                if (src == null && scene != null && scene.m_prefabs != null)
                {
                    var seen = new List<string>();
                    foreach (var p in scene.m_prefabs)
                        if (p != null && p.name.IndexOf("iron", StringComparison.OrdinalIgnoreCase) >= 0 && seen.Count < 40)
                            seen.Add(p.name);
                    Plugin.Log.LogWarning("SailTrim: no iron beam found. Prefabs with 'iron' in the name: " + string.Join(", ", seen.ToArray()));
                }
            }
            if (src == null) src = Models.FindFirst(ObjectDB.instance, out name, "wood_beam", "wood_beam_1", "wood_pole");
            if (src != null && !_loggedBeam)
            {
                _loggedBeam = true;
                Plugin.Log.LogInfo("SailTrim: gangway ironwork cut from " + name);
            }
            return src;
        }

        private static bool _loggedTimber, _loggedBeam, _loggedBeamMats;

        internal static GameObject BuildTread(Transform parent, string name, Vector3 size, Vector3 centre, int layer)
        {
            if (Models.Headless || parent == null) return null;
            var src = IronBeamSource(out _);
            if (src == null)
            {
                var mat0 = WoodMaterial();
                if (mat0 == null) return null;
                var mb0 = new Models.MeshBuilder();
                mb0.Box(centre + new Vector3(0f, size.y * 0.5f, 0f), size);
                var made = Models.MeshPart(parent, name, mb0.Build("SailTrim_Tread"), mat0);
                if (made != null) SetLayer(made.transform, layer);
                return made;
            }
            var part = Models.CopyVisual(src, parent, name, out var b, true);
            if (part == null || b.size.x < 0.05f || b.size.z < 0.05f) return null;
            var scale = new Vector3(size.x / b.size.x, Mathf.Max(0.05f, size.y / Mathf.Max(0.01f, b.size.y)), size.z / b.size.z);
            bool fittedT = Models.ScaleInto(part, scale, ref b);
            Models.Place(part, b, new Vector3(0.5f, 1f, 0.5f), centre + new Vector3(0f, size.y * 0.5f, 0f), fittedT ? Vector3.one : scale, Quaternion.identity);
            SetLayer(part.transform, layer);
            return part;
        }

        /// <summary>
        /// The visible parts belong on the layer the boat's own MESHES use, not the layer of its colliders: they
        /// are different layers, and a mesh left on the collider layer is not lit. That is why the plank was black.
        /// </summary>
        internal static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int k = 0; k < t.childCount; k++) SetLayer(t.GetChild(k), layer);
        }

        /// <summary>A copy of an item's shared data, so changing ours never touches the item we cloned.</summary>
        private static ItemDrop.ItemData.SharedData CloneShared(ItemDrop.ItemData.SharedData src)
        {
            var copy = new ItemDrop.ItemData.SharedData();
            foreach (var f in typeof(ItemDrop.ItemData.SharedData).GetFields())
                f.SetValue(copy, f.GetValue(src));
            return copy;
        }

        internal static void OnObjectDb(ObjectDB db)
        {
            if (!Plugin.GangwayEnabled.Value || db == null) return;
            Models.FindStandardTemplate(db);

            if (_itemPrefab == null)
            {
                // Clone a plain material item: that brings the ItemDrop, the ZNetView and the pickup behaviour with it.
                var src = db.GetItemPrefab("FineWood") ?? db.GetItemPrefab("Wood");
                if (src == null) return;
                var go = UnityEngine.Object.Instantiate(src, Cleat.EnsureRoot());
                go.name = ItemName;
                var drop = go.GetComponent<ItemDrop>();
                if (drop == null) { UnityEngine.Object.Destroy(go); return; }
                var shared = CloneShared(drop.m_itemData.m_shared);
                shared.m_name = "Gangway";
                shared.m_description = "A hinged plank of fine wood, iron-nailed, for a ship's rail. Fit it to either side of any boat but a raft, then lower it to walk ashore with a load that is too heavy to climb with.";
                shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
                shared.m_maxStackSize = 10;
                shared.m_weight = 6f;
                shared.m_teleportable = true;
                drop.m_itemData.m_shared = shared;
                drop.m_itemData.m_stack = 1;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                _itemPrefab = go;
            }

            var itemDrop = _itemPrefab.GetComponent<ItemDrop>();

            // The model, and an icon rendered from it, once a world's ObjectDB has given us a material to copy.
            if (_icon == null && !Models.Headless && Models.StandardTemplate != null)
            {
                var old = _itemPrefab.transform.Find("visual");
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                var visual = BuildWalkway(_itemPrefab.transform, "visual", Plugin.GangwayLength.Value, 0.9f, _itemPrefab.layer);
                if (visual != null)
                {
                    foreach (var r in visual.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                    // Laid flat and shrunk to sit in the hand and on the ground like an ordinary item.
                    visual.transform.localScale = Vector3.one * 0.22f;
                    visual.transform.localPosition = new Vector3(-Plugin.GangwayLength.Value * 0.11f, 0.05f, 0f);
                    _icon = Models.RenderIcon(visual, "icon_gangway", 256, 150f, 28f);
                }
            }
            if (_icon != null) itemDrop.m_itemData.m_shared.m_icons = new[] { _icon };

            if (!db.m_items.Contains(_itemPrefab))
            {
                db.m_items.Add(_itemPrefab);
                db.UpdateRegisters();
            }

            // The recipe: at the workbench, taken from whatever station an existing workbench recipe uses.
            if (_recipe == null)
            {
                CraftingStation bench = null;
                foreach (var r in db.m_recipes)
                {
                    var cs = r != null ? r.m_craftingStation : null;
                    if (cs != null && cs.name.IndexOf("workbench", StringComparison.OrdinalIgnoreCase) >= 0) { bench = cs; break; }
                }
                // Fine wood and iron nails: a fitting you come to once the longship is within reach, not a starter piece.
                var wood = db.GetItemPrefab("FineWood")?.GetComponent<ItemDrop>() ?? db.GetItemPrefab("Wood")?.GetComponent<ItemDrop>();
                var nails = db.GetItemPrefab("IronNails")?.GetComponent<ItemDrop>();
                var reqs = new List<Piece.Requirement>();
                if (wood != null) reqs.Add(new Piece.Requirement { m_resItem = wood, m_amount = Mathf.Max(1, Plugin.GangwayFineWoodCost.Value), m_recover = true });
                if (nails != null && Plugin.GangwayIronNailCost.Value > 0)
                    reqs.Add(new Piece.Requirement { m_resItem = nails, m_amount = Plugin.GangwayIronNailCost.Value, m_recover = true });
                if (reqs.Count > 0)
                {
                    _recipe = ScriptableObject.CreateInstance<Recipe>();
                    _recipe.name = "Recipe_" + ItemName;
                    _recipe.m_item = itemDrop;
                    _recipe.m_amount = 1;
                    _recipe.m_enabled = true;
                    _recipe.m_craftingStation = bench;
                    _recipe.m_minStationLevel = 1;
                    _recipe.m_resources = reqs.ToArray();
                }
            }
            if (_recipe != null && !db.m_recipes.Contains(_recipe)) db.m_recipes.Add(_recipe);
            Plugin.Log.LogInfo($"SailTrim: gangway item {(db.GetItemPrefab("SailTrim_GangwayKit") != null ? "registered" : "MISSING")}, " +
                $"recipe {(_recipe != null ? "at " + (_recipe.m_craftingStation != null ? _recipe.m_craftingStation.name : "no station") : "MISSING")}, " +
                $"needs {string.Join(" + ", System.Array.ConvertAll(_recipe != null ? _recipe.m_resources : new Piece.Requirement[0], r => r.m_amount + "x" + r.m_resItem.name))}");
        }

        internal static void OnZNetScene(ZNetScene scene)
        {
            if (!Plugin.GangwayEnabled.Value || scene == null || _itemPrefab == null) return;
            int hash = ItemName.GetStableHashCode();
            if (!scene.m_prefabs.Contains(_itemPrefab)) scene.m_prefabs.Add(_itemPrefab);
            if (!scene.m_namedPrefabs.ContainsKey(hash)) scene.m_namedPrefabs.Add(hash, _itemPrefab);
        }

        /// <summary>Does the player have a gangway to fit?</summary>
        private static readonly HashSet<ZDOID> _told = new HashSet<ZDOID>();
        private static float _tellAt;

        /// <summary>
        /// Tell someone carrying a gangway, once per boat, that the boat has somewhere to put it. A fitting you
        /// have to already know about is a fitting nobody finds: the brackets on the rail say where, and this
        /// says that there is a where at all.
        /// </summary>
        internal static void Tick()
        {
            if (!Plugin.GangwayEnabled.Value || Time.time < _tellAt) return;
            _tellAt = Time.time + 1f;
            var player = Player.m_localPlayer;
            if (player == null || !player.TakeInput()) return;
            var ship = player.GetStandingOnShip();
            if (ship == null || !Eligible(ship) || ship.m_nview == null || !ship.m_nview.IsValid()) return;
            ZDOID id = ship.m_nview.GetZDO().m_uid;
            if (_told.Contains(id)) return;
            if (Fitted(ship, -1) || Fitted(ship, 1)) { _told.Add(id); return; }
            if (!HasKit(player)) return;
            _told.Add(id);
            player.Message(MessageHud.MessageType.Center,
                "Gangway: fit it to the brackets on either rail");
        }

        internal static bool HasKit(Humanoid user)
        {
            var inv = user != null ? user.GetInventory() : null;
            return inv != null && inv.CountItems("Gangway") > 0;
        }

        /// <summary>
        /// The boat has been destroyed: put the gangways back in the world. A gangway is a thing you made and
        /// fitted, not part of the hull, so losing the boat should not also cost you the fine wood and the iron
        /// nails in it. One item for each side that had one, dropped where she went.
        /// </summary>
        internal static void DropFitted(Ship ship)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            if (_itemPrefab == null) return;
            var drop = _itemPrefab.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null) return;

            int n = 0;
            foreach (int side in Sides) if (Fitted(ship, side)) n++;
            if (n == 0) return;

            // Take them off the boat's record first, so nothing can hand them out twice.
            var zdo = nv.GetZDO();
            zdo.Set(StateHash, State(ship) & ~(FittedPort | FittedStbd | DownPort | DownStbd));

            for (int i = 0; i < n; i++)
            {
                Vector3 at = ship.transform.position + Vector3.up * 1.2f
                           + ship.transform.right * (i == 0 ? 0.6f : -0.6f);
                ItemDrop.DropItem(drop.m_itemData, 1, at, Quaternion.identity);
            }
            Plugin.Log.LogInfo($"SailTrim: {ship.name} broke up, {n} gangway(s) given back");
        }

        internal static bool ConsumeKit(Humanoid user)
        {
            var inv = user != null ? user.GetInventory() : null;
            if (inv == null) return false;
            var item = inv.GetItem("Gangway");
            if (item == null) return false;
            inv.RemoveItem(item, 1);
            return true;
        }
    }

    /// <summary>
    /// One gangway at one rail. The component sits on the plank, which is the thing you hover and walk on: the
    /// game routes hovering to a component on the hit collider's own object, so it has to live here and not on
    /// a parent. The plank's own transform is the hinge; its visual and collider hang off +X, outboard.
    /// </summary>
    /// <summary>
    /// Marks a gangway part you can stand on. The plank is its own body so that it cannot shove the boat about
    /// or prop it up on the dock, but the game works out what carries a passenger from the body under their feet,
    /// and ours is not the boat. This says which boat it belongs to; Character.UpdateGroundContact is corrected
    /// with it, so standing on the plank carries you exactly as standing on the deck does.
    /// </summary>
    internal class GangwayFooting : MonoBehaviour
    {
        internal Ship Ship;
    }

    /// <summary>
    /// Marks everything belonging to a gangway. It hangs on the mount, so anything under it answers to
    /// GetComponentInParent: the plank, the brow, the brackets and their colliders alike. The cleat asks the boat
    /// for its nearest hull collider to lead its rope to, and a lowered gangway reaching for the dock is nearer
    /// than the hull -- so the boat was being tied to its own gangway.
    /// </summary>
    internal class GangwayPart : MonoBehaviour
    {
    }

    /// <summary>
    /// What you actually look at and press Use on: a patch sitting exactly on the brackets, which answers for the
    /// mount. The mount's own collider could not do this job. It is the plank -- it swings out over the side and
    /// lies along the rail when stowed, so the place to interact moved a metre away from the fitting it belongs
    /// to; and it is solid, so while nothing was fitted there was an invisible block on the rail to walk into.
    /// </summary>
    internal class GangwayHandle : MonoBehaviour, Hoverable, Interactable
    {
        internal GangwayMount Mount;

        public string GetHoverName() => Mount != null ? Mount.GetHoverName() : "";
        public float GetHoverOffset() => 0f;
        public string GetHoverText() => Mount != null ? Mount.GetHoverText() : "";
        public bool Interact(Humanoid user, bool hold, bool alt) => Mount != null && Mount.Interact(user, hold, alt);
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => Mount != null && Mount.UseItem(user, item);
    }

    internal class GangwayMount : MonoBehaviour, Hoverable, Interactable
    {
        private Ship _ship;
        private int _side;
        private Transform _mount;      // the hinge point at the rail
        private BoxCollider _box;
        private GameObject _visual;         // the first section; the others hang off it
        private Transform _seg2, _seg3;     // hinged end to end, so six metres stows as two

        // Degrees below horizontal: 0 is straight out over the side, positive drops the far end.
        private float _restAngle;
        // 0 stowed, 1 down. The first half swings the plank out from along the rail, the second half lowers it:
        // one number so the two run into each other instead of stepping.
        private float _deploy;
        private const float FoldAngle = 180f;
        private const float LeafLift = 0.17f;    // how far each folded leaf stands clear of the one below it
        private const float StowLean = 80f;      // degrees off flat: nearly upright, so its footprint on deck is narrow
        private const float StowInset = 0.50f;   // metres inboard of the hinge that the stack stands
        private const float StepInset = 0.75f;   // metres inboard of the hinge that the step runs
        private const float StepWidth = 0.7f;    // how much of the rail the steps take up, fore and aft
        private const float BrowReach = 0.9f;    // roughly how far inboard the ramp comes down
        private const float BrowWidth = 0.7f;    // how wide the inboard ramp is
        private const float BrowSlope = 34f;     // degrees: a slope you can walk up with a full load
        private const float RailFace = 0.1f;     // where the ramp hinges: just inside the rail
        private const float StepClear = 0.04f;   // the step lands this far above the timber, never in it
        private bool _walkable;
        private bool _deckFound;
        private float _deckDrop = 0.6f;   // rail top above the deck at this mount, measured per hull
        private GameObject _step;
        private Rigidbody _rb;
        private float _nextProbe;
        // The probe is noisy: the boat lifts and rolls under it and a ray can catch an edge. Keep the last few
        // readings and steer for the middle one, then move smoothly toward that, rather than following each frame.
        private readonly float[] _probes = new float[5];
        private int _probeCount;
        private float _restDist;   // how far out along the plank the thing it rests on was found
        // Where the plank was set down, kept in the frame of the thing it was set down on. Re-probing every
        // frame meant the landing moved with every wave: half a metre of swell and the tip was reading the top
        // of a beam one frame and the ground beside it the next, so the plank climbed in and out of the timber.
        // A gangway put down on something stays put down on that spot, the way a rope stays on the cleat it was
        // made fast to, and only the angle to reach it changes as the boat works.
        private Transform _anchorOn;
        private Vector3 _anchorLocal, _anchorWorld;
        private bool _anchored;
        private bool _wasFitted, _wasDown;
        private Gangway.Placement _place;
        private Vector3 _railLocal;   // where the rail was found, before the per-hull correction
        private BoxCollider _stepCol;
        private Transform _brow, _browLeaf;
        private float _browDrop = 0.6f, _browLen = 1.1f;
        private readonly List<Collider> _mine = new List<Collider>();
        // The boat this plank is lying on, and the pairs struck out so that it does not lean on it.
        private float _lashIgnoreAt;
        private readonly List<Collider> _lashIgnored = new List<Collider>();
        private GameObject _brackets;
        private bool _timbered;
        private GameObject _lashing;
        // How far each folded leaf must stand clear of the one below it, measured off a leaf once it is built.
        // A fixed figure was right until the edge beams got thicker, and then the leaves folded into each other.
        // A folded leaf is not symmetrical: its planking hangs below its origin and its kerbs stand above. Leaf
        // two is turned over, so it meets leaf one kerb to kerb and leaf three plank to plank, and those two gaps
        // want different clearances. One figure for both had to be the larger of them, which left the first pair
        // standing a hand's breadth apart for no reason.
        private float _leafLo = -0.22f, _leafHi = 0.1f;
        private float LiftKerbs => Mathf.Max(0.05f, 2f * _leafHi + 0.02f);      // leaf one to leaf two
        private float LiftPlanks => Mathf.Max(0.05f, -2f * _leafLo + 0.02f);    // leaf two to leaf three
        // The brow is built before the plank and is narrower, so it measures its own rather than borrowing the
        // plank's. They come out the same today, which is exactly when a borrowed figure goes unnoticed.
        private float _browHi = 0.1f;
        private float BrowLift => Mathf.Max(0.05f, 2f * _browHi + 0.02f);

        /// <summary>How far the built thing reaches above and below its own origin.</summary>
        private static bool MeshSpan(Transform root, out float lo, out float hi)
        {
            lo = 0f; hi = 0f;
            float h = MeshHeight(root, out lo, out hi);
            return h > 0.001f;
        }

        /// <summary>How tall the built thing is, in its own space.</summary>
        private static float MeshHeight(Transform root) => MeshHeight(root, out _, out _);

        private static float MeshHeight(Transform root, out float lo, out float hi)
        {
            lo = 0f; hi = 0f;
            bool any = false;
            Bounds bb = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                Bounds lb = m.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = lb.center + Vector3.Scale(lb.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 pt = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { bb = new Bounds(pt, Vector3.zero); any = true; } else bb.Encapsulate(pt);
                }
            }
            if (!any) return 0f;
            lo = bb.min.y;
            hi = bb.max.y;
            return bb.size.y;
        }
        private float _ignoreAt;
        private int _stowDir = 1;      // +1 lies forward along the rail, -1 aft
        private bool _stowChosen;
        private float _ignoreStop = float.MaxValue;
        private string _profile = "";  // the shape of the hull the rail was picked out of

        /// <summary>What this mount worked out for itself, for the survey notes.</summary>
        internal string ProfileText() => _profile;

        /// <summary>The walking surface, for the survey to measure against the hull.</summary>
        internal Collider WalkCollider => _box;

        internal Collider StepCollider => _stepCol;

        internal Transform Visual => _visual != null ? _visual.transform : null;

        internal float Deploy => _deploy;
        internal int Side => _side;
        /// <summary>Out over the side, unfolded and resting: something to walk on.</summary>
        internal bool IsDown => _walkable;
        /// <summary>The hinge at the rail: where the plank starts on this deck.</summary>
        internal Vector3 HingePoint => transform.position;
        /// <summary>The far end of the plank, where it rests (the plank runs along the mount's own x axis).</summary>
        internal Vector3 FootPoint => transform.TransformPoint(new Vector3(Length, -0.05f, 0f));
        /// <summary>The plank has found something to rest on.</summary>
        internal bool Anchored => _anchored;
        /// <summary>The boat the plank rests on, if it rests on a boat.</summary>
        internal Ship RestsOnShip => _anchored && _anchorOn != null ? _anchorOn.GetComponentInParent<Ship>() : null;

        /// <summary>
        /// Put the rig at a given point of its travel and leave it there. The survey walks the whole swing this
        /// way and photographs it, which is the only way to see whether the plank passes through the hull on its
        /// way out; the next Update puts it back where the boat's state says it should be.
        /// </summary>
        internal void PoseFor(float deploy)
        {
            bool was = _wasFitted;
            _wasFitted = true;
            Apply(deploy);
            _wasFitted = was;
        }

        internal string Describe()
        {
            return $"rail(measured) {_railLocal}, inset {_place.Inset:0.00}, rise {_place.Rise:0.00}, " +
                   $"deckDrop {_deckDrop:0.00}, step {_place.Step}, deploy {_deploy:0.00}, rest {_restAngle:0.0} deg, length {Length:0.0}";
        }

        private float Length => Mathf.Max(1.5f, Plugin.GangwayLength.Value);

        internal void Init(Ship ship, int side, Transform mount, Gangway.Placement place)
        {
            _ship = ship;
            _side = side;
            _mount = mount;
            _place = place;
            transform.localPosition = Vector3.zero;
            _box = gameObject.AddComponent<BoxCollider>();
            _mine.Add(_box);
            BuildBrackets();
            _box.center = new Vector3(Length * 0.5f, -0.05f, 0f);
            _box.size = new Vector3(Length, 0.12f, 0.84f);
            // The layer the hull uses, so it hovers, blocks and carries a player exactly as the deck does.
            gameObject.layer = HullLayer(ship);
            // A child collider of the boat is part of the boat to the physics engine: resting the far end on the
            // shore propped the hull up and heeled it over as the water fell. Give the plank its own kinematic
            // body and it stops being part of the boat: still solid to stand on, but it cannot push the boat about.
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            // Never interpolate. Our bodies are moved by being children of the boat, not by the physics engine,
            // and interpolation makes Unity write the body's own physics pose over the transform every frame --
            // a pose that never changes, because we never call MovePosition. The boat then sails away and leaves
            // the collider hanging where it was first built. This was set to match the hull while chasing the
            // shading flicker, which turned out to be the material; the reason was wrong and so was the change.
            rb.interpolation = RigidbodyInterpolation.None;
            gameObject.AddComponent<GangwayFooting>().Ship = ship;
            _rb = rb;
            SetCollider(0);
            Apply(0f);
            IgnoreShip();
        }

        /// <summary>
        /// The plank is its own body, so to the physics engine it is a separate object sitting inside the boat,
        /// and a kinematic body overlapping a floating one shoves it with everything it has: every hull went over
        /// on its beam ends. Nothing we bolt on may push the boat about, so every pair is struck out by hand.
        /// Ship colliders can arrive after Awake, so this is redone over the first few seconds.
        /// </summary>
        private void IgnoreShip()
        {
            if (_ship == null) return;
            foreach (var c in _ship.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || _mine.Contains(c)) continue;
                foreach (var own in _mine) if (own != null) Physics.IgnoreCollision(own, c, true);
            }
            _ignoreAt = Time.time + 1f;
            if (_ignoreStop == float.MaxValue) _ignoreStop = Time.time + 20f;
        }

        /// <summary>
        /// Unfitted, all that is here is a small patch of rail to interact with; fitted, the collider is the plank
        /// you walk on. The object itself always stays active, because its own Update is what watches the boat's state.
        /// </summary>
        /// <summary>
        /// 0 nothing fitted, 1 fitted and stowed, 2 out over the side. The game gives hovering to the FIRST thing
        /// its ray meets, so an unfitted mount tucked inside the rail is invisible to it: it has to stand proud.
        /// </summary>
        private void SetCollider(int mode)
        {
            if (_box == null) return;
            // Unfitted there is no plank, so there is nothing to stand on and nothing to bump into. The patch on
            // the brackets is what you interact with, and it is not solid.
            bool on = mode != 0;
            if (_box.enabled != on) _box.enabled = on;
            if (!on) return;
            switch (mode)
            {
                case 2: // the plank you walk on
                    _box.center = new Vector3(Length * 0.5f, -0.05f, 0f);
                    _box.size = new Vector3(Length, 0.12f, 0.84f);
                    break;
                case 1: // folded and stowed: one section long, three leaves thick
                    float top = LiftKerbs + LiftPlanks + _leafHi;
                    _box.center = new Vector3(Length / 6f, (top + _leafLo) * 0.5f, 0f);
                    _box.size = new Vector3(Length / 3f, Mathf.Max(0.2f, top - _leafLo), 0.84f);
                    break;
                default:
                    break;
            }
        }

        private void RefreshCollider() => SetCollider(!_wasFitted ? 0 : (_deploy > 0.6f ? 2 : 1));

        /// <summary>The layer the boat's visible meshes are on, which is not the layer of its colliders.</summary>
        private static int VisualLayer(Ship ship)
        {
            foreach (var r in ship.GetComponentsInChildren<MeshRenderer>(true))
                if (r != null && r.gameObject.activeInHierarchy) return r.gameObject.layer;
            return 0;
        }

        private static int HullLayer(Ship ship)
        {
            foreach (var c in ship.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger && c.gameObject != ship.gameObject) return c.gameObject.layer;
            int piece = LayerMask.NameToLayer("piece");
            return piece >= 0 ? piece : ship.gameObject.layer;
        }

        /// <summary>
        /// Stowed, the plank lies flat along the rail pointing forward, which is where you would actually stow six
        /// metres of timber and keeps it clear of the shrouds, the mast and the steering oar. Deployed, it is
        /// square out over the side on its hinge. Between the two it swings out, then drops.
        /// </summary>
        private float _appliedDeploy = float.NaN, _appliedAngle = float.NaN;
        private bool _appliedFitted;

        private void Apply(float deploy)
        {
            // Stowed and still, there is nothing to write: the rig is a child of the boat and rides with it. This
            // runs on every mount of every boat, so the case of "nothing is happening" wants to cost nothing.
            if (deploy == _appliedDeploy && _wasFitted == _appliedFitted
                && (deploy <= 0f || _restAngle == _appliedAngle)) return;
            _appliedDeploy = deploy;
            _appliedAngle = _restAngle;
            _appliedFitted = _wasFitted;

            _deploy = deploy;
            // Nothing fitted: the mount is only somewhere to interact, and it belongs on the rail where it was put.
            if (!_wasFitted)
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                if (_walkable) { _walkable = false; RefreshCollider(); }
                return;
            }

            // In order, with a little overlap so they run into one another: unfold the sections, swing the whole
            // thing out from along the rail, then lower it onto what it rests on.
            float unfold = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(deploy / 0.4f));
            float swing = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((deploy - 0.35f) / 0.35f));
            float drop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((deploy - 0.7f) / 0.3f));

            // Stowed it stands on edge against the inside of the rail, pointing fore and aft, the way spare timber
            // is actually kept aboard. Lying flat on top of the rail put half its width through the rail itself.
            float stow = 1f - swing;
            float angle = Mathf.Lerp(0f, _restAngle, drop);
            Quaternion outPose = Quaternion.Euler(0f, 0f, -angle);
            Quaternion stowPose = Quaternion.Euler(0f, -90f * _side * _stowDir, 0f) * Quaternion.Euler(StowLean * _side * _stowDir, 0f, 0f);
            transform.localRotation = Quaternion.Slerp(outPose, stowPose, stow);
            transform.localPosition = Vector3.Lerp(Vector3.zero, StowOffset(), stow);

            // Folded, each leaf stands clear of the one under it. Folded flat about a shared hinge they were three
            // slabs in one plane, and the renderer had to choose between them every pixel: they crawled with z-fighting.
            float folded = 1f - unfold;
            float fold = folded * FoldAngle;
            float seg = Length / 3f;
            if (_seg2 != null) { _seg2.localPosition = new Vector3(seg, LiftKerbs * folded, 0f); _seg2.localRotation = Quaternion.Euler(0f, 0f, fold); }
            if (_seg3 != null) { _seg3.localPosition = new Vector3(seg, -LiftPlanks * folded, 0f); _seg3.localRotation = Quaternion.Euler(0f, 0f, -fold); }

            // The treads come down first: they are what you climb to reach the plank.
            FoldBrow(unfold);

            // Cast off as soon as it moves: a lashed bundle that swings out over the side would be a lashing
            // holding nothing.
            bool lashed = deploy < 0.02f;
            if (_lashing != null && _lashing.activeSelf != lashed) _lashing.SetActive(lashed);

            // Only a ramp that is out over the side and unfolded is something to walk on.
            bool walk = deploy > 0.6f;
            if (walk != _walkable) { _walkable = walk; RefreshCollider(); }
        }

        /// <summary>Where the stowed stack sits: inboard of the rail, down on the deck, straddling the mount.</summary>
        private Vector3 StowOffset()
        {
            // Forward of the mount, so the stack and the step are not fighting for the same patch of deck.
            return new Vector3(-(_place.Stow > 0f ? _place.Stow : StowInset), -_deckDrop + 0.45f, _side * 1.1f * _stowDir);
        }

        /// <summary>
        /// Three sections hinged end to end, the way a real boarding ramp is made, so six metres of timber stows
        /// as two. Each section hangs off the outboard end of the one before it and folds back over it.
        /// </summary>
        private void BuildSections()
        {
            if (_visual != null || Models.Headless) return;
            int layer = VisualLayer(_ship);
            float seg = Length / 3f;

            // A hair short at each end, so the joints are joints. Built to the full section length the sections
            // met face to face in exactly one plane, which flickers the same way the kerbs did.
            const float Joint = 0.02f;
            var s1 = new GameObject("section1");
            s1.transform.SetParent(transform, false);
            if (Gangway.BuildWalkway(s1.transform, "visual", seg - Joint * 2f, 0.84f, layer) == null)
            { UnityEngine.Object.Destroy(s1); return; }

            var s2 = new GameObject("section2");
            s2.transform.SetParent(s1.transform, false);
            s2.transform.localPosition = new Vector3(seg, 0f, 0f);
            Gangway.BuildWalkway(s2.transform, "visual", seg - Joint * 2f, 0.84f, layer);

            var s3 = new GameObject("section3");
            s3.transform.SetParent(s2.transform, false);
            s3.transform.localPosition = new Vector3(seg, 0f, 0f);
            Gangway.BuildWalkway(s3.transform, "visual", seg - Joint * 2f, 0.84f, layer);

            foreach (var sec in new[] { s1, s2, s3 })
            {
                var v = sec.transform.Find("visual");
                if (v != null) v.localPosition = new Vector3(Joint, 0f, 0f);
            }
            if (MeshSpan(s1.transform, out float lo, out float hi)) { _leafLo = lo; _leafHi = hi; }
            _visual = s1; _seg2 = s2.transform; _seg3 = s3.transform;
        }

        /// <summary>Where the far end of the plank would be at this angle.</summary>
        private Vector3 TipAt(float angle) => PointAt(Length, angle);

        // ------------------------------------------------------------------
        public string GetHoverName() => Gangway.Fitted(_ship, _side) ? "Gangway" : "Gangway brackets";
        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            if (_ship == null || !Plugin.GangwayEnabled.Value) return "";
            var p = Player.m_localPlayer;
            if (p == null || !_ship.IsPlayerInBoat(p)) return "";
            bool fitted = Gangway.Fitted(_ship, _side);
            if (!fitted)
            {
                if (!Gangway.HasKit(p))
                    return Localization.instance.Localize(
                        "Gangway brackets (" + Gangway.SideName(_side) + ")\n" +
                        "<color=#999999>Craft a Gangway at the workbench to fit one here</color>");
                return Localization.instance.Localize($"Gangway brackets ({Gangway.SideName(_side)})\n[<color=yellow><b>$KEY_Use</b></color>] Fit the gangway");
            }
            if (Gangway.Down(_ship, _side))
            {
                var onto = Gangway.LashTarget(_ship, _side);
                string where = onto != null ? ", across to the " + Gangway.ShipName(onto) : ", down";
                return Localization.instance.Localize($"Gangway ({Gangway.SideName(_side)}){where}\n[<color=yellow><b>$KEY_Use</b></color>] Raise");
            }
            return Localization.instance.Localize($"Gangway ({Gangway.SideName(_side)}), stowed\n[<color=yellow><b>$KEY_Use</b></color>] Lower");
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (_ship == null || item == null || item.m_shared == null) return false;
            if (Gangway.Fitted(_ship, _side) || item.m_shared.m_name != "Gangway") return false;
            return Fit(user);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _ship == null || !Plugin.GangwayEnabled.Value) return false;
            var player = user as Player;
            if (player == null) return false;
            // You work the gangway from on board, which is what keeps it separate from the cleat on the dock.
            if (!_ship.IsPlayerInBoat(player))
            {
                user.Message(MessageHud.MessageType.Center, "Come aboard to work the gangway");
                return false;
            }
            if (!Gangway.Fitted(_ship, _side)) return Fit(user);
            if (Gangway.Down(_ship, _side))
            {
                Gangway.Request(_ship, _side, 2);
                user.Message(MessageHud.MessageType.TopLeft, "Gangway up");
                return true;
            }
            return Lower(user);
        }

        private bool Fit(Humanoid user)
        {
            if (!Gangway.HasKit(user))
            {
                user.Message(MessageHud.MessageType.Center, "You need a Gangway");
                return false;
            }
            if (!Gangway.ConsumeKit(user)) return false;
            Gangway.Request(_ship, _side, 0);
            user.Message(MessageHud.MessageType.TopLeft, $"Gangway fitted to {Gangway.SideName(_side)}");
            return true;
        }

        private bool Lower(Humanoid user)
        {
            // Not under way: see Mooring.SlowEnough. Lowering holds the boat where she lies, and a plank aimed
            // from a moving boat is aimed at something it will no longer be over.
            if (!Mooring.SlowEnough(_ship, user)) return false;
            EnsureDeck();
            if (!FindRest(out float angle, out float _, out float _, out Ship onto, out Vector3 spot, out Transform on))
            {
                user.Message(MessageHud.MessageType.Center, "Nothing within reach to rest it on");
                return false;
            }
            _restAngle = angle;
            SetAnchor(spot, on);
            // It came down on another boat's deck rather than on the shore: the two are lashed alongside and both
            // hold where they lie. Nothing here decides whether the plank reaches; the plank decides that by
            // landing, exactly as it does on a dock.
            if (onto != null && (!Plugin.GangwayLashShips.Value || onto.m_nview == null || !onto.m_nview.IsValid()))
                onto = null;
            ZDOID target = onto != null ? onto.m_nview.GetZDO().m_uid : ZDOID.None;
            Gangway.Request(_ship, _side, 1, target);
            if (onto != null)
            {
                user.Message(MessageHud.MessageType.TopLeft, "Gangway down onto the " + Gangway.ShipName(onto) + ": both boats held");
                return true;
            }
            user.Message(MessageHud.MessageType.TopLeft, "Gangway down");
            // One press does the dock: a cleat in reach with no boat on it takes this one.
            if (Plugin.GangwayTiesCleat.Value && !Mooring.IsMoored(_ship) && Cleat.AutoTie(_ship))
                user.Message(MessageHud.MessageType.TopLeft, "Tied up");
            return true;
        }

        /// <summary>
        /// The rail height, measured once: cast down through the boat at the mount and take the highest thing of
        /// the boat's own that the ray finds. Awake is too early for this, so it is done on the first frame that
        /// has physics.
        /// </summary>
        private void EnsureDeck()
        {
            if (_deckFound || _ship == null || _mount == null) return;
            _deckFound = true;
            Vector3 start = _mount.localPosition;
            float sign = Mathf.Sign(start.x == 0f ? _side : start.x);
            float found = float.NaN, foundY = 0f;
            // Take the whole section across the beam rather than stopping at the first thing a ray finds. Stopping
            // at the first hit put the Drakkar's mount out in the air beside the ship, because an oar or a shield
            // stands further out than the rail and answers the ray exactly the same way.
            var xs = new List<float>();
            var ys = new List<float>();
            var profile = new System.Text.StringBuilder();
            for (float ax = Mathf.Abs(start.x) + 3f; ax > 0.3f; ax -= 0.08f)
            {
                float y = TopOfShip(sign * ax, start.z, start.y);
                if (float.IsNegativeInfinity(y)) continue;
                if (y < start.y - 3f || y > start.y + 3f) continue;   // not the masthead, not the keel
                xs.Add(ax); ys.Add(y);
                profile.Append(ax.ToString("0.00")).Append(':').Append(y.ToString("0.00")).Append(' ');
            }
            _profile = profile.ToString();
            if (xs.Count > 0)
            {
                // The rail is the highest timber of the hull, so take the outermost place that reaches that height.
                float high = float.NegativeInfinity;
                foreach (float y in ys) if (y > high) high = y;
                int railAt = -1;
                for (int i = 0; i < xs.Count; i++)
                    if (ys[i] >= high - 0.07f) { found = xs[i]; foundY = ys[i]; railAt = i; break; }

                // What you stand on beside the rail is the first level run of deck inboard of it, and the lowest
                // point of that run. Probing at fixed fractions of the beam and taking the lowest found the
                // Drakkar's main deck, a metre and a half down, when what is actually alongside the rail there is
                // a side walkway a hand's breadth below it: the stowed plank ended up buried in the hull.
                if (railAt >= 0)
                {
                    int runStart = railAt, best = -1;
                    for (int i = railAt + 1; i <= xs.Count; i++)
                    {
                        bool broke = i == xs.Count || Mathf.Abs(ys[i] - ys[i - 1]) > 0.02f;
                        if (!broke) continue;
                        if (i - runStart >= 7) { best = runStart; break; }
                        runStart = i;
                    }
                    if (best >= 0)
                    {
                        float deck = float.PositiveInfinity;
                        for (int i = best; i < xs.Count && (i == best || Mathf.Abs(ys[i] - ys[i - 1]) <= 0.02f); i++)
                            deck = Mathf.Min(deck, ys[i]);
                        if (!float.IsInfinity(deck)) _deckDrop = Mathf.Clamp(foundY - deck, 0.1f, 1.8f);
                    }
                }
            }
            // How far the rail stands above the deck here: probe inboard and take the lowest top surface we find,
            // which is the deck rather than a bench or a chest standing on it.
            if (!float.IsNaN(found))
            {
                // A hand's breadth inboard of the edge, so the hinge sits on the rail rather than over the water,
                // plus whatever this hull needs on top of that.
                _railLocal = new Vector3(sign * found, foundY, start.z);
                _mount.localPosition = new Vector3(sign * (found - 0.12f - _place.Inset), foundY + 0.05f + _place.Rise, start.z);
                Plugin.Log.LogInfo($"SailTrim: {_ship.name} gangway {Gangway.SideName(_side)} rail at x {sign * found:0.00}, y {foundY:0.00}, {_deckDrop:0.00} m above the deck (guess was {start.x:0.00}, {start.y:0.00})");
            }
            else Plugin.Log.LogWarning($"SailTrim: {_ship.name} gangway {Gangway.SideName(_side)} found no rail, kept the guess {start.x:0.00}, {start.y:0.00}");
            IgnoreShip();
        }

        /// <summary>The top of the boat's own timber at this spot on the deck plan, in the boat's frame.</summary>
        private float TopOfShip(float localX, float localZ, float aroundY)
        {
            // Down the boat's own mast, not the world's. Cast straight down in world space, a heeled boat gives a
            // section through a slanted column of hull: the same Karve read its rail at 0.92 on one side and 1.56
            // on the other, and differently again on the next boat, purely from how it happened to be lying.
            Vector3 from = _ship.transform.TransformPoint(new Vector3(localX, aroundY + 6f, localZ));
            Vector3 dir = -_ship.transform.up;
            var hits = Physics.RaycastAll(from, dir, 12f, ~0, QueryTriggerInteraction.Ignore);
            float top = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (_mine.Contains(h.collider)) continue;
                if (!h.collider.transform.IsChildOf(_ship.transform)) continue;
                float y = _ship.transform.InverseTransformPoint(h.point).y;
                if (y > top) top = y;
            }
            return top;
        }

        /// <summary>
        /// The angle the plank comes to rest at: sweep it down from a little above horizontal and take the first
        /// thing its far end meets, which is what a plank let down on its hinge does. Nothing within
        /// GangwayMaxAngle means nothing it could rest on that you could still climb carrying a load.
        /// </summary>
        /// <summary>The middle of the last few readings: one ray catching an edge cannot move the plank.</summary>
        private float ProbeMedian()
        {
            int n = Mathf.Min(_probeCount, _probes.Length);
            var a = new float[n];
            System.Array.Copy(_probes, a, n);
            System.Array.Sort(a);
            return a[n / 2];
        }

        private void PushProbe(float a, bool reset)
        {
            if (reset) { for (int i = 0; i < _probes.Length; i++) _probes[i] = a; _probeCount = _probes.Length; return; }
            _probes[_probeCount % _probes.Length] = a;
            _probeCount++;
        }

        private bool FindRest(out float angle, out float groundY, out float dist, out Ship onto)
            => FindRest(out angle, out groundY, out dist, out onto, out _, out _);

        private bool FindRest(out float angle, out float groundY, out float dist, out Ship onto,
                              out Vector3 spot, out Transform on)
        {
            angle = 0f;
            groundY = 0f;
            dist = 0f;
            onto = null;
            spot = Vector3.zero;
            on = null;
            float max = Mathf.Max(5f, Plugin.GangwayMaxAngle.Value);
            // Not the sea, and not people: a gangway rests on something solid, or it does not go down at all.
            int mask = ~LayerMask.GetMask("Water", "WaterVolume", "water", "character", "character_net",
                                          "character_ghost", "character_trigger", "viewblock", "weapon", "smoke");
            // Whatever the plank would touch first, anywhere along its length, is what holds it up. Looking only
            // under the far end, a beam halfway out was something the plank went straight through.
            float shallowest = float.MaxValue;
            for (float d = 1f; d <= Length + 0.01f; d += 0.4f)
            {
                float a = AngleOnto(d, max, mask, out float g, out Ship s, out Transform t);
                if (float.IsNaN(a) || a >= shallowest) continue;
                shallowest = a;
                groundY = g;
                dist = d;
                onto = s;
                on = t;
                spot = PointAt(d, a);
                spot.y = g;
            }
            if (shallowest == float.MaxValue) return false;
            angle = Mathf.Clamp(shallowest, -20f, max);
            return true;
        }

        /// <summary>
        /// The angle at which the plank, this far out from its hinge, would come down onto whatever is under it.
        /// The plank's reach shortens as it tilts, so where it lands moves: two passes settle it.
        /// </summary>
        private float AngleOnto(float d, float max, int mask, out float groundY, out Ship onto, out Transform on)
        {
            groundY = 0f;
            onto = null;
            on = null;
            float a = 0f;
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 p = PointAt(d, a);
                var hits = Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 6f, mask, QueryTriggerInteraction.Ignore);
                float top = float.NegativeInfinity;
                Ship found = null;
                Transform foundOn = null;
                foreach (var h in hits)
                {
                    if (h.collider.transform.IsChildOf(_ship.transform)) continue;  // the boat's own timber, not the shore
                    if (h.point.y > p.y + 0.4f) continue;                            // something overhead, not underfoot
                    if (h.point.y <= top) continue;
                    top = h.point.y;
                    foundOn = h.collider.transform;
                    // Another hull is ground like any other, and the deck of a boat moored alongside is the one
                    // piece of ground that would sail away: whatever the plank lands on, we remember whose it is.
                    found = h.collider.GetComponentInParent<Ship>();
                }
                onto = found;
                on = foundOn;
                if (float.IsNegativeInfinity(top)) return float.NaN;
                groundY = top;
                float next = Mathf.Asin(Mathf.Clamp((_mount.position.y - top) / d, -1f, 1f)) * Mathf.Rad2Deg;
                next = Mathf.Clamp(next, -20f, max);
                if (Mathf.Abs(next - a) < 0.2f) return next;
                a = next;
            }
            return a;
        }

        /// <summary>
        /// A plank lying on another boat must not lean on it. Our body is kinematic, and a kinematic body
        /// overlapping a floating one shoves it with everything it has -- the same thing that put every hull on
        /// its beam ends until the boat's own colliders were struck out. So the pairs come out while the plank is
        /// across, and go back in when it comes up, or the two hulls would pass through each other afterwards.
        /// Once a second, because a boat sailing up alongside brings its colliders with it.
        /// </summary>
        private void UpdateLashIgnore(bool down)
        {
            // Nothing to do at all unless a plank is out; a stowed gangway cannot touch another boat.
            if (!down && _lashIgnored.Count == 0) return;
            if (Time.time < _lashIgnoreAt) return;
            _lashIgnoreAt = Time.time + 1f;
            SetLashIgnore(down);
        }

        /// <summary>
        /// While the plank is out it may not touch any boat. Sparing only the boat it lashed to left the shove in
        /// place everywhere else: on the way out before the lash is made, on a boat it lands on without lashing,
        /// and on every boat at all when GangwayLashShips is off. Ours is a kinematic body and a kinematic body
        /// overlapping a floating one throws it on its beam ends, which is what put a hull on its side.
        /// </summary>
        private void SetLashIgnore(bool down)
        {
            if (!down)
            {
                if (_lashIgnored.Count == 0) return;
                foreach (var c in _lashIgnored)
                {
                    if (c == null) continue;
                    foreach (var own in _mine) if (own != null) Physics.IgnoreCollision(own, c, false);
                }
                _lashIgnored.Clear();
                return;
            }
            Vector3 here = _mount != null ? _mount.position : transform.position;
            foreach (var other in Gangway.NearbyShips())
            {
                if (other == null || other == _ship) continue;   // our own is struck out already, in IgnoreShip
                if (Vector3.Distance(other.transform.position, here) > Length + 30f) continue;
                foreach (var c in other.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null || _mine.Contains(c) || _lashIgnored.Contains(c)) continue;
                    foreach (var own in _mine) if (own != null) Physics.IgnoreCollision(own, c, true);
                    _lashIgnored.Add(c);
                }
            }
        }

        /// <summary>The spot the plank was set down on, followed into the present if it belongs to something that moves.</summary>
        private bool AnchorPoint(out Vector3 world)
        {
            world = _anchorWorld;
            if (!_anchored) return false;
            if (_anchorOn != null) world = _anchorOn.TransformPoint(_anchorLocal);
            return true;
        }

        private void SetAnchor(Vector3 spot, Transform on)
        {
            _anchorOn = on;
            _anchorWorld = spot;
            _anchorLocal = on != null ? on.InverseTransformPoint(spot) : spot;
            _anchored = true;
        }

        /// <summary>Where the plank is, this far out from the hinge, at this angle.</summary>
        private Vector3 PointAt(float d, float angle)
        {
            float r = angle * Mathf.Deg2Rad;
            return _mount.TransformPoint(new Vector3(Mathf.Cos(r), -Mathf.Sin(r), 0f) * d);
        }

        /// <summary>
        /// A ramp from the deck up to the hinge. Without it the gangway starts at the top of the rail, which you
        /// would have to climb to reach, and climbing is the one thing a loaded player cannot do. It is a child of
        /// the mount, not of the plank, so it stays put while the plank swings.
        /// </summary>
        private void EnsureStep()
        {
            if (_step != null || _mount == null || !_deckFound || !_place.Step) return;
            if (!_wasFitted && !Gangway.Fitted(_ship, _side)) return;   // no gangway, nothing to climb to
            // How far there is to climb, probed just inside the rail rather than assumed flat: a Karve has no deck
            // at all, you stand on the curve of the hull, and a reading taken anywhere else is the wrong height.
            float drop = _deckDrop;
            for (int pass = 0; pass < 2; pass++)
            {
                float foot = float.NegativeInfinity;
                for (int i = 1; i <= 3; i++)
                {
                    Vector3 sp = _ship.transform.InverseTransformPoint(
                        _mount.TransformPoint(new Vector3(-RailFace - BrowReach * i / 3f, 0f, 0f)));
                    float y = TopOfShip(sp.x, sp.z, _mount.localPosition.y);
                    if (y > foot) foot = y;
                }
                if (float.IsNegativeInfinity(foot)) break;
                drop = Mathf.Clamp(_mount.localPosition.y - foot, 0.1f, 1.8f);
            }
            _browDrop = drop;
            // A slope, not a ledge. Valheim characters do not step up: you jump, and the whole point of a gangway
            // is that a loaded player cannot jump. A single tread was something to stand in front of, not on.
            _browLen = Mathf.Clamp(drop / Mathf.Sin(BrowSlope * Mathf.Deg2Rad), 0.8f, 2.2f);

            _step = new GameObject("step");
            _step.transform.SetParent(_mount, false);
            _step.layer = gameObject.layer;
            _step.transform.localPosition = new Vector3(-RailFace, 0f, 0f);

            // Its own body, for the same reason the plank has one: anything of ours left in the boat's own
            // compound is part of the boat, and props the hull the moment it touches the shore.
            var rb = _step.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;   // see the note on the plank's body
            _step.AddComponent<GangwayFooting>().Ship = _ship;

            // Two leaves, so that a ramp long enough to walk up folds into something short enough to stand
            // against the rail. Folded it takes no deck at all; a step that lives on the deck is a step you walk
            // round for the rest of the voyage.
            int layer = VisualLayer(_ship);
            float half = _browLen * 0.5f;
            var a = new GameObject("browA");
            a.transform.SetParent(_step.transform, false);
            a.layer = _step.layer;
            Gangway.BuildWalkway(a.transform, "visual", half - 0.02f, BrowWidth, layer);
            var bLeaf = new GameObject("browB");
            bLeaf.transform.SetParent(a.transform, false);
            bLeaf.layer = _step.layer;
            bLeaf.transform.localPosition = new Vector3(half, 0f, 0f);
            Gangway.BuildWalkway(bLeaf.transform, "visual", half - 0.02f, BrowWidth, layer);

            var ramp = new GameObject("ramp");
            ramp.transform.SetParent(a.transform, false);
            ramp.layer = _step.layer;
            ramp.transform.localPosition = new Vector3(_browLen * 0.5f, -0.05f, 0f);
            _stepCol = ramp.AddComponent<BoxCollider>();
            _stepCol.size = new Vector3(_browLen, 0.1f, BrowWidth);
            _mine.Add(_stepCol);

            if (MeshSpan(a.transform, out float _, out float bhi)) _browHi = bhi;
            _brow = a.transform;
            _browLeaf = bLeaf.transform;
            _step.SetActive(_wasFitted);
            FoldBrow(0f);
            IgnoreShip();
            Plugin.Log.LogInfo($"SailTrim: {_ship.name} gangway {Gangway.SideName(_side)} brow {_browLen:0.00} m for a {drop:0.00} m climb (deck drop at the rail was {_deckDrop:0.00})");
        }

        /// <summary>
        /// The inboard ramp swings down off the rail with the gangway and folds in two against it when stowed, so
        /// it is only on the deck while it is being walked on.
        /// </summary>
        private void FoldBrow(float down)
        {
            if (_brow == null) return;
            float reach = Mathf.Rad2Deg * Mathf.Asin(Mathf.Clamp01(_browDrop / Mathf.Max(0.01f, _browLen)));
            // -90 hangs it straight down inside the planking, -reach lays it out onto the deck. It never rises
            // above horizontal: folded up above the rail it was the first thing you saw of the boat.
            float z = Mathf.Lerp(-90f, -reach, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(down)));
            _brow.localRotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(0f, 0f, z);

            float folded = 1f - Mathf.Clamp01(down);
            if (_browLeaf != null)
            {
                _browLeaf.localPosition = new Vector3(_browLen * 0.5f, BrowLift * folded, 0f);
                _browLeaf.localRotation = Quaternion.Euler(0f, 0f, folded * FoldAngle);
            }
            bool solid = down > 0.8f;
            if (_stepCol != null && _stepCol.enabled != solid) _stepCol.enabled = solid;
        }

        /// <summary>
        /// Two lashings round the folded bundle, so a stowed gangway looks stowed rather than balanced there. They
        /// are built in the plank's own frame, so they sit on the bundle however it is leaning, and they are cast
        /// off the moment it starts to go over the side.
        /// </summary>
        private void BuildLashings()
        {
            if (_lashing != null || Models.Headless || _visual == null) return;
            // Only once the bundle is actually folded, because the rope is measured round whatever is there.
            if (!_wasFitted || _deploy > 0.001f) return;
            var mat = CleatPiece.RopeMaterial();
            if (mat == null) return;   // the world's prefabs are not up yet; try again next frame

            // Measure the folded bundle rather than working it out from the leaf thickness. Worked out, the rope
            // passed straight through the edge beams stacked on top of the leaves, which is a rope round nothing.
            Bounds bb = new Bounds();
            bool any = false;
            foreach (var mf in _visual.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                Bounds lb = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = lb.center + Vector3.Scale(lb.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 pt = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { bb = new Bounds(pt, Vector3.zero); any = true; } else bb.Encapsulate(pt);
                }
            }
            if (!any) return;

            float ry = bb.extents.y + 0.05f;
            float rz = bb.extents.z + 0.05f;
            var mb = new Models.MeshBuilder();
            foreach (float t0 in new[] { 0.24f, 0.72f })
            {
                float x0 = bb.min.x + bb.size.x * t0;
                var path = new List<Vector3>();
                for (int i = 0; i <= 32; i++)
                {
                    // A rounded rectangle, because the bundle is a rectangle. An ellipse round it stands off the
                    // flats and cuts the corners, which is a rope sunk into the timber at four places.
                    float a = Mathf.Lerp(-Mathf.PI, Mathf.PI, i / 32f);
                    float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    float y = Mathf.Sign(c) * Mathf.Sqrt(Mathf.Abs(c)) * ry;
                    float z = Mathf.Sign(sn) * Mathf.Sqrt(Mathf.Abs(sn)) * rz;
                    path.Add(new Vector3(x0, bb.center.y + y, bb.center.z + z));
                }
                mb.Sweep(path, 0.045f, 8);
            }
            _lashing = Models.MeshPart(transform, "lashing", mb.Build("SailTrim_GangwayLashing"), mat);
            if (_lashing != null)
            {
                // The world's own rope, told it is on a moving object and otherwise left alone. Darkening it by
                // a guessed fraction only made it a colour nothing else in the game is.
                var r = _lashing.GetComponent<MeshRenderer>();
                if (r != null && r.sharedMaterial != null)
                {
                    var mine = new Material(r.sharedMaterial) { name = r.sharedMaterial.name + " (SailTrim lashing)" };
                    Gangway.TameMaterial(mine);
                    r.sharedMaterial = mine;
                }
            }
            if (_lashing != null)
            {
                Gangway.SetLayer(_lashing.transform, VisualLayer(_ship));
                _lashing.SetActive(_wasFitted && _deploy < 0.02f);
            }
        }

        /// <summary>
        /// A pair of iron-strapped timber brackets bolted to the rail, there whether a gangway is fitted or not.
        /// Nothing marked the spot before: you had to know that a particular stretch of rail on a particular boat
        /// would answer, and nobody was going to work that out. Now the boat shows you where its gangway goes.
        /// </summary>
        private void BuildBrackets()
        {
            if (Models.Headless || _mount == null || _brackets != null) return;
            _brackets = new GameObject("brackets");
            _brackets.transform.SetParent(_mount, false);
            int layer = VisualLayer(_ship);
            // Something to look at, on the piece layer the game's own ladders and benches use: hoverable, and
            // you walk through it rather than into it.
            var patch = _brackets.AddComponent<BoxCollider>();
            patch.center = new Vector3(-0.02f, 0.10f, 0f);
            patch.size = new Vector3(0.42f, 0.42f, 0.98f);
            int soft = LayerMask.NameToLayer("piece_nonsolid");
            _brackets.layer = soft >= 0 ? soft : gameObject.layer;
            _brackets.AddComponent<GangwayHandle>().Mount = this;
            _mine.Add(patch);

            foreach (float sz in new[] { -1f, 1f })
                Gangway.BuildTread(_brackets.transform, sz < 0f ? "bracketL" : "bracketR",
                                   // Chunky, because it is a stub cut from a beam. Squashed to a wafer the
                                   // ironwork painted along that beam smears into a stripe.
                                   new Vector3(0.30f, 0.20f, 0.22f),
                                   new Vector3(-0.02f, 0.02f, sz * 0.34f), layer);
        }

        /// <summary>
        /// Which way along the rail the stack lies, settled by trying both and keeping the one that is not inside
        /// the mast or a bench. Every hull puts its furniture somewhere different and a rule that suited the Karve
        /// put the Longship's stack through a rowing bench.
        /// </summary>
        private void ChooseStowSide()
        {
            if (_stowChosen || _box == null || !_wasFitted) return;
            _stowChosen = true;
            float was = _deploy;
            float best = float.MaxValue;
            int bestDir = 1;
            foreach (int dir in new[] { 1, -1 })
            {
                _stowDir = dir;
                Apply(0f);
                Physics.SyncTransforms();
                float worst = 0f;
                foreach (var c in _ship.GetComponentsInChildren<Collider>())
                {
                    if (c == null || c.isTrigger || c.GetComponentInParent<GangwayMount>() != null) continue;
                    if (Physics.ComputePenetration(_box, _box.transform.position, _box.transform.rotation,
                                                   c, c.transform.position, c.transform.rotation, out _, out float d))
                        worst = Mathf.Max(worst, d);
                }
                if (worst < best) { best = worst; bestDir = dir; }
            }
            _stowDir = bestDir;
            Apply(was);
            Physics.SyncTransforms();
            Plugin.Log.LogInfo($"SailTrim: {_ship.name} gangway {Gangway.SideName(_side)} stows {(bestDir > 0 ? "forward" : "aft")} ({best:0.00} m into the boat)");
        }

        // ------------------------------------------------------------------
        private void Update()
        {
            if (_ship == null || _ship.m_nview == null || !_ship.m_nview.IsValid()) return;
            // The rail height needs physics, which Awake does not have, so it is measured on the first frame.
            EnsureDeck();
            EnsureStep();
            // Pieces of the boat go on arriving for a while after Awake, and any pair we have not struck out is a
            // kinematic body wedged in a floating one.
            if (Time.time >= _ignoreAt && Time.time < _ignoreStop) IgnoreShip();
            // One read of the boat's state, not two: this runs on every mount of every boat in the world.
            int state = Gangway.State(_ship);
            bool fitted = (state & Gangway.FittedBit(_side)) != 0;
            bool down = fitted && (state & Gangway.DownBit(_side)) != 0;

            // Only once something is actually fitted. Every hull but the raft carries these mounts whether it has
            // a gangway or not, and building three sections of walkway, a brow and their baked meshes for every
            // boat in the world is a great deal of work for planks nobody has made yet.
            if (fitted && _visual == null)
            {
                BuildSections();
                if (_visual != null) _visual.SetActive(true);
            }
            if (fitted && _visual != null) BuildLashings();
            if (_visual != null && !_timbered)
            {
                _timbered = true;
                Gangway.SettleMaterials(_mount, _ship);
            }
            if (fitted && _visual != null && _box != null) ChooseStowSide();
            if (fitted != _wasFitted)
            {
                _wasFitted = fitted;
                if (_visual != null) _visual.SetActive(fitted);
                if (_step != null) _step.SetActive(fitted);
                if (_lashing != null) _lashing.SetActive(fitted && _deploy < 0.02f);
                if (!fitted) _deploy = 0f;
                RefreshCollider();
            }
            if (!fitted) { Apply(0f); return; }

            if (down != _wasDown)
            {
                _wasDown = down;
                if (!down) { _anchored = false; _anchorOn = null; _anchorWorld = Vector3.zero; }
                if (down)
                {
                    EnsureDeck();
                    if (FindRest(out float a0, out float g0, out float d0, out Ship _))
                    {
                        PushProbe(g0, true);
                        _restDist = d0;
                        _restAngle = a0;
                    }
                    else { _restDist = 0f; _restAngle = Plugin.GangwayMaxAngle.Value; }
                    _lashIgnoreAt = 0f;
                }
            }
            UpdateLashIgnore(down);

            // Down, the plank follows whatever it rests on: the boat still lifts and rolls on the swell even
            // held at its spot, and a gangway that did not ride with it would hang in the air or sink into the dock.
            if (down && _deploy > 0.5f)
            {
                // Only if there is nothing to hold to: the spot was chosen when it went down. It is looked for
                // again when what it was resting on has gone, a dock taken down or a boat sailed off.
                if (!_anchored || (_anchorOn == null && _anchorWorld == Vector3.zero))
                {
                    if (Time.time >= _nextProbe)
                    {
                        _nextProbe = Time.time + 0.25f;
                        if (FindRest(out float _, out float g, out float d, out Ship _, out Vector3 sp, out Transform on2))
                        {
                            PushProbe(g, false);
                            _restDist = d;
                            SetAnchor(sp, on2);
                        }
                    }
                }
                // Smooth the ground, not the angle. What the plank rests on is a dock or a rock and holds still;
                // the hinge is the thing that moves, a metre at a time on the swell. Smoothing the angle meant the
                // plank held the angle that suited the last wave, so every time the boat dropped it drove its far
                // end into the beach. The ground reading is steadied against a noisy ray, and the angle is worked
                // out afresh from wherever the hinge is this frame.
                if (AnchorPoint(out Vector3 target))
                {
                    // Straight at the spot it was set down on, in the boat's own frame so heel is taken care of.
                    Vector3 v = target - _mount.position;
                    float reach = Vector3.Dot(v, _mount.right);
                    float drop = -Vector3.Dot(v, _mount.up);
                    float want = Mathf.Atan2(drop, Mathf.Max(0.05f, reach)) * Mathf.Rad2Deg;
                    _restAngle = Mathf.Clamp(want + 0.3f, -20f, Mathf.Max(5f, Plugin.GangwayMaxAngle.Value));
                }
                else if (_restDist > 0.1f)
                {
                    float want = Mathf.Asin(Mathf.Clamp((_mount.position.y - ProbeMedian()) / _restDist, -1f, 1f)) * Mathf.Rad2Deg;
                    _restAngle = Mathf.Clamp(want + 0.3f, -20f, Mathf.Max(5f, Plugin.GangwayMaxAngle.Value));
                }
            }

            float span = Mathf.Max(0.2f, Plugin.GangwaySwingTime.Value);
            Apply(Mathf.MoveTowards(_deploy, down ? 1f : 0f, Time.deltaTime / span));
        }
    }
}
