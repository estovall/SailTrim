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
    /// No assets: the model is three bronze blocks, the rope a LineRenderer.
    /// </summary>
    internal static class Cleat
    {
        internal const string PrefabName = "SailTrim_Cleat";
        internal const string BoatKey = "SailTrim_Boat";

        private static GameObject _root, _prefab;
        private static bool _bronzeApplied, _effectsApplied;

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
            piece.m_description = "Tie a boat up so it stays put, crew aboard or not.";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_groundPiece = false;
            piece.m_allowedInDungeons = false;
            piece.m_canBeRemoved = true;
            piece.m_canRotate = true;
            piece.m_noInWater = false;
            piece.m_randomInitBuildRotation = false;

            var wnt = go.AddComponent<WearNTear>();
            wnt.m_health = 300f;
            wnt.m_materialType = WearNTear.MaterialType.Wood; // support rules of a dock fitting: sits on wood
            wnt.m_burnable = false;
            wnt.m_noRoofWear = true;
            wnt.m_noSupportWear = true;
            wnt.m_supports = false;

            go.AddComponent<CleatPiece>();

            Mesh cube = CubeMesh();
            Material mat = FallbackMaterial();
            // A cleat: a foot plate, a short post, and the horn bar across the top.
            Part(go, "foot", new Vector3(0f, 0.02f, 0f), new Vector3(0.28f, 0.04f, 0.14f), cube, mat, true);
            Part(go, "post", new Vector3(0f, 0.085f, 0f), new Vector3(0.07f, 0.09f, 0.07f), cube, mat, false);
            Part(go, "bar", new Vector3(0f, 0.15f, 0f), new Vector3(0.42f, 0.05f, 0.06f), cube, mat, true);
            Part(go, "hornL", new Vector3(-0.19f, 0.135f, 0f), new Vector3(0.04f, 0.08f, 0.05f), cube, mat, false);
            Part(go, "hornR", new Vector3(0.19f, 0.135f, 0f), new Vector3(0.04f, 0.08f, 0.05f), cube, mat, false);

            var snap = new GameObject("_snappoint");
            snap.transform.SetParent(go.transform, false);
            snap.tag = "snappoint";
            snap.layer = pieceLayer;

            _prefab = go;
            Plugin.Log.LogInfo("SailTrim: cleat prefab built.");
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
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (collider) p.AddComponent<BoxCollider>();
        }

        private static Mesh CubeMesh()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tmp);
            return mesh;
        }

        private static Material FallbackMaterial()
        {
            var shader = Shader.Find("Standard");
            var m = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            m.color = new Color(0.72f, 0.48f, 0.22f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.8f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.55f);
            return m;
        }

        /// <summary>ObjectDB is up (menu or game): cost, icon and the bronze look, and the piece into the hammer.</summary>
        internal static void OnObjectDb(ObjectDB db)
        {
            if (!Plugin.CleatEnabled.Value || db == null) return;
            EnsurePrefab();
            var piece = _prefab.GetComponent<Piece>();
            var bronze = db.GetItemPrefab("Bronze");
            if (bronze != null)
            {
                var drop = bronze.GetComponent<ItemDrop>();
                if (drop != null)
                {
                    piece.m_resources = new[] { new Piece.Requirement { m_resItem = drop, m_amount = Mathf.Max(1, Plugin.CleatCost.Value), m_recover = true } };
                    if (piece.m_icon == null) piece.m_icon = drop.m_itemData.GetIcon();
                }
                if (!_bronzeApplied)
                {
                    var mr = bronze.GetComponentInChildren<MeshRenderer>(true);
                    if (mr != null && mr.sharedMaterial != null)
                    {
                        foreach (var r in _prefab.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = mr.sharedMaterial;
                        _bronzeApplied = true;
                    }
                }
            }
            var hammer = db.GetItemPrefab("Hammer");
            var table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table == null) { Plugin.Log.LogWarning("SailTrim: no hammer piece table; the cleat cannot be built."); return; }
            if (!table.m_pieces.Contains(_prefab)) table.m_pieces.Add(_prefab);
        }

        /// <summary>ZNetScene is up (a world is loading): the prefab must be known before any cleat ZDO arrives.</summary>
        internal static void OnZNetScene(ZNetScene scene)
        {
            if (!Plugin.CleatEnabled.Value || scene == null) return;
            EnsurePrefab();
            int hash = PrefabName.GetStableHashCode();
            if (!scene.m_prefabs.Contains(_prefab)) scene.m_prefabs.Add(_prefab);
            if (!scene.m_namedPrefabs.ContainsKey(hash)) scene.m_namedPrefabs.Add(hash, _prefab);
            if (!_effectsApplied)
            {
                // Placement, hit and break effects of a wooden chest: the sounds and puffs the player expects.
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

    /// <summary>The placed cleat. Tie and untie, the rope, and letting go of a boat that is gone.</summary>
    internal class CleatPiece : MonoBehaviour, Hoverable, Interactable
    {
        private ZNetView _nview;
        private LineRenderer _rope;
        private float _checkTimer;
        private static Material _ropeMaterial;
        private const int RopePoints = 18;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
        }

        private ZDOID Boat
        {
            get { var z = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null; return z != null ? z.GetZDOID(Cleat.BoatKey) : ZDOID.None; }
        }

        private Vector3 Top => transform.position + transform.up * 0.16f;

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
            var ship = ShipOf(boat);
            if (ship == null) { HideRope(); return; }
            DrawRope(ship);
        }

        private float _tieTime;

        private void DrawRope(Ship ship)
        {
            if (_rope == null)
            {
                var go = new GameObject("SailTrim_Rope");
                go.transform.SetParent(transform, false);
                _rope = go.AddComponent<LineRenderer>();
                _rope.useWorldSpace = true;
                _rope.positionCount = RopePoints;
                _rope.widthMultiplier = 0.035f;
                _rope.numCapVertices = 2;
                _rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _rope.receiveShadows = false;
                _rope.alignment = LineAlignment.View;
                _rope.textureMode = LineTextureMode.Stretch;
                _rope.sharedMaterial = RopeMaterial(ship);
            }
            Vector3 a = Top;
            Vector3 b = HullPoint(ship, a);
            float len = Vector3.Distance(a, b);
            float sag = Mathf.Clamp(len * 0.09f, 0.05f, 0.7f);
            for (int i = 0; i < RopePoints; i++)
            {
                float t = i / (float)(RopePoints - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= sag * 4f * t * (1f - t);
                _rope.SetPosition(i, p);
            }
            if (!_rope.enabled) _rope.enabled = true;
        }

        private void HideRope()
        {
            if (_rope != null && _rope.enabled) _rope.enabled = false;
        }

        /// <summary>Where the rope meets the boat: the nearest point of the hull's float box, lifted to the gunwale.</summary>
        private static Vector3 HullPoint(Ship ship, Vector3 from)
        {
            var box = ship.m_floatCollider;
            if (box != null)
            {
                Vector3 p = box.ClosestPoint(from);
                return p + Vector3.up * 0.55f;
            }
            return ship.transform.position + Vector3.up * 0.8f;
        }

        private static Material RopeMaterial(Ship ship)
        {
            if (_ropeMaterial != null) return _ropeMaterial;
            // The boat's own rope material, if it has one; else a plain brown.
            foreach (var r in ship.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null) continue;
                string n = (m.name ?? "").ToLowerInvariant();
                if (n.Contains("rope")) { _ropeMaterial = m; return m; }
            }
            var shader = Shader.Find("Standard");
            var mat = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            mat.color = new Color(0.45f, 0.35f, 0.22f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.1f);
            _ropeMaterial = mat;
            return mat;
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

        private Ship NearestShip(out float dist)
        {
            Ship best = null; dist = Plugin.CleatRange.Value;
            Vector3 from = Top;
            foreach (var ship in Object.FindObjectsByType<Ship>(FindObjectsSortMode.None))
            {
                if (ship == null || ship.m_nview == null || !ship.m_nview.IsValid()) continue;
                var box = ship.m_floatCollider;
                float d = box != null ? Vector3.Distance(box.ClosestPoint(from), from) : Vector3.Distance(ship.transform.position, from);
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
