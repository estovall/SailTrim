using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace SailTrim
{
    /// <summary>
    /// Model helpers for the cleat and the buoy: copying the visible parts of the game's own prefabs (so the pieces
    /// wear real game art), procedural meshes and textures, and rendering a piece's icon from its model.
    /// Nothing here runs on a dedicated server: without a graphics device the pieces are colliders only.
    /// </summary>
    internal static class Models
    {
        internal static bool Headless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        internal static string CacheDir
        {
            get
            {
                string d = Path.Combine(BepInEx.Paths.BepInExRootPath, "cache", "SailTrim");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        // ------------------------------------------------------------------
        // Finding the game's prefabs
        // ------------------------------------------------------------------
        /// <summary>A game prefab by name: the world's list when a world is up, else items and every tool's piece table (the main menu has those).</summary>
        internal static GameObject Find(ObjectDB db, string name)
        {
            var scene = ZNetScene.instance;
            if (scene != null)
            {
                var p = scene.GetPrefab(name);
                if (p != null) return p;
            }
            if (db == null) return null;
            var item = db.GetItemPrefab(name);
            if (item != null) return item;
            foreach (var it in db.m_items)
            {
                if (it == null) continue;
                var drop = it.GetComponent<ItemDrop>();
                var table = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null ? drop.m_itemData.m_shared.m_buildPieces : null;
                if (table == null) continue;
                foreach (var p in table.m_pieces)
                    if (p != null && p.name == name) return p;
            }
            return null;
        }

        internal static GameObject FindFirst(ObjectDB db, out string found, params string[] names)
        {
            foreach (var n in names)
            {
                var p = Find(db, n);
                if (p != null) { found = n; return p; }
            }
            found = null;
            return null;
        }

        // ------------------------------------------------------------------
        // Copying a prefab's visible parts
        // ------------------------------------------------------------------
        /// <summary>
        /// A new child of parent holding copies of src's visible meshes (the undamaged, most detailed ones), placed
        /// as they are in src, with src's materials. bounds is the copy's extent in the child's own space.
        /// </summary>
        internal static GameObject CopyVisual(GameObject src, Transform parent, string name, out Bounds bounds)
            => CopyVisual(src, parent, name, out bounds, false);

        /// <summary>
        /// The game's pieces are statically batched: the mesh a piece renders holds its vertices in the world
        /// coordinates of whatever scene baked it, tens of units from its own origin, and the renderer carries a
        /// matching negative translation to put it back. Reusing that mesh reproduces the offset, and every part
        /// we build out of one is then drawn from the difference of two large numbers. Baking rewrites the
        /// vertices into the part's own space, around its own origin, and keeps only the triangles inside this
        /// renderer's own bounds, so nothing of a neighbour in the same batch comes along with it.
        /// </summary>
        internal static GameObject CopyVisual(GameObject src, Transform parent, string name, out Bounds bounds, bool bake)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.layer = parent.gameObject.layer;
            bounds = new Bounds();
            bool any = false;
            foreach (var r in VisibleRenderers(src))
            {
                Mesh mesh = null;
                if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    mesh = mf != null ? mf.sharedMesh : null;
                }
                else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                if (mesh == null) continue;
                Matrix4x4 m = src.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var go = new GameObject(r.gameObject.name);
                go.layer = part.layer;
                go.transform.SetParent(part.transform, false);
                Mesh use = mesh;
                if (bake)
                {
                    var baked = Bake(r, mesh, m);
                    if (baked != null)
                    {
                        use = baked;
                        m = Matrix4x4.identity;   // the vertices already carry it
                    }
                }
                if (m != Matrix4x4.identity)
                {
                    go.transform.localPosition = m.GetColumn(3);
                    go.transform.localRotation = m.rotation;
                    go.transform.localScale = m.lossyScale;
                }
                go.AddComponent<MeshFilter>().sharedMesh = use;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                // A thin plank self-shadowing at a grazing angle is acne, and the shadow cascades shift with the
                // camera every frame, so it reads as the whole surface flashing between lit and dark.
                mr.shadowCastingMode = bake ? ShadowCastingMode.Off : ShadowCastingMode.On;
                var b = use.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 w = m.MultiplyPoint3x4(c);
                    if (!any) { bounds = new Bounds(w, Vector3.zero); any = true; }
                    else bounds.Encapsulate(w);
                }
            }
            if (!any) { Object.Destroy(part); return null; }
            return part;
        }

        /// <summary>
        /// This renderer's own geometry, in the part's space, around the origin. Triangles outside the renderer's
        /// own world bounds belong to other members of the same static batch and are left behind. Returns null if
        /// the mesh cannot be read, in which case the caller keeps the original and its offset.
        /// </summary>
        private static Mesh Bake(Renderer r, Mesh mesh, Matrix4x4 m)
        {
            try
            {
                if (!mesh.isReadable) return null;
                var verts = mesh.vertices;
                if (verts.Length == 0) return null;
                var norms = mesh.normals;
                var uvs = mesh.uv;
                var uv2 = mesh.uv2;
                var cols = mesh.colors;
                var tans = mesh.tangents;

                // The renderer's own slice of the batch, in mesh space.
                Matrix4x4 w2l = r.transform.worldToLocalMatrix;
                Bounds wb = r.bounds;
                Bounds keep = new Bounds(w2l.MultiplyPoint3x4(wb.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = wb.center + Vector3.Scale(wb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    keep.Encapsulate(w2l.MultiplyPoint3x4(c));
                }
                keep.Expand(0.02f);

                var map = new Dictionary<int, int>();
                var outV = new List<Vector3>();
                var outN = new List<Vector3>();
                var outU = new List<Vector2>();
                var outU2 = new List<Vector2>();
                var outC = new List<Color>();
                var outTan = new List<Vector4>();
                var subs = new List<List<int>>();
                bool any = false;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var tris = mesh.GetTriangles(sm);
                    var outT = new List<int>();
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        if (!keep.Contains(verts[tris[t]]) || !keep.Contains(verts[tris[t + 1]]) || !keep.Contains(verts[tris[t + 2]])) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            int vi = tris[t + k];
                            if (!map.TryGetValue(vi, out int ni))
                            {
                                ni = outV.Count;
                                map[vi] = ni;
                                outV.Add(m.MultiplyPoint3x4(verts[vi]));
                                if (norms.Length == verts.Length) outN.Add(m.MultiplyVector(norms[vi]).normalized);
                                if (uvs.Length == verts.Length) outU.Add(uvs[vi]);
                                // A game shader reads more than position and uv: vertex colour carries wear and
                                // snow, the tangent carries the normal map. Dropping them lights the surface from
                                // nowhere in particular.
                                if (uv2.Length == verts.Length) outU2.Add(uv2[vi]);
                                if (cols.Length == verts.Length) outC.Add(cols[vi]);
                                if (tans.Length == verts.Length)
                                {
                                    Vector3 td = m.MultiplyVector(new Vector3(tans[vi].x, tans[vi].y, tans[vi].z)).normalized;
                                    outTan.Add(new Vector4(td.x, td.y, td.z, tans[vi].w));
                                }
                            }
                            outT.Add(ni);
                        }
                    }
                    if (outT.Count > 0) any = true;
                    subs.Add(outT);
                }
                if (!any || outV.Count == 0) return null;

                var baked = new Mesh { name = mesh.name + "_SailTrim" };
                baked.SetVertices(outV);
                if (outN.Count == outV.Count) baked.SetNormals(outN);
                if (outU.Count == outV.Count) baked.SetUVs(0, outU);
                if (outU2.Count == outV.Count) baked.SetUVs(1, outU2);
                if (outC.Count == outV.Count) baked.SetColors(outC);
                if (outTan.Count == outV.Count) baked.SetTangents(outTan);
                baked.subMeshCount = subs.Count;
                for (int sm = 0; sm < subs.Count; sm++) baked.SetTriangles(subs[sm], sm);
                if (outN.Count != outV.Count) baked.RecalculateNormals();
                baked.RecalculateBounds();
                return baked;
            }
            catch { return null; }
        }

        private static List<Renderer> VisibleRenderers(GameObject src)
        {
            var result = new List<Renderer>();
            Transform root = src.transform;
            var wnt = src.GetComponent<WearNTear>();
            if (wnt != null && wnt.m_new != null) root = wnt.m_new.transform;
            var lod = src.GetComponentInChildren<LODGroup>(true);
            if (lod != null)
            {
                var lods = lod.GetLODs();
                if (lods.Length > 0)
                    foreach (var r in lods[0].renderers)
                        if (Usable(r, src.transform) && (root == src.transform || r.transform.IsChildOf(root)) && !IsAlternate(r.transform, src.transform)) result.Add(r);
            }
            if (result.Count == 0)
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (Usable(r, src.transform) && !IsAlternate(r.transform, src.transform)) result.Add(r);
            return result;
        }

        private static bool Usable(Renderer r, Transform top)
        {
            if (r == null || !r.enabled) return false;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) return false;
            for (var t = r.transform; t != null && t != top; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

        private static bool IsAlternate(Transform t, Transform top)
        {
            for (; t != null && t != top; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("lod") || n.Contains("broken") || n.Contains("worn") || n.Contains("destruction") || n.Contains("fragment"))
                    return true;
            }
            return false;
        }

        /// <summary>Scale and turn part (rot, then uniform scale), and put the point of its bounds at anchor (fractions 0..1 per axis) on target.</summary>
        internal static void Place(GameObject part, Bounds b, Vector3 anchor, Vector3 target, float scale, Quaternion rot)
        {
            Vector3 a = b.min + Vector3.Scale(b.size, anchor);
            part.transform.localRotation = rot;
            part.transform.localScale = Vector3.one * scale;
            part.transform.localPosition = target - rot * (a * scale);
        }

        /// <summary>Same with a separate scale per axis (of the part's own space).</summary>
        internal static void Place(GameObject part, Bounds b, Vector3 anchor, Vector3 target, Vector3 scale, Quaternion rot)
        {
            Vector3 a = b.min + Vector3.Scale(b.size, anchor);
            part.transform.localRotation = rot;
            part.transform.localScale = scale;
            part.transform.localPosition = target - rot * Vector3.Scale(a, scale);
        }

        // ------------------------------------------------------------------
        // Procedural textures and materials
        // ------------------------------------------------------------------
        /// <summary>A tileable texture: base colour with value noise (grain along y when streaks is above 0).</summary>
        internal static Texture2D NoiseTexture(Color baseColor, float amount, int seed, float streaks = 0f, int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var rnd = new System.Random(seed);
            int g = 8;
            var grid = new float[g * g];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)rnd.NextDouble();
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = x / (float)size * g, fy = y / (float)size * g;
                    int x0 = (int)fx, y0 = (int)fy; float tx = fx - x0, ty = fy - y0;
                    float a = grid[(y0 % g) * g + x0 % g], b = grid[(y0 % g) * g + (x0 + 1) % g];
                    float c = grid[((y0 + 1) % g) * g + x0 % g], d = grid[((y0 + 1) % g) * g + (x0 + 1) % g];
                    float n = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                    float fine = (float)rnd.NextDouble();
                    float s = streaks > 0f ? Mathf.Sin((x + n * 6f) * 0.9f) * 0.5f + 0.5f : 0.5f;
                    float v = 1f + amount * ((n - 0.5f) * 1.2f + (fine - 0.5f) * 0.5f + (s - 0.5f) * streaks);
                    px[y * size + x] = new Color(baseColor.r * v, baseColor.g * v, baseColor.b * v, 1f);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        /// <summary>A lit material on the Standard shader (null without one), for the procedural parts.</summary>
        /// <summary>
        /// A material of the game's that uses the Standard shader and renders (the lantern item's). The Standard
        /// shader looked up by name renders magenta in this build: only the variants the game's own materials use
        /// are in it. Set once a world's ObjectDB is up.
        /// </summary>
        internal static Material StandardTemplate;

        internal static void FindStandardTemplate(ObjectDB db)
        {
            if (StandardTemplate != null || db == null) return;
            foreach (var name in new[] { "Lantern", "piece_hoodedlantern", "piece_snowlantern" })
            {
                var p = Find(db, name);
                if (p == null) continue;
                foreach (var r in p.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == "Standard")
                    { StandardTemplate = r.sharedMaterial; return; }
            }
        }

        /// <summary>A lit Standard material from the game's template (null before a world's ObjectDB is up).</summary>
        internal static Material Standard(Texture2D tex, float metallic, float smoothness)
        {
            if (StandardTemplate == null) return null;
            var m = new Material(StandardTemplate) { name = "SailTrim_Standard" };
            foreach (var p in m.GetTexturePropertyNames())
                if (p != "_MainTex") m.SetTexture(p, null);
            m.SetTexture("_MainTex", tex);
            m.mainTextureScale = Vector2.one;
            m.mainTextureOffset = Vector2.zero;
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", smoothness);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            m.DisableKeyword("_NORMALMAP");
            m.DisableKeyword("_METALLICGLOSSMAP");
            return m;
        }

        /// <summary>A copy of a game material wearing another main texture (its other maps are dropped, since they belong to the old one).</summary>
        internal static Material Retextured(Material src, Texture2D tex)
        {
            var m = new Material(src);
            foreach (var p in m.GetTexturePropertyNames())
            {
                if (p == "_MainTex") m.SetTexture(p, tex);
                else if (p == "_BumpMap" || p == "_MetallicGlossMap" || p == "_MetalTex" || p == "_EmissionMap" || p == "_StyleTex" || p == "_NoiseTex")
                    m.SetTexture(p, null);
            }
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            return m;
        }

        // ------------------------------------------------------------------
        // Procedural meshes
        // ------------------------------------------------------------------
        internal class MeshBuilder
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector3> _n = new List<Vector3>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int> _t = new List<int>();

            /// <summary>A box with its own faces (flat shading), UVs in metres so a tiling texture keeps its scale.</summary>
            internal void Box(Vector3 center, Vector3 size)
            {
                Vector3 h = size * 0.5f;
                for (int axis = 0; axis < 3; axis++)
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        Vector3 n = Vector3.zero; n[axis] = sgn;
                        Vector3 u = Vector3.zero, v = Vector3.zero;
                        u[(axis + 1) % 3] = 1f; v[(axis + 2) % 3] = 1f;
                        if (sgn < 0) { var tmp = u; u = v; v = tmp; }
                        int b0 = _v.Count;
                        for (int k = 0; k < 4; k++)
                        {
                            float su = (k == 1 || k == 2) ? 1f : -1f, sv = (k >= 2) ? 1f : -1f;
                            Vector3 p = center + Vector3.Scale(n + u * su + v * sv, h);
                            _v.Add(p); _n.Add(n);
                            _uv.Add(new Vector2(Vector3.Dot(p, u) * 2f, Vector3.Dot(p, v) * 2f));
                        }
                        _t.Add(b0); _t.Add(b0 + 2); _t.Add(b0 + 1);
                        _t.Add(b0); _t.Add(b0 + 3); _t.Add(b0 + 2);
                    }
            }

            /// <summary>A round tube from a to b, its radius along the way given by radius(t), t from 0 to 1; the ends closed to a point.</summary>
            internal void Tube(Vector3 a, Vector3 b, System.Func<float, float> radius, int sides = 16, int rings = 12, bool capA = true, bool capB = true)
            {
                Vector3 axis = (b - a).normalized;
                Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 up = Vector3.Cross(side, axis);
                float len = Vector3.Distance(a, b);
                int b0 = _v.Count;
                for (int i = 0; i <= rings; i++)
                {
                    float t = i / (float)rings;
                    float r = radius(t);
                    float dr = (radius(Mathf.Min(1f, t + 0.01f)) - radius(Mathf.Max(0f, t - 0.01f))) / (0.02f * len);
                    Vector3 c = Vector3.Lerp(a, b, t);
                    for (int s = 0; s <= sides; s++)
                    {
                        float ang = s / (float)sides * Mathf.PI * 2f;
                        Vector3 dir = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                        _v.Add(c + dir * r);
                        _n.Add((dir - axis * dr).normalized);
                        _uv.Add(new Vector2(s / (float)sides * 2f * Mathf.PI * Mathf.Max(0.02f, r) * 2f, t * len * 2f));
                    }
                }
                int row = sides + 1;
                for (int i = 0; i < rings; i++)
                    for (int s = 0; s < sides; s++)
                    {
                        int p0 = b0 + i * row + s, p1 = p0 + 1, p2 = p0 + row, p3 = p2 + 1;
                        _t.Add(p0); _t.Add(p2); _t.Add(p1);
                        _t.Add(p1); _t.Add(p2); _t.Add(p3);
                    }
                if (capA) Cap(a - axis * 0.001f, -axis, b0, row, sides, true);
                if (capB) Cap(b + axis * 0.001f, axis, b0 + rings * row, row, sides, false);
            }

            private void Cap(Vector3 c, Vector3 n, int ringStart, int row, int sides, bool flip)
            {
                int ci = _v.Count;
                _v.Add(c); _n.Add(n); _uv.Add(Vector2.zero);
                for (int s = 0; s < sides; s++)
                {
                    int p0 = ringStart + s, p1 = p0 + 1;
                    if (flip) { _t.Add(ci); _t.Add(p1); _t.Add(p0); }
                    else { _t.Add(ci); _t.Add(p0); _t.Add(p1); }
                }
            }

            /// <summary>A round tube of constant radius swept along a path (a rope), frames carried along without twisting.</summary>
            internal void Sweep(IList<Vector3> path, float radius, int sides = 8)
            {
                if (path.Count < 2) return;
                int b0 = _v.Count;
                Vector3 prevT = (path[1] - path[0]).normalized;
                Vector3 n = Vector3.Cross(prevT, Mathf.Abs(prevT.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                float along = 0f;
                for (int i = 0; i < path.Count; i++)
                {
                    Vector3 tan = i == 0 ? path[1] - path[0] : (i == path.Count - 1 ? path[i] - path[i - 1] : path[i + 1] - path[i - 1]);
                    tan.Normalize();
                    // Parallel transport: turn the previous normal by the change in direction.
                    n = Quaternion.FromToRotation(prevT, tan) * n;
                    n = (n - tan * Vector3.Dot(n, tan)).normalized;
                    prevT = tan;
                    Vector3 bn = Vector3.Cross(tan, n);
                    if (i > 0) along += Vector3.Distance(path[i], path[i - 1]);
                    for (int s2 = 0; s2 <= sides; s2++)
                    {
                        float ang = s2 / (float)sides * Mathf.PI * 2f;
                        Vector3 dir = n * Mathf.Cos(ang) + bn * Mathf.Sin(ang);
                        _v.Add(path[i] + dir * radius);
                        _n.Add(dir);
                        _uv.Add(new Vector2(s2 / (float)sides, along / (radius * 6f)));
                    }
                }
                int row = sides + 1;
                for (int i = 0; i < path.Count - 1; i++)
                    for (int s2 = 0; s2 < sides; s2++)
                    {
                        int p0 = b0 + i * row + s2, p1 = p0 + 1, p2 = p0 + row, p3 = p2 + 1;
                        _t.Add(p0); _t.Add(p1); _t.Add(p2);
                        _t.Add(p1); _t.Add(p3); _t.Add(p2);
                    }
            }

            internal Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _uv); m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
        }

        internal static GameObject MeshPart(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        // ------------------------------------------------------------------
        // Icons
        // ------------------------------------------------------------------
        /// <summary>
        /// The visual child rendered on its own against a clear background, three-quarter view from above, as a
        /// sprite; the image is also written to BepInEx\cache\SailTrim\{file}.png. Null on failure.
        /// </summary>
        private static int _iconCount;

        internal static Sprite RenderIcon(GameObject visual, string file, int size = 256, float yaw = 145f, float pitch = 22f)
        {
            if (Headless || visual == null) return null;
            // Rendered at twice the size and averaged down: the game draws deferred, which has no MSAA.
            int big = size * 2;
            Vector3 spot = new Vector3(40f * (++_iconCount % 50), 6000f, 0f);
            int layer = FreeLayer();
            GameObject copy = null, camGo = null, lightGo = null;
            RenderTexture rt = null;
            var fog = RenderSettings.fog; var ambMode = RenderSettings.ambientMode; var amb = RenderSettings.ambientLight; var ambI = RenderSettings.ambientIntensity;
            try
            {
                copy = Object.Instantiate(visual);
                copy.name = "SailTrim_IconModel";
                copy.transform.SetParent(null, false);
                copy.transform.position = spot;
                copy.transform.rotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one;
                copy.SetActive(true);
                var bounds = new Bounds(); bool any = false;
                foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    r.gameObject.layer = layer;
                    if (!(r is MeshRenderer)) { r.enabled = false; continue; }
                    if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                }
                foreach (var l in copy.GetComponentsInChildren<Light>(true)) l.enabled = false;
                if (!any) return null;

                camGo = new GameObject("SailTrim_IconCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 1 << layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                // Deferred, as the game draws: this build has no forward pass for the Standard shader (magenta).
                cam.renderingPath = RenderingPath.DeferredShading;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.orthographic = true;
                Quaternion view = Quaternion.Euler(pitch, yaw, 0f);
                float radius = bounds.extents.magnitude;
                cam.transform.rotation = view;
                cam.transform.position = bounds.center - view * Vector3.forward * (radius * 4f);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = radius * 8f;
                cam.orthographicSize = radius * 1.02f;

                lightGo = new GameObject("SailTrim_IconLight");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.cullingMask = 1 << layer;
                light.intensity = 1.25f;
                light.color = new Color(1f, 0.96f, 0.9f);
                light.shadows = LightShadows.None;
                lightGo.transform.rotation = Quaternion.Euler(45f, yaw - 40f, 0f);
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.52f);
                RenderSettings.ambientIntensity = 1f;

                rt = new RenderTexture(big, big, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var black = Grab(cam, rt, Color.black, big);
                var white = Grab(cam, rt, Color.white, big);
                cam.targetTexture = null;

                // Alpha from how much the background shows through; colour unmultiplied from the black render;
                // each output pixel the average of four.
                var px = new Color[size * size];
                var pb = black.GetPixels(); var pw = white.GetPixels();
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float r = 0f, g = 0f, bl = 0f, a = 0f;
                        for (int k = 0; k < 4; k++)
                        {
                            int i = (y * 2 + (k >> 1)) * big + x * 2 + (k & 1);
                            float ak = Mathf.Clamp01(1f - Mathf.Max(pw[i].r - pb[i].r, Mathf.Max(pw[i].g - pb[i].g, pw[i].b - pb[i].b)));
                            r += pb[i].r; g += pb[i].g; bl += pb[i].b; a += ak;
                        }
                        r *= 0.25f; g *= 0.25f; bl *= 0.25f; a *= 0.25f;
                        px[y * size + x] = a > 0.004f ? new Color(Mathf.Clamp01(r / a), Mathf.Clamp01(g / a), Mathf.Clamp01(bl / a), a) : new Color(0f, 0f, 0f, 0f);
                    }
                Object.DestroyImmediate(black); Object.DestroyImmediate(white);
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.SetPixels(px);
                tex.Apply();
                try { File.WriteAllBytes(Path.Combine(CacheDir, file + ".png"), ImageConversion.EncodeToPNG(tex)); }
                catch (System.Exception e) { Plugin.Log.LogWarning("SailTrim: could not save " + file + ".png: " + e.Message); }
                return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("SailTrim: icon render failed for " + file + ": " + e.Message);
                return null;
            }
            finally
            {
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = amb; RenderSettings.ambientIntensity = ambI;
                // Gone now, not at the end of the frame: the next icon is rendered in this same frame.
                if (copy != null) Object.DestroyImmediate(copy);
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (lightGo != null) Object.DestroyImmediate(lightGo);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            }
        }

        private static Texture2D Grab(Camera cam, RenderTexture rt, Color bg, int size)
        {
            cam.backgroundColor = bg;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }

        private static int FreeLayer()
        {
            // An unnamed layer nobody draws; 31 is left alone (another of Max's mods renders on it).
            for (int l = 30; l >= 8; l--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l;
            return 30;
        }

        // ------------------------------------------------------------------
        // Previews of candidate game models (only when BepInEx\cache\SailTrim\preview.request exists)
        // ------------------------------------------------------------------
        private static bool _previewsDone;

        internal static void MaybeRenderCandidatePreviews(ObjectDB db)
        {
            if (_previewsDone || Headless || db == null || db.GetItemPrefab("Hammer") == null) return;
            string req = Path.Combine(CacheDir, "preview.request");
            if (!File.Exists(req)) return;
            _previewsDone = true;
            var names = new List<string>();
            try { foreach (var line in File.ReadAllLines(req)) if (!string.IsNullOrWhiteSpace(line)) names.Add(line.Trim()); } catch { }
            var holder = new GameObject("SailTrim_PreviewHolder");
            holder.SetActive(false);
            var log = new List<string>();
            foreach (var n in names)
            {
                var src = Find(db, n);
                if (src == null) { log.Add(n + ": not found"); continue; }
                var part = CopyVisual(src, holder.transform, n, out var b);
                if (part == null) { log.Add(n + ": no visible meshes"); continue; }
                log.Add($"{n}: bounds centre {b.center} size {b.size}, {part.transform.childCount} meshes: " + string.Join(", ", ChildNames(part)));
                RenderIcon(part, "candidate_" + n, 256);
            }
            Object.DestroyImmediate(holder);
            try { File.WriteAllLines(Path.Combine(CacheDir, "candidates.txt"), log.ToArray()); } catch { }
            Plugin.Log.LogInfo("SailTrim: rendered " + names.Count + " candidate previews to " + CacheDir);
        }

        private static IEnumerable<string> ChildNames(GameObject part)
        {
            foreach (Transform c in part.transform)
            {
                var mf = c.GetComponent<MeshFilter>(); var mr = c.GetComponent<MeshRenderer>();
                string mats = mr != null ? string.Join("/", System.Array.ConvertAll(mr.sharedMaterials, m => m != null ? m.name + "(" + (m.shader != null ? m.shader.name : "?") + ")" : "null")) : "";
                yield return c.name + "[" + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?") + "|" + mats + "]";
            }
        }
    }
}
