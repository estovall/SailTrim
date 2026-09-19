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
            wnt.m_noRoofWear = true;
            wnt.m_noSupportWear = true;
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
                float r = HornRadius(x) + WrapRopeRadius * (1f + 1.7f * layer);
                path.Add(new Vector3(x, HornY + Mathf.Sin(phi) * r, Mathf.Cos(phi) * r * side));
            }
            // The finishing turn round the middle, lying on top of the crossings.
            float rTop = HornRadius(0f) + WrapRopeRadius * 6.2f;
            for (int i = 1; i <= 24; i++)
            {
                float phi = Mathf.PI / 2f - i / 24f * Mathf.PI * 2f;
                path.Add(new Vector3(0.03f * side * i / 24f, HornY + Mathf.Sin(phi) * rTop, Mathf.Cos(phi) * rTop));
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
    internal class CleatPiece : MonoBehaviour, Hoverable, Interactable
    {
        private ZNetView _nview;
        private float _checkTimer, _tieTime;

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

        private void Tie(Ship ship, Humanoid user)
        {
            var shipView = ship.m_nview;
            if (shipView == null || !shipView.IsValid()) return;
            _nview.ClaimOwnership();
            _nview.GetZDO().Set(Cleat.BoatKey, shipView.GetZDO().m_uid);
            _tieTime = Time.time;
            // To the boat's owner, who runs its physics and writes its ZDO.
            shipView.InvokeRPC(Mooring.RpcName, _nview.GetZDO().m_uid, true);
            user.Message(MessageHud.MessageType.TopLeft, "Tied up " + Localization.instance.Localize(ShipName(ship)));
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
            _checkTimer -= Time.deltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = 1f;
                // The boat is gone (sunk, broken up, or the world forgot it), or it is tied to another cleat now.
                var shipZdo = ZDOMan.instance.GetZDO(boat);
                if (shipZdo == null || shipZdo.GetZDOID(Mooring.CleatKey) != _nview.GetZDO().m_uid)
                {
                    // Only one client should take this on; the one nearest the cleat does.
                    var p = Player.m_localPlayer;
                    if (p != null && Vector3.Distance(p.transform.position, transform.position) < 40f)
                    {
                        // A boat freshly tied has not had its owner write the cleat id yet: give it a moment.
                        if (shipZdo == null || Time.time - _tieTime > 5f) { Untie(null); return; }
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
        private const float RopeRadius = 0.025f;
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

        private static bool HullCollider(Ship ship, Collider c)
        {
            if (c == null || !c.enabled || c.isTrigger) return false;
            if (ship.m_mastObject != null && c.transform.IsChildOf(ship.m_mastObject.transform)) return false;
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
        private static readonly int PosHash = "SailTrim_MoorPos".GetStableHashCode();
        private static readonly int YawHash = "SailTrim_MoorYaw".GetStableHashCode();

        private static readonly Dictionary<Ship, float> _checkAt = new Dictionary<Ship, float>();
        private static readonly Dictionary<Ship, float> _repairAt = new Dictionary<Ship, float>();
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
                zdo.Set(PosHash, ship.m_body.position);
                zdo.Set(YawHash, ship.transform.eulerAngles.y);
                ship.m_speed = Ship.Speed.Stop;
                var st = SailTrimShip.Get(ship);
                if (st != null) st.OnMoored();
            }
            else
            {
                zdo.Set(CleatKey, ZDOID.None);
            }
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
            ZDOID cleat = zdo.GetZDOID(CleatKey);
            if (cleat.IsNone()) return;
            if (!_checkAt.TryGetValue(ship, out float at) || Time.time > at)
            {
                _checkAt[ship] = Time.time + 1f;
                // The cleat is gone, or it no longer claims this boat: let go.
                var cz = ZDOMan.instance.GetZDO(cleat);
                if (cz == null || cz.GetZDOID(Cleat.BoatKey) != zdo.m_uid) { zdo.Set(CleatKey, ZDOID.None); return; }
            }
            var body = ship.m_body;
            if (body == null) return;
            // Tied up, the crew sees to the hull: a few percent of its health back every minute, in small steps.
            if (Plugin.MoorRepairPerMinute.Value > 0f && (!_repairAt.TryGetValue(ship, out float rAt) || Time.time > rAt))
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
            float hold = Plugin.MooringHold.Value;
            // What vanilla does to an empty boat every step, tied or not: nine tenths of the sideways and forward
            // speed go each step, so a shove carries it a few centimetres and no farther. The pull back to the
            // tie-up spot is gentle, in case the hull is against the dock.
            Vector3 v = body.linearVelocity;
            v.x *= 0.1f; v.z *= 0.1f;
            Vector3 d = zdo.GetVec3(PosHash, body.position) - body.position; d.y = 0f;
            if (d.magnitude > 6f) d = d.normalized * 6f;
            v += d * (0.1f * hold);
            body.linearVelocity = v;
            Vector3 av = body.angularVelocity;
            av.y *= 0.1f;
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
