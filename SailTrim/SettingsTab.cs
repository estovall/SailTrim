using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace SailTrim
{
    /// <summary>
    /// A "SailTrim" tab in the vanilla Settings menu (main menu and in-game). Only personal controls live here:
    /// key bindings and the two key-behaviour toggles. Physics, heel, gusts etc. stay config-file (and server) only.
    /// Built at runtime from clones of vanilla widgets so it matches the game's look and needs no asset bundle.
    /// </summary>
    public class SettingsTab : MonoBehaviour, ISettingsTab
    {
        private const string TabName = "SailTrim";

        private class KeyRow
        {
            public ConfigEntry<KeyCode> Entry;
            public KeyCode Pending;
            public TMP_Text ValueText;
            public Button Button;
        }

        private class ToggleRow
        {
            public ConfigEntry<bool> Entry;
            public Toggle Toggle;
        }

        private readonly List<KeyRow> _keys = new List<KeyRow>();
        private readonly List<ToggleRow> _toggles = new List<ToggleRow>();
        private KeyRow _capturing;
        private float _captureDelay;
        private TMP_Text _hint;

#pragma warning disable 67 // required by ISettingsTab; this tab has no shared settings
        public event Action<string, int> SharedSettingChanged;
#pragma warning restore 67

        // ------------------------------------------------------------------
        // Installation: called from a prefix on Settings.Awake, before the game reads its tab list.
        // ------------------------------------------------------------------
        internal static void Install(Settings settings)
        {
            // The settings screen holds more than one TabHandler (the Controller page has its own sub-tabs);
            // the main one is the handler whose pages carry ISettingsTab components.
            TabHandler tabs = settings.m_tabHandler;
            if (tabs == null || tabs.m_tabs.Count == 0 || !HasSettingsPages(tabs))
            {
                tabs = null;
                foreach (var th in settings.GetComponentsInChildren<TabHandler>(true))
                    if (HasSettingsPages(th) && (tabs == null || th.m_tabs.Count > tabs.m_tabs.Count)) tabs = th;
            }
            if (tabs == null) { Plugin.Log.LogWarning("SailTrim: main settings tab handler not found, no SailTrim tab."); return; }
            foreach (var t in tabs.m_tabs)
                if (t.m_page != null && t.m_page.name == TabName + "Page") return; // already installed

            // Templates from the vanilla tabs of this very Settings instance.
            TabHandler.Tab template = null;
            KeyboardMouseSettings controls = null;
            GameplaySettings gameplay = null;
            foreach (var t in tabs.m_tabs)
            {
                if (t.m_page == null || t.m_button == null) continue;
                var km = t.m_page.GetComponent<KeyboardMouseSettings>();
                if (km != null) controls = km;
                var gp = t.m_page.GetComponent<GameplaySettings>();
                if (gp != null) { gameplay = gp; template = t; }
            }
            if (template == null) template = tabs.m_tabs[tabs.m_tabs.Count - 1];
            if (template.m_page == null || template.m_button == null) { Plugin.Log.LogWarning("SailTrim: no usable template tab."); return; }

            RectTransform keyRowTemplate = controls != null && controls.m_keys.Count > 0 ? controls.m_keys[0].m_keyTransform : null;
            Toggle toggleTemplate = gameplay != null ? gameplay.m_toggleRun : null;
            if (keyRowTemplate == null || toggleTemplate == null)
            {
                Plugin.Log.LogWarning($"SailTrim: settings widget templates missing (controls={(controls != null)} keys={(controls != null ? controls.m_keys.Count : -1)} gameplay={(gameplay != null)} toggle={(toggleTemplate != null)}), no SailTrim tab.");
                foreach (var t in tabs.m_tabs)
                    Plugin.Log.LogInfo("SailTrim: tab button=" + (t.m_button != null ? t.m_button.name : "null") + " page=" + Describe(t.m_page, 1));
                var km2 = settings.GetComponentInChildren<KeyboardMouseSettings>(true);
                Plugin.Log.LogInfo("SailTrim: KeyboardMouseSettings anywhere: " + (km2 != null ? km2.gameObject.name + " keys=" + km2.m_keys.Count : "none"));
                var gp2 = settings.GetComponentInChildren<GameplaySettings>(true);
                Plugin.Log.LogInfo("SailTrim: GameplaySettings anywhere: " + (gp2 != null ? gp2.gameObject.name + " toggleRun=" + (gp2.m_toggleRun != null) : "none"));
                return;
            }

            // Tab button: clone the template's button, relabel, drop its serialized click handlers.
            var buttonGo = Instantiate(template.m_button.gameObject, template.m_button.transform.parent);
            buttonGo.name = TabName;
            buttonGo.transform.SetSiblingIndex(template.m_button.transform.GetSiblingIndex() + 1);
            var button = buttonGo.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            // The button prefab is: Label, Selected/LabelSelected, and a KeyHint ("Q"/"E" tab-cycle hint) that the
            // game shows/hides through Settings.m_tabKeyHints, which only references the original buttons. Drop the
            // hint from the clone and relabel just the two labels.
            for (int i = buttonGo.transform.childCount - 1; i >= 0; i--)
            {
                var child = buttonGo.transform.GetChild(i);
                if (child.name != "Label" && child.name != "Selected") Destroy(child.gameObject);
            }
            foreach (var txt in buttonGo.GetComponentsInChildren<TMP_Text>(true))
            {
                var p = txt.transform.parent;
                bool own = p == buttonGo.transform || (p != null && p.name == "Selected" && p.parent == buttonGo.transform);
                if (own) txt.text = TabName;
            }

            // Page: a fresh full-stretch panel next to the template page.
            var pageGo = new GameObject(TabName + "Page", typeof(RectTransform));
            var page = pageGo.GetComponent<RectTransform>();
            page.SetParent(template.m_page.parent, false);
            page.anchorMin = Vector2.zero; page.anchorMax = Vector2.one;
            page.offsetMin = Vector2.zero; page.offsetMax = Vector2.zero;
            page.SetSiblingIndex(template.m_page.GetSiblingIndex() + 1);
            pageGo.SetActive(false);

            var tab = pageGo.AddComponent<SettingsTab>();
            tab.Build(page, keyRowTemplate, toggleTemplate);

            tabs.m_tabs.Add(new TabHandler.Tab { m_button = button, m_page = page, m_default = false, m_onClick = new UnityEngine.Events.UnityEvent() });
            Plugin.Log.LogInfo("SailTrim: settings tab installed.");
        }

        private static bool HasSettingsPages(TabHandler th)
        {
            foreach (var t in th.m_tabs)
                if (t.m_page != null && t.m_page.GetComponent<ISettingsTab>() != null) return true;
            return false;
        }

        /// <summary>Compact one-line description of a UI subtree: name, size, component types, active flag.</summary>
        private static string Describe(RectTransform rt, int depth)
        {
            if (rt == null) return "null";
            var sb = new System.Text.StringBuilder();
            var comps = new List<string>();
            foreach (var c in rt.GetComponents<Component>())
            {
                if (c is RectTransform || c is CanvasRenderer) continue;
                string s = c.GetType().Name;
                if (c is TMP_Text t) s += "('" + t.text + "'," + (t.font != null ? t.font.name : "nofont") + "," + ColorUtility.ToHtmlStringRGB(t.color) + ")";
                comps.Add(s);
            }
            sb.Append(rt.name).Append('[').Append(rt.rect.width.ToString("0")).Append('x').Append(rt.rect.height.ToString("0")).Append(rt.gameObject.activeSelf ? "" : ",off").Append("]{").Append(string.Join(",", comps.ToArray())).Append('}');
            if (depth > 0 && rt.childCount > 0)
            {
                sb.Append('(');
                for (int i = 0; i < rt.childCount; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(Describe(rt.GetChild(i) as RectTransform, depth - 1));
                }
                sb.Append(')');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------
        private void Build(RectTransform page, RectTransform keyRowTemplate, Toggle toggleTemplate)
        {
            // Scrollable: a masked viewport filling the page, the list as its content (mouse wheel scrolls).
            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            var viewport = viewportGo.GetComponent<RectTransform>();
            viewport.SetParent(page, false);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(20f, 20f); viewport.offsetMax = new Vector2(-20f, -20f);
            var vpImage = viewportGo.AddComponent<Image>();
            vpImage.color = new Color(0f, 0f, 0f, 0f); // invisible, but catches the wheel between rows
            viewportGo.AddComponent<RectMask2D>();

            var listGo = new GameObject("List", typeof(RectTransform));
            var list = listGo.GetComponent<RectTransform>();
            list.SetParent(viewport, false);
            list.anchorMin = new Vector2(0.5f, 1f); list.anchorMax = new Vector2(0.5f, 1f); list.pivot = new Vector2(0.5f, 1f);
            list.anchoredPosition = new Vector2(0f, -20f);
            list.sizeDelta = new Vector2(540f, 0f);

            var scroll = page.gameObject.AddComponent<ScrollRect>();
            scroll.content = list;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 40f;
            var layout = listGo.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false; layout.childControlHeight = false;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
            layout.spacing = 12f;
            listGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // The toggle's own label is the reliable source for the settings font/colour.
            TMP_Text labelSource = toggleTemplate.GetComponentInChildren<TMP_Text>(true);
            if (labelSource == null) labelSource = keyRowTemplate.GetComponentInChildren<TMP_Text>(true);

            AddHeader(list, labelSource, "SailTrim keys");
            AddKey(list, keyRowTemplate, "Manual trim on/off", Plugin.ToggleKey);
            AddKey(list, keyRowTemplate, "Take in sail (hold)", Plugin.LowerSailKey);
            AddKey(list, keyRowTemplate, "Let out sail (hold; E also)", Plugin.RaiseSailKey);
            AddKey(list, keyRowTemplate, "Row forward", Plugin.RowForwardKey);
            AddKey(list, keyRowTemplate, "Row astern", Plugin.RowBackKey);
            AddKey(list, keyRowTemplate, "Ease sheet out (extra)", Plugin.EaseKey);
            AddKey(list, keyRowTemplate, "Sheet in (extra)", Plugin.SheetInKey);

            AddHeader(list, labelSource, "Game keys (change under Keyboard & Mouse)");
            AddInfoKey(list, keyRowTemplate, "Let out sail (hold)", BoundKey("Use", "E"));
            AddInfoKey(list, keyRowTemplate, "Sheet in / ease out", BoundKey("Forward", "W") + " / " + BoundKey("Backward", "S"));
            AddInfoKey(list, keyRowTemplate, "Steer", BoundKey("Left", "A") + " / " + BoundKey("Right", "D"));
            AddInfoKey(list, keyRowTemplate, "Let go of the helm", BoundKey("Jump", "Space"));

            AddHeader(list, labelSource, "Options");
            AddToggle(list, toggleTemplate, "W/S trim the sheet at the helm", Plugin.MoveKeysTrimSheet);
            AddToggle(list, toggleTemplate, "Invert sheet keys", Plugin.InvertSheetKeys);
            AddToggle(list, toggleTemplate, "Row keys toggle (off = hold to row)", Plugin.RowKeysToggle);
            AddToggle(list, toggleTemplate, "Rudder self-centres", Plugin.RudderSelfCenter);

            _hint = AddHeader(list, labelSource, "Click a key to rebind. Esc cancels, Delete clears.");
            _hint.fontSize = Mathf.Max(12f, labelSource.fontSize * 0.8f);
            _hint.alignment = TextAlignmentOptions.Center;
        }

        private TMP_Text AddHeader(RectTransform list, TMP_Text source, string text)
        {
            var go = Instantiate(source.gameObject, list);
            go.name = "Header";
            go.SetActive(true);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(list.sizeDelta.x - 40f, 30f);
            var t = go.GetComponent<TMP_Text>();
            t.text = text;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableAutoSizing = false;
            t.raycastTarget = false;
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth = rt.sizeDelta.x; le.preferredHeight = rt.sizeDelta.y;
            return t;
        }

        private void AddKey(RectTransform list, RectTransform template, string label, ConfigEntry<KeyCode> entry)
        {
            // The vanilla row is an empty container sized by its grid at runtime, so lay it out ourselves:
            // a 20x32 anchor (same column as the toggles), the button growing right from it, the label
            // hanging left of it, right-aligned, like the toggle labels.
            var go = Instantiate(template.gameObject, list);
            go.name = "Key_" + entry.Definition.Key;
            go.SetActive(true);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(20f, 32f);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth = 20f; le.preferredHeight = 32f;

            var button = go.GetComponentInChildren<Button>(true);
            var valueText = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (button == null || valueText == null) { Plugin.Log.LogWarning("SailTrim: key row template has no button/text; " + label + " skipped."); Destroy(go); return; }
            button.gameObject.SetActive(true);
            var brt = button.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.5f); brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(140f, 32f);
            var vrt = valueText.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = new Vector2(4f, 2f); vrt.offsetMax = new Vector2(-4f, -2f);
            valueText.alignment = TextAlignmentOptions.Center;

            TMP_Text rowLabel = null;
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                if (t != valueText && t.transform.parent == go.transform) { rowLabel = t; break; }
            if (rowLabel != null)
            {
                rowLabel.gameObject.SetActive(true);
                var lrt = rowLabel.GetComponent<RectTransform>();
                lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f); lrt.pivot = new Vector2(1f, 0.5f);
                lrt.anchoredPosition = new Vector2(-10f, 0f);
                lrt.sizeDelta = new Vector2(320f, 32f);
                rowLabel.text = label;
                rowLabel.alignment = TextAlignmentOptions.MidlineRight;
                rowLabel.enableAutoSizing = false;
            }
            button.onClick = new Button.ButtonClickedEvent();

            var row = new KeyRow { Entry = entry, Pending = entry.Value, ValueText = valueText, Button = button };
            button.onClick.AddListener(() => BeginCapture(row));
            _keys.Add(row);
        }

        /// <summary>The player's current binding for one of the game's own buttons, as the game would print it.</summary>
        private static string BoundKey(string binding, string fallback)
        {
            try
            {
                string s = Localization.instance != null ? Localization.instance.GetBoundKeyString(binding, true) : "";
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            catch { return fallback; }
        }

        /// <summary>A key row that only displays a value (for the game's own bindings); not clickable.</summary>
        private void AddInfoKey(RectTransform list, RectTransform template, string label, string value)
        {
            var go = Instantiate(template.gameObject, list);
            go.name = "Info_" + label;
            go.SetActive(true);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(20f, 32f);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth = 20f; le.preferredHeight = 32f;

            var button = go.GetComponentInChildren<Button>(true);
            var valueText = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (button == null || valueText == null) { Destroy(go); return; }
            button.gameObject.SetActive(true);
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = false;
            var brt = button.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.5f); brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(140f, 32f);
            var vrt = valueText.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = new Vector2(4f, 2f); vrt.offsetMax = new Vector2(-4f, -2f);
            valueText.alignment = TextAlignmentOptions.Center;
            valueText.text = value;

            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == valueText || t.transform.parent != go.transform) continue;
                t.gameObject.SetActive(true);
                var lrt = t.GetComponent<RectTransform>();
                lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f); lrt.pivot = new Vector2(1f, 0.5f);
                lrt.anchoredPosition = new Vector2(-10f, 0f);
                lrt.sizeDelta = new Vector2(320f, 32f);
                t.text = label;
                t.alignment = TextAlignmentOptions.MidlineRight;
                t.enableAutoSizing = false;
                break;
            }
        }

        private void AddToggle(RectTransform list, Toggle template, string label, ConfigEntry<bool> entry)
        {
            var go = Instantiate(template.gameObject, list);
            go.name = "Toggle_" + entry.Definition.Key;
            go.SetActive(true);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = Vector2.zero;
            var trt = template.GetComponent<RectTransform>();
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth = trt.rect.width; le.preferredHeight = trt.rect.height;

            var toggle = go.GetComponent<Toggle>() ?? go.GetComponentInChildren<Toggle>(true);
            if (toggle == null) { Plugin.Log.LogWarning("SailTrim: toggle template has no Toggle; " + label + " skipped."); Destroy(go); return; }
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.isOn = entry.Value;
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) t.text = label;
            _toggles.Add(new ToggleRow { Entry = entry, Toggle = toggle });
        }

        // ------------------------------------------------------------------
        // ISettingsTab
        // ------------------------------------------------------------------
        public void Initialize()
        {
            foreach (var k in _keys) { k.Pending = k.Entry.Value; }
            foreach (var t in _toggles) t.Toggle.isOn = t.Entry.Value;
            RefreshKeyTexts();
        }

        public void Terminate()
        {
            if (_capturing != null) EndCapture();
        }

        public void OnTabOpen(Button backButton, Button okButton) { }

        public void OnOkAsync(OkActionCompletedHandler okActionCompletedCallback)
        {
            if (_capturing != null) EndCapture();
            bool changed = false;
            foreach (var k in _keys) if (k.Entry.Value != k.Pending) { k.Entry.Value = k.Pending; changed = true; }
            foreach (var t in _toggles) if (t.Entry.Value != t.Toggle.isOn) { t.Entry.Value = t.Toggle.isOn; changed = true; }
            if (changed) Plugin.Instance.Config.Save();
            okActionCompletedCallback?.Invoke();
        }

        public void OnBack()
        {
            if (_capturing != null) EndCapture();
        }

        public void OnSharedSettingChanged(string setting, int value) { }

        // ------------------------------------------------------------------
        // Key capture
        // ------------------------------------------------------------------
        private static readonly KeyCode[] Capturable = BuildCapturable();

        private static KeyCode[] BuildCapturable()
        {
            var l = new List<KeyCode>();
            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.None || kc == KeyCode.Escape || kc == KeyCode.Delete) continue;
                if (kc == KeyCode.Mouse0 || kc == KeyCode.Mouse1) continue;          // clicking must stay clicking
                if (kc >= KeyCode.JoystickButton0) continue;                            // gamepad: not through this tab
                l.Add(kc);
            }
            return l.ToArray();
        }

        private void BeginCapture(KeyRow row)
        {
            if (_capturing != null) return;
            _capturing = row;
            _captureDelay = 0.15f;
            row.ValueText.text = "...";
            foreach (var k in _keys) k.Button.interactable = false;
            if (Settings.instance != null) Settings.instance.BlockNavigation(true);
        }

        private void EndCapture()
        {
            _capturing = null;
            foreach (var k in _keys) k.Button.interactable = true;
            if (Settings.instance != null) Settings.instance.BlockNavigation(false);
            RefreshKeyTexts();
        }

        private void Update()
        {
            if (_capturing == null) return;
            _captureDelay -= Time.unscaledDeltaTime;
            if (_captureDelay > 0f) return;
            if (ZInput.GetKeyDown(KeyCode.Escape, false)) { EndCapture(); return; }
            if (ZInput.GetKeyDown(KeyCode.Delete, false)) { _capturing.Pending = KeyCode.None; EndCapture(); return; }
            foreach (var kc in Capturable)
            {
                if (!ZInput.GetKeyDown(kc, false)) continue;
                _capturing.Pending = kc;
                EndCapture();
                return;
            }
        }

        private void RefreshKeyTexts()
        {
            foreach (var k in _keys) k.ValueText.text = k.Pending == KeyCode.None ? "-" : KeyName(k.Pending);
        }

        private static string KeyName(KeyCode kc)
        {
            switch (kc)
            {
                case KeyCode.LeftShift: return "L Shift";
                case KeyCode.RightShift: return "R Shift";
                case KeyCode.LeftControl: return "L Ctrl";
                case KeyCode.RightControl: return "R Ctrl";
                case KeyCode.LeftAlt: return "L Alt";
                case KeyCode.RightAlt: return "R Alt";
                case KeyCode.Mouse2: return "Mouse 3";
                case KeyCode.Mouse3: return "Mouse 4";
                case KeyCode.Mouse4: return "Mouse 5";
                case KeyCode.Mouse5: return "Mouse 6";
                case KeyCode.Mouse6: return "Mouse 7";
            }
            string s = kc.ToString();
            if (s.StartsWith("Alpha")) return s.Substring(5);
            if (s.StartsWith("Keypad")) return "Num " + s.Substring(6);
            return s;
        }
    }
}
