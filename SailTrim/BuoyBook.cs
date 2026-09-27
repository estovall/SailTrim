using System.Collections.Generic;
using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// Every buoy in the world, whether or not it is loaded here, so that a channel shows on the chart from the
    /// other side of it.
    ///
    /// A buoy used to put a pin on the map while it was loaded and take it away again when it unloaded, which
    /// meant the marks you most wanted to see -- the ones at the far end of the passage you are planning -- were
    /// the ones that were never there. Max's word: they should behave like map pins, which are remembered.
    ///
    /// So the server keeps the book. It is the one machine that holds every buoy's record whether anyone is near
    /// it or not, and it reads them straight out of the world (<c>GetAllZDOsWithPrefabIterative</c>, a slice at a
    /// time so a harbour full of them costs nothing) and sends the list to each client. A client draws a pin for
    /// every entry and keeps its own entry for a buoy standing in front of it up to date at once, so changing a
    /// buoy's colour shows now rather than at the next sweep.
    ///
    /// In single player the host is the client, and the same code does both halves with no message sent.
    /// </summary>
    internal static class BuoyBook
    {
        internal const string RpcName = "SailTrim_Buoys";

        internal struct Mark
        {
            public long Id;
            public Vector3 Pos;
            public int Color;
            public string Name => Color >= 0 && Color < Buoy.ColorNames.Length ? Buoy.ColorNames[Color] + " buoy" : "buoy";
            public Color Tint => Color >= 0 && Color < Buoy.PinColors.Length ? Buoy.PinColors[Color] : UnityEngine.Color.white;
        }

        private static readonly Dictionary<long, Mark> _marks = new Dictionary<long, Mark>();
        private static readonly Dictionary<long, Minimap.PinData> _pins = new Dictionary<long, Minimap.PinData>();
        private static readonly List<ZDO> _found = new List<ZDO>();
        private static readonly HashSet<long> _toldPeers = new HashSet<long>();
        private static readonly List<long> _gone = new List<long>();

        private static bool _registered;
        private static int _scanIndex;
        private static float _scanAt;
        private static bool _dirty;
        private static float _sendAt;

        internal static int Count => _marks.Count;
        internal static Dictionary<long, Mark>.ValueCollection All => _marks.Values;

        private static bool Host => ZNet.instance != null && ZNet.instance.IsServer();

        /// <summary>A buoy with no tag of its own (one built before the book) is known by where it stands.</summary>
        private static long KeyFor(long tag, Vector3 pos)
        {
            if (tag != 0L) return tag;
            long x = Mathf.RoundToInt(pos.x * 4f);
            long z = Mathf.RoundToInt(pos.z * 4f);
            return (x << 24) ^ z ^ 0x5A17B002L;
        }

        internal static void Reset()
        {
            _marks.Clear();
            foreach (var kv in _pins)
            {
                BuoyPiece.Pins.Remove(kv.Value);
                if (Minimap.instance != null) Minimap.instance.RemovePin(kv.Value);
            }
            _pins.Clear();
            _toldPeers.Clear();
            _found.Clear();
            _registered = false;
            _scanIndex = 0;
            _dirty = false;
        }

        /// <summary>A buoy standing in front of us: its own record is the freshest there is, so it goes in now
        /// rather than at the host's next sweep. On a client the host's list replaces this soon after and says
        /// the same thing; the point is only that changing a buoy's colour shows immediately.</summary>
        internal static void ReportPiece(ZNetView nv, Vector3 pos, int color)
        {
            if (nv == null || !nv.IsValid()) return;
            long tag = nv.IsOwner() ? Tag.Of(nv) : Tag.Read(nv);
            Report(KeyFor(tag, pos), pos, color);
        }

        /// <summary>A buoy loaded here: its own record is the freshest there is, so it goes in at once.</summary>
        internal static void Report(long id, Vector3 pos, int color)
        {
            if (id == 0L) return;
            if (_marks.TryGetValue(id, out var was) && was.Color == color && (was.Pos - pos).sqrMagnitude < 0.04f) return;
            _marks[id] = new Mark { Id = id, Pos = pos, Color = color };
            _dirty = true;
        }

        internal static void Forget(long id)
        {
            if (id == 0L || !_marks.Remove(id)) return;
            _dirty = true;
        }

        /// <summary>The buoy nearest a spot on the chart, loaded or not.</summary>
        internal static bool Nearest(Vector3 at, float radius, out Mark best)
        {
            best = default(Mark);
            float bestD = radius;
            bool any = false;
            foreach (var m in _marks.Values)
            {
                Vector3 d = m.Pos - at;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist >= bestD) continue;
                bestD = dist;
                best = m;
                any = true;
            }
            return any;
        }

        // ------------------------------------------------------------------
        internal static void Tick()
        {
            if (ZNet.instance == null || ZRoutedRpc.instance == null) { if (_registered) Reset(); return; }
            if (!_registered)
            {
                _registered = true;
                ZRoutedRpc.instance.Register<ZPackage>(RpcName, OnList);
            }
            if (Host) HostSweep();
            Pins();
        }

        /// <summary>
        /// The host reads the world's own record of every buoy, a slice of the ZDO table per frame. Nothing here
        /// loads anything or touches an object: a buoy three zones away is a row in a table, which is exactly why
        /// the host is the one that can see them all.
        /// </summary>
        private static void HostSweep()
        {
            if (ZDOMan.instance == null) return;
            if (_scanIndex == 0)
            {
                if (Time.time < _scanAt) { Share(); return; }
                _found.Clear();
            }
            bool done = ZDOMan.instance.GetAllZDOsWithPrefabIterative(Buoy.PrefabName, _found, ref _scanIndex);
            if (!done) return;

            _scanIndex = 0;
            _scanAt = Time.time + 10f;
            var seen = new HashSet<long>();
            foreach (var zdo in _found)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                Vector3 pos = zdo.GetPosition();
                long id = KeyFor(zdo.GetLong(Tag.Key, 0L), pos);
                seen.Add(id);
                Report(id, pos, zdo.GetInt(Buoy.ColorHash, 0));
            }
            // Anything the world no longer holds is gone: broken up, or never there.
            _gone.Clear();
            foreach (var id in _marks.Keys) if (!seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone) Forget(id);
            _found.Clear();
            Share();
        }

        /// <summary>Send the book to anyone who has not had it, and to everyone when it has changed.</summary>
        private static void Share()
        {
            var net = ZNet.instance;
            if (!Host || net == null || ZRoutedRpc.instance == null) return;
            var peers = net.GetPeers();
            bool anyNew = false;
            foreach (var p in peers) if (p != null && !_toldPeers.Contains(p.m_uid)) { anyNew = true; break; }
            if (!_dirty && !anyNew) return;
            if (_dirty && Time.time < _sendAt && !anyNew) return;
            _sendAt = Time.time + 2f;

            var pkg = Write();
            _toldPeers.Clear();
            foreach (var p in peers)
            {
                if (p == null || !p.IsReady()) continue;
                _toldPeers.Add(p.m_uid);
                ZRoutedRpc.instance.InvokeRoutedRPC(p.m_uid, RpcName, new object[] { pkg });
            }
            _dirty = false;
        }

        private static ZPackage Write()
        {
            var pkg = new ZPackage();
            pkg.Write(_marks.Count);
            foreach (var m in _marks.Values)
            {
                pkg.Write(m.Id);
                pkg.Write(m.Pos);
                pkg.Write(m.Color);
            }
            return pkg;
        }

        private static void OnList(long sender, ZPackage pkg)
        {
            // The host keeps its own book; it has no use for a copy of one, and never for a client's.
            if (Host || pkg == null) return;
            try
            {
                int n = pkg.ReadInt();
                if (n < 0 || n > 20000) return;
                _marks.Clear();
                for (int i = 0; i < n; i++)
                {
                    long id = pkg.ReadLong();
                    Vector3 pos = pkg.ReadVector3();
                    int color = pkg.ReadInt();
                    _marks[id] = new Mark { Id = id, Pos = pos, Color = color };
                }
            }
            catch (System.Exception e) { Plugin.Once("buoy list", e); }
        }

        // ------------------------------------------------------------------
        /// <summary>One pin per buoy in the book, in its own colour. The game paints every pin white each frame
        /// and <see cref="BuoyPiece.Pins"/> is what the minimap patch re-tints them from.</summary>
        private static void Pins()
        {
            var map = Minimap.instance;
            if (map == null) return;
            if (!Plugin.BuoyPins.Value)
            {
                if (_pins.Count == 0) return;
                foreach (var kv in _pins) { BuoyPiece.Pins.Remove(kv.Value); map.RemovePin(kv.Value); }
                _pins.Clear();
                return;
            }

            foreach (var m in _marks.Values)
            {
                if (!_pins.TryGetValue(m.Id, out var pin) || pin == null)
                {
                    pin = map.AddPin(m.Pos, Minimap.PinType.Icon3, "", false, false);
                    if (pin == null) continue;
                    pin.m_icon = Buoy.PinSprite();
                    _pins[m.Id] = pin;
                }
                pin.m_pos = m.Pos;
                BuoyPiece.Pins[pin] = m.Tint;
            }

            if (_pins.Count == _marks.Count) return;
            _gone.Clear();
            foreach (var kv in _pins) if (!_marks.ContainsKey(kv.Key)) _gone.Add(kv.Key);
            foreach (long id in _gone)
            {
                var pin = _pins[id];
                _pins.Remove(id);
                if (pin == null) continue;
                BuoyPiece.Pins.Remove(pin);
                map.RemovePin(pin);
            }
        }
    }
}
