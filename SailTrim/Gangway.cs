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
        private static Mesh _plankMesh;
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

        /// <summary>Ship.Awake: a mount at each rail, amidships. They show and hide themselves from the boat's state.</summary>
        internal static void OnShipAwake(Ship ship)
        {
            if (!Plugin.GangwayEnabled.Value || !Eligible(ship)) return;
            if (ship.transform.Find("SailTrim_Gangway_P") != null) return;

            var fc = ship.m_floatCollider;
            Vector3 centre = ship.transform.InverseTransformPoint(fc.transform.TransformPoint(fc.center));
            float halfBeam = fc.size.x * 0.5f;
            float halfLen = fc.size.z * 0.5f;
            // Aft of amidships, to keep the plank clear of the mast and its shrouds.
            float z = centre.z - halfLen * 0.2f;

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
                mount.Init(ship, side, go.transform);
            }
            Plugin.Log.LogInfo($"SailTrim: {ship.name} gangway mounts at +-{halfBeam:0.0} m, z {z:0.0}");
        }

        // ------------------------------------------------------------------
        // The item and its recipe
        // ------------------------------------------------------------------
        private static Mesh PlankMesh()
        {
            if (_plankMesh != null) return _plankMesh;
            float len = Mathf.Max(1.5f, Plugin.GangwayLength.Value);
            float halfW = 0.42f;
            var mb = new Models.MeshBuilder();
            // The plank itself, from the hinge outboard along +X, and a batten every half metre to walk on.
            mb.Box(new Vector3(len * 0.5f, -0.04f, 0f), new Vector3(len, 0.08f, halfW * 2f));
            for (float x = 0.35f; x < len - 0.1f; x += 0.5f)
                mb.Box(new Vector3(x, 0.02f, 0f), new Vector3(0.07f, 0.04f, halfW * 1.9f));
            // A rib down each edge so it reads as a gangway and not a floorboard.
            foreach (float sz in new[] { -1f, 1f })
                mb.Box(new Vector3(len * 0.5f, 0.01f, sz * (halfW - 0.03f)), new Vector3(len - 0.06f, 0.06f, 0.06f));
            _plankMesh = mb.Build("SailTrim_Gangway");
            return _plankMesh;
        }

        private static Material WoodMaterial()
        {
            if (_woodMat != null) return _woodMat;
            if (Models.StandardTemplate == null) return null;
            var tex = Models.NoiseTexture(new Color(0.42f, 0.30f, 0.18f), 0.10f, 4711, 0.35f);
            _woodMat = Models.Standard(tex, 0f, 0.12f);
            return _woodMat;
        }

        /// <summary>Hangs the plank model on a mount (needs a world's ObjectDB for the material).</summary>
        internal static GameObject BuildVisual(Transform parent)
        {
            if (Models.Headless || parent == null) return null;
            var existing = parent.Find("visual");
            if (existing != null) return existing.gameObject;
            var mat = WoodMaterial();
            if (mat == null) return null;
            return Models.MeshPart(parent, "visual", PlankMesh(), mat);
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
                shared.m_description = "A hinged plank for a ship's rail. Fit it to either side of any boat but a raft, then lower it to walk ashore with a load that is too heavy to climb with.";
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
                var visual = BuildVisual(_itemPrefab.transform);
                if (visual != null)
                {
                    foreach (var r in visual.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                    // Laid flat and shrunk to sit in the hand and on the ground like an ordinary item.
                    visual.transform.localScale = Vector3.one * 0.32f;
                    visual.transform.localPosition = new Vector3(-Plugin.GangwayLength.Value * 0.16f, 0.05f, 0f);
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
                var wood = db.GetItemPrefab("Wood")?.GetComponent<ItemDrop>();
                var nails = db.GetItemPrefab("BronzeNails")?.GetComponent<ItemDrop>();
                var reqs = new List<Piece.Requirement>();
                if (wood != null) reqs.Add(new Piece.Requirement { m_resItem = wood, m_amount = Mathf.Max(1, Plugin.GangwayWoodCost.Value), m_recover = true });
                if (nails != null && Plugin.GangwayNailCost.Value > 0)
                    reqs.Add(new Piece.Requirement { m_resItem = nails, m_amount = Plugin.GangwayNailCost.Value, m_recover = true });
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
    internal class GangwayMount : MonoBehaviour, Hoverable, Interactable
    {
        private Ship _ship;
        private int _side;
        private Transform _mount;      // the hinge point at the rail
        private BoxCollider _box;
        private GameObject _visual;

        // Degrees below horizontal: 0 is straight out over the side, negative lifts the far end.
        private float StowedAngle => Plugin.GangwayStowAngle.Value;
        private float _angle = -78f;
        private float _restAngle = -78f;
        private bool _deckFound;
        private float _nextProbe;
        private bool _wasFitted, _wasDown;

        private float Length => Mathf.Max(1.5f, Plugin.GangwayLength.Value);

        internal void Init(Ship ship, int side, Transform mount)
        {
            _ship = ship;
            _side = side;
            _mount = mount;
            transform.localPosition = Vector3.zero;
            _box = gameObject.AddComponent<BoxCollider>();
            _box.center = new Vector3(Length * 0.5f, -0.05f, 0f);
            _box.size = new Vector3(Length, 0.12f, 0.84f);
            // The layer the hull uses, so it hovers, blocks and carries a player exactly as the deck does.
            gameObject.layer = HullLayer(ship);
            SetFitted(false);
            Apply(Plugin.GangwayStowAngle.Value);
        }

        /// <summary>
        /// Unfitted, all that is here is a small patch of rail to interact with; fitted, the collider is the plank
        /// you walk on. The object itself always stays active, because its own Update is what watches the boat's state.
        /// </summary>
        private void SetFitted(bool fitted)
        {
            if (fitted)
            {
                _box.center = new Vector3(Length * 0.5f, -0.05f, 0f);
                _box.size = new Vector3(Length, 0.12f, 0.84f);
            }
            else
            {
                _box.center = new Vector3(0.1f, -0.05f, 0f);
                _box.size = new Vector3(0.45f, 0.3f, 0.7f);
            }
            if (_visual != null && _visual.activeSelf != fitted) _visual.SetActive(fitted);
        }

        private static int HullLayer(Ship ship)
        {
            foreach (var c in ship.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger && c.gameObject != ship.gameObject) return c.gameObject.layer;
            int piece = LayerMask.NameToLayer("piece");
            return piece >= 0 ? piece : ship.gameObject.layer;
        }

        private void Apply(float angle)
        {
            _angle = angle;
            transform.localRotation = Quaternion.Euler(0f, 0f, -angle);
        }

        /// <summary>Where the far end of the plank would be at this angle.</summary>
        private Vector3 TipAt(float angle)
        {
            float r = angle * Mathf.Deg2Rad;
            return _mount.TransformPoint(new Vector3(Mathf.Cos(r), -Mathf.Sin(r), 0f) * Length);
        }

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
            Vector3 from = _mount.position + Vector3.up * 4f;
            var hits = Physics.RaycastAll(from, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider == _box || !h.collider.transform.IsChildOf(_ship.transform)) continue;
                if (h.point.y > best) best = h.point.y;
            }
            if (best > float.NegativeInfinity)
            {
                Vector3 p = _mount.position; p.y = best + 0.05f;
                _mount.position = p;
            }
        }

        /// <summary>
        /// The angle the plank comes to rest at: sweep it down from a little above horizontal and take the first
        /// thing its far end meets, which is what a plank let down on its hinge does. Nothing within
        /// GangwayMaxAngle means nothing it could rest on that you could still climb carrying a load.
        /// </summary>
        private bool FindRest(out float angle)
        {
            angle = 0f;
            float max = Mathf.Max(5f, Plugin.GangwayMaxAngle.Value);
            int mask = ~LayerMask.GetMask("character", "character_net", "character_ghost", "character_trigger", "viewblock");
            for (float a = -20f; a <= max + 0.01f; a += 1.5f)
            {
                Vector3 tip = TipAt(a);
                if (Physics.Raycast(tip + Vector3.up * 0.5f, Vector3.down, out var hit, 0.75f, mask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(_ship.transform)) continue; // the boat's own rail, not the shore
                    angle = Mathf.Clamp(a + (tip.y - hit.point.y) * 6f, -20f, max);
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        private void Update()
        {
            if (_ship == null || _ship.m_nview == null || !_ship.m_nview.IsValid()) return;
            bool fitted = Gangway.Fitted(_ship, _side);
            bool down = fitted && Gangway.Down(_ship, _side);

            if (_visual == null)
            {
                _visual = Gangway.BuildVisual(transform);
                if (_visual != null) _visual.SetActive(fitted);
            }
            if (fitted != _wasFitted)
            {
                _wasFitted = fitted;
                SetFitted(fitted);
            }
            if (!fitted) return;

            if (down != _wasDown)
            {
                _wasDown = down;
                if (down) { EnsureDeck(); if (!FindRest(out _restAngle)) _restAngle = Plugin.GangwayMaxAngle.Value; }
            }

            // Down, the plank follows whatever it rests on: the boat still lifts and rolls on the swell even
            // held at its spot, and a gangway that did not ride with it would hang in the air or sink into the dock.
            if (down && Time.time >= _nextProbe)
            {
                _nextProbe = Time.time + 0.1f;
                if (FindRest(out float a)) _restAngle = a;
            }

            float target = down ? _restAngle : StowedAngle;
            float rate = Mathf.Max(5f, Plugin.GangwaySwingRate.Value);
            Apply(Mathf.MoveTowards(_angle, target, rate * Time.deltaTime));
        }
    }
}
