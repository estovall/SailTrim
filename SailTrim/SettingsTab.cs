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
        private int _part;
        private TMP_Text _partRow;
        private TMP_Dropdown _partDrop;
        private TMP_Text _labelSource;
        private CanvasGroup _fade;
        private float _fadeWas = 1f;
        private Slider _hudX, _hudY, _hudS;
        private bool _hudRefreshing;
        private class SliderText { public TMP_Text Text; public Func<float, string> Show; }
        private readonly Dictionary<Slider, SliderText> _sliderValues = new Dictionary<Slider, SliderText>();
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
            // Any of the game's own sliders will do as a pattern; the volumes on the audio page are sliders.
            Slider sliderTemplate = settings.GetComponentInChildren<Slider>(true);
            TMP_Dropdown dropTemplate = settings.GetComponentInChildren<TMP_Dropdown>(true);
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
            tab.Build(page, keyRowTemplate, toggleTemplate, sliderTemplate, dropTemplate);

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
        private void Build(RectTransform page, RectTransform keyRowTemplate, Toggle toggleTemplate, Slider sliderTemplate, TMP_Dropdown dropTemplate)
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
            scroll.scrollSensitivity = 0f; // the wheel is handled in Update: one notch = three rows
            _scroll = scroll; _list = list; _viewport = viewport;
            var layout = listGo.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false; layout.childControlHeight = false;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
            layout.spacing = 12f;
            listGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // The toggle's own label is the reliable source for the settings font/colour.
            TMP_Text labelSource = toggleTemplate.GetComponentInChildren<TMP_Text>(true);
            _labelSource = labelSource;
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
            AddToggle(list, toggleTemplate, "Show the sailing keys beside the ship HUD", Plugin.ShowControls);

            // The HUD keeps drawing behind this page, so it can be arranged from here and watched while it is
            // done. Pick a piece, then move and size it; OK keeps the lot and Back puts it all as it was.
            AddHeader(list, labelSource, "HUD layout (watch it move at the right)");
            if (sliderTemplate != null)
            {
                if (dropTemplate != null) AddDropdown(list, dropTemplate, "Move", i => { _part = i; RefreshHudRows(); });
                else
                    _partRow = AddButtonRow(list, keyRowTemplate, "Move", HudLayout.NameOf(_part), () =>
                    {
                        _part = (_part + 1) % HudLayout.Count;
                        RefreshHudRows();
                    });
                _hudX = AddSlider(list, sliderTemplate, "Left / right", -600f, 600f, () => HudLayout.Live(_part).x,
                                  v => HudLayout.Nudge(_part, v, null, null), v => v.ToString("0"));
                _hudY = AddSlider(list, sliderTemplate, "Up / down", -600f, 600f, () => HudLayout.Live(_part).y,
                                  v => HudLayout.Nudge(_part, null, v, null), v => v.ToString("0"));
                _hudS = AddSlider(list, sliderTemplate, "Size", 0.4f, 2f, () => HudLayout.Live(_part).z,
                                  v => HudLayout.Nudge(_part, null, null, v), v => (v * 100f).ToString("0") + "%");
                AddButtonRow(list, keyRowTemplate, "Put the HUD back", "Reset", () => { HudLayout.ResetAll(); RefreshHudRows(); });
            }
            else AddInfoKey(list, keyRowTemplate, "HUD layout", "no slider to copy");

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

        /// <summary>A row whose button does something when pressed, rather than capturing a key.</summary>
        private TMP_Text AddButtonRow(RectTransform list, RectTransform template, string label, string value, Action onClick)
        {
            var go = Instantiate(template.gameObject, list);
            go.name = "Btn_" + label;
            go.SetActive(true);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(20f, 32f);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth = 20f; le.preferredHeight = 32f;

            var button = go.GetComponentInChildren<Button>(true);
            var valueText = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (button == null || valueText == null) { Destroy(go); return null; }
            button.gameObject.SetActive(true);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick());
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
            return valueText;
        }

        /// <summary>
        /// One of the game's sliders, relabelled, reporting straight into the live HUD. The clone arrives with
        /// its own label and its own readout ("Mouse sensitivity ... 100%"); those are what get relabelled. Adding
        /// a label of our own on top of one already there is how the rows came out written over each other.
        /// </summary>
        private Slider AddSlider(RectTransform list, Slider template, string label,
                                 float min, float max, Func<float> read, Action<float> write, Func<float, string> show)
        {
            var go = Instantiate(template.gameObject, list);
            go.name = "Slider_" + label;
            go.SetActive(true);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth = 20f; le.preferredHeight = 32f;
            var slider = go.GetComponent<Slider>();
            if (slider == null) { Destroy(go); return null; }

            // Left to right: the label, then the slider, then the readout.
            var texts = new List<TMP_Text>(go.GetComponentsInChildren<TMP_Text>(true));
            texts.Sort((a, b) => a.rectTransform.position.x.CompareTo(b.rectTransform.position.x));
            TMP_Text lbl = texts.Count > 0 ? texts[0] : null;
            TMP_Text val = texts.Count > 1 ? texts[texts.Count - 1] : null;
            if (lbl != null)
            {
                lbl.gameObject.SetActive(true);
                lbl.text = label;
                lbl.enableAutoSizing = false;
                lbl.alignment = TextAlignmentOptions.MidlineRight;
            }

            slider.onValueChanged = new Slider.SliderEvent();
            slider.minValue = min; slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.value = Mathf.Clamp(read(), min, max);
            if (val != null) val.text = show(slider.value);
            slider.onValueChanged.AddListener(v =>
            {
                if (val != null) val.text = show(v);
                if (!_hudRefreshing) write(v);
            });
            _sliderValues[slider] = new SliderText { Text = val, Show = show };
            return slider;
        }

        /// <summary>The list of things that can be moved, as one of the game's own dropdowns.</summary>
        private void AddDropdown(RectTransform list, TMP_Dropdown template, string label, Action<int> onPick)
        {
            var go = Instantiate(template.gameObject, list);
            go.name = "Drop_" + label;
            go.SetActive(true);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth = 20f; le.preferredHeight = 32f;
            var drop = go.GetComponent<TMP_Dropdown>();
            if (drop == null) { Destroy(go); return; }

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(220f, 30f);

            drop.onValueChanged = new TMP_Dropdown.DropdownEvent();
            drop.ClearOptions();
            var names = new List<string>();
            for (int i = 0; i < HudLayout.Count; i++) names.Add(HudLayout.NameOf(i));
            drop.AddOptions(names);
            drop.value = Mathf.Clamp(_part, 0, names.Count - 1);
            drop.RefreshShownValue();
            drop.onValueChanged.AddListener(i => onPick(i));
            _partDrop = drop;

            // Its own caption is inside it; the row label goes to the left, like the others.
            var text = Instantiate(_labelSource, go.transform.parent);
            text.gameObject.SetActive(true);
            text.text = label;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.MidlineRight;
            var lrt = text.GetComponent<RectTransform>();
            lrt.SetSiblingIndex(go.transform.GetSiblingIndex());
            lrt.sizeDelta = new Vector2(320f, 32f);
        }

        private void RefreshHudRows()
        {
            _hudRefreshing = true;
            if (_partRow != null) _partRow.text = HudLayout.NameOf(_part);
            if (_partDrop != null && _partDrop.value != _part) { _partDrop.value = _part; _partDrop.RefreshShownValue(); }
            Vector3 v = HudLayout.Live(_part);
            SetSlider(_hudX, v.x);
            SetSlider(_hudY, v.y);
            SetSlider(_hudS, v.z);
            _hudRefreshing = false;
        }

        private void SetSlider(Slider s, float value)
        {
            if (s == null) return;
            s.value = Mathf.Clamp(value, s.minValue, s.maxValue);
            if (_sliderValues.TryGetValue(s, out var st) && st.Text != null) st.Text.text = st.Show(s.value);
        }

        // ------------------------------------------------------------------
        // ISettingsTab
        // ------------------------------------------------------------------
        public void Initialize()
        {
            foreach (var k in _keys) { k.Pending = k.Entry.Value; }
            foreach (var t in _toggles) t.Toggle.isOn = t.Entry.Value;
            HudLayout.BeginEdit();
            RefreshHudRows();
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
            HudLayout.EndEdit(true);
            if (changed) Plugin.Instance.Config.Save();
            okActionCompletedCallback?.Invoke();
        }

        public void OnBack()
        {
            if (_capturing != null) EndCapture();
            HudLayout.EndEdit(false);
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

        private ScrollRect _scroll;
        private RectTransform _list, _viewport;
        private float _wheelAcc;
        private const float WheelStepPixels = 3f * 44f; // three rows per notch

        /// <summary>
        /// Show the world through the settings panel while this page is up. The HUD is drawn behind it and the
        /// whole point of arranging it here is to watch it move; a panel you cannot see past is a panel that hides
        /// the thing being adjusted. Only while this page is the one showing, and put back when it is not.
        /// </summary>
        private void OnEnable()
        {
            var settings = GetComponentInParent<Settings>();
            var root = settings != null ? settings.gameObject : null;
            if (root == null) return;
            _fade = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            _fadeWas = _fade.alpha;
            _fade.alpha = 0.55f;
        }

        private void OnDisable()
        {
            if (_fade != null) _fade.alpha = _fadeWas;
            _fade = null;
        }

        private void Update()
        {
            UpdateWheel();
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

        /// <summary>Mouse wheel scrolls the list a fixed three rows per notch, whatever the wheel's raw scale.</summary>
        private void UpdateWheel()
        {
            if (_scroll == null || _list == null || _viewport == null) return;
            float w = ZInput.GetMouseScrollWheel();
            if (w == 0f) { return; }
            _wheelAcc += w;
            if (Mathf.Abs(_wheelAcc) < 0.1f) return;
            float dir = Mathf.Sign(_wheelAcc);
            _wheelAcc = 0f;
            float range = LayoutUtility.GetPreferredHeight(_list) + 40f - _viewport.rect.height;
            if (range <= 1f) { _scroll.verticalNormalizedPosition = 1f; return; }
            // Wheel up (positive) shows earlier rows: normalized 1 = top.
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + dir * WheelStepPixels / range);
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
