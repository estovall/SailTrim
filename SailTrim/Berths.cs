using System.Collections.Generic;
using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// The berth a cleat marks (1.11). A cleat stands on the dock's edge with its horn along the edge; the berth
    /// is the water off it on whichever side is deeper: a ship of the cleat's size lying along the dock, her
    /// side BerthGap off the cleat, her middle abreast of it. Small cleats are karve berths, medium longship,
    /// large drakkar; each ship's hull is measured from its prefab when the world loads (fallback sizes if a
    /// prefab is not found). With the hammer out the outline of every berth nearby is drawn on the water, and
    /// of the cleat being placed: green where the water is deep enough all along, amber where it is not, blue
    /// with a boat tied; the cleat's size written over it.
    /// </summary>
    internal static class Berths
    {
        internal static readonly string[][] ShipPrefabs =
        {
            new[] { "Karve" },
            new[] { "VikingShip" },
            new[] { "VikingShip_Ashlands", "Drakkar" },
        };
        // Half beam, half length: fallbacks until the prefabs are measured.
        private static readonly Vector2[] s_hull = { new Vector2(1.7f, 5f), new Vector2(2.8f, 10f), new Vector2(3.4f, 13f) };
        private static readonly float[] s_draft = { 1.2f, 1.6f, 2f };
        private static readonly Dictionary<string, int> s_sizeByPrefab = new Dictionary<string, int>();
        private static bool s_measured;

        internal static Vector2 Hull(int size) => s_hull[Mathf.Clamp(size, 0, 2)];
        internal static float Draft(int size) => s_draft[Mathf.Clamp(size, 0, 2)];

        /// <summary>The ships' hulls from their prefabs' colliders (solid ones), in the prefab's own frame.</summary>
        internal static void Measure(ZNetScene scene)
        {
            if (s_measured || scene == null) return;
            s_measured = true;
            for (int size = 0; size < 3; size++)
                foreach (var name in ShipPrefabs[size])
                {
                    var pf = scene.GetPrefab(name);
                    if (pf == null || pf.GetComponent<Ship>() == null) continue;
                    s_sizeByPrefab[name] = size;
                    Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
                    foreach (var c in pf.GetComponentsInChildren<Collider>(true))
                    {
                        if (c.isTrigger) continue;
                        Bounds b;
                        if (c is BoxCollider bc) b = new Bounds(bc.center, bc.size);
                        else if (c is MeshCollider mc && mc.sharedMesh != null) b = mc.sharedMesh.bounds;
                        else continue;
                        var m = pf.transform.worldToLocalMatrix * c.transform.localToWorldMatrix;
                        for (int k = 0; k < 8; k++)
                        {
                            var corner = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                            var p = m.MultiplyPoint3x4(corner);
                            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
                        }
                    }
                    if (mx.x > mn.x && mx.z > mn.z)
                    {
                        s_hull[size] = new Vector2((mx.x - mn.x) * 0.5f, (mx.z - mn.z) * 0.5f);
                        Plugin.Log.LogInfo($"SailTrim: {name} measured {mx.x - mn.x:F1} m beam, {mx.z - mn.z:F1} m long: the {Cleat.SizeLabels[size]} berth");
                    }
                    break;
                }
        }

        internal static int SizeOf(Ship ship)
        {
            if (ship == null) return -1;
            if (s_sizeByPrefab.TryGetValue(Utils.GetPrefabName(ship.gameObject), out int s)) return s;
            // Unknown hull (a modded ship): by length against the three.
            float len = 0f;
            foreach (var c in ship.GetComponentsInChildren<Collider>()) if (!c.isTrigger) len = Mathf.Max(len, Vector3.Dot(c.bounds.extents, Vector3.one));
            return len < s_hull[0].y * 1.3f ? 0 : len < s_hull[1].y * 1.2f ? 1 : 2;
        }

        private static float Ground(Vector3 p) => ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out float h) ? h : WorldGenerator.instance != null ? WorldGenerator.instance.GetHeight(p.x, p.z) : 0f;
        private static float Water => ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;

        /// <summary>A cleat's berth: which side is water, where the ship lies, whether it is deep enough.</summary>
        internal static SailTrimApi.Berth Layout(Transform cleat, int size, long tag = 0L, bool occupied = false)
        {
            var hull = Hull(size);
            Vector3 along = cleat.right; along.y = 0f; along.Normalize();
            Vector3 off = cleat.forward; off.y = 0f; off.Normalize();
            float reach = Plugin.BerthGap.Value + hull.x;
            // The deeper side is the water.
            float front = 0f, back = 0f;
            foreach (float k in new[] { 0.5f, 1f, 1.5f })
            {
                front += Ground(cleat.position + off * reach * k);
                back += Ground(cleat.position - off * reach * k);
            }
            int side = front <= back ? 1 : -1;
            Vector3 centre = cleat.position + off * side * reach;
            centre.y = Water;
            // Deep enough: under her whole length and beam, with a metre to spare.
            bool deep = true;
            for (int i = -2; i <= 2 && deep; i++)
                for (int j = -1; j <= 1 && deep; j++)
                {
                    Vector3 p = centre + along * hull.y * i * 0.5f + off * side * hull.x * j * 0.8f;
                    if (Ground(p) > Water - Draft(size) - 1f) deep = false;
                }
            return new SailTrimApi.Berth
            {
                CleatTag = tag, Cleat = cleat.TransformPoint(Cleat.RopePoint), Centre = centre,
                AxisYaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg, Size = size, WaterSide = side, Deep = deep, Occupied = occupied,
            };
        }

        internal static List<SailTrimApi.Berth> Near(Vector3 near, float range)
        {
            var list = new List<SailTrimApi.Berth>();
            foreach (var c in CleatPiece.All)
            {
                if (c == null || c.View == null || !c.View.IsValid()) continue;
                if (Vector3.Distance(c.transform.position, near) > range) continue;
                list.Add(Layout(c.transform, c.Size, Tag.Of(c.View), !c.HasNoBoat));
            }
            return list;
        }
    }

    /// <summary>The berths drawn on the water while the hammer is out.</summary>
    internal sealed class BerthOutline : MonoBehaviour
    {
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private readonly List<SailTrimApi.Berth> _shown = new List<SailTrimApi.Berth>();
        private static Material s_mat;
        private GUIStyle _style;
        private float _scanAt;

        internal static void Install(GameObject host) { if (host.GetComponent<BerthOutline>() == null) host.AddComponent<BerthOutline>(); }

        private void Update()
        {
            int used = 0;
            var p = Player.m_localPlayer;
            bool on = Plugin.CleatEnabled.Value && Plugin.BerthOutline.Value && p != null && !p.IsDead() && p.InPlaceMode();
            if (on)
            {
                if (Time.time >= _scanAt)
                {
                    _scanAt = Time.time + 0.25f;
                    _shown.Clear();
                    _shown.AddRange(Berths.Near(p.transform.position, 70f));
                }
                // The one being placed, live.
                var ghost = p.m_placementGhost;
                int gs = ghost != null && ghost.activeInHierarchy ? Cleat.SizeOf(Utils.GetPrefabName(ghost)) : -1;
                if (gs >= 0) used = Draw(Berths.Layout(ghost.transform, gs), used, true);
                foreach (var b in _shown) used = Draw(b, used, false);
            }
            else _shown.Clear();
            for (int i = used; i < _lines.Count; i++) _lines[i].enabled = false;
        }

        private int Draw(SailTrimApi.Berth b, int used, bool ghost)
        {
            var hull = Berths.Hull(b.Size);
            float yaw = b.AxisYaw * Mathf.Deg2Rad;
            Vector3 along = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 across = new Vector3(along.z, 0f, -along.x);
            Vector3 c = b.Centre + Vector3.up * 0.15f;
            Color col = b.Occupied ? new Color(0.4f, 0.65f, 1f, 0.9f) : b.Deep ? new Color(0.45f, 0.95f, 0.45f, 0.9f) : new Color(1f, 0.7f, 0.2f, 0.9f);
            if (ghost) col.a = 1f;
            // The hull's outline, pointed at both ends like a ship's, and a line from it to the cleat.
            var pts = new[]
            {
                c + along * hull.y,
                c + along * hull.y * 0.7f + across * hull.x,
                c - along * hull.y * 0.7f + across * hull.x,
                c - along * hull.y,
                c - along * hull.y * 0.7f - across * hull.x,
                c + along * hull.y * 0.7f - across * hull.x,
            };
            Set(used++, col, ghost ? 0.14f : 0.09f, true, pts);
            Vector3 toCleat = b.Cleat - c; toCleat.y = 0f;
            Set(used++, new Color(col.r, col.g, col.b, 0.5f), 0.05f, false, c + toCleat.normalized * hull.x, new Vector3(b.Cleat.x, b.Cleat.y, b.Cleat.z));
            return used;
        }

        private void Set(int i, Color c, float w, bool loop, params Vector3[] pts)
        {
            while (_lines.Count <= i)
            {
                var go = new GameObject("SailTrim_BerthLine");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                if (s_mat == null) s_mat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
                lr.material = s_mat; lr.useWorldSpace = true; lr.numCapVertices = 2; lr.numCornerVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
                _lines.Add(lr);
            }
            var l = _lines[i];
            l.enabled = true; l.loop = loop;
            l.startColor = l.endColor = c; l.startWidth = l.endWidth = w;
            l.positionCount = pts.Length; l.SetPositions(pts);
        }

        private void OnGUI()
        {
            var p = Player.m_localPlayer;
            var cam = Camera.main;
            if (cam == null || p == null || !p.InPlaceMode() || !Plugin.BerthOutline.Value) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, richText = true };
            void Label(SailTrimApi.Berth b)
            {
                Vector3 s = cam.WorldToScreenPoint(b.Centre + Vector3.up * 1.5f);
                if (s.z <= 0f || s.z > 60f) return;
                string text = $"{Cleat.SizeLabels[b.Size]} berth{(b.Occupied ? " (taken)" : b.Deep ? "" : " (too shallow)")}";
                var r = new Rect(s.x - 100f, Screen.height - s.y - 12f, 200f, 24f);
                var was = GUI.color;
                GUI.color = Color.black; GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _style);
                GUI.color = was; GUI.Label(r, text, _style);
            }
            var ghost = p.m_placementGhost;
            int gs = ghost != null && ghost.activeInHierarchy ? Cleat.SizeOf(Utils.GetPrefabName(ghost)) : -1;
            if (gs >= 0) Label(Berths.Layout(ghost.transform, gs));
            foreach (var b in _shown) Label(b);
        }
    }
}
