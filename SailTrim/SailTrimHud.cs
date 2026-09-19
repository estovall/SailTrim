using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SailTrim
{
    /// <summary>
    /// uGUI overlay built onto the vanilla ship HUD: a sail icon on the wind circle that rotates with the
    /// sheet, an apparent-wind arrow, a speed gauge in knots, and state / heel text in the game's font.
    ///
    /// Pilot: the sail icon is a child of the vanilla boat-frame root (bow = up follows the game); the rest
    /// lives in our own container under the HUD canvas root, pinned to the wind circle's screen position.
    /// Passenger: the vanilla ship HUD is hidden, so the container draws its own sail icon and arrow,
    /// rotated by the ship's yaw the same way vanilla rotates its root.
    /// </summary>
    internal static class SailTrimHud
    {
        private static RectTransform _circle;
        private static RectTransform _canvasRoot;
        private static RectTransform _container;
        private static RectTransform _sailRect;          // pilot: inside vanilla's rotating root
        private static Image _sailImage;
        private static RectTransform _gaugeRoot;
        private static Image _gaugeFill;
        private static Image _gaugeOver;
        private static TMP_Text _unitText;
        private static TMP_Text _speedText;
        private static TMP_Text _stateText;
        private static TMP_Text _infoText;
        private static TMP_Text _hintText;
        private static TMP_Text _controlsText;
        private static bool _hintDismissed;
        private static bool _built;
        private static bool _buildFailed;
        private static float _buildFailedAt;
        private static float _d;       // circle diameter in canvas units
        private static float _radius;  // vanilla wind icon radius in canvas units

        /// <summary>Called when the player raises or lowers the sail with the mod's keys: the hint has done its job.</summary>
        internal static void NoteSailKeyUsed() => _hintDismissed = true;

        private static readonly Color ColGold = new Color(0.93f, 0.72f, 0.36f);
        private static readonly Color ColText = new Color(1f, 0.96f, 0.88f);
        private static readonly Color ColTrimmed = new Color(0.62f, 0.92f, 0.55f);
        private static readonly Color ColAdjust = new Color(1f, 0.85f, 0.45f);
        private static readonly Color ColLuff = new Color(1f, 0.62f, 0.35f);
        private static readonly Color ColBad = new Color(1f, 0.42f, 0.38f);
        private static readonly Color ColIdle = new Color(0.8f, 0.78f, 0.72f);
        private const float GaugeOverRange = 0.4f;

        internal static void Update()
        {
            if (!Plugin.Enabled.Value || !Plugin.ShowHud.Value) { SetVisible(false); return; }
            var hud = Hud.instance;
            var player = Player.m_localPlayer;
            if (hud == null || hud.m_shipWindIndicatorRoot == null || player == null) { SetVisible(false); return; }

            // The widgets live under the game's Hud, which is destroyed on logout and recreated on the next join.
            // If our cached objects are gone or belong to an old Hud, forget them so the next boat rebuilds.
            if (_built && (_container == null || _circle == null || _circle != hud.m_shipWindIndicatorRoot)) ResetBuild();
            // A failed build is retried after a while rather than remembered for the whole session.
            if (_buildFailed && Time.unscaledTime - _buildFailedAt > 15f) _buildFailed = false;

            bool piloting = Plugin.IsLocalPlayerPiloting(out var ship);
            if (!piloting) ship = Plugin.PassengerHud.Value ? Plugin.GetShipAboard(player) : null;
            if (ship == null || !hud.m_shipHudRoot.activeInHierarchy) { SetVisible(false); return; }
            var st = SailTrimShip.Get(ship);
            if (st == null || (!piloting && !st.ManualMode)) { SetVisible(false); return; }

            if (!_built && !_buildFailed) Build(hud);
            if (!_built) return;
            SetVisible(true);

            _container.position = _circle.position;
            _container.rotation = _canvasRoot.rotation;

            bool manual = piloting ? Plugin.ManualTrim.Value : st.ManualMode;

            // ---- Sail icon ----
            float sailAng = 0f;
            if (manual)
            {
                Vector2 yard = st.YardUi, belly = st.BellyUi;
                sailAng = Mathf.Atan2(yard.y, yard.x) * Mathf.Rad2Deg;
                Vector2 bellyAfter = new Vector2(Mathf.Sin(sailAng * Mathf.Deg2Rad), -Mathf.Cos(sailAng * Mathf.Deg2Rad));
                if (Vector2.Dot(bellyAfter, belly) < 0f) sailAng += 180f;
                if (st.State == SailTrimShip.TrimState.Luffing && ship.IsSailUp())
                    sailAng += Mathf.Sin(Time.time * Mathf.PI * 2f * Plugin.FlapFrequency.Value) * 4f;
                else if (st.State == SailTrimShip.TrimState.Spilled && ship.IsSailUp())
                    sailAng += Mathf.Sin(Time.time * Mathf.PI * 2f * Plugin.FlapFrequency.Value * 1.5f) * 9f;
            }
            Show(_sailRect, manual);
            if (manual)
            {
                _sailRect.localRotation = Quaternion.Euler(0f, 0f, sailAng);
                _sailImage.color = StateColor(st, ship, sailIcon: true);
            }

            // ---- Speed gauge ----
            float ratio = st.SpeedRatio;
            _gaugeFill.fillAmount = Mathf.Clamp01(ratio / (1f + GaugeOverRange)) * 0.75f;
            float overT = Mathf.Clamp01((ratio - 1f) / GaugeOverRange);
            _gaugeFill.color = Color.Lerp(ColGold, ColBad, overT);
            _speedText.text = st.SpeedKnots.ToString("0.0");
            _speedText.color = overT > 0f ? Color.Lerp(ColText, ColBad, overT) : ColText;
            _unitText.text = overT > 0.05f ? "over" : "kn";

            // ---- Text ----
            if (!manual)
            {
                _stateText.text = "Vanilla sailing" + (char)10 + $"{Plugin.ToggleKey.Value} for manual trim";
                _stateText.color = ColIdle;
                _infoText.text = "";
                _hintText.text = "";
                _controlsText.text = "";
                return;
            }
            _controlsText.text = piloting && Plugin.ShowControls.Value ? ControlsList() : "";
            _stateText.text = st.IsMastStraining ? "Mast straining – ease out or reef" : StateLabel(st, ship);
            _stateText.color = st.IsMastStraining ? ColBad : StateColor(st, ship, sailIcon: false);
            string extra = st.GustFactor > 0.12f ? "   Gust" : (st.GustFactor < -0.12f ? "   Lull" : (st.ShadowFactor > 0.35f ? "   Lee" : ""));
            string sail = st.SailAmount >= 0f ? $"Sail {Mathf.RoundToInt(st.SailAmount * 100f)}%   " : "";
            _infoText.text = $"{sail}Sheet {st.SheetAngle:0}°   Heel {Mathf.Abs(st.HeelAngle):0}°{extra}";

            string hint = "";
            if (piloting)
            {
                if (st.SheetHand != 0L) hint = "A crew member is on the sheet with you";
                else if (Plugin.ControlHints.Value && !Plugin.ShowControls.Value && !_hintDismissed && !ship.IsSailUp())
                {
                    string use = Localization.instance != null ? Localization.instance.Localize("$KEY_Use") : "E";
                    string jump = Localization.instance != null ? Localization.instance.Localize("$KEY_Jump") : "Space";
                    hint = $"Hold {use} let out sail  ·  hold {Plugin.LowerSailKey.Value} take in" + (char)10
                         + $"{Plugin.RowForwardKey.Value} row  ·  {jump} let go";
                }
            }
            else if (Plugin.CrewCanTrim.Value)
            {
                if (Plugin.CrewActive) hint = "You have the sheet: W in, S out" + (char)10 + "Jump to let go";
                else if (st.SheetHand == 0L) hint = "Hold fast on the mast to trim the sail";
                else hint = "Someone has the sheet";
            }
            _hintText.text = hint;
        }

        private static string _controlsCache; private static float _controlsCacheAt = -10f;

        /// <summary>The sailing keys, as bound right now, one per line; rebuilt every few seconds in case of a rebind.</summary>
        private static string ControlsList()
        {
            if (_controlsCache != null && Time.unscaledTime - _controlsCacheAt < 3f) return _controlsCache;
            _controlsCacheAt = Time.unscaledTime;
            string use = BoundKey("Use", "E"), jump = BoundKey("Jump", "Space");
            string fwd = BoundKey("Forward", "W"), back = BoundKey("Backward", "S"), left = BoundKey("Left", "A"), right = BoundKey("Right", "D");
            string sheetIn = Plugin.InvertSheetKeys.Value ? back : fwd, ease = Plugin.InvertSheetKeys.Value ? fwd : back;
            if (!Plugin.MoveKeysTrimSheet.Value) { sheetIn = Key(Plugin.SheetInKey.Value); ease = Key(Plugin.EaseKey.Value); }
            string raise = Plugin.RaiseSailKey.Value != KeyCode.None && Plugin.RaiseSailKey.Value != KeyCode.E ? Key(Plugin.RaiseSailKey.Value) : use;
            const string k = "<color=#F2C46B>", e = "</color>";
            string nl = ((char)10).ToString();
            var sb = new System.Text.StringBuilder();
            sb.Append(k).Append("Hold ").Append(raise).Append(e).Append("  let out sail").Append(nl);
            sb.Append(k).Append("Hold ").Append(Key(Plugin.LowerSailKey.Value)).Append(e).Append("  take in sail").Append(nl);
            sb.Append(k).Append(sheetIn).Append(" / ").Append(ease).Append(e).Append("  sheet in / ease out").Append(nl);
            sb.Append(k).Append(left).Append(" / ").Append(right).Append(e).Append("  steer").Append(nl);
            sb.Append(k).Append(Key(Plugin.RowForwardKey.Value)).Append(" / ").Append(Key(Plugin.RowBackKey.Value)).Append(e)
              .Append(Plugin.RowKeysToggle.Value ? "  row ahead / astern (press)" : "  row ahead / astern (hold)").Append(nl);
            sb.Append(k).Append(jump).Append(e).Append("  let go of the helm").Append(nl);
            sb.Append(k).Append(Key(Plugin.ToggleKey.Value)).Append(e).Append("  vanilla sailing");
            _controlsCache = sb.ToString();
            return _controlsCache;
        }

        private static string BoundKey(string binding, string fallback)
        {
            try
            {
                string s = Localization.instance != null ? Localization.instance.GetBoundKeyString(binding, true) : "";
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            catch { return fallback; }
        }

        private static string Key(KeyCode kc)
        {
            switch (kc)
            {
                case KeyCode.None: return "-";
                case KeyCode.LeftShift: return "Shift";
                case KeyCode.RightShift: return "R Shift";
                case KeyCode.LeftControl: return "Ctrl";
                case KeyCode.RightControl: return "R Ctrl";
                case KeyCode.LeftAlt: return "Alt";
                case KeyCode.RightAlt: return "R Alt";
                case KeyCode.Space: return "Space";
            }
            string s = kc.ToString();
            if (s.StartsWith("Alpha")) return s.Substring(5);
            if (s.StartsWith("Keypad")) return "Num " + s.Substring(6);
            return s;
        }

        private static void Show(RectTransform rt, bool on)
        {
            if (rt != null && rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on);
        }

        private static string StateLabel(SailTrimShip st, Ship ship)
        {
            if (st.IsRowing) return ship.GetSpeedSetting() == Ship.Speed.Back ? "Rowing astern" : "Rowing";
            if (!ship.IsSailUp()) return "Sail furled";
            switch (st.State)
            {
                case SailTrimShip.TrimState.NoseDiving: return "Bow buried – ease out or reef";
                case SailTrimShip.TrimState.Backwinded: return "Aback – going astern";
                case SailTrimShip.TrimState.Luffing: return "Luffing – sheet in";
                case SailTrimShip.TrimState.Spilled: return st.IsTacking ? "Tacking – sail spilled" : "Sail spilled – bear away to fill";
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
                case SailTrimShip.TrimState.Spilled:
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
            if (!on && _sailRect != null) Show(_sailRect, false);
        }

        /// <summary>Drop the cached widgets (destroying any that still exist) so the next Update rebuilds them.</summary>
        private static void ResetBuild()
        {
            if (_container != null) Object.Destroy(_container.gameObject);
            if (_sailRect != null) Object.Destroy(_sailRect.gameObject);
            _container = null; _sailRect = null; _circle = null; _canvasRoot = null;
            _built = false;
            _buildFailed = false;
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

                float rootScale = _canvasRoot.lossyScale.x > 1e-4f ? _canvasRoot.lossyScale.x : 1f;
                float scaleFix = _circle.lossyScale.x / rootScale;
                _d = Mathf.Max(60f, _circle.rect.width * scaleFix);
                float d = _d;
                _radius = d * 0.55f;
                if (hud.m_shipWindIcon != null)
                {
                    float r = Vector3.Distance(hud.m_shipWindIcon.rectTransform.position, _circle.position) / rootScale;
                    if (r > d * 0.2f && r < d * 1.2f) _radius = r;
                }

                Sprite sailSprite = MakeSailSprite();

                // Pilot sail icon, child of the rotating boat-frame root so bow = up.
                _sailRect = MakeImage("SailTrim_Sail", _circle, sailSprite, out _sailImage);
                _sailRect.sizeDelta = new Vector2(_circle.rect.width * 0.6f, _circle.rect.width * 0.6f);

                // Our container at the canvas root, pinned to the circle each frame.
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
                RectTransform overRect = MakeImage("Over", _gaugeRoot, ring, out _gaugeOver);
                overRect.sizeDelta = new Vector2(gd, gd);
                SetupArc(_gaugeOver, 0.75f * GaugeOverRange / (1f + GaugeOverRange), new Color(0.9f, 0.25f, 0.2f, 0.35f));
                _gaugeOver.fillClockwise = false;
                _gaugeOver.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                RectTransform fillRect = MakeImage("Fill", _gaugeRoot, ring, out _gaugeFill);
                fillRect.sizeDelta = new Vector2(gd, gd);
                SetupArc(_gaugeFill, 0f, ColGold);

                _speedText = MakeText("Speed", _gaugeRoot, fontSource, gd * 0.32f, new Vector2(0f, gd * 0.05f), new Vector2(gd, gd * 0.5f));
                _unitText = MakeText("Unit", _gaugeRoot, fontSource, gd * 0.16f, new Vector2(0f, -gd * 0.2f), new Vector2(gd, gd * 0.3f));
                _unitText.text = "kn";
                _unitText.color = ColGold;

                // State, info and hint lines under the circle.
                _stateText = MakeText("State", _container, fontSource, d * 0.16f, new Vector2(0f, -d * 0.74f), new Vector2(d * 2.4f, d * 0.44f));
                _stateText.textWrappingMode = TextWrappingModes.Normal;
                _stateText.alignment = TextAlignmentOptions.Top;
                _infoText = MakeText("Info", _container, fontSource, d * 0.13f, new Vector2(0f, -d * 0.98f), new Vector2(d * 2.6f, d * 0.22f));
                _infoText.color = ColText;
                _hintText = MakeText("Hint", _container, fontSource, d * 0.12f, new Vector2(0f, -d * 1.3f), new Vector2(d * 2.6f, d * 0.4f));
                _hintText.color = ColGold;
                _hintText.textWrappingMode = TextWrappingModes.Normal;
                _hintText.alignment = TextAlignmentOptions.Top;

                // The key list, right of the circle (the speed gauge has the left), top-aligned with it.
                float cw = d * 2.3f, ch = d * 1.6f;
                _controlsText = MakeText("Controls", _container, fontSource, d * 0.115f, new Vector2(d * 0.62f + cw * 0.5f, d * 0.55f - ch * 0.5f), new Vector2(cw, ch));
                _controlsText.color = ColText;
                _controlsText.alignment = TextAlignmentOptions.TopLeft;
                _controlsText.textWrappingMode = TextWrappingModes.NoWrap;
                _controlsText.richText = true;
                _controlsText.lineSpacing = 8f;

                _built = true;
            }
            catch (System.Exception e)
            {
                _buildFailed = true;
                _buildFailedAt = Time.unscaledTime;
                Plugin.Log.LogError("SailTrim HUD build failed: " + e);
            }
        }

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
