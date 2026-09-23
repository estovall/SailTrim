using System.Collections.Generic;
using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// The cleat: a build piece (hammer, Misc, one bronze) that ties a boat up. Interact with a boat within
    /// CleatRange and the boat is moored: it holds its spot and heading the way an empty boat does in vanilla
    /// (its horizontal speed is bled off every physics step), whether or not anyone is aboard, and a shove from
    /// a creature or a wave does not carry it off. A rope with a little sag runs from the cleat to the hull.
    /// Interact again to untie.
    ///
    /// State: the cleat's ZDO holds the boat's id (SailTrim_Boat); the boat's ZDO holds the cleat's id and the
    /// spot it was tied at (SailTrim_Cleat, SailTrim_MoorPos, SailTrim_MoorYaw), written by the boat's owner on
    /// the SailTrim_Moor RPC. Either side that finds the other gone lets go.
    ///
    /// No asset bundle: the model is a procedural mesh, the rope a simulated line.
    /// </summary>
    internal static class Cleat
    {
        internal const string PrefabName = "SailTrim_Cleat";
        internal const string BoatKey = "SailTrim_Boat";
        /// <summary>The boat this cleat holds, by tag rather than by ZDOID. See <see cref="Tag"/>.</summary>
        internal const string BoatTagKey = "SailTrim_BoatTag";

        /// <summary>Where the rope is made fast: the middle of the horn, in the cleat's own space.</summary>
        internal static readonly Vector3 RopePoint = new Vector3(0f, 0.25f, 0f);

        private static GameObject _root, _prefab;
        private static bool _effectsApplied;
        private static Sprite _icon;

        internal static GameObject Prefab => _prefab;

        /// <summary>An inactive root that survives scene changes: prefabs built under it never run their Awake.</summary>
        internal static Transform EnsureRoot()
        {
            if (_root == null)
            {
                _root = new GameObject("SailTrim_Prefabs");
                _root.SetActive(false);
                Object.DontDestroyOnLoad(_root);
            }
            return _root.transform;
        }

        /// <summary>The prefab, built once and kept in the inactive root.</summary>
        internal static void EnsurePrefab()
        {
            if (_prefab != null) return;
            EnsureRoot();

            int pieceLayer = LayerMask.NameToLayer("piece");
            var go = new GameObject(PrefabName);
            go.transform.SetParent(_root.transform, false);
            go.layer = pieceLayer;

            var nview = go.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;

            var piece = go.AddComponent<Piece>();
            piece.m_name = "Cleat";
            piece.m_description = "A bronze horn cleat for the dock. Tie up a boat within reach and it stays put, crew aboard or not.";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_groundPiece = false;
            piece.m_allowedInDungeons = false;
            piece.m_canBeRemoved = true;
            piece.m_canRotate = true;
            piece.m_noInWater = false;
            piece.m_randomInitBuildRotation = false;
            // On top of a plank or a floor only: not on the side of a beam, and not sunk into anything.
            piece.m_notOnTiltingSurface = true;
            piece.m_noClipping = true;

            var wnt = go.AddComponent<WearNTear>();
            wnt.m_health = 400f;
            wnt.m_materialType = WearNTear.MaterialType.Iron;
            wnt.m_burnable = false;
            wnt.m_noRoofWear = false;   // bronze: no rain wear (true would turn it on)
            wnt.m_noSupportWear = true; // falls with the dock under it
            wnt.m_ashDamageImmune = true;
            wnt.m_supports = false;

            go.AddComponent<CleatPiece>();

            // One box around the whole cleat for hovering, hitting and the placement checks.
            var col = new GameObject("collider");
            col.transform.SetParent(go.transform, false);
            col.layer = pieceLayer;
            var box = col.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.15f, 0f);
            box.size = new Vector3(0.9f, 0.3f, 0.2f);


            _prefab = go;
            Plugin.Log.LogInfo("SailTrim: cleat prefab built.");
        }

        /// <summary>
        /// A horn cleat, 0.9 m across: a foot plate on the planks, two splayed legs, and the horn, thickest in the
        /// middle and tapering to rounded tips. Cast bronze with a little tarnish.
        /// </summary>
        private static void BuildVisual(Transform parent)
        {
            if (Models.Headless || parent.Find("visual") != null || Models.StandardTemplate == null) return;
            var visual = new GameObject("visual");
            visual.transform.SetParent(parent, false);
            visual.layer = parent.gameObject.layer;
            var mb = new Models.MeshBuilder();
            mb.Box(new Vector3(0f, 0.022f, 0f), new Vector3(0.56f, 0.044f, 0.17f));
            mb.Box(new Vector3(0f, 0.05f, 0f), new Vector3(0.46f, 0.02f, 0.13f));
            foreach (float sx in new[] { -1f, 1f })
                mb.Tube(new Vector3(sx * 0.17f, 0.04f, 0f), new Vector3(sx * 0.13f, 0.23f, 0f), t => Mathf.Lerp(0.052f, 0.036f, t), 14, 4, false, false);
            mb.Tube(new Vector3(-0.45f, 0.245f, 0f), new Vector3(0.45f, 0.245f, 0f), t =>
            {
                float u = Mathf.Abs(t * 2f - 1f);
                float r = 0.05f * (1f - 0.5f * u * u);
                // Round the tips off over the last few centimetres.
                float tip = Mathf.Clamp01((1f - u) / 0.05f);
                return r * Mathf.Sqrt(tip);
            }, 18, 24, false, false);
            var mat = Models.Standard(Models.NoiseTexture(new Color(0.72f, 0.46f, 0.22f), 0.35f, 7), 0.85f, 0.5f);
            Models.MeshPart(visual.transform, "cleat", mb.Build("SailTrim_Cleat"), mat);
            // The rope made fast round the horn, one for each end the lead can leave from; shown while a boat is tied.
            foreach (int side in new[] { -1, 1 })
            {
                var wb = new Models.MeshBuilder();
                wb.Sweep(WrapPath(side), WrapRopeRadius, 8);
                var wrap = Models.MeshPart(visual.transform, side < 0 ? "wrapL" : "wrapR", wb.Build("SailTrim_CleatWrap"), null);
                wrap.SetActive(false);
            }
        }

        internal const float WrapRopeRadius = 0.017f;
        private const float HornY = 0.245f, WrapReach = 0.27f;

        /// <summary>The horn's radius at x (the same taper the horn mesh has).</summary>
        private static float HornRadius(float x)
        {
            float u = Mathf.Clamp01(Mathf.Abs(x) / 0.45f);
            return 0.05f * (1f - 0.5f * u * u);
        }

        /// <summary>
        /// A cleat hitch on the horn: two and a half figure-eights, each crossing the top diagonally and passing
        /// under one horn end, then a last turn round the middle. It starts under the horn at the side's end, where
        /// the lead to the boat leaves (side -1 = the left end, +1 = the right). The turns lie a little further out
        /// each time so they sit on each other instead of in each other.
        /// </summary>
        internal static List<Vector3> WrapPath(int side)
        {
            var path = new List<Vector3>();
            const int steps = 160;
            float turns = 2.5f;
            for (int i = 0; i <= steps; i++)
            {
                float t = -Mathf.PI / 2f + i / (float)steps * turns * Mathf.PI * 2f;
                float x = WrapReach * Mathf.Sin(t) * side;
                float phi = Mathf.PI / 2f - 2f * t; // round the horn: top at x = 0, under it at either end
                float layer = i / (float)steps * turns;
                // Snug on the horn; each figure-eight a little outside the one before (the crossings on top stack).
                float r = HornRadius(x) + WrapRopeRadius * (1.05f + 0.55f * layer);
                path.Add(new Vector3(x, HornY + Mathf.Sin(phi) * r, Mathf.Cos(phi) * r * side));
            }
            // The finishing half hitch: one snug turn round the horn beside the crossings, on the lead's side.
            Vector3 last = path[path.Count - 1];
            float hx = 0.17f * side;
            float rHitch = HornRadius(hx) + WrapRopeRadius * 1.1f;
            for (int i = 1; i <= 8; i++)
            {
                float u = i / 8f;
                float phi = Mathf.PI / 2f - u * Mathf.PI * 0.25f;
                path.Add(Vector3.Lerp(last, new Vector3(hx, HornY + Mathf.Sin(phi) * rHitch, Mathf.Cos(phi) * rHitch * side), u));
            }
            for (int i = 1; i <= 28; i++)
            {
                float u = i / 28f;
                float phi = Mathf.PI / 2f - Mathf.PI * 0.25f - u * Mathf.PI * 2f;
                path.Add(new Vector3(hx + 0.04f * side * u, HornY + Mathf.Sin(phi) * rHitch, Mathf.Cos(phi) * rHitch * side));
            }
            return path;
        }

        /// <summary>Where the lead leaves the hitch toward the boat, in the cleat's own space.</summary>
        internal static Vector3 LeadPoint(int side) => WrapPath(side)[0];

        /// <summary>ObjectDB is up (menu or game): cost, icon, and the piece into the hammer.</summary>
        internal static void OnObjectDb(ObjectDB db)
        {
            if (!Plugin.CleatEnabled.Value || db == null) return;
            EnsurePrefab();
            Plugin.Log.LogInfo($"SailTrim: ObjectDB with {db.m_items.Count} items, hammer {(db.GetItemPrefab("Hammer") != null ? "found" : "missing")}, graphics {SystemInfo.graphicsDeviceType}");
            Models.MaybeRenderCandidatePreviews(db);
            // The model needs a material of the game's (see Models.StandardTemplate): built once a world's ObjectDB is up.
            Models.FindStandardTemplate(db);
            BuildVisual(_prefab.transform);
            var piece = _prefab.GetComponent<Piece>();
            var bronze = db.GetItemPrefab("Bronze");
            var drop = bronze != null ? bronze.GetComponent<ItemDrop>() : null;
            if (drop != null)
                piece.m_resources = new[] { new Piece.Requirement { m_resItem = drop, m_amount = Mathf.Max(1, Plugin.CleatCost.Value), m_recover = true } };
            if (_icon == null && _prefab.transform.Find("visual") != null)
            {
                var visual = _prefab.transform.Find("visual");
                _icon = visual != null ? Models.RenderIcon(visual.gameObject, "icon_cleat", 256, 150f, 32f) : null;

            }
            piece.m_icon = _icon != null ? _icon : (drop != null ? drop.m_itemData.GetIcon() : piece.m_icon);
            var hammer = db.GetItemPrefab("Hammer");
            var table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            MaybeRenderTiedPreview();
            if (table == null) return; // the main menu: the pieces go in when a world's ObjectDB is up
            if (!table.m_pieces.Contains(_prefab)) table.m_pieces.Add(_prefab);
        }

        private static bool _tiedPreviewDone;

        /// <summary>A render of the cleat tied up, to BepInEx\cache\SailTrim, once per session (needs the model and the world's rope material).</summary>
        private static void MaybeRenderTiedPreview()
        {
            if (_tiedPreviewDone || _prefab == null || Models.Headless) return;
            var visual = _prefab.transform.Find("visual");
            var wrapT = visual != null ? visual.Find("wrapR") : null;
            var ropeMat = CleatPiece.RopeMaterial();
            if (wrapT == null || ropeMat == null) return;
            _tiedPreviewDone = true;
            wrapT.GetComponent<MeshRenderer>().sharedMaterial = ropeMat;
            wrapT.gameObject.SetActive(true);
            Models.RenderIcon(visual.gameObject, "preview_cleat_tied", 256, 150f, 32f);
            Models.RenderIcon(visual.gameObject, "preview_cleat_tied_side", 256, 90f, 10f);
            wrapT.gameObject.SetActive(false);
        }

        /// <summary>
        /// A cleat in reach with no boat on it takes this one. Lowering a gangway alongside a dock ties the boat
        /// up as well, so one press does the whole job.
        /// </summary>
        internal static bool AutoTie(Ship ship)
        {
            if (!Plugin.CleatEnabled.Value || ship == null) return false;
            CleatPiece best = null;
            float bestD = Plugin.CleatRange.Value;
            foreach (var c in Object.FindObjectsByType<CleatPiece>(FindObjectsSortMode.None))
            {
                if (c == null || !c.HasNoBoat) continue;
                float d = c.DistanceToHull(ship);
                if (d < bestD) { bestD = d; best = c; }
            }
            if (best == null) return false;
            best.TieTo(ship);
            return true;
        }

        /// <summary>ZNetScene is up (a world is loading): the prefab must be known before any cleat ZDO arrives.</summary>
        internal static void OnZNetScene(ZNetScene scene)
        {
            if (!Plugin.CleatEnabled.Value || scene == null) return;
            EnsurePrefab();
            int hash = PrefabName.GetStableHashCode();
            if (!scene.m_prefabs.Contains(_prefab)) scene.m_prefabs.Add(_prefab);
            if (!scene.m_namedPrefabs.ContainsKey(hash)) scene.m_namedPrefabs.Add(hash, _prefab);
            MaybeRenderTiedPreview();
            if (!_effectsApplied)
            {
                // Placement, hit and break effects of an iron piece where there is one, else a wooden chest's.
                var src = scene.GetPrefab("iron_grate") ?? scene.GetPrefab("piece_chest_wood");
                if (src != null)
                {
                    var cp = src.GetComponent<Piece>(); var cw = src.GetComponent<WearNTear>();
                    var piece = _prefab.GetComponent<Piece>(); var wnt = _prefab.GetComponent<WearNTear>();
                    if (cp != null) piece.m_placeEffect = cp.m_placeEffect;
                    if (cw != null) { wnt.m_hitEffect = cw.m_hitEffect; wnt.m_destroyedEffect = cw.m_destroyedEffect; }
                    _effectsApplied = true;
                }
            }
        }
    }

    /// <summary>The placed cleat. Tie and untie, the rope, and letting go of a boat that is gone.</summary>
    /// <summary>
    /// A number on an object that survives a world load. A ZDOID does not: `ZDO.Load` hands every ZDO a fresh
    /// one, so anything saved that points at another object by ZDOID points at nothing after a restart, or worse
    /// at whatever now holds that id. That is why a tied boat came back untied while the gangway, which keeps its
    /// state as a plain number on the boat's own ZDO, came back as it was left. Both ends of a link keep the
    /// other's tag, and the ZDOIDs are put back from them when the world returns.
    /// </summary>
    internal static class Tag
    {
        internal const string Key = "SailTrim_Tag";

        internal static long Of(ZNetView nv)
        {
            if (nv == null || !nv.IsValid()) return 0L;
            var zdo = nv.GetZDO();
            long id = zdo.GetLong(Key, 0L);
            if (id != 0L) return id;
            if (!nv.IsOwner()) return 0L;
            id = ((long)UnityEngine.Random.Range(int.MinValue, int.MaxValue) << 32)
               ^ (uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            if (id == 0L) id = 1L;
            zdo.Set(Key, id);
            return id;
        }

        internal static long Read(ZNetView nv)
        {
            if (nv == null || !nv.IsValid()) return 0L;
            return nv.GetZDO().GetLong(Key, 0L);
        }
    }

    internal class CleatPiece : MonoBehaviour, Hoverable, Interactable
    {
        private ZNetView _nview;
        private float _checkTimer, _tieTime, _relinkTimer;
        private static readonly List<CleatPiece> _all = new List<CleatPiece>();

        internal ZNetView View => _nview;

        private void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
        private void OnDisable() { _all.Remove(this); }

        /// <summary>The loaded cleat carrying this tag, if its zone is up.</summary>
        internal static CleatPiece ByTag(long tag)
        {
            if (tag == 0L) return null;
            foreach (var c in _all)
                if (c != null && Tag.Read(c._nview) == tag) return c;
            return null;
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
        }

        private ZDOID Boat
        {
            get { var z = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null; return z != null ? z.GetZDOID(Cleat.BoatKey) : ZDOID.None; }
        }

        private Vector3 Top => transform.TransformPoint(Cleat.RopePoint);

        // The hitch on the horn: the wrap on the end nearer the boat is shown, and the rope leads from it.
        private GameObject _wrapL, _wrapR;
        private int _wrapSide;

        private void ShowWrap(int side)
        {
            if (_wrapL == null && _wrapR == null)
            {
                var l = transform.Find("visual/wrapL"); var r = transform.Find("visual/wrapR");
                _wrapL = l != null ? l.gameObject : null; _wrapR = r != null ? r.gameObject : null;
                var mat = RopeMaterial();
                foreach (var w in new[] { _wrapL, _wrapR })
                    if (w != null && mat != null) w.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            if (side == _wrapSide) return;
            _wrapSide = side;
            if (_wrapL != null && _wrapL.activeSelf != (side < 0)) _wrapL.SetActive(side < 0);
            if (_wrapR != null && _wrapR.activeSelf != (side > 0)) _wrapR.SetActive(side > 0);
        }

        // ------------------------------------------------------------------
        public string GetHoverName() => "Cleat";
        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            if (_nview == null || !_nview.IsValid()) return "Cleat";
            var boat = Boat;
            if (!boat.IsNone())
            {
                var ship = ShipOf(boat);
                return Localization.instance.Localize("Cleat: " + ShipName(ship) + " tied up\n[<color=yellow><b>$KEY_Use</b></color>] Untie");
            }
            var near = NearestShip(out float d);
            if (near == null) return Localization.instance.Localize($"Cleat\nNo boat within {Plugin.CleatRange.Value:0} m");
            return Localization.instance.Localize("Cleat\n[<color=yellow><b>$KEY_Use</b></color>] Tie up " + ShipName(near) + $" ({d:0.0} m)");
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _nview == null || !_nview.IsValid()) return false;
            var boat = Boat;
            if (!boat.IsNone())
            {
                Untie(user);
                return true;
            }
            var ship = NearestShip(out _);
            if (ship == null)
            {
                user.Message(MessageHud.MessageType.Center, $"No boat within {Plugin.CleatRange.Value:0} m");
                return false;
            }
            Tie(ship, user);
            return true;
        }

        internal bool HasNoBoat => Boat.IsNone();

        /// <summary>How near this cleat the boat's hull comes.</summary>
        internal float DistanceToHull(Ship ship)
        {
            if (ship == null) return float.MaxValue;
            Vector3 from = Top;
            float d = float.MaxValue;
            foreach (var c in ship.GetComponentsInChildren<Collider>())
                if (HullCollider(ship, c)) d = Mathf.Min(d, Vector3.Distance(c.ClosestPoint(from), from));
            if (d == float.MaxValue) d = Vector3.Distance(ship.transform.position, from);
            return d;
        }

        /// <summary>Tie up with nobody's hands on it (a gangway going down alongside).</summary>
        internal void TieTo(Ship ship) => Tie(ship, null);

        private void Tie(Ship ship, Humanoid user)
        {
            var shipView = ship.m_nview;
            if (shipView == null || !shipView.IsValid()) return;
            if (!Mooring.SlowEnough(ship, user)) return;
            _nview.ClaimOwnership();
            _nview.GetZDO().Set(Cleat.BoatKey, shipView.GetZDO().m_uid);
            // And by tag, which is what will still mean something after a restart.
            _nview.GetZDO().Set(Cleat.BoatTagKey, Tag.Of(shipView));
            Tag.Of(_nview);
            _tieTime = Time.time;
            // To the boat's owner, who runs its physics and writes its ZDO.
            shipView.InvokeRPC(Mooring.RpcName, _nview.GetZDO().m_uid, true);
            if (user != null) user.Message(MessageHud.MessageType.TopLeft, "Tied up " + Localization.instance.Localize(ShipName(ship)));
        }

        /// <summary>The boat's pilot took the helm: let go of it from this end (no message here; the pilot gets one).</summary>
        internal void UntieFromBoat()
        {
            if (_nview == null || !_nview.IsValid() || Boat.IsNone()) return;
            _nview.ClaimOwnership();
            _nview.GetZDO().Set(Cleat.BoatKey, ZDOID.None);
            HideRope();
        }

        private void Untie(Humanoid user)
        {
            var boat = Boat;
            _nview.ClaimOwnership();
            _nview.GetZDO().Set(Cleat.BoatKey, ZDOID.None);
            var shipZdo = ZDOMan.instance.GetZDO(boat);
            if (shipZdo != null) ZRoutedRpc.instance.InvokeRoutedRPC(shipZdo.GetOwner(), boat, Mooring.RpcName, _nview.GetZDO().m_uid, false);
            if (user != null) user.Message(MessageHud.MessageType.TopLeft, "Untied");
            HideRope();
        }

        // ------------------------------------------------------------------
        private void Update()
        {
            if (_nview == null || !_nview.IsValid()) { HideRope(); return; }
            var boat = Boat;
            if (boat.IsNone()) { HideRope(); return; }
            _relinkTimer -= Time.deltaTime;
            if (_relinkTimer <= 0f) { _relinkTimer = 2f; Relink(); }
            _checkTimer -= Time.deltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = 1f;
                // The boat says it is tied to another cleat now, so let go of it. A boat whose data has not
                // reached us is NOT a reason to let go: right after a world loads nothing is here yet, and up to
                // 1.6.1 every cleat untied itself in those first seconds, which is why moorings did not survive a
                // restart.
                var shipZdo = ZDOMan.instance.GetZDO(boat);
                if (shipZdo != null && shipZdo.GetZDOID(Mooring.CleatKey) != _nview.GetZDO().m_uid)
                {
                    // Only one client should take this on; the one nearest the cleat does.
                    var p = Player.m_localPlayer;
                    if (p != null && Vector3.Distance(p.transform.position, transform.position) < 40f)
                    {
                        // A boat freshly tied has not had its owner write the cleat id yet: give it a moment.
                        if (Time.time - _tieTime > 5f) { Untie(null); return; }
                    }
                }
            }
        }

        private void LateUpdate()
        {
            if (_nview == null || !_nview.IsValid() || Models.Headless) return;
            var boat = Boat;
            var ship = boat.IsNone() ? null : ShipOf(boat);
            if (ship == null) { HideRope(); return; }
            DrawRope(ship);
        }

        // ------------------------------------------------------------------
        // The rope: a chain of points under gravity with fixed ends, kept at its length and pushed out of anything
        // solid (the dock, the hull, the ground), drawn as a line. The ship end is made fast at the point of the
        // hull nearest the cleat, found again every half second and carried with the ship in between.
        // ------------------------------------------------------------------
        private const int RopePoints = 20;
        private const float RopeRadius = 0.019f;
        private LineRenderer _rope;
        private Vector3[] _pts, _prev;
        private SphereCollider _probe;
        private readonly Collider[] _hits = new Collider[16];
        private static int _ropeMask;
        private static Material _ropeMaterial;
        private Ship _anchorShip;
        private Vector3 _anchorLocal;
        private float _anchorTimer;

        private void DrawRope(Ship ship)
        {
            if (_rope == null) BuildRope();
            Vector3 b = ShipEnd(ship, Top);
            // The lead leaves from the end of the horn nearer the boat (with a little hold, so it does not flick
            // from one end to the other when the boat lies nearly square to the cleat).
            float lx = transform.InverseTransformPoint(b).x;
            int side = _wrapSide == 0 ? (lx < 0f ? -1 : 1) : (Mathf.Abs(lx) > 0.3f ? (lx < 0f ? -1 : 1) : _wrapSide);
            ShowWrap(side);
            Vector3 a = transform.TransformPoint(Cleat.LeadPoint(side));
            var cam = Camera.main;
            bool near = cam == null || Vector3.Distance(cam.transform.position, a) < 70f;
            if (near) Simulate(a, b);
            else Hang(a, b);
            _rope.SetPositions(_pts);
            if (!_rope.enabled) _rope.enabled = true;
        }

        private void BuildRope()
        {
            var go = new GameObject("SailTrim_Rope");
            go.transform.SetParent(transform, false);
            _rope = go.AddComponent<LineRenderer>();
            _rope.useWorldSpace = true;
            _rope.positionCount = RopePoints;
            _rope.widthMultiplier = RopeRadius * 2f;
            _rope.numCapVertices = 3;
            _rope.numCornerVertices = 2;
            _rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _rope.receiveShadows = true;
            _rope.generateLightingData = true;
            _rope.alignment = LineAlignment.View;
            _rope.textureMode = LineTextureMode.Tile;
            _rope.sharedMaterial = RopeMaterial();
            var probeGo = new GameObject("SailTrim_RopeProbe");
            probeGo.transform.SetParent(transform, false);
            probeGo.layer = 2; // Ignore Raycast
            _probe = probeGo.AddComponent<SphereCollider>();
            _probe.radius = RopeRadius;
            _probe.isTrigger = true;
            if (_ropeMask == 0) _ropeMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
        }

        private void HideRope()
        {
            if (_rope != null && _rope.enabled) _rope.enabled = false;
            _pts = null;
            if (_wrapSide != 0) ShowWrap(0);
        }

        /// <summary>The ship end of the rope: the hull point nearest the cleat, kept in the ship's own space.</summary>
        private Vector3 ShipEnd(Ship ship, Vector3 from)
        {
            _anchorTimer -= Time.deltaTime;
            if (_anchorShip != ship || _anchorTimer <= 0f)
            {
                _anchorTimer = 0.5f;
                _anchorShip = ship;
                Vector3 best = ship.transform.position + Vector3.up * 0.8f; float bestD = float.MaxValue;
                foreach (var c in ship.GetComponentsInChildren<Collider>())
                {
                    if (!HullCollider(ship, c)) continue;
                    Vector3 p = c.ClosestPoint(from);
                    float d = (p - from).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = p; }
                }
                // A hand's breadth off the planking, so the rope lies against the hull rather than in it.
                Vector3 outward = from - best; outward.y = 0f;
                if (outward.sqrMagnitude > 1e-4f) best += outward.normalized * 0.03f;
                _anchorLocal = ship.transform.InverseTransformPoint(best);
            }
            return ship.transform.TransformPoint(_anchorLocal);
        }

        /// <summary>
        /// Put our end of the link back after a world load, from the boat's tag. Until this runs the stored
        /// ZDOID is a number from the last session and means nothing.
        /// </summary>
        private void Relink()
        {
            if (_nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            long want = zdo.GetLong(Cleat.BoatTagKey, 0L);
            if (want == 0L) return;
            ZDOID have = zdo.GetZDOID(Cleat.BoatKey);
            if (!have.IsNone() && ZDOMan.instance != null)
            {
                var hz = ZDOMan.instance.GetZDO(have);
                if (hz != null && hz.GetLong(Tag.Key, 0L) == want) return;   // still pointing at the right boat
            }
            foreach (var ship in Object.FindObjectsByType<Ship>(FindObjectsSortMode.None))
            {
                var sv = ship.m_nview;
                if (sv == null || !sv.IsValid() || Tag.Read(sv) != want) continue;
                if (!_nview.IsOwner()) _nview.ClaimOwnership();
                if (!_nview.IsOwner()) return;
                zdo.Set(Cleat.BoatKey, sv.GetZDO().m_uid);
                _tieTime = Time.time;   // give the boat a moment to put its own end back
                Plugin.Log.LogInfo("SailTrim: cleat found its boat again after a reload");
                return;
            }
        }

        private static bool HullCollider(Ship ship, Collider c)
        {
            if (c == null || !c.enabled || c.isTrigger) return false;
            if (ship.m_mastObject != null && c.transform.IsChildOf(ship.m_mastObject.transform)) return false;
            // Not the boat's own fittings. A lowered gangway reaches for the dock, so it is nearer the cleat than
            // the hull is, and the rope was being made fast to it instead of to the boat.
            if (c.GetComponentInParent<GangwayPart>() != null) return false;
            if (c is MeshCollider mc) return mc.convex;
            return c is BoxCollider || c is SphereCollider || c is CapsuleCollider;
        }

        private void Hang(Vector3 a, Vector3 b)
        {
            EnsurePoints(a, b, true);
        }

        private void EnsurePoints(Vector3 a, Vector3 b, bool reset)
        {
            if (_pts == null) { _pts = new Vector3[RopePoints]; _prev = new Vector3[RopePoints]; reset = true; }
            if (!reset && (_pts[0] - a).sqrMagnitude < 9f && (_pts[RopePoints - 1] - b).sqrMagnitude < 9f) return;
            float len = Vector3.Distance(a, b);
            float sag = Mathf.Clamp(len * 0.08f, 0.04f, 0.6f);
            for (int i = 0; i < RopePoints; i++)
            {
                float t = i / (float)(RopePoints - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= sag * 4f * t * (1f - t);
                _pts[i] = p; _prev[i] = p;
            }
        }

        private void Simulate(Vector3 a, Vector3 b)
        {
            EnsurePoints(a, b, false);
            int n = RopePoints;
            float straight = Vector3.Distance(a, b);
            float seg = (straight * 1.03f + 0.1f) / (n - 1); // a little sag, never slack enough to drag in the water
            float dt = Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f);
            Vector3 g = Physics.gravity * (dt * dt);
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 cur = _pts[i];
                Vector3 vel = (cur - _prev[i]) * 0.9f;
                _prev[i] = cur;
                _pts[i] = cur + vel + g;
            }
            _pts[0] = a; _pts[n - 1] = b;
            for (int iter = 0; iter < 14; iter++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 d = _pts[i + 1] - _pts[i];
                    float len = d.magnitude;
                    if (len < 1e-5f) continue;
                    float w0 = i == 0 ? 0f : 1f, w1 = i + 1 == n - 1 ? 0f : 1f;
                    float wsum = w0 + w1;
                    if (wsum <= 0f) continue;
                    Vector3 corr = d * ((len - seg) / len / wsum);
                    _pts[i] += corr * w0;
                    _pts[i + 1] -= corr * w1;
                }
                if (iter % 3 == 2 || iter == 13) Collide();
            }
        }

        /// <summary>Every inner point out of whatever solid it sits in (not the cleat itself, which the rope is tied round).</summary>
        private void Collide()
        {
            for (int i = 1; i < RopePoints - 1; i++)
            {
                Vector3 p = _pts[i];
                int count = Physics.OverlapSphereNonAlloc(p, RopeRadius, _hits, _ropeMask, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < count; k++)
                {
                    var c = _hits[k];
                    if (c == null || c.transform.IsChildOf(transform)) continue;
                    if (Physics.ComputePenetration(_probe, p, Quaternion.identity, c, c.transform.position, c.transform.rotation, out Vector3 dir, out float dist))
                        p += dir * (dist + 0.002f);
                }
                _pts[i] = p;
            }
        }

        internal static Material RopeMaterial()
        {
            if (_ropeMaterial != null) return _ropeMaterial;
            // The cart's rope, or any ship's; else a plain hemp-coloured one. Nothing is decided before the world's
            // prefabs are up (the ObjectDB comes first).
            Material src = null;
            var scene = ZNetScene.instance;
            if (scene == null) return null;
            if (scene != null)
                foreach (var name in new[] { "Cart", "Karve", "VikingShip", "Raft" })
                {
                    var prefab = scene.GetPrefab(name);
                    if (prefab == null) continue;
                    foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    {
                        foreach (var m in r.sharedMaterials)
                            if (m != null && (m.name ?? "").ToLowerInvariant().Contains("rope")) { src = m; break; }
                        if (src != null) break;
                    }
                    if (src != null) break;
                }
            if (src != null)
            {
                _ropeMaterial = new Material(src);
                Plugin.Log.LogInfo("SailTrim: rope material " + src.name + " (" + (src.shader != null ? src.shader.name : "?") + ")");
            }
            else
            {
                _ropeMaterial = Models.Standard(Models.NoiseTexture(new Color(0.5f, 0.4f, 0.27f), 0.4f, 11, 0.8f), 0f, 0.05f);
                if (_ropeMaterial == null) { var sh = Shader.Find("Sprites/Default"); if (sh != null) _ropeMaterial = new Material(sh) { color = new Color(0.45f, 0.36f, 0.24f) }; }
                Plugin.Log.LogInfo("SailTrim: no rope material found in the game; using a plain one");
            }
            return _ropeMaterial;
        }

        // ------------------------------------------------------------------
        private static Ship ShipOf(ZDOID id)
        {
            var scene = ZNetScene.instance;
            if (scene == null || id.IsNone()) return null;
            var go = scene.FindInstance(id);
            return go != null ? go.GetComponent<Ship>() : null;
        }

        private static string ShipName(Ship ship)
        {
            if (ship == null) return "boat";
            var piece = ship.GetComponent<Piece>();
            return piece != null && !string.IsNullOrEmpty(piece.m_name) ? piece.m_name : "boat";
        }

        /// <summary>The boat whose hull comes nearest the cleat, within CleatRange.</summary>
        private Ship NearestShip(out float dist)
        {
            Ship best = null; dist = Plugin.CleatRange.Value;
            Vector3 from = Top;
            foreach (var ship in Object.FindObjectsByType<Ship>(FindObjectsSortMode.None))
            {
                if (ship == null || ship.m_nview == null || !ship.m_nview.IsValid()) continue;
                float d = float.MaxValue;
                foreach (var c in ship.GetComponentsInChildren<Collider>())
                    if (HullCollider(ship, c)) d = Mathf.Min(d, Vector3.Distance(c.ClosestPoint(from), from));
                if (d == float.MaxValue) d = Vector3.Distance(ship.transform.position, from);
                if (d < dist) { dist = d; best = ship; }
            }
            return best;
        }
    }

    /// <summary>The boat's side of a mooring: the RPC, the ZDO keys and the hold applied on the owner every physics step.</summary>
    internal static class Mooring
    {
        internal const string RpcName = "SailTrim_Moor";
        internal const string CleatKey = "SailTrim_Cleat";
        internal const string CleatTagKey = "SailTrim_CleatTag";
        private static readonly int PosHash = "SailTrim_MoorPos".GetStableHashCode();
        private static readonly int YawHash = "SailTrim_MoorYaw".GetStableHashCode();

        private static readonly Dictionary<Ship, float> _checkAt = new Dictionary<Ship, float>();
        private static readonly Dictionary<Ship, float> _repairAt = new Dictionary<Ship, float>();
        /// <summary>Ships whose cleat has not been seen lately, and since when (see FixedStep).</summary>
        private static readonly Dictionary<Ship, float> _cleatMissingSince = new Dictionary<Ship, float>();
        // Boats still carrying their way off, and when they are expected to be still. A boat that stops dead the
        // instant a line goes on looks like it hit a wall; a real one carries her way and settles where she ends.
        private static readonly Dictionary<Ship, float> _settleUntil = new Dictionary<Ship, float>();
        private static readonly HashSet<Ship> _wasHeld = new HashSet<Ship>();
        private const float RepairStep = 5f;
        private static float _messageTime;

        internal static void OnShipStart(Ship ship)
        {
            var nv = ship.m_nview;
            if (nv == null || !nv.IsValid()) return;
            nv.Register<ZDOID, bool>(RpcName, (sender, cleat, tie) => RPC_Moor(ship, cleat, tie));
        }

        /// <summary>Runs on the boat's owner: the boat's ZDO records the cleat and the spot it was tied at.</summary>
        private static void RPC_Moor(Ship ship, ZDOID cleat, bool tie)
        {
            var nv = ship.m_nview;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var zdo = nv.GetZDO();
            if (tie)
            {
                zdo.Set(CleatKey, cleat);
                var cz = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(cleat) : null;
                if (cz != null) zdo.Set(CleatTagKey, cz.GetLong(Tag.Key, 0L));
                Tag.Of(nv);
                zdo.Set(PosHash, ship.m_body.position);
                zdo.Set(YawHash, ship.transform.eulerAngles.y);
                ship.m_speed = Ship.Speed.Stop;
                var st = SailTrimShip.Get(ship);
                if (st != null) st.OnMoored();
            }
            else
            {
                zdo.Set(CleatKey, ZDOID.None);
                zdo.Set(CleatTagKey, 0L);
            }
        }

        /// <summary>
        /// The boat's end of the same repair: find the cleat by the tag we kept and write its new ZDOID back.
        /// A cleat whose zone is not loaded is not a cleat that has gone; the grace in FixedStep covers that.
        /// </summary>
        internal static void Relink(Ship ship)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var zdo = nv.GetZDO();
            long want = zdo.GetLong(CleatTagKey, 0L);
            if (want == 0L) return;
            ZDOID have = zdo.GetZDOID(CleatKey);
            if (!have.IsNone() && ZDOMan.instance != null)
            {
                var hz = ZDOMan.instance.GetZDO(have);
                if (hz != null && hz.GetLong(Tag.Key, 0L) == want) return;
            }
            var piece = CleatPiece.ByTag(want);
            var pv = piece != null ? piece.View : null;
            if (pv == null || !pv.IsValid()) return;
            zdo.Set(CleatKey, pv.GetZDO().m_uid);
            Plugin.Log.LogInfo("SailTrim: " + ship.name + " found its cleat again after a reload");
        }

        /// <summary>
        /// Is she slow enough to be made fast? Holding a boat that still has way on her stops her against a wall,
        /// however gently the hold is applied afterwards, and a gangway put down from a moving boat is aimed at
        /// something it will no longer be over. Both the line and the plank want her nearly still.
        /// </summary>
        internal static bool SlowEnough(Ship ship, Humanoid user)
        {
            float limit = Plugin.MooringMaxSpeed.Value;
            if (limit <= 0f || ship == null || ship.m_body == null) return true;
            float kn = ship.m_body.linearVelocity.magnitude * 1.94384f;
            if (kn <= limit) return true;
            if (user != null)
                user.Message(MessageHud.MessageType.Center, $"Too much way on ({kn:0.0} kn): take it off first");
            return false;
        }

        /// <summary>Owner: remember the spot to hold the boat at (tying up, or a gangway going down).</summary>
        /// <summary>Move the spot a held boat is kept at (owner only): warping her along a line.</summary>
        internal static void SetHold(Ship ship, Vector3 pos, float yaw)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var zdo = nv.GetZDO();
            zdo.Set(PosHash, pos);
            zdo.Set(YawHash, yaw);
        }

        internal static bool GetHold(Ship ship, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f;
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid()) return false;
            var zdo = nv.GetZDO();
            pos = zdo.GetVec3(PosHash, Vector3.zero);
            yaw = zdo.GetFloat(YawHash, ship.transform.eulerAngles.y);
            return pos != Vector3.zero;
        }

        internal static void HoldHere(Ship ship)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || ship.m_body == null) return;
            var zdo = nv.GetZDO();
            zdo.Set(PosHash, ship.m_body.position);
            zdo.Set(YawHash, ship.transform.eulerAngles.y);
        }

        internal static bool IsMoored(Ship ship)
        {
            var nv = ship != null ? ship.m_nview : null;
            if (nv == null || !nv.IsValid()) return false;
            return !nv.GetZDO().GetZDOID(CleatKey).IsNone();
        }

        /// <summary>Owner, after vanilla's step: hold the boat at its spot. Nothing when it is not moored.</summary>
        internal static void FixedStep(Ship ship, float dt)
        {
            var nv = ship.m_nview;
            var zdo = nv.GetZDO();
            // Before anything trusts the stored id: after a world load it is a number from the last session.
            if (Time.frameCount % 60 == 0) Relink(ship);
            ZDOID cleat = zdo.GetZDOID(CleatKey);
            // A gangway down holds the boat too, so it cannot be shoved out from under someone walking across,
            // and so does another boat's gangway lying across this one: the plank holds both ends of the raft or
            // it holds neither. That is all either does: a gangway is not a mooring and does not mend the hull.
            bool byGangway = Gangway.AnyDown(ship) || Gangway.LashedAlongside(ship);
            bool byCleat = !cleat.IsNone();
            if (!byCleat && !byGangway) { _wasHeld.Remove(ship); _settleUntil.Remove(ship); return; }
            if (byCleat && (!_checkAt.TryGetValue(ship, out float at) || Time.time > at))
            {
                _checkAt[ship] = Time.time + 1f;
                var cz = ZDOMan.instance.GetZDO(cleat);
                if (cz != null)
                {
                    _cleatMissingSince.Remove(ship);
                    // It no longer claims this boat: let go.
                    if (cz.GetZDOID(Cleat.BoatKey) != zdo.m_uid) { zdo.Set(CleatKey, ZDOID.None); byCleat = false; }
                }
                else
                {
                    // The cleat's data is not here. Just after a world loads that is normal and the mooring must
                    // hold; a cleat that has really been broken never comes back, so give it half a minute.
                    if (!_cleatMissingSince.TryGetValue(ship, out float since)) { _cleatMissingSince[ship] = Time.time; }
                    else if (Time.time - since > 30f)
                    {
                        _cleatMissingSince.Remove(ship);
                        zdo.Set(CleatKey, ZDOID.None);
                        byCleat = false;
                    }
                }
                if (!byCleat && !byGangway) { _wasHeld.Remove(ship); _settleUntil.Remove(ship); return; }
            }
            var body = ship.m_body;
            if (body == null) return;
            // Tied up, the crew sees to the hull: a few percent of its health back every minute, in small steps.
            if (byCleat && Plugin.MoorRepairPerMinute.Value > 0f && (!_repairAt.TryGetValue(ship, out float rAt) || Time.time > rAt))
            {
                _repairAt[ship] = Time.time + RepairStep;
                var wnt = ship.GetComponent<WearNTear>();
                if (wnt != null)
                {
                    float max = wnt.m_health;
                    float h = zdo.GetFloat(ZDOVars.s_health, max);
                    if (h < max)
                    {
                        h = Mathf.Min(max, h + max * Plugin.MoorRepairPerMinute.Value * 0.01f * (RepairStep / 60f));
                        zdo.Set(ZDOVars.s_health, h);
                        nv.InvokeRPC(ZNetView.Everybody, "RPC_HealthChanged", h);
                    }
                }
            }
            ship.m_speed = Ship.Speed.Stop;
            ship.m_rudderValue = 0f;

            // Just made fast: let her run on and lose it rather than stopping against a wall. The spot she is
            // held at does NOT move with her while she settles: a gangway is aimed at something before it goes
            // down, and a boat allowed to drift while it lowers would put the plank in the water. She is under
            // two knots to have got here at all, so the pull back to the spot is centimetres.
            if (!_wasHeld.Contains(ship))
            {
                _wasHeld.Add(ship);
                float settle = Mathf.Max(0f, Plugin.MooringSettle.Value);
                if (settle > 0f) _settleUntil[ship] = Time.time + settle;
            }
            bool settling = false;
            if (_settleUntil.TryGetValue(ship, out float until))
            {
                if (Time.time < until) settling = true;
                else _settleUntil.Remove(ship);
            }

            float hold = Plugin.MooringHold.Value;
            // What vanilla does to an empty boat every step, tied or not: nine tenths of the sideways and forward
            // speed go each step, so a shove carries it a few centimetres and no farther. The pull back to the
            // tie-up spot is gentle, in case the hull is against the dock.
            // Nine tenths of the speed every step is a wall at fifty steps a second; for the first moments after
            // she is made fast she loses it over a couple of seconds instead, and only then is held hard.
            float keep = settling ? Mathf.Exp(-dt * 2.2f) : 0.1f;
            Vector3 v = body.linearVelocity;
            v.x *= keep; v.z *= keep;
            Vector3 d = zdo.GetVec3(PosHash, body.position) - body.position; d.y = 0f;
            if (d.magnitude > 6f) d = d.normalized * 6f;
            v += d * (0.1f * hold);
            body.linearVelocity = v;
            Vector3 av = body.angularVelocity;
            av.y *= keep;
            float yawErr = Mathf.DeltaAngle(body.rotation.eulerAngles.y, zdo.GetFloat(YawHash, body.rotation.eulerAngles.y)) * Mathf.Deg2Rad;
            av.y += yawErr * (0.1f * hold);
            body.angularVelocity = av;
        }

        private static Ship _helmShip;
        private static float _helmTime;

        /// <summary>Pilot's client, every physics step at the helm of a moored boat: after a second, cast off.</summary>
        internal static void PilotAtHelm(Ship ship, float dt)
        {
            if (_helmShip != ship) { _helmShip = ship; _helmTime = 0f; }
            _helmTime += dt;
            if (_helmTime < Plugin.CastOffDelay.Value) return;
            _helmTime = 0f;
            CastOff(ship);
        }

        /// <summary>Untie from wherever this boat is tied: the cleat, if it is loaded here, else the boat's own record (the cleat notices and lets go).</summary>
        internal static void CastOff(Ship ship)
        {
            var nv = ship.m_nview;
            if (nv == null || !nv.IsValid()) return;
            ZDOID cleat = nv.GetZDO().GetZDOID(CleatKey);
            var scene = ZNetScene.instance;
            var go = scene != null && !cleat.IsNone() ? scene.FindInstance(cleat) : null;
            var piece = go != null ? go.GetComponent<CleatPiece>() : null;
            if (piece != null) piece.UntieFromBoat();
            nv.InvokeRPC(RpcName, cleat, false);
            var p = Player.m_localPlayer;
            if (p != null && Time.time - _messageTime > 2f) { _messageTime = Time.time; p.Message(MessageHud.MessageType.TopLeft, "Cast off"); }
        }
    }
}
