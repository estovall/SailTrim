using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// A way to look at the boats without being at the keyboard. Press SurveyKey and every ship near the player is
    /// photographed from fixed angles into BepInEx\cache\SailTrim\survey\, next to a text file of the numbers each
    /// gangway measured for itself. It exists so placement can be judged and corrected from the pictures rather
    /// than by description; it costs nothing when the key is not pressed.
    /// </summary>
    internal static class Survey
    {
        private static int _run;

        internal static string Dir
        {
            get
            {
                string d = Path.Combine(Models.CacheDir, "survey");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        internal static void Update()
        {
            if (Models.Headless || Plugin.SurveyKey.Value == KeyCode.None) return;
            var p = Player.m_localPlayer;
            if (p == null || !p.TakeInput()) return;
            if (!ZInput.GetKeyDown(Plugin.SurveyKey.Value, false)) return;
            try { Capture(); }
            catch (System.Exception e) { Plugin.Log.LogError("SailTrim: survey failed: " + e); }
        }

        private static void Capture()
        {
            var player = Player.m_localPlayer;
            var ships = new List<Ship>();
            foreach (var s in Object.FindObjectsByType<Ship>(FindObjectsSortMode.None))
                if (s != null && Vector3.Distance(s.transform.position, player.transform.position) < 80f) ships.Add(s);
            if (ships.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center, "No boats within 80 m to survey");
                return;
            }

            _run++;
            var notes = new StringBuilder();
            notes.AppendLine($"SailTrim survey {_run}, {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}, {ships.Count} boat(s)");
            int shots = 0;
            foreach (var ship in ships)
            {
                string name = ship.name.Replace("(Clone)", "");
                string state = Gangway.AnyDown(ship) ? "down" : (Gangway.Fitted(ship, 1) || Gangway.Fitted(ship, -1) ? "stowed" : "bare");
                notes.AppendLine();
                notes.AppendLine($"--- {name} [{state}] ---");
                Describe(ship, notes);

                Bounds b = HullBounds(ship);
                string stem = $"{name}_{state}";
                // Abeam from starboard, a player's three-quarter view, and close on the starboard mount.
                shots += Shot(ship, b, stem + "_beam", ship.transform.right, 8f, 1.0f) ? 1 : 0;
                shots += Shot(ship, b, stem + "_quarter", (ship.transform.right * 1.1f + ship.transform.forward * 0.7f + Vector3.up * 0.75f).normalized, 30f, 1.0f) ? 1 : 0;
                shots += Shot(ship, b, stem + "_astern", (-ship.transform.forward * 1.2f + ship.transform.right * 0.35f + Vector3.up * 0.5f).normalized, 22f, 1.0f) ? 1 : 0;
                var mount = ship.transform.Find("SailTrim_Gangway_S");
                if (mount != null)
                {
                    var mb = new Bounds(mount.position, Vector3.one * 4.5f);
                    shots += Shot(ship, mb, stem + "_mount", (ship.transform.right * 1.2f + ship.transform.forward * 0.5f + Vector3.up * 0.5f).normalized, 22f, 1.0f) ? 1 : 0;
                }
            }
            File.WriteAllText(Path.Combine(Dir, "survey.txt"), notes.ToString());
            Plugin.Log.LogInfo($"SailTrim: survey wrote {shots} image(s) to {Dir}");
            player.Message(MessageHud.MessageType.Center, $"Surveyed {ships.Count} boat(s): {shots} images");
        }

        /// <summary>The numbers each mount worked out for itself, so a picture can be turned into a correction.</summary>
        private static void Describe(Ship ship, StringBuilder notes)
        {
            var fc = ship.m_floatCollider;
            if (fc != null)
                notes.AppendLine($"float collider size {fc.size}, centre(local) {ship.transform.InverseTransformPoint(fc.transform.TransformPoint(fc.center))}");
            Bounds b = HullBounds(ship);
            notes.AppendLine($"hull bounds size {b.size}, centre(local) {ship.transform.InverseTransformPoint(b.center)}");
            foreach (int side in Gangway.Sides)
            {
                var t = ship.transform.Find(side < 0 ? "SailTrim_Gangway_P" : "SailTrim_Gangway_S");
                string s = t == null ? "no mount" : $"mount(local) {t.localPosition}";
                var m = t != null ? t.GetComponentInChildren<GangwayMount>(true) : null;
                if (m != null) s += $", {m.Describe()}";
                notes.AppendLine($"{Gangway.SideName(side)}: fitted {Gangway.Fitted(ship, side)}, down {Gangway.Down(ship, side)}, {s}");
                // The section across the beam the rail was picked out of: x:top, outboard first.
                if (m != null && m.ProfileText().Length > 0) notes.AppendLine("  profile " + m.ProfileText());
            }
        }

        /// <summary>
        /// The hull, not the rig. Framing on the renderers put the camera far enough back to fit a twenty metre
        /// mast and sail, which buried it in the hillside and left the boat a speck; the float collider is the
        /// hull itself, and a little room round it is what wants looking at.
        /// </summary>
        private static Bounds HullBounds(Ship ship)
        {
            var fc = ship.m_floatCollider;
            if (fc != null)
            {
                Vector3 c = fc.transform.TransformPoint(fc.center);
                Vector3 sz = fc.size;
                return new Bounds(c + Vector3.up * Mathf.Max(1f, sz.y * 0.75f),
                                  new Vector3(sz.x * 1.5f, Mathf.Max(3f, sz.y * 2.5f), sz.z * 1.15f));
            }
            Bounds b = new Bounds(ship.transform.position, Vector3.one * 8f);
            return b;
        }

        /// <summary>
        /// One picture of the live scene: a camera copied from the game's own (so fog, layers and the rendering
        /// path all match), moved to frame these bounds from this direction.
        /// </summary>
        private static bool Shot(Ship ship, Bounds b, string file, Vector3 dir, float pitchDeg, float fill)
        {
            var main = Utils.GetMainCamera();
            if (main == null) return false;
            int w = Plugin.SurveyWidth.Value, h = Mathf.RoundToInt(Plugin.SurveyWidth.Value * 9f / 16f);

            GameObject camGo = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                camGo = new GameObject("SailTrim_SurveyCam");
                var cam = camGo.AddComponent<Camera>();
                cam.CopyFrom(main);
                cam.enabled = false;
                cam.targetTexture = null;

                Vector3 d = dir.normalized;
                // Tip the eye down by the asked-for angle about the horizontal across the view.
                Vector3 flat = new Vector3(d.x, 0f, d.z);
                if (flat.sqrMagnitude < 1e-4f) flat = ship.transform.right;
                flat.Normalize();
                Vector3 axis = Vector3.Cross(Vector3.up, flat);
                Vector3 eyeDir = Quaternion.AngleAxis(-pitchDeg, axis) * flat;

                float radius = Mathf.Max(0.5f, b.extents.magnitude);
                float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(0.2f, fill);
                cam.transform.position = b.center + eyeDir * dist;
                cam.transform.rotation = Quaternion.LookRotation(-eyeDir, Vector3.up);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = Mathf.Max(600f, dist * 4f);

                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;

                File.WriteAllBytes(Path.Combine(Dir, file + ".png"), tex.EncodeToPNG());
                return true;
            }
            finally
            {
                if (tex != null) Object.Destroy(tex);
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
                if (camGo != null) Object.Destroy(camGo);
            }
        }
    }
}
