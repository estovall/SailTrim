using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SailTrim
{
    /// <summary>
    /// A small readout in the corner of the big map, so a passage can be planned without losing sight of how she
    /// is going. The sailing HUD is drawn over the world and the map covers the world, so with the map up you are
    /// sailing blind; and the HUD can be put anywhere now, which means it is often somewhere the map will cover.
    ///
    /// It carries what matters when your eyes are on the chart rather than the sail: whether she is trimmed, and
    /// where she is actually going. That last is set and drift. A boat does not travel where her bow points: a
    /// stalled sail or a hard-heeled hull slides to leeward, so the course she makes good is off her heading and
    /// a long board can end up well to leeward of where it was aimed. The heading is what the helmsman steers;
    /// the track is what the chart cares about.
    /// </summary>
    internal static class MapHud
    {
        private static RectTransform _panel;
        private static TMP_Text _title, _line1, _line2, _line3, _line4, _line5;
        private static bool _failed;
        private static Vector2 _home;

        private static readonly Color ColTrimmed = new Color(0.62f, 0.92f, 0.55f);
        private static readonly Color ColAdjust = new Color(1f, 0.82f, 0.29f);
        private static readonly Color ColBad = new Color(0.95f, 0.5f, 0.42f);
        private static readonly Color ColText = new Color(0.90f, 0.87f, 0.80f);

        /// <summary>Is the big map up? The small corner map does not cover anything and is left alone.</summary>
        internal static bool LargeMapOpen =>
            Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large;

        internal static void Update()
        {
            if (_failed || !Plugin.MapHudEnabled.Value) { Hide(); return; }
            if (!LargeMapOpen) { Hide(); return; }

            var player = Player.m_localPlayer;
            var ship = player != null ? Plugin.GetShipAboard(player) : null;
            var st = ship != null ? SailTrimShip.Get(ship) : null;
            if (st == null) { Hide(); return; }

            if (_panel == null) Build();
            if (_panel == null) return;
            _panel.gameObject.SetActive(true);
            HudLayout.Apply(HudLayout.Part.Map, _panel, _home);
            PickMark();
            Write(ship, st);
        }

        /// <summary>
        /// The mark is one of the player's own pins, chosen with the cursor over it. Using the pins there are
        /// rather than inventing a marker of our own means the course is set with the tool a player already has
        /// for saying "there", and it shows on the chart without us drawing anything.
        /// </summary>
        private static void PickMark()
        {
            if (Plugin.SetMarkKey.Value == KeyCode.None) return;
            if (!ZInput.GetKeyDown(Plugin.SetMarkKey.Value, false)) return;
            var map = Minimap.instance;
            if (map == null) return;
            var pin = map.GetClosestPinToCursor();
            if (pin == null) { Course.Clear(); return; }
            Course.Set(pin.m_pos, pin.m_name);
        }

        private static void Hide()
        {
            if (_panel != null && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
        }

        private static void Build()
        {
            var map = Minimap.instance;
            var hud = Hud.instance;
            if (map == null || map.m_largeRoot == null || hud == null || hud.m_healthText == null) { _failed = true; return; }

            // A child of the map's own root, so it comes and goes with the map and needs no watching.
            var root = map.m_largeRoot.transform as RectTransform;
            if (root == null) { _failed = true; return; }

            var go = new GameObject("SailTrim_MapHud", typeof(RectTransform));
            _panel = go.GetComponent<RectTransform>();
            _panel.SetParent(root, false);
            // Anchored and pivoted on the map's own bottom-left corner, so it sits in the corner at any screen
            // size or aspect: the inset below is the only number, and it is in the corner's own frame.
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0f, 0f);
            _panel.sizeDelta = new Vector2(Width, Height);
            _home = new Vector2(14f, 14f);
            _panel.anchoredPosition = _home;

            // Something to read it against. The chart is busy and light in places, and unbacked text on top of
            // a snowfield is text you have to hunt for.
            var bgGo = new GameObject("Back", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bg = bgGo.GetComponent<RectTransform>();
            bg.SetParent(_panel, false);
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
            bg.offsetMin = Vector2.zero; bg.offsetMax = Vector2.zero;
            var img = bgGo.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.55f);
            img.raycastTarget = false;

            TMP_Text src = hud.m_healthText;
            _title = Line("Title", src, 21f, 10f);
            _line1 = Line("Line1", src, 17f, 32f);
            _line2 = Line("Line2", src, 17f, 52f);
            _line3 = Line("Line3", src, 17f, 72f);
            _line4 = Line("Line4", src, 17f, 94f);
            _line5 = Line("Line5", src, 15f, 113f);
            Plugin.Log.LogInfo("SailTrim: map readout built");
        }

        private const float Width = 260f, Height = 132f;

        /// <summary>A line of the block, measured down from the top-left of the panel.</summary>
        private static TMP_Text Line(string name, TMP_Text src, float size, float down)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_panel, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, -down);
            rt.sizeDelta = new Vector2(Width - 20f, 20f);
            rt.localScale = Vector3.one;
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = src.font;
            t.fontSharedMaterial = src.fontSharedMaterial;
            t.fontStyle = src.fontStyle;
            t.outlineWidth = src.outlineWidth;
            t.outlineColor = src.outlineColor;
            t.enableAutoSizing = false;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = false;
            t.raycastTarget = false;
            t.color = ColText;
            return t;
        }

        /// <summary>Compass bearing of a world direction. The map's north is +Z, as the game draws it.</summary>
        private static float Bearing(Vector3 dir)
        {
            float b = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            return b < 0f ? b + 360f : b;
        }

        private static void Write(Ship ship, SailTrimShip st)
        {
            string state = StateWord(st.State);
            _title.text = state;
            _title.color = StateColour(st.State);

            float kn = st.SpeedKnots;
            _line1.text = $"{kn:0.0} kn   sheet {st.SheetAngle:0}   heel {Mathf.Abs(st.HeelAngle):0}";

            // Set and drift. Below a walking pace the track is noise, so it is not reported at all rather than
            // spun round the compass by the wash.
            float heading = Bearing(ship.transform.forward);
            Vector3 vel = ship.m_body != null ? ship.m_body.linearVelocity : Vector3.zero;
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            if (flat.magnitude * 1.94384f < 0.4f)
            {
                _line2.text = $"heading {heading:000}   track --";
                _line3.text = "not making way";
                _line3.color = ColText;
                return;
            }

            float track = Bearing(flat);
            float off = Mathf.DeltaAngle(heading, track);
            float sideways = Mathf.Abs(Vector3.Dot(flat, ship.transform.right)) * 1.94384f;
            _line2.text = $"heading {heading:000}   track {track:000}";

            // A couple of degrees is the sea moving her about, not leeway worth steering for.
            if (Mathf.Abs(off) < 2.5f)
            {
                _line3.text = "holding her course";
                _line3.color = ColTrimmed;
            }
            else
            {
                _line3.text = $"set {Mathf.Abs(off):0} {(off > 0f ? "stbd" : "port")}   drift {sideways:0.0} kn";
                _line3.color = Mathf.Abs(off) > 8f ? ColBad : ColAdjust;
            }
            Mark(ship, st);
        }

        private static void Mark(Ship ship, SailTrimShip st)
        {
            var fix = Course.Reckon(ship);
            if (!fix.Valid)
            {
                _line4.text = "no mark set";
                _line4.color = ColText;
                _line5.text = $"[{Plugin.SetMarkKey.Value}] over one of your pins to steer for it";
                _line5.color = ColText;
                return;
            }
            _line4.text = Course.Line(fix);
            _line4.color = Mathf.Abs(fix.Off) < 3f ? ColTrimmed : (Mathf.Abs(fix.Off) > 15f ? ColBad : ColAdjust);
            string helm = Course.HelmAdvice(ship, st);
            _line5.text = helm != "" ? helm : $"[{Plugin.SetMarkKey.Value}] away from a pin to give it up";
            _line5.color = helm != "" ? ColAdjust : ColText;
        }

        private static string StateWord(SailTrimShip.TrimState s)
        {
            switch (s)
            {
                case SailTrimShip.TrimState.Luffing: return "Luffing";
                case SailTrimShip.TrimState.Stalled: return "Stalled";
                case SailTrimShip.TrimState.Backwinded: return "Aback";
                case SailTrimShip.TrimState.NoseDiving: return "Bow buried";
                case SailTrimShip.TrimState.Spilled: return "Spilled";
                case SailTrimShip.TrimState.OverTrimmed: return "Ease out";
                case SailTrimShip.TrimState.UnderTrimmed: return "Sheet in";
                default: return "Trimmed";
            }
        }

        private static Color StateColour(SailTrimShip.TrimState s)
        {
            switch (s)
            {
                case SailTrimShip.TrimState.Luffing:
                case SailTrimShip.TrimState.Stalled:
                case SailTrimShip.TrimState.Backwinded:
                case SailTrimShip.TrimState.NoseDiving: return ColBad;
                case SailTrimShip.TrimState.OverTrimmed:
                case SailTrimShip.TrimState.UnderTrimmed: return ColAdjust;
                default: return ColTrimmed;
            }
        }
    }
}
