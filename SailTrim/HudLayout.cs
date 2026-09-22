using System.Globalization;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// Where each piece of the sailing HUD sits, and a way to move them about while looking at them.
    ///
    /// One offset for the whole HUD is not enough. Other mods rearrange the ship's dials in their own ways, and
    /// whatever we choose will be wrong beside the next one; what a player needs is to drag our pieces clear of
    /// whatever is in the way on their screen. So each piece carries its own offset and scale, and there is a
    /// layout mode that nudges them with the arrow keys and shows the result as it goes. Nothing is written to
    /// the config until it is accepted, and Escape puts everything back as it was.
    /// </summary>
    internal static class HudLayout
    {
        internal enum Part { Gauge, State, Info, Hint, Controls }

        private static readonly Part[] Order = { Part.Gauge, Part.State, Part.Info, Part.Hint, Part.Controls };

        private static readonly string[] Names = { "speed gauge", "state line", "readings line", "hint line", "controls list" };

        private static ConfigEntry<string>[] _entries;
        private static Vector3[] _live;     // x, y, scale as they stand this instant, saved or not
        private static Vector3[] _saved;    // what the config held when the layout mode was entered

        internal static bool Editing { get; private set; }
        private static int _sel;
        private static float _repeatAt;

        internal static void Bind(ConfigFile config)
        {
            _entries = new ConfigEntry<string>[Order.Length];
            _live = new Vector3[Order.Length];
            _saved = new Vector3[Order.Length];
            for (int i = 0; i < Order.Length; i++)
            {
                _entries[i] = config.Bind("1. General", "HudPart" + Order[i],
                    "0,0,1",
                    "Where the " + Names[i] + " sits: sideways, up/down and size, as three numbers. Set these with the layout mode rather than by hand (see HudLayoutKey).");
                _live[i] = Parse(_entries[i].Value);
            }
        }

        internal static Vector3 Of(Part p)
        {
            int i = System.Array.IndexOf(Order, p);
            return _live != null && i >= 0 ? _live[i] : new Vector3(0f, 0f, 1f);
        }

        private static Vector3 Parse(string s)
        {
            var v = new Vector3(0f, 0f, 1f);
            if (string.IsNullOrEmpty(s)) return v;
            var bits = s.Split(',');
            if (bits.Length > 0) float.TryParse(bits[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v.x);
            if (bits.Length > 1) float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v.y);
            if (bits.Length > 2 && float.TryParse(bits[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float sc) && sc > 0.05f) v.z = sc;
            return v;
        }

        private static string Write(Vector3 v) =>
            v.x.ToString("0.##", CultureInfo.InvariantCulture) + "," +
            v.y.ToString("0.##", CultureInfo.InvariantCulture) + "," +
            v.z.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>The line shown across the HUD while the layout is being set.</summary>
        internal static string Banner()
        {
            if (!Editing) return "";
            Vector3 v = _live[_sel];
            return $"<color=#FFD24A>Moving the {Names[_sel]}</color>   {v.x:0}, {v.y:0}   size {v.z:0.00}\n" +
                   "<color=#AAAAAA>Tab next piece   arrows move (Shift: fine)   Page Up/Down size   " +
                   "Backspace reset piece   Delete reset all   Enter keep   Esc undo</color>";
        }

        internal static void Update()
        {
            if (_entries == null) return;
            var player = Player.m_localPlayer;
            if (Plugin.HudLayoutKey.Value != KeyCode.None && player != null && player.TakeInput()
                && ZInput.GetKeyDown(Plugin.HudLayoutKey.Value, false))
            {
                if (Editing) Finish(true); else Begin();
            }
            if (!Editing) return;
            if (player == null) { Finish(true); return; }

            if (ZInput.GetKeyDown(KeyCode.Escape, false)) { Finish(false); return; }
            if (ZInput.GetKeyDown(KeyCode.Return, false) || ZInput.GetKeyDown(KeyCode.KeypadEnter, false)) { Finish(true); return; }
            if (ZInput.GetKeyDown(KeyCode.Tab, false))
                _sel = (_sel + (ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false) ? Order.Length - 1 : 1)) % Order.Length;
            // Backspace puts this piece back; Delete puts the whole HUD back, including the offsets that move
            // all of it at once. Anyone who has moved things about wants one key that undoes the lot.
            if (ZInput.GetKeyDown(KeyCode.Backspace, false)) _live[_sel] = new Vector3(0f, 0f, 1f);
            if (ZInput.GetKeyDown(KeyCode.Delete, false))
            {
                for (int i = 0; i < _live.Length; i++) _live[i] = new Vector3(0f, 0f, 1f);
                Plugin.HudOffsetX.Value = 0f;
                Plugin.HudOffsetY.Value = 0f;
                Plugin.HudScale.Value = 1f;
                Plugin.HudAnchor.Value = HudCorner.WindDial;
            }

            // Held keys repeat, slowly at first, so a nudge is a nudge and a long press is a sweep.
            float step = ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false) ? 1f : 5f;
            bool go = Time.unscaledTime >= _repeatAt;
            Vector2 move = Vector2.zero;
            if (Key(KeyCode.LeftArrow, go)) move.x -= step;
            if (Key(KeyCode.RightArrow, go)) move.x += step;
            if (Key(KeyCode.UpArrow, go)) move.y += step;
            if (Key(KeyCode.DownArrow, go)) move.y -= step;
            float grow = 0f;
            if (Key(KeyCode.PageUp, go)) grow += 0.02f;
            if (Key(KeyCode.PageDown, go)) grow -= 0.02f;
            if (move != Vector2.zero || grow != 0f)
            {
                var v = _live[_sel];
                v.x += move.x; v.y += move.y;
                v.z = Mathf.Clamp(v.z + grow, 0.3f, 3f);
                _live[_sel] = v;
                _repeatAt = Time.unscaledTime + 0.05f;
            }
        }

        private static bool Key(KeyCode k, bool repeatDue) => ZInput.GetKeyDown(k) || (ZInput.GetKey(k) && repeatDue);

        private static void Begin()
        {
            for (int i = 0; i < _live.Length; i++) _saved[i] = _live[i];
            _sel = 0;
            Editing = true;
        }

        private static void Finish(bool keep)
        {
            Editing = false;
            if (keep)
            {
                for (int i = 0; i < _live.Length; i++) _entries[i].Value = Write(_live[i]);
                Plugin.Instance?.Config.Save();
            }
            else for (int i = 0; i < _live.Length; i++) _live[i] = _saved[i];
        }

        /// <summary>Put a piece where its offset says, from where it was first laid out.</summary>
        internal static void Apply(Part p, RectTransform rt, Vector2 home)
        {
            if (rt == null) return;
            Vector3 v = Of(p);
            rt.anchoredPosition = home + new Vector2(v.x, v.y);
            float s = Mathf.Clamp(v.z, 0.3f, 3f);
            if (!Mathf.Approximately(rt.localScale.x, s)) rt.localScale = Vector3.one * s;
        }

        internal static void Apply(Part p, TMP_Text t, Vector2 home)
        {
            if (t != null) Apply(p, t.rectTransform, home);
        }
    }
}
