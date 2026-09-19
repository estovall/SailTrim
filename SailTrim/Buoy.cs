using System.Collections.Generic;
using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// The buoy: a build piece for open water, no workbench needed. A tarred barrel riding the water with a staff,
    /// a banner in the buoy's colour on a crossbar, and a lantern on top that burns at night. It is placed on the
    /// water like a boat, floats on the waves, and holds the spot it was set at, so a line of them marks a channel
    /// or a race course. A boat that hits one nudges it aside for a moment; it works its way back.
    ///
    /// The model is put together from the game's own barrel, pole, banner and lantern meshes and materials (the
    /// banner's cloth re-dyed), so there is no asset bundle; any part the game does not have falls back to a
    /// plain shape. The float is a rigidbody the owner steers every physics step: its height follows the water,
    /// its horizontal speed is bled off and it is pulled back to its anchor (SailTrim_Anchor in the ZDO).
    /// </summary>
    internal static class Buoy
    {
        internal const string PrefabName = "SailTrim_Buoy";
        internal static readonly int AnchorHash = "SailTrim_Anchor".GetStableHashCode();
        internal static readonly int ColorHash = "SailTrim_Color".GetStableHashCode();

        /// <summary>The colours a buoy can wear (its banner, and its map pin). Interact cycles through them.</summary>
        internal static readonly string[] ColorNames = { "Red", "Green", "Yellow", "White", "Blue", "Orange", "Black" };
        internal static readonly Color[] Colors =
        {
            new Color(0.72f, 0.1f, 0.08f), new Color(0.1f, 0.5f, 0.18f), new Color(0.92f, 0.76f, 0.14f), new Color(0.9f, 0.88f, 0.82f),
            new Color(0.12f, 0.26f, 0.72f), new Color(0.92f, 0.42f, 0.08f), new Color(0.07f, 0.07f, 0.07f),
        };
        internal static readonly Color[] PinColors =
        {
            new Color(1f, 0.25f, 0.2f), new Color(0.3f, 0.95f, 0.35f), new Color(1f, 0.9f, 0.2f), Color.white,
            new Color(0.35f, 0.55f, 1f), new Color(1f, 0.6f, 0.15f), new Color(0.25f, 0.25f, 0.25f),
        };

        /// <summary>One banner material per colour; the buoy swaps its banner between them.</summary>
        internal static Material[] FlagMaterials;
        internal static Material LanternMaterial;
        internal static readonly Color LanternDim = new Color(0.35f, 0.26f, 0.14f), LanternLit = new Color(1f, 0.82f, 0.45f);

        // Heights on the model, metres above the waterline (the buoy's origin).
        private const float BarrelHeight = 0.95f, BarrelDraft = 0.42f, StaffTop = 2.5f, BarY = 2.4f, LanternHeight = 0.52f, BannerHeight = 1.25f;

        private static Sprite _pinSprite, _icon;
        private static GameObject _prefab;
        private static bool _effectsApplied, _visualBuilt;

        internal static GameObject Prefab => _prefab;

        /// <summary>A buoy seen from above: a filled disc with a dark rim, white so the pin's colour tints it.</summary>
        internal static Sprite PinSprite()
        {
            if (_pinSprite != null) return _pinSprite;
            const int n = 48;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            float c0 = n * 0.5f, rOut = n * 0.46f, rRim = n * 0.36f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - c0, dy = y + 0.5f - c0;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rOut - r);
                    float inner = Mathf.Clamp01(rRim - r);
                    float v = Mathf.Lerp(0.15f, 1f, inner);
                    px[y * n + x] = new Color(v, v, v, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            _pinSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return _pinSprite;
        }

        internal static void EnsurePrefab(Transform root)
        {
            if (_prefab != null) return;
            int pieceLayer = LayerMask.NameToLayer("piece");
            var go = new GameObject(PrefabName);
            go.transform.SetParent(root, false);
            go.layer = pieceLayer;

            var nview = go.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;
            // A "distant" object: the game keeps it in the world out to the distant area (several zones, hundreds
            // of metres) instead of only the zones around the player, so a mark shows from far down a channel.
            nview.m_distant = true;

            var body = go.AddComponent<Rigidbody>();
            body.mass = 30f;
            body.useGravity = false;
            body.linearDamping = 0.5f;
            body.angularDamping = 2f;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;

            var sync = go.AddComponent<ZSyncTransform>();
            sync.m_syncPosition = true;
            sync.m_syncRotation = true;
            sync.m_syncBodyVelocity = true;

            var piece = go.AddComponent<Piece>();
            piece.m_name = "Buoy";
            piece.m_description = "A tarred barrel with a staff, a banner and a lantern. Set it on the water and it stays there: a channel marker, a race mark. Press E at it to change the banner's colour.";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_waterPiece = true;
            piece.m_groundPiece = false;
            piece.m_noInWater = false;
            piece.m_allowedInDungeons = false;
            piece.m_canBeRemoved = true;
            piece.m_canRotate = true;
            piece.m_randomInitBuildRotation = false;

            var wnt = go.AddComponent<WearNTear>();
            wnt.m_health = 200f;
            wnt.m_materialType = WearNTear.MaterialType.Wood;
            wnt.m_burnable = false;
            wnt.m_noRoofWear = true;
            wnt.m_noSupportWear = true;
            wnt.m_supports = false;
            wnt.m_staticPosition = false;

            go.AddComponent<BuoyPiece>();

            // Colliders: the barrel, and the staff up to the banner.
            var colGo = new GameObject("collider");
            colGo.transform.SetParent(go.transform, false);
            colGo.layer = pieceLayer;
            var cap = colGo.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.radius = 0.32f;
            cap.height = BarrelHeight;
            cap.center = new Vector3(0f, BarrelHeight * 0.5f - BarrelDraft, 0f);
            var staff = colGo.AddComponent<BoxCollider>();
            staff.center = new Vector3(0f, (BarrelHeight - BarrelDraft + StaffTop) * 0.5f, 0f);
            staff.size = new Vector3(0.12f, StaffTop - (BarrelHeight - BarrelDraft), 0.12f);

            var lightGo = new GameObject("light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, StaffTop + LanternHeight * 0.45f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.4f);
            light.intensity = 1.6f;
            light.range = 10f;
            light.shadows = LightShadows.None;
            light.enabled = false;

            _prefab = go;
            Plugin.Log.LogInfo("SailTrim: buoy prefab built.");
        }

        /// <summary>
        /// The model, from the game's own parts (found through ObjectDB, so it works in the main menu too). Built
        /// once; the parts' meshes and materials are the game's shared ones.
        /// </summary>
        private static void BuildVisual(ObjectDB db)
        {
            if (_visualBuilt || Models.Headless || _prefab == null) return;
            var old = _prefab.transform.Find("visual");
            if (old != null) Object.Destroy(old.gameObject);
            var visual = new GameObject("visual");
            visual.transform.SetParent(_prefab.transform, false);
            visual.layer = _prefab.layer;
            var t = visual.transform;
            var used = new List<string>();

            // ---- The float: a barrel standing in the water ----
            var barrelSrc = Models.FindFirst(db, out string barrelName, "piece_chest_barrel", "barrell", "bogwitch_barrel");
            Bounds bb = default;
            GameObject barrel = barrelSrc != null ? Models.CopyVisual(barrelSrc, t, "float", out bb) : null;
            if (barrel != null)
            {
                bb = FitBounds(barrel, bb);
                float s = BarrelHeight / Mathf.Max(0.05f, bb.size.y);
                Models.Place(barrel, bb, new Vector3(0.5f, 0f, 0.5f), new Vector3(0f, -BarrelDraft, 0f), s, Quaternion.identity);
                used.Add("float " + barrelName);
            }
            else
            {
                var mb = new Models.MeshBuilder();
                mb.Tube(new Vector3(0f, -BarrelDraft, 0f), new Vector3(0f, BarrelHeight - BarrelDraft, 0f), u => 0.3f + 0.04f * Mathf.Sin(u * Mathf.PI), 20, 8);
                Models.MeshPart(t, "float", mb.Build("SailTrim_BuoyFloat"), Models.Standard(Models.NoiseTexture(new Color(0.3f, 0.21f, 0.13f), 0.35f, 3, 0.8f), 0f, 0.15f));
                used.Add("float plain");
            }

            // ---- The staff and the crossbar the banner hangs from ----
            float staffBottom = BarrelHeight - BarrelDraft - 0.1f;
            var poleSrc = Models.FindFirst(db, out string poleName, "wood_pole2", "wood_pole");
            if (poleSrc != null)
            {
                var staffGo = Models.CopyVisual(poleSrc, t, "staff", out var pb);
                if (staffGo != null) StretchPole(staffGo, pb, new Vector3(0f, staffBottom, 0f), new Vector3(0f, StaffTop, 0f), 0.085f);
                used.Add("staff " + poleName);
            }
            else
            {
                var wood = Models.Standard(Models.NoiseTexture(new Color(0.42f, 0.3f, 0.18f), 0.3f, 5, 0.9f), 0f, 0.1f);
                var mb = new Models.MeshBuilder();
                mb.Tube(new Vector3(0f, staffBottom, 0f), new Vector3(0f, StaffTop, 0f), u => 0.042f, 10, 1);
                Models.MeshPart(t, "staff", mb.Build("SailTrim_BuoyStaff"), wood);
                used.Add("staff plain");
            }

            // ---- The banner, dyed ----
            var bannerSrc = Models.FindFirst(db, out string bannerName, "piece_banner01", "piece_banner02", "piece_banner03");
            Bounds fb = default;
            GameObject flag = bannerSrc != null ? Models.CopyVisual(bannerSrc, t, "flag", out fb) : null;
            Material bannerMat = null;
            if (flag != null)
            {
                // The banner piece is a beam with the cloth hanging from it; the cloth is the renderer on the
                // swaying vegetation shader (or named for a banner).
                foreach (var mr in flag.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var m = mr.sharedMaterial;
                    if (m == null) continue;
                    string sn = m.shader != null ? m.shader.name : "";
                    if (sn.Contains("Vegetation") || m.name.ToLowerInvariant().Contains("banner")) { bannerMat = m; break; }
                }
                // Turn it so its thinnest side faces front, and hang it, beam and all, by the top of the staff.
                Quaternion rot = fb.size.x < fb.size.z ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
                float s = BannerHeight / Mathf.Max(0.05f, fb.size.y);
                Models.Place(flag, fb, new Vector3(0.5f, 1f, 0.5f), new Vector3(0f, BarY + 0.05f, 0.075f), s, rot);
                used.Add("flag " + bannerName);
            }
            else
            {
                var mb = new Models.MeshBuilder();
                mb.Box(new Vector3(0f, BarY - 0.02f - 0.45f, 0.07f), new Vector3(0.6f, 0.9f, 0.015f));
                mb.Box(new Vector3(0f, BarY, 0.07f), new Vector3(0.7f, 0.05f, 0.05f));
                flag = Models.MeshPart(t, "flag", mb.Build("SailTrim_BuoyFlag"), null);
                used.Add("flag plain");
            }
            FlagMaterials = new Material[Colors.Length];
            for (int i = 0; i < Colors.Length; i++)
            {
                var cloth = Models.NoiseTexture(Colors[i], 0.18f, 100 + i, 0.25f);
                FlagMaterials[i] = bannerMat != null ? Models.Retextured(bannerMat, cloth) : Models.Standard(cloth, 0f, 0.05f);
                FlagMaterials[i].name = "SailTrim_Flag_" + ColorNames[i];
            }
            foreach (var r in flag.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                    if ((bannerMat != null && mats[k] == bannerMat) || (bannerMat == null && r.transform.parent == flag.transform)) mats[k] = FlagMaterials[0];
                r.sharedMaterials = mats;
            }

            // ---- The lantern on top, and a glow inside it that reads from far off at night ----
            var lanternSrc = Models.FindFirst(db, out string lanternName, "Lantern", "piece_hoodedlantern", "piece_snowlantern");
            Bounds lb = default;
            GameObject lantern = lanternSrc != null ? Models.CopyVisual(lanternSrc, t, "lantern", out lb) : null;
            LanternMaterial = null;
            if (lantern != null)
            {
                float s = LanternHeight / Mathf.Max(0.05f, lb.size.y);
                Models.Place(lantern, lb, new Vector3(0.5f, 0f, 0.5f), new Vector3(0f, StaffTop - 0.02f, 0f), s, Quaternion.identity);
                // Its own material, copied, so the night glow (emission) touches only buoys.
                foreach (var mr in lantern.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = mr.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++)
                    {
                        if (mats[k] == null) continue;
                        if (LanternMaterial == null) { LanternMaterial = new Material(mats[k]) { name = "SailTrim_BuoyLantern" }; LanternMaterial.EnableKeyword("_EMISSION"); }
                        mats[k] = LanternMaterial;
                    }
                    mr.sharedMaterials = mats;
                }
                used.Add("lantern " + lanternName);
            }
            else
            {
                var glowMesh = new Models.MeshBuilder();
                glowMesh.Box(new Vector3(0f, StaffTop + LanternHeight * 0.4f, 0f), new Vector3(0.18f, LanternHeight * 0.8f, 0.18f));
                LanternMaterial = Models.Standard(Models.NoiseTexture(new Color(0.25f, 0.2f, 0.12f), 0.2f, 9), 0.4f, 0.4f);
                if (LanternMaterial != null) { LanternMaterial.EnableKeyword("_EMISSION"); Models.MeshPart(t, "lantern", glowMesh.Build("SailTrim_BuoyLantern"), LanternMaterial); }
                used.Add("lantern plain");
            }
            SetLanternLit(false);

            foreach (var r in visual.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = _prefab.layer;
            _visualBuilt = true;
            Plugin.Log.LogInfo("SailTrim: buoy model: " + string.Join(", ", used));
        }

        /// <summary>The lantern glows at night (emission on its material, shared by every buoy).</summary>
        internal static void SetLanternLit(bool on)
        {
            if (LanternMaterial == null) return;
            if (LanternMaterial.HasProperty("_EmissionColor")) LanternMaterial.SetColor("_EmissionColor", on ? LanternLit * 2.2f : Color.black);
        }

        /// <summary>The copy's bounds as they come (kept as a hook for parts that need trimming).</summary>
        private static Bounds FitBounds(GameObject part, Bounds b) => b;

        /// <summary>A pole's copy stretched between two points (its longest side along the line), this thick.</summary>
        private static void StretchPole(GameObject pole, Bounds b, Vector3 from, Vector3 to, float thickness)
        {
            // Which of the copy's own axes is its length.
            int axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : (b.size.y >= b.size.z ? 1 : 2);
            Vector3 lengthAxis = axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
            Quaternion rot = Quaternion.FromToRotation(lengthAxis, (to - from).normalized);
            float len = Vector3.Distance(from, to);
            Vector3 scale = Vector3.one;
            for (int i = 0; i < 3; i++) scale[i] = i == axis ? len / Mathf.Max(0.01f, b.size[i]) : thickness / Mathf.Max(0.01f, b.size[i]);
            Vector3 anchor = new Vector3(0.5f, 0.5f, 0.5f); anchor[axis] = 0f;
            Models.Place(pole, b, anchor, from, scale, rot);
        }

        /// <summary>ObjectDB is up: the model and its icon, the cost (wood and resin, no station), and the piece into the hammer.</summary>
        internal static void OnObjectDb(ObjectDB db, Transform root)
        {
            if (!Plugin.BuoyEnabled.Value || db == null) return;
            EnsurePrefab(root);
            // The main menu's ObjectDB has no items or pieces: the model is built from the game's parts once a
            // world's ObjectDB is up (it has the hammer), never from fallbacks in the menu.
            bool full = db.GetItemPrefab("Hammer") != null;
            if (full) { Models.FindStandardTemplate(db); BuildVisual(db); }
            var piece = _prefab.GetComponent<Piece>();
            var wood = db.GetItemPrefab("Wood"); var resin = db.GetItemPrefab("Resin");
            var wd = wood != null ? wood.GetComponent<ItemDrop>() : null;
            var rd = resin != null ? resin.GetComponent<ItemDrop>() : null;
            if (wd != null)
                piece.m_resources = rd != null
                    ? new[] { new Piece.Requirement { m_resItem = wd, m_amount = 6, m_recover = true }, new Piece.Requirement { m_resItem = rd, m_amount = 2, m_recover = true } }
                    : new[] { new Piece.Requirement { m_resItem = wd, m_amount = 6, m_recover = true } };
            if (_icon == null && _visualBuilt)
            {
                var visual = _prefab.transform.Find("visual");
                _icon = visual != null ? Models.RenderIcon(visual.gameObject, "icon_buoy", 256, 150f, 12f) : null;
                // A night view too, for checking the lantern.
                if (visual != null) { SetLanternLit(true); Models.RenderIcon(visual.gameObject, "preview_buoy_night", 256, 150f, 12f); SetLanternLit(false); }
            }
            piece.m_icon = _icon != null ? _icon : (wd != null ? wd.m_itemData.GetIcon() : piece.m_icon);
            var hammer = db.GetItemPrefab("Hammer");
            var table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table != null && !table.m_pieces.Contains(_prefab)) table.m_pieces.Add(_prefab);
        }

        /// <summary>ZNetScene is up: the prefab into the world's list.</summary>
        internal static void OnZNetScene(ZNetScene scene, Transform root)
        {
            if (!Plugin.BuoyEnabled.Value || scene == null) return;
            EnsurePrefab(root);
            int hash = PrefabName.GetStableHashCode();
            if (!scene.m_prefabs.Contains(_prefab)) scene.m_prefabs.Add(_prefab);
            if (!scene.m_namedPrefabs.ContainsKey(hash)) scene.m_namedPrefabs.Add(hash, _prefab);
            if (!_effectsApplied)
            {
                var chest = scene.GetPrefab("piece_chest_barrel") ?? scene.GetPrefab("piece_chest_wood");
                if (chest != null)
                {
                    var cp = chest.GetComponent<Piece>(); var cw = chest.GetComponent<WearNTear>();
                    var piece = _prefab.GetComponent<Piece>(); var wnt = _prefab.GetComponent<WearNTear>();
                    if (cp != null) piece.m_placeEffect = cp.m_placeEffect;
                    if (cw != null) { wnt.m_hitEffect = cw.m_hitEffect; wnt.m_destroyedEffect = cw.m_destroyedEffect; }
                    _effectsApplied = true;
                }
            }
        }
    }

    /// <summary>The placed buoy: floats on the owner's client, holds its anchor, lights up at night everywhere, wears its colour, and keeps a pin on the map.</summary>
    internal class BuoyPiece : MonoBehaviour, Hoverable, Interactable
    {
        private ZNetView _nview;
        private Rigidbody _body;
        private Light _light;
        private WaterVolume _water;
        private float _lightTimer;
        private int _shownColor = -1;
        private MeshRenderer[] _flag;
        private Minimap.PinData _pin;

        /// <summary>Every loaded buoy's map pin and its colour, for the minimap patch to tint (the game paints every pin white each frame).</summary>
        internal static readonly Dictionary<Minimap.PinData, Color> Pins = new Dictionary<Minimap.PinData, Color>();

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _body = GetComponent<Rigidbody>();
            _light = GetComponentInChildren<Light>(true);
            var flag = transform.Find("visual/flag");
            _flag = flag != null ? flag.GetComponentsInChildren<MeshRenderer>(true) : new MeshRenderer[0];
        }

        private void OnDestroy()
        {
            RemovePin();
        }

        private int ColorIndex
        {
            get
            {
                var z = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
                int i = z != null ? z.GetInt(Buoy.ColorHash, 0) : 0;
                return Mathf.Clamp(i, 0, Buoy.Colors.Length - 1);
            }
        }

        private void ApplyColor(int index)
        {
            if (index == _shownColor) return;
            _shownColor = index;
            var mats = Buoy.FlagMaterials;
            if (mats != null && index < mats.Length && mats[index] != null)
                foreach (var r in _flag)
                {
                    if (r == null) continue;
                    var m = r.sharedMaterials;
                    for (int k = 0; k < m.Length; k++) if (m[k] != null && m[k].name.StartsWith("SailTrim_Flag_")) m[k] = mats[index];
                    r.sharedMaterials = m;
                }
            if (_pin != null) Pins[_pin] = Buoy.PinColors[index];
        }

        // ---- map pin ----
        private void UpdatePin()
        {
            var map = Minimap.instance;
            if (map == null) return;
            if (!Plugin.BuoyPins.Value || _nview == null || !_nview.IsValid()) { RemovePin(); return; }
            if (_pin == null)
            {
                _pin = map.AddPin(transform.position, Minimap.PinType.Icon3, "", false, false);
                _pin.m_icon = Buoy.PinSprite();
                Pins[_pin] = Buoy.PinColors[ColorIndex];
            }
            _pin.m_pos = transform.position;
        }

        private void RemovePin()
        {
            if (_pin == null) return;
            Pins.Remove(_pin);
            if (Minimap.instance != null) Minimap.instance.RemovePin(_pin);
            _pin = null;
        }

        // ---- hover / interact: the colour ----
        public string GetHoverName() => "Buoy";
        public float GetHoverOffset() => 0f;
        public string GetHoverText() => Localization.instance.Localize("Buoy (" + Buoy.ColorNames[ColorIndex] + ")\n[<color=yellow><b>$KEY_Use</b></color>] Change colour");
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _nview == null || !_nview.IsValid()) return false;
            int next = (ColorIndex + 1) % Buoy.Colors.Length;
            _nview.ClaimOwnership();
            _nview.GetZDO().Set(Buoy.ColorHash, next);
            ApplyColor(next);
            user.Message(MessageHud.MessageType.TopLeft, "Buoy: " + Buoy.ColorNames[next]);
            return true;
        }

        private void Update()
        {
            ApplyColor(ColorIndex);
            _lightTimer -= Time.deltaTime;
            if (_lightTimer > 0f) return;
            _lightTimer = 1f;
            UpdatePin();
            if (_light == null) return;
            bool on = Plugin.BuoyLight.Value && EnvMan.instance != null && EnvMan.IsNight();
            if (_light.enabled != on) _light.enabled = on;
            Buoy.SetLanternLit(on);
        }

        private void FixedUpdate()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || _body == null) return;
            var zdo = _nview.GetZDO();
            Vector3 pos = _body.position;
            Vector3 anchor = zdo.GetVec3(Buoy.AnchorHash, Vector3.zero);
            if (anchor == Vector3.zero)
            {
                // Just placed (the game sets a water piece down above the surface): this is its spot.
                anchor = pos;
                zdo.Set(Buoy.AnchorHash, anchor);
            }
            Vector3 v = _body.linearVelocity;
            float water = Floating.GetWaterLevel(pos, ref _water);
            if (water > -5000f) v.y = (water - pos.y) * 4f; // ride the surface: the origin is the waterline
            else v.y = Mathf.Max(v.y - 9.81f * Time.fixedDeltaTime, -5f); // no water here: sink to whatever is below
            // Hold the spot: most of any push is gone the next step, and the anchor pulls it home.
            v.x *= 0.2f; v.z *= 0.2f;
            Vector3 d = anchor - pos; d.y = 0f;
            if (d.magnitude > 10f) d = d.normalized * 10f;
            v += d * 0.6f;
            _body.linearVelocity = v;
            _body.angularVelocity = Vector3.zero;
        }
    }
}
