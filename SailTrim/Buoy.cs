using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// The buoy: a build piece for open water, no workbench needed. A tarred wooden float with a staff, a red
    /// pennant and a small lantern that burns at night. It is placed on the water like a boat, floats on the
    /// waves, and holds the spot it was set at, so a line of them marks a channel or a race course. A boat that
    /// hits one nudges it aside for a moment; it works its way back.
    ///
    /// Built from primitives with the game's own wood material, like the cleat (no asset bundle). The float is
    /// a rigidbody the owner steers every physics step: its height follows the water, its horizontal speed is
    /// bled off and it is pulled back to its anchor (SailTrim_Anchor in the ZDO, set when placed).
    /// </summary>
    internal static class Buoy
    {
        internal const string PrefabName = "SailTrim_Buoy";
        internal static readonly int AnchorHash = "SailTrim_Anchor".GetStableHashCode();

        private static GameObject _prefab;
        private static bool _materialsApplied, _effectsApplied;

        internal static GameObject Prefab => _prefab;

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
            piece.m_description = "A tarred float with a staff and pennant. Set it on the water and it stays there: a channel marker, a race mark. The lantern burns at night.";
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

            Mesh cube = Primitive(PrimitiveType.Cube), cyl = Primitive(PrimitiveType.Cylinder);
            Material wood = Fallback(new Color(0.36f, 0.26f, 0.16f), 0.05f), cloth = Fallback(new Color(0.62f, 0.12f, 0.1f), 0.02f);
            // The float: a squat barrel, its waterline at the buoy's origin. Cylinder primitives are 2 units tall.
            Part(go, "float", new Vector3(0f, 0.12f, 0f), new Vector3(0.7f, 0.22f, 0.7f), cyl, wood, true);
            Part(go, "hoopTop", new Vector3(0f, 0.3f, 0f), new Vector3(0.72f, 0.02f, 0.72f), cyl, cloth, false);
            Part(go, "staff", new Vector3(0f, 1.05f, 0f), new Vector3(0.08f, 0.45f, 0.08f), cyl, wood, true);
            Part(go, "pennant", new Vector3(0f, 1.62f, 0.26f), new Vector3(0.02f, 0.26f, 0.5f), cube, cloth, false);
            Part(go, "lantern", new Vector3(0f, 1.98f, 0f), new Vector3(0.12f, 0.12f, 0.12f), cube, wood, false);

            var lightGo = new GameObject("light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.4f);
            light.intensity = 1.4f;
            light.range = 9f;
            light.shadows = LightShadows.None;
            light.enabled = false;

            _prefab = go;
            Plugin.Log.LogInfo("SailTrim: buoy prefab built.");
        }

        private static void Part(GameObject parent, string name, Vector3 pos, Vector3 size, Mesh mesh, Material mat, bool collider)
        {
            var p = new GameObject(name);
            p.transform.SetParent(parent.transform, false);
            p.transform.localPosition = pos;
            p.transform.localScale = size;
            p.layer = parent.layer;
            p.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = p.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (collider)
            {
                var c = p.AddComponent<CapsuleCollider>();
                c.direction = 1;
            }
        }

        private static Mesh Primitive(PrimitiveType type)
        {
            var tmp = GameObject.CreatePrimitive(type);
            var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tmp);
            return mesh;
        }

        private static Material Fallback(Color color, float gloss)
        {
            var shader = Shader.Find("Standard");
            var m = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            m.color = color;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            return m;
        }

        /// <summary>ObjectDB is up: the cost (wood and resin, no station) and the piece into the hammer.</summary>
        internal static void OnObjectDb(ObjectDB db, Transform root)
        {
            if (!Plugin.BuoyEnabled.Value || db == null) return;
            EnsurePrefab(root);
            var piece = _prefab.GetComponent<Piece>();
            var wood = db.GetItemPrefab("Wood"); var resin = db.GetItemPrefab("Resin");
            var wd = wood != null ? wood.GetComponent<ItemDrop>() : null;
            var rd = resin != null ? resin.GetComponent<ItemDrop>() : null;
            if (wd != null)
            {
                piece.m_resources = rd != null
                    ? new[] { new Piece.Requirement { m_resItem = wd, m_amount = 6, m_recover = true }, new Piece.Requirement { m_resItem = rd, m_amount = 2, m_recover = true } }
                    : new[] { new Piece.Requirement { m_resItem = wd, m_amount = 6, m_recover = true } };
                if (piece.m_icon == null) piece.m_icon = wd.m_itemData.GetIcon();
            }
            var hammer = db.GetItemPrefab("Hammer");
            var table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table != null && !table.m_pieces.Contains(_prefab)) table.m_pieces.Add(_prefab);
        }

        /// <summary>ZNetScene is up: the prefab into the world's list, and the game's wood for the float and staff.</summary>
        internal static void OnZNetScene(ZNetScene scene, Transform root)
        {
            if (!Plugin.BuoyEnabled.Value || scene == null) return;
            EnsurePrefab(root);
            int hash = PrefabName.GetStableHashCode();
            if (!scene.m_prefabs.Contains(_prefab)) scene.m_prefabs.Add(_prefab);
            if (!scene.m_namedPrefabs.ContainsKey(hash)) scene.m_namedPrefabs.Add(hash, _prefab);
            if (!_materialsApplied)
            {
                Material wood = null;
                foreach (var name in new[] { "wood_pole", "wood_beam", "piece_chest_wood" })
                {
                    var src = scene.GetPrefab(name);
                    var mr = src != null ? src.GetComponentInChildren<MeshRenderer>(true) : null;
                    if (mr != null && mr.sharedMaterial != null) { wood = mr.sharedMaterial; break; }
                }
                if (wood != null)
                {
                    foreach (var n in new[] { "float", "staff", "lantern" })
                    {
                        var t = _prefab.transform.Find(n);
                        if (t != null) t.GetComponent<MeshRenderer>().sharedMaterial = wood;
                    }
                    _materialsApplied = true;
                }
            }
            if (!_effectsApplied)
            {
                var chest = scene.GetPrefab("piece_chest_wood");
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

    /// <summary>The placed buoy: floats on the owner's client, holds its anchor, lights up at night everywhere.</summary>
    internal class BuoyPiece : MonoBehaviour
    {
        private ZNetView _nview;
        private Rigidbody _body;
        private Light _light;
        private WaterVolume _water;
        private float _lightTimer;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _body = GetComponent<Rigidbody>();
            _light = GetComponentInChildren<Light>(true);
        }

        private void Update()
        {
            _lightTimer -= Time.deltaTime;
            if (_lightTimer > 0f || _light == null) return;
            _lightTimer = 1f;
            bool on = Plugin.BuoyLight.Value && EnvMan.instance != null && EnvMan.IsNight();
            if (_light.enabled != on) _light.enabled = on;
        }

        private void FixedUpdate()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || _body == null) return;
            var zdo = _nview.GetZDO();
            Vector3 pos = _body.position;
            Vector3 anchor = zdo.GetVec3(Buoy.AnchorHash, Vector3.zero);
            if (anchor == Vector3.zero)
            {
                // Just placed (the game sets a water piece down a little above the surface): this is its spot.
                anchor = pos;
                zdo.Set(Buoy.AnchorHash, anchor);
            }
            Vector3 v = _body.linearVelocity;
            float water = Floating.GetWaterLevel(pos, ref _water);
            if (water > -5000f)
            {
                // Ride the surface: the float's waterline sits just under the buoy's origin.
                v.y = (water - 0.06f - pos.y) * 4f;
            }
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
