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

        /// <summary>Ask the boat's owner to change a gangway. action: 0 fit, 1 lower, 2 raise.</summary>
        internal static void Request(Ship ship, int side, int action)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid()) return;
            nv.InvokeRPC(RpcName, side, action);
        }

        internal static void OnShipStart(Ship ship)
        {
            var nv = ship.m_nview;
            if (nv == null || !nv.IsValid()) return;
            nv.Register<int, int>(RpcName, (sender, side, action) => RPC_Gangway(ship, side, action));
        }

        /// <summary>Runs on the boat's owner: the boat's ZDO is the one record of what is fitted and what is down.</summary>
        private static void RPC_Gangway(Ship ship, int side, int action)
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
                    break;
                case 2: state &= ~DownBit(side); break;
                default: return;
            }
            zdo.Set(StateHash, state);
        }

        private static readonly Dictionary<Ship, float> _helmTime = new Dictionary<Ship, float>();

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
            public float ZOffset; // metres further aft than the boat's own ladder
        }

        internal static Placement PlacementFor(Ship ship)
        {
            var p = new Placement { Z = Plugin.GangwayMountZ.Value, Inset = 0f, Rise = 0f, Step = true, ZOffset = -0.85f };
            string n = (ship != null ? ship.name : "") ?? "";
            if (n.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) >= 0)
                p.Step = false;   // the Drakkar's own hull already climbs to the rail here
            else if (n.IndexOf("VikingShip", StringComparison.OrdinalIgnoreCase) >= 0) { } // Longship
            else if (n.IndexOf("Karve", StringComparison.OrdinalIgnoreCase) >= 0) { }
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
            var floorSrc = Models.FindFirst(db, out string floorName, "wood_floor", "wood_floor_1x1", "piece_woodfloor");
            if (floorSrc != null)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(length / 2f));
                float seg = length / n;
                for (int k = 0; k < n; k++)
                {
                    var part = Models.CopyVisual(floorSrc, root.transform, "deck" + k, out var b);
                    if (part == null || b.size.x < 0.05f || b.size.z < 0.05f) continue;
                    var scale = new Vector3(seg / b.size.x, 1f, width / b.size.z);
                    Models.Place(part, b, new Vector3(0f, 1f, 0.5f), new Vector3(k * seg, 0f, 0f), scale, Quaternion.identity);
                    built = true;
                }
            }
            var beamSrc = built ? Models.FindFirst(db, out beamName, "wood_beam", "wood_beam_1", "wood_pole") : null;
            if (beamSrc != null)
            {
                const float Kerb = 0.1f;   // a kerb down each edge, not a second plank
                foreach (float sz in new[] { -1f, 1f })
                {
                    var part = Models.CopyVisual(beamSrc, root.transform, sz < 0f ? "edgeL" : "edgeR", out var b);
                    if (part == null) continue;
                    // Lay the beam along the walkway whichever way round it was modelled. Taking the longest
                    // dimension and then always stretching X meant that a beam modelled along Z was squashed flat
                    // and blown out sideways: two fat slabs lying in the surface of the deck, fighting with it for
                    // every pixel.
                    Vector3 sz3 = b.size;
                    int longAxis = (sz3.x >= sz3.y && sz3.x >= sz3.z) ? 0 : (sz3.y >= sz3.z ? 1 : 2);
                    if (sz3[longAxis] < 0.05f) continue;
                    Vector3 scale = new Vector3(Kerb / Mathf.Max(0.01f, sz3.x), Kerb / Mathf.Max(0.01f, sz3.y), Kerb / Mathf.Max(0.01f, sz3.z));
                    scale[longAxis] = length / sz3[longAxis];
                    Quaternion rot = longAxis == 0 ? Quaternion.identity
                                   : longAxis == 1 ? Quaternion.Euler(0f, 0f, -90f)   // +Y along the walkway
                                                   : Quaternion.Euler(0f, 90f, 0f);   // +Z along the walkway
                    // Anchored at the near end of its long axis and centred on the other two, so it stands proud
                    // of the deck rather than lying in it.
                    Vector3 anchor = new Vector3(0.5f, 0.5f, 0.5f);
                    anchor[longAxis] = 0f;
                    Models.Place(part, b, anchor, new Vector3(0f, Kerb * 0.5f - 0.01f, sz * (width * 0.5f - Kerb * 0.5f)), scale, rot);
                }
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
        internal static bool HasKit(Humanoid user)
        {
            var inv = user != null ? user.GetInventory() : null;
            return inv != null && inv.CountItems("Gangway") > 0;
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
        private const float StepWidth = 0.7f;    // narrow enough to keep its outboard edge off the side planking
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
        private float _restVel;
        private bool _wasFitted, _wasDown;
        private Gangway.Placement _place;
        private Vector3 _railLocal;   // where the rail was found, before the per-hull correction
        private BoxCollider _stepCol;
        private Rigidbody _stepRb;
        private float _ignoreAt;
        private float _ignoreStop = float.MaxValue;
        private string _profile = "";  // the shape of the hull the rail was picked out of

        /// <summary>What this mount worked out for itself, for the survey notes.</summary>
        internal string ProfileText() => _profile;

        /// <summary>The walking surface, for the survey to measure against the hull.</summary>
        internal Collider WalkCollider => _box;

        internal Collider StepCollider => _stepCol;

        internal Transform Visual => _visual != null ? _visual.transform : null;

        internal float Deploy => _deploy;

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
                if (c == null || c == _box || c == _stepCol) continue;
                if (_box != null) Physics.IgnoreCollision(_box, c, true);
                if (_stepCol != null) Physics.IgnoreCollision(_stepCol, c, true);
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
            switch (mode)
            {
                case 2: // the plank you walk on
                    _box.center = new Vector3(Length * 0.5f, -0.05f, 0f);
                    _box.size = new Vector3(Length, 0.12f, 0.84f);
                    break;
                case 1: // folded and stowed: one section long, three leaves thick
                    _box.center = new Vector3(Length / 6f, LeafLift, 0f);
                    _box.size = new Vector3(Length / 3f, LeafLift * 2f + 0.16f, 0.84f);
                    break;
                default: // a bare rail: a small post standing above it, clear of the hull
                    _box.center = new Vector3(0.05f, 0.3f, 0f);
                    _box.size = new Vector3(0.5f, 0.66f, 0.9f);
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
        private void Apply(float deploy)
        {
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
            Quaternion stowPose = Quaternion.Euler(0f, -90f * _side, 0f) * Quaternion.Euler(StowLean * _side, 0f, 0f);
            transform.localRotation = Quaternion.Slerp(outPose, stowPose, stow);
            transform.localPosition = Vector3.Lerp(Vector3.zero, StowOffset(), stow);

            // Folded, each leaf stands clear of the one under it. Folded flat about a shared hinge they were three
            // slabs in one plane, and the renderer had to choose between them every pixel: they crawled with z-fighting.
            float folded = 1f - unfold;
            float lift = LeafLift * folded;
            float fold = folded * FoldAngle;
            float seg = Length / 3f;
            if (_seg2 != null) { _seg2.localPosition = new Vector3(seg, lift, 0f); _seg2.localRotation = Quaternion.Euler(0f, 0f, fold); }
            if (_seg3 != null) { _seg3.localPosition = new Vector3(seg, -lift, 0f); _seg3.localRotation = Quaternion.Euler(0f, 0f, -fold); }

            // Only a ramp that is out over the side and unfolded is something to walk on.
            bool walk = deploy > 0.6f;
            if (walk != _walkable) { _walkable = walk; RefreshCollider(); }
        }

        /// <summary>Where the stowed stack sits: inboard of the rail, down on the deck, straddling the mount.</summary>
        private Vector3 StowOffset()
        {
            // Forward of the mount, so the stack and the step are not fighting for the same patch of deck.
            return new Vector3(-StowInset, -_deckDrop + 0.45f, _side * 1.1f);
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

            var s1 = new GameObject("section1");
            s1.transform.SetParent(transform, false);
            if (Gangway.BuildWalkway(s1.transform, "visual", seg, 0.84f, layer) == null)
            { UnityEngine.Object.Destroy(s1); return; }

            var s2 = new GameObject("section2");
            s2.transform.SetParent(s1.transform, false);
            s2.transform.localPosition = new Vector3(seg, 0f, 0f);
            Gangway.BuildWalkway(s2.transform, "visual", seg, 0.84f, layer);

            var s3 = new GameObject("section3");
            s3.transform.SetParent(s2.transform, false);
            s3.transform.localPosition = new Vector3(seg, 0f, 0f);
            Gangway.BuildWalkway(s3.transform, "visual", seg, 0.84f, layer);

            _visual = s1; _seg2 = s2.transform; _seg3 = s3.transform;
        }

        /// <summary>Where the far end of the plank would be at this angle.</summary>
        private Vector3 TipAt(float angle) => PointAt(Length, angle);

        // ------------------------------------------------------------------
        public string GetHoverName() => "Gangway";
        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            if (_ship == null || !Plugin.GangwayEnabled.Value) return "";
            var p = Player.m_localPlayer;
            if (p == null || !_ship.IsPlayerInBoat(p)) return "";
            bool fitted = Gangway.Fitted(_ship, _side);
            if (!fitted)
            {
                if (!Gangway.HasKit(p)) return Localization.instance.Localize("<color=#888888>Gangway: none in your inventory</color>");
                return Localization.instance.Localize($"Rail ({Gangway.SideName(_side)})\n[<color=yellow><b>$KEY_Use</b></color>] Fit the gangway");
            }
            if (Gangway.Down(_ship, _side))
                return Localization.instance.Localize($"Gangway ({Gangway.SideName(_side)}), down\n[<color=yellow><b>$KEY_Use</b></color>] Raise");
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
            EnsureDeck();
            if (!FindRest(out float angle))
            {
                user.Message(MessageHud.MessageType.Center, "Nothing within reach to rest it on");
                return false;
            }
            _restAngle = angle;
            Gangway.Request(_ship, _side, 1);
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
            Vector3 from = _ship.transform.TransformPoint(new Vector3(localX, aroundY + 6f, localZ));
            var hits = Physics.RaycastAll(from, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
            float top = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider == _box || h.collider == _stepCol) continue;
                if (!h.collider.transform.IsChildOf(_ship.transform)) continue;
                if (h.point.y > top) top = h.point.y;
            }
            if (float.IsNegativeInfinity(top)) return top;
            return _ship.transform.InverseTransformPoint(new Vector3(from.x, top, from.z)).y;
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

        private bool FindRest(out float angle)
        {
            angle = 0f;
            float max = Mathf.Max(5f, Plugin.GangwayMaxAngle.Value);
            // Not the sea, and not people: a gangway rests on something solid, or it does not go down at all.
            int mask = ~LayerMask.GetMask("Water", "WaterVolume", "water", "character", "character_net",
                                          "character_ghost", "character_trigger", "viewblock", "weapon", "smoke");
            // Whatever the plank would touch first, anywhere along its length, is what holds it up. Looking only
            // under the far end, a beam halfway out was something the plank went straight through.
            float shallowest = float.MaxValue;
            for (float d = 1f; d <= Length + 0.01f; d += 0.4f)
            {
                float a = AngleOnto(d, max, mask);
                if (!float.IsNaN(a) && a < shallowest) shallowest = a;
            }
            if (shallowest == float.MaxValue) return false;
            angle = Mathf.Clamp(shallowest, -20f, max);
            return true;
        }

        /// <summary>
        /// The angle at which the plank, this far out from its hinge, would come down onto whatever is under it.
        /// The plank's reach shortens as it tilts, so where it lands moves: two passes settle it.
        /// </summary>
        private float AngleOnto(float d, float max, int mask)
        {
            float a = 0f;
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 p = PointAt(d, a);
                var hits = Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 6f, mask, QueryTriggerInteraction.Ignore);
                float top = float.NegativeInfinity;
                foreach (var h in hits)
                {
                    if (h.collider.transform.IsChildOf(_ship.transform)) continue;  // the boat's own timber, not the shore
                    if (h.point.y > p.y + 0.4f) continue;                            // something overhead, not underfoot
                    if (h.point.y > top) top = h.point.y;
                }
                if (float.IsNegativeInfinity(top)) return float.NaN;
                float next = Mathf.Asin(Mathf.Clamp((_mount.position.y - top) / d, -1f, 1f)) * Mathf.Rad2Deg;
                next = Mathf.Clamp(next, -20f, max);
                if (Mathf.Abs(next - a) < 0.2f) return next;
                a = next;
            }
            return a;
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
            // Where the step comes down, probed along its own line rather than assumed flat. A Karve has no deck
            // at all: you stand on the curve of the hull, which rises as you go inboard, and a straight ramp set
            // from the lowest reading anywhere on the section dug half a metre into the planking.
            float drop = _deckDrop;
            float run = Mathf.Max(0.9f, drop * 2.2f);
            for (int pass = 0; pass < 2; pass++)
            {
                float foot = float.NegativeInfinity;
                for (int i = 1; i <= 3; i++)
                {
                    Vector3 sp = _ship.transform.InverseTransformPoint(
                        _mount.TransformPoint(new Vector3(-StepInset, 0f, -_side * run * i / 3f)));
                    float y = TopOfShip(sp.x, sp.z, _mount.localPosition.y);
                    if (y > foot) foot = y;
                }
                if (float.IsNegativeInfinity(foot)) break;
                drop = Mathf.Clamp(_mount.localPosition.y - foot - StepClear, 0.05f, 1.8f);
                run = Mathf.Max(0.9f, drop * 2.2f);
            }
            float len = Mathf.Sqrt(run * run + drop * drop);
            float pitch = Mathf.Atan2(drop, run) * Mathf.Rad2Deg;
            Plugin.Log.LogInfo($"SailTrim: {_ship.name} gangway {Gangway.SideName(_side)} step drop {drop:0.00} run {run:0.00} (deck drop at the rail was {_deckDrop:0.00})");

            _step = new GameObject("step");
            _step.transform.SetParent(_mount, false);
            _step.layer = gameObject.layer;
            // Along the rail rather than across the boat. Across, it reached most of the way over a Karve's deck
            // and you had to walk round it to get anywhere. Its high end is at the hinge, so you climb it and step
            // straight onto the plank, and it runs the opposite way to where the stowed stack lies.
            _step.transform.localPosition = new Vector3(-StepInset, 0.02f, 0f);
            _step.transform.localRotation = Quaternion.Euler(0f, 90f * _side, 0f) * Quaternion.Euler(0f, 0f, -pitch);

            _stepCol = _step.AddComponent<BoxCollider>();
            _stepCol.center = new Vector3(len * 0.5f, -0.06f, 0f);
            _stepCol.size = new Vector3(len, 0.12f, StepWidth);

            // Its own body, for the same reason the plank has one: anything of ours left in the boat's own compound
            // is part of the boat to the physics engine, and props the hull up the moment it touches the shore.
            _stepRb = _step.AddComponent<Rigidbody>();
            _stepRb.isKinematic = true;
            _stepRb.useGravity = false;
            _stepRb.interpolation = RigidbodyInterpolation.None;
            _step.AddComponent<GangwayFooting>().Ship = _ship;

            // The ramp is the same timber as the plank, laid from the game's own floor pieces.
            Gangway.BuildWalkway(_step.transform, "visual", len, StepWidth, VisualLayer(_ship));
            _step.SetActive(_wasFitted);
            IgnoreShip();
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
            bool fitted = Gangway.Fitted(_ship, _side);
            bool down = fitted && Gangway.Down(_ship, _side);

            if (_visual == null)
            {
                BuildSections();
                if (_visual != null) _visual.SetActive(fitted);
            }
            if (fitted != _wasFitted)
            {
                _wasFitted = fitted;
                if (_visual != null) _visual.SetActive(fitted);
                if (_step != null) _step.SetActive(fitted);
                if (!fitted) _deploy = 0f;
                RefreshCollider();
            }
            if (!fitted) return;

            if (down != _wasDown)
            {
                _wasDown = down;
                if (down)
                {
                    EnsureDeck();
                    if (!FindRest(out float a0)) a0 = Plugin.GangwayMaxAngle.Value;
                    PushProbe(a0, true);
                    _restAngle = a0;
                    _restVel = 0f;
                }
            }

            // Down, the plank follows whatever it rests on: the boat still lifts and rolls on the swell even
            // held at its spot, and a gangway that did not ride with it would hang in the air or sink into the dock.
            if (down && _deploy > 0.5f)
            {
                if (Time.time >= _nextProbe)
                {
                    _nextProbe = Time.time + 0.08f;
                    if (FindRest(out float a)) PushProbe(a, false);
                }
                // A touch beyond where it reads, so the tip sits on what it rests on rather than hovering over it.
                // A little of the tip inside the dock is better than a plank that shivers.
                float want = ProbeMedian() + 1f;
                _restAngle = Mathf.SmoothDamp(_restAngle, want, ref _restVel, 0.35f, 90f, Time.deltaTime);
            }

            float span = Mathf.Max(0.2f, Plugin.GangwaySwingTime.Value);
            Apply(Mathf.MoveTowards(_deploy, down ? 1f : 0f, Time.deltaTime / span));
        }
    }
}
