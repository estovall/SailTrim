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
    /// The second is weather helm. A heeled boat tries to round up into the wind, hard enough close-hauled that
    /// she will sail herself into irons if you let her, and the answer is to carry a little helm to leeward all
    /// the time rather than to keep correcting after the fact. That is not obvious to anyone who has not sailed,
    /// and nothing in the game says it, so the readout says it: which way to hold the helm, and whether what you
    /// are holding is enough.
    /// </summary>
    internal static class Course
    {
        internal static bool HasMark { get; private set; }
        internal static Vector3 Mark { get; private set; }
        internal static string MarkName { get; private set; } = "";

        internal static void Set(Vector3 pos, string name)
        {
            Mark = pos;
            MarkName = string.IsNullOrEmpty(name) ? "the mark" : name;
            HasMark = true;
            Plugin.MarkPos.Value = $"{pos.x:0.#},{pos.z:0.#}";
            Plugin.MarkName.Value = MarkName;
        }

        internal static void Clear()
        {
            HasMark = false;
            MarkName = "";
            Plugin.MarkPos.Value = "";
            Plugin.MarkName.Value = "";
        }

        /// <summary>Restore the mark a player was steering for when they last played.</summary>
        internal static void Load()
        {
            string s = Plugin.MarkPos.Value;
            if (string.IsNullOrEmpty(s)) return;
            var bits = s.Split(',');
            if (bits.Length != 2) return;
            if (!float.TryParse(bits[0], out float x) || !float.TryParse(bits[1], out float z)) return;
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
            f.Moving = flat.magnitude * 1.94384f >= 0.4f;
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

        /// <summary>
        /// What the helm is doing about the heel, in words. She rounds up into the wind as she lies over, so the
        /// helm has to be held toward the side she is leaning: down, away from the wind. A helmsman who does not
        /// know that fights her all the way to windward and wonders why she keeps stalling.
        /// </summary>
        internal static string HelmAdvice(Ship ship, SailTrimShip st)
        {
            if (ship == null || st == null) return "";
            float heel = st.HeelAngle;                    // positive: lying over to starboard
            if (Mathf.Abs(heel) < 7f) return "";          // upright enough to carry no helm worth mentioning
            if (st.SailAmount <= 0.01f) return "";        // under oars she does not round up

            // Rudder: positive turns her one way, and which way that is we take from the boat itself rather than
            // assume. Held toward the low side is helm that balances her.
            float rudder = ship.m_rudderValue;
            float want = Mathf.Sign(heel);                // the low side is the side she is heeled to
            float held = rudder * want;                   // positive when the helm is already the right way
            string side = heel > 0f ? "starboard" : "port";

            if (held > 0.15f) return $"Carrying helm to {side}, holding her";
            if (Mathf.Abs(heel) > 20f) return $"She is rounding up: helm to {side}, or ease the sheet";
            return $"Weather helm: hold a little to {side}";
        }
    }
}
