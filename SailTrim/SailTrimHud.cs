using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SailTrim
{
    /// <summary>
    /// uGUI overlay built onto the vanilla ship HUD: a sail icon on the wind circle that rotates with the
    /// sheet, a speed gauge in knots, and state / heel text in the game's font.
    ///
    /// The sail icon is a child of the vanilla boat-frame root (so bow = up follows the game). Everything
    /// else lives in our own container under the HUD canvas root, pinned to the wind circle's screen
    /// position every frame, so whatever rotation or scale the vanilla hierarchy carries cannot affect it.
    /// </summary>
    internal static class SailTrimHud
    {
        private static RectTransform _circle;
        private static RectTransform _canvasRoot;
        private static RectTransform _container;
        private static RectTransform _sailRect;
        private static Image _sailImage;
        private static RectTransform _gaugeRoot;
        private static Image _gaugeFill;
        private static Image _gaugeOver;
        private static TMP_Text _unitText;
        private static TMP_Text _speedText;
        private static TMP_Text _stateText;
        private static TMP_Text _infoText;
        private static TMP_Text _hintText;
        private static bool _hintDismissed;

        /// <summary>Called when the player raises or lowers the sail with the mod's keys: the hint has done its job.</summary>
        internal static void NoteSailKeyUsed()
        {
            _hintDismissed = true;
        }
        private static bool _built;
        private static bool _buildFailed;
        private static float _d; // circle diameter in canvas units

        // Vanilla-ish palette: the wind circle ring is a warm gold, text is off-white.
        private static readonly Color ColGold = new Color(0.93f, 0.72f, 0.36f);
        private static readonly Color ColText = new Color(1f, 0.96f, 0.88f);
        private static readonly Color ColTrimmed = new Color(0.62f, 0.92f, 0.55f);
        private static readonly Color ColAdjust = new Color(1f, 0.85f, 0.45f);
        private static readonly Color ColLuff = new Color(1f, 0.62f, 0.35f);
        private static readonly Color ColBad = new Color(1f, 0.42f, 0.38f);
        private const float GaugeOverRange = 0.4f; // gauge full scale = hull speed * (1 + this)
        private static readonly Color ColIdle = new Color(0.8f, 0.78f, 0.72f);

        internal static void Update()
        {
            if (!Plugin.Enabled.Value || !Plugin.ShowHud.Value) { SetVisible(false); return; }
            var hud = Hud.instance;
            if (hud == null || hud.m_shipWindIndicatorRoot == null) return;
            if (!Plugin.IsLocalPlayerPiloting(out var ship) || !hud.m_shipHudRoot.activeInHierarchy) { SetVisible(false); return; }
            var st = SailTrimShip.Get(ship);
            if (st == null) { SetVisible(false); return; }

            if (!_built && !_buildFailed) Build(hud);
            if (!_built) return;
            SetVisible(true);

            // Pin our container to the circle, ignoring any rotation the vanilla hierarchy has.
            _container.position = _circle.position;
            _container.rotation = _canvasRoot.rotation;

            bool manual = Plugin.ManualTrim.Value;

            // ---- Sail icon: yard line with belly to leeward, in the boat's frame (bow = up) ----
            if (_sailRect.gameObject.activeSelf != manual) _sailRect.gameObject.SetActive(manual);
            if (manual)
            {
                Vector2 yard = st.YardUi;
                Vector2 belly = st.BellyUi;
                float ang = Mathf.Atan2(yard.y, yard.x) * Mathf.Rad2Deg;
                Vector2 bellyAfter = new Vector2(Mathf.Sin(ang * Mathf.Deg2Rad), -Mathf.Cos(ang * Mathf.Deg2Rad));
                if (Vector2.Dot(bellyAfter, belly) < 0f) ang += 180f;
                if (st.State == SailTrimShip.TrimState.Luffing && ship.IsSailUp())
                    ang += Mathf.Sin(Time.time * Mathf.PI * 2f * Plugin.FlapFrequency.Value) * 4f;
                _sailRect.localRotation = Quaternion.Euler(0f, 0f, ang);
                _sailImage.color = StateColor(st, ship, sailIcon: true);
            }

            // ---- Speed gauge, scaled to this hull's speed: the arc up to hull speed is gold, beyond is red ----
            float ratio = st.SpeedRatio;
            float frac = Mathf.Clamp01(ratio / (1f + GaugeOverRange));
            _gaugeFill.fillAmount = frac * 0.75f;
            float overT = Mathf.Clamp01((ratio - 1f) / GaugeOverRange);
            _gaugeFill.color = Color.Lerp(ColGold, ColBad, overT);
            _speedText.text = st.SpeedKnots.ToString("0.0");
            _speedText.color = overT > 0f ? Color.Lerp(ColText, ColBad, overT) : ColText;
            _unitText.text = overT > 0.05f ? "over" : "kn";

            // ---- Text ----
            if (!manual)
            {
                _stateText.text = $"Vanilla sailing" + (char)10 + $"{Plugin.ToggleKey.Value} for manual trim";
                _stateText.color = ColIdle;
                _infoText.text = "";
                _hintText.text = "";
                return;
            }
            _stateText.text = StateLabel(st, ship);
            _stateText.color = StateColor(st, ship, sailIcon: false);
            string gust = st.GustFactor > 0.12f ? "   Gust" : (st.GustFactor < -0.12f ? "   Lull" : "");
            _infoText.text = $"Sheet {st.SheetAngle:0}°   Heel {Mathf.Abs(st.HeelAngle):0}°{gust}";

            // First-time hint: the sail keys moved, so say so while the sail is still furled.
            bool showHint = Plugin.ControlHints.Value && !_hintDismissed && !ship.IsSailUp();
            if (showHint)
            {
                string use = Localization.instance != null ? Localization.instance.Localize("$KEY_Use") : "E";
                _hintText.text = $"Tap {use} raise sail  ·  {Plugin.LowerSailKey.Value} lower sail\nHold {use} let go";
            }
            else _hintText.text = "";
        }

        private static string StateLabel(SailTrimShip st, Ship ship)
        {
            if (!ship.IsSailUp()) return "Sail furled";
            switch (st.State)
            {
                case SailTrimShip.TrimState.NoseDiving: return "Bow buried – ease out or reef";
                case SailTrimShip.TrimState.Backwinded: return "Aback – going astern";
                case SailTrimShip.TrimState.Luffing: return "Luffing – sheet in";
                case SailTrimShip.TrimState.Stalled: return "Stalled – ease out";
                case SailTrimShip.TrimState.OverTrimmed: return "Ease out";
                case SailTrimShip.TrimState.UnderTrimmed: return "Sheet in";
                default: return "Trimmed";
            }
        }

        private static Color StateColor(SailTrimShip st, Ship ship, bool sailIcon)
        {
            if (!ship.IsSailUp()) return sailIcon ? new Color(0.9f, 0.85f, 0.75f, 0.45f) : ColIdle;
            switch (st.State)
            {
                case SailTrimShip.TrimState.NoseDiving:
                case SailTrimShip.TrimState.Backwinded:
                case SailTrimShip.TrimState.Stalled: return ColBad;
                case SailTrimShip.TrimState.Luffing: return ColLuff;
                case SailTrimShip.TrimState.OverTrimmed:
                case SailTrimShip.TrimState.UnderTrimmed: return ColAdjust;
                default: return sailIcon ? ColText : ColTrimmed;
            }
        }

        private static void SetVisible(bool on)
        {
            if (!_built) return;
            if (_container != null && _container.gameObject.activeSelf != on) _container.gameObject.SetActive(on);
            if (_sailRect != null && !on && _sailRect.gameObject.activeSelf) _sailRect.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------
        private static void Build(Hud hud)
        {
            try
            {
                _circle = hud.m_shipWindIndicatorRoot;
                var canvas = _circle.GetComponentInParent<Canvas>();
                _canvasRoot = (canvas != null ? canvas.rootCanvas.transform : _circle.root) as RectTransform;
                TMP_Text fontSource = hud.m_healthText;

                // Circle diameter expressed in canvas-root units.
                float scaleFix = _canvasRoot.lossyScale.x > 1e-4f ? _circle.lossyScale.x / _canvasRoot.lossyScale.x : 1f;
                _d = Mathf.Max(60f, _circle.rect.width * scaleFix);
                float d = _d;

                // Sail icon, child of the rotating boat-frame root so bow = up.
                _sailRect = MakeImage("SailTrim_Sail", _circle, MakeSailSprite(), out _sailImage);
                _sailRect.anchoredPosition = Vector2.zero;
                _sailRect.sizeDelta = new Vector2(_circle.rect.width * 0.6f, _circle.rect.width * 0.6f);

                // Our own container at the canvas root, pinned to the circle each frame.
                var containerGo = new GameObject("SailTrim_HudRoot", typeof(RectTransform));
                _container = containerGo.GetComponent<RectTransform>();
                _container.SetParent(_canvasRoot, false);
                _container.anchorMin = _container.anchorMax = _container.pivot = new Vector2(0.5f, 0.5f);
                _container.sizeDelta = Vector2.zero;
                _container.localScale = Vector3.one;

                // Speed gauge: 270-degree ring left of the circle, gap at the bottom, number inside.
                float gd = d * 0.6f;
                var gaugeGo = new GameObject("Gauge", typeof(RectTransform));
                _gaugeRoot = gaugeGo.GetComponent<RectTransform>();
                _gaugeRoot.SetParent(_container, false);
                _gaugeRoot.anchorMin = _gaugeRoot.anchorMax = _gaugeRoot.pivot = new Vector2(0.5f, 0.5f);
                _gaugeRoot.anchoredPosition = new Vector2(-d * 0.5f - gd * 0.65f, -d * 0.08f);
                _gaugeRoot.sizeDelta = new Vector2(gd, gd);

                Sprite ring = MakeRingSprite();
                RectTransform bgRect = MakeImage("Bg", _gaugeRoot, ring, out var bg);
                bgRect.sizeDelta = new Vector2(gd, gd);
                SetupArc(bg, 0.75f, new Color(0.1f, 0.08f, 0.05f, 0.55f));
                // Faint red band over the last part of the arc: the "past hull speed" zone.
                RectTransform overRect = MakeImage("Over", _gaugeRoot, ring, out _gaugeOver);
                overRect.sizeDelta = new Vector2(gd, gd);
                SetupArc(_gaugeOver, 0.75f * GaugeOverRange / (1f + GaugeOverRange), new Color(0.9f, 0.25f, 0.2f, 0.35f));
                // Fill this one backwards from the arc's end so only the over-speed zone is tinted.
                _gaugeOver.fillClockwise = false;
                _gaugeOver.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                RectTransform fillRect = MakeImage("Fill", _gaugeRoot, ring, out _gaugeFill);
                fillRect.sizeDelta = new Vector2(gd, gd);
                SetupArc(_gaugeFill, 0f, ColGold);

                _speedText = MakeText("Speed", _gaugeRoot, fontSource, gd * 0.32f, new Vector2(0f, gd * 0.05f), new Vector2(gd, gd * 0.5f));
                _speedText.color = ColText;
                _unitText = MakeText("Unit", _gaugeRoot, fontSource, gd * 0.16f, new Vector2(0f, -gd * 0.2f), new Vector2(gd, gd * 0.3f));
                _unitText.text = "kn";
                _unitText.color = ColGold;

                // State and info lines under the circle.
                _stateText = MakeText("State", _container, fontSource, d * 0.16f, new Vector2(0f, -d * 0.74f), new Vector2(d * 2.4f, d * 0.44f));
                _stateText.textWrappingMode = TextWrappingModes.Normal;
                _stateText.alignment = TextAlignmentOptions.Top;
                _infoText = MakeText("Info", _container, fontSource, d * 0.13f, new Vector2(0f, -d * 0.98f), new Vector2(d * 2.6f, d * 0.22f));
                _infoText.color = ColText;
                _hintText = MakeText("Hint", _container, fontSource, d * 0.12f, new Vector2(0f, -d * 1.3f), new Vector2(d * 2.6f, d * 0.4f));
                _hintText.color = ColGold;
                _hintText.textWrappingMode = TextWrappingModes.Normal;
                _hintText.alignment = TextAlignmentOptions.Top;

                _built = true;
            }
            catch (System.Exception e)
            {
                _buildFailed = true;
                Plugin.Log.LogError("SailTrim HUD build failed: " + e);
            }
        }

        /// <summary>Radial arc that starts at 7:30 and runs clockwise, so a 0.75 fill leaves the gap at the bottom.</summary>
        private static void SetupArc(Image img, float fill, Color color)
        {
            img.color = color;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Bottom;
            img.fillClockwise = true;
            img.fillAmount = fill;
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        }

        private static RectTransform MakeImage(string name, Transform parent, Sprite sprite, out Image image)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;
            image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = true;
            return rt;
        }

        private static TMP_Text MakeText(string name, Transform parent, TMP_Text source, float size, Vector2 pos, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = source.font;
            t.fontSharedMaterial = source.fontSharedMaterial;
            t.fontStyle = source.fontStyle;
            t.outlineWidth = source.outlineWidth;
            t.outlineColor = source.outlineColor;
            t.enableAutoSizing = false;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = false;
            t.raycastTarget = false;
            t.color = ColText;
            t.text = "";
            return t;
        }

        /// <summary>Top-down sail: a yard bar along X through the centre with a belly hanging toward -Y.</summary>
        private static Sprite MakeSailSprite()
        {
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            float cx = n * 0.5f, cy = n * 0.5f;
            float halfLen = n * 0.44f, barHalf = 2.2f;
            float bellyDepth = n * 0.2f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                Color c = Color.clear;
                if (dy <= 0f && Mathf.Abs(dx) <= halfLen)
                {
                    float u = dx / halfLen;
                    float depth = bellyDepth * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
                    if (-dy <= depth)
                    {
                        float edge = Mathf.Clamp01((depth + dy) / 1.5f);
                        c = new Color(1f, 1f, 1f, 0.5f * edge);
                    }
                }
                if (Mathf.Abs(dy) <= barHalf && Mathf.Abs(dx) <= halfLen)
                {
                    float edge = Mathf.Clamp01((barHalf - Mathf.Abs(dy)) / 1f) * Mathf.Clamp01((halfLen - Mathf.Abs(dx)) / 1.5f);
                    c = Color.Lerp(c, new Color(1f, 1f, 1f, 1f), edge);
                }
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r <= 4.5f) c = Color.Lerp(c, new Color(1f, 1f, 1f, 1f), Mathf.Clamp01(4.5f - r));
                px[y * n + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite MakeRingSprite()
        {
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            float c0 = n * 0.5f, rOut = n * 0.48f, rIn = n * 0.40f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - c0, dy = y + 0.5f - c0;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(rOut - r) * Mathf.Clamp01(r - rIn);
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
