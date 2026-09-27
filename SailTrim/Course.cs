using System.Globalization;
using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// A mark to steer for, taken off the map, and the two things a helmsman needs that a bearing alone does not
    /// give him.
    ///
    /// The first is that a boat does not go where her bow points. She makes leeway, so steering straight at a
    /// mark puts you downwind of it by the end of a long board. The course to steer is the bearing to the mark
    /// with that offset taken out, which is the oldest piece of navigation there is.
    ///
    /// The second is the wind: a course worked from geometry alone will ask for a bearing inside the no-go, and
    /// a helmsman who is told to steer it will steer it and stop dead.
    ///
    /// It states, it does not coach. There was a line here that read the heel and told the helmsman which way to
    /// hold the helm; it is gone at Max's word, and he is right. Advice on the screen while you steer is advice
    /// that gets followed instead of read, and it takes the sailing out of sailing.
    /// </summary>
    internal static class Course
    {
        internal static bool HasMark { get; private set; }
        internal static Vector3 Mark { get; private set; }
        internal static string MarkName { get; private set; } = "";

        /// <summary>
        /// The world this mark belongs to. A mark is a place, and a place means nothing in another world: without
        /// this, opening a different save showed a mark sitting at coordinates from the last one.
        /// </summary>
        private static string World()
        {
            var net = ZNet.instance;
            string s = net != null ? net.GetWorldName() : null;
            return string.IsNullOrEmpty(s) ? "" : s;
        }

        internal static void Set(Vector3 pos, string name)
        {
            Mark = pos;
            MarkName = string.IsNullOrEmpty(name) ? "the mark" : name;
            HasMark = true;
            // Written plainly, in numbers that read the same everywhere. Formatted in the machine's own way, a
            // mark saved on a keyboard that writes decimals with a comma came back as four fields instead of
            // two and was quietly lost.
            Plugin.MarkPos.Value = pos.x.ToString("0.#", CultureInfo.InvariantCulture) + ","
                                 + pos.z.ToString("0.#", CultureInfo.InvariantCulture) + "," + World();
            Plugin.MarkName.Value = MarkName;
        }

        internal static void Clear()
        {
            HasMark = false;
            MarkName = "";
            Plugin.MarkPos.Value = "";
            Plugin.MarkName.Value = "";
        }

        private static bool _loaded;

        /// <summary>
        /// Restore the mark, once the world is up: which world it is decides whether the saved mark means
        /// anything. Called every frame and does nothing after the first.
        /// </summary>
        internal static void Tick()
        {
            if (ZNet.instance == null) { _loaded = false; return; }
            if (_loaded) return;
            _loaded = true;
            Load();
        }

        /// <summary>Restore the mark a player was steering for when they last played this world.</summary>
        internal static void Load()
        {
            HasMark = false;
            string s = Plugin.MarkPos.Value;
            if (string.IsNullOrEmpty(s)) return;
            var bits = s.Split(',');
            if (bits.Length < 2) return;
            if (bits.Length > 2 && bits[2] != World()) return;   // another world's mark: leave it where it is
            if (!float.TryParse(bits[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return;
            if (!float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return;
            Mark = new Vector3(x, 0f, z);
            MarkName = Plugin.MarkName.Value;
            HasMark = true;
        }

        private static float Wrap(float deg)
        {
            while (deg < 0f) deg += 360f;
            while (deg >= 360f) deg -= 360f;
            return deg;
        }

        internal static float Bearing(Vector3 dir)
        {
            float b = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            return b < 0f ? b + 360f : b;
        }

        /// <summary>Everything the helm wants to know at once, so the callers do not each work it out again.</summary>
        internal struct Fix
        {
            public bool Valid;
            public float Distance;     // metres to the mark
            public float ToMark;       // bearing of the mark from here
            public float Heading;      // where her bow points
            public float Track;        // the course she is making good, if she is moving
            public bool Moving;
            public float Leeway;       // track minus heading, signed: positive is being set to starboard
            public float Steer;        // the bearing to hold so the track comes out on the mark
            public float Off;          // track minus bearing to mark, signed: positive is passing to starboard
            public bool CanLay;        // is the course to steer outside the no-go, so she can actually sail it
            public float BoardA, BoardB;   // the closest she can sail on either side of the wind
            public float Best;         // of those two, the one nearer where she is pointing now
        }

        /// <summary>
        /// The bearing straight into the wind. Anything within the no-go of this cannot be sailed, and a course
        /// worked out from geometry alone will cheerfully ask for it: told to steer 010 with the wind out of 350,
        /// a helmsman does what he is told and stops dead in irons. Advice that ignores the wind is worse than no
        /// advice, because it is followed.
        /// </summary>
        private static bool Upwind(out float intoWind)
        {
            intoWind = 0f;
            var env = EnvMan.instance;
            if (env == null) return false;
            Vector3 to = env.GetWindDir();
            to.y = 0f;
            if (to.sqrMagnitude < 1e-4f) return false;
            intoWind = Bearing(-to.normalized);
            return true;
        }

        internal static Fix Reckon(Ship ship)
        {
            var f = new Fix();
            if (!HasMark || ship == null) return f;
            Vector3 here = ship.transform.position;
            Vector3 to = Mark - here;
            to.y = 0f;
            if (to.sqrMagnitude < 1f) return f;

            f.Valid = true;
            f.Distance = to.magnitude;
            f.ToMark = Bearing(to);
            f.Heading = Bearing(ship.transform.forward);

            Vector3 vel = ship.m_body != null ? ship.m_body.linearVelocity : Vector3.zero;
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            // Fast enough for the track to mean something, and going forwards. Under a knot or so what the hull
            // is doing is the swell moving it about, and making sternway -- rowing back, or drifting out of
            // irons -- the track is the reciprocal of the heading, which read as 180 degrees of leeway and
            // turned the course to steer into a course away from the mark.
            f.Moving = flat.magnitude * 1.94384f >= 1.5f && Vector3.Dot(flat, ship.transform.forward) > 0f;
            if (f.Moving)
            {
                f.Track = Bearing(flat);
                f.Leeway = Mathf.DeltaAngle(f.Heading, f.Track);
                f.Off = Mathf.DeltaAngle(f.ToMark, f.Track);
            }
            else
            {
                f.Track = f.Heading;
                f.Off = Mathf.DeltaAngle(f.ToMark, f.Heading);
            }
            // Steer up into the leeway by as much as it is setting you down.
            f.Steer = f.ToMark - f.Leeway;
            if (f.Steer < 0f) f.Steer += 360f;
            if (f.Steer >= 360f) f.Steer -= 360f;

            f.CanLay = true;
            if (Upwind(out float intoWind))
            {
                float noGo = Mathf.Clamp(Plugin.NoGoAngle.Value, 5f, 80f);
                f.CanLay = Mathf.Abs(Mathf.DeltaAngle(intoWind, f.Steer)) >= noGo;
                f.BoardA = Wrap(intoWind - noGo);
                f.BoardB = Wrap(intoWind + noGo);
                f.Best = Mathf.Abs(Mathf.DeltaAngle(f.Heading, f.BoardA))
                       <= Mathf.Abs(Mathf.DeltaAngle(f.Heading, f.BoardB)) ? f.BoardA : f.BoardB;
            }
            return f;
        }

        internal static string Line(Fix f)
        {
            if (!f.Valid) return "";
            string dist = f.Distance >= 1000f ? $"{f.Distance / 1000f:0.0} km" : $"{f.Distance:0} m";
            // The mark is dead to windward: there is no course that fetches it, so say so and give the board
            // she is already nearest to rather than a bearing she cannot hold.
            if (!f.CanLay)
                return $"{MarkName}  {dist}   dead to windward: beat on {f.Best:000}";

            string off = Mathf.Abs(f.Off) < 3f
                ? "on for it"
                : $"{Mathf.Abs(f.Off):0} {(f.Off > 0f ? "right" : "left")} of it";
            return $"{MarkName}  {dist}   steer {f.Steer:000}   {off}";
        }

    }
}
