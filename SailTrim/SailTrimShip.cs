using System;
using UnityEngine;

namespace SailTrim
{
    /// <summary>
    /// Per-ship trim state, physics and network sync. Added to every Ship in Ship.Awake.
    ///
    /// Conventions
    ///   SheetAngle     0 = yard hauled fully in (along the hull), 90 = fully eased (square across the hull).
    ///   WindFromAngle  signed angle off the bow that the (apparent) wind comes FROM; + = starboard, - = port.
    ///   AngleOfAttack  angle between the wind and the sail plane. <= LuffAngle luffs, 15-30 is the sweet
    ///                  spot, > StallAngle is stalled.
    /// </summary>
    public class SailTrimShip : MonoBehaviour
    {
        public const string RpcName = "SailTrim_Sheet";
        public const string RpcModeName = "SailTrim_Mode";
        public const string RpcSheetHandName = "SailTrim_SheetHand";
        public static readonly int ZdoSheetHandHash = "sailtrim_sheethand".GetStableHashCode();
        public static readonly int ZdoSheetHash = "sailtrim_sheet".GetStableHashCode();
        public static readonly int ZdoModeHash = "sailtrim_manual".GetStableHashCode();

        private const float SendInterval = 0.1f;

        // Lift/drag coefficient tables versus angle of attack (degrees). Linear interpolation.
        // Roughly a soft sail: lift peaks around 20 deg, collapses past 45, drag climbs toward a flat plate at 90.
        private static readonly float[] LiftKeys =
        {
            0f, 0f, 5f, 0.3f, 10f, 0.75f, 15f, 1.05f, 20f, 1.2f, 25f, 1.15f, 30f, 1.0f,
            40f, 0.7f, 45f, 0.6f, 60f, 0.45f, 75f, 0.28f, 90f, 0.05f
        };
        private static readonly float[] DragKeys =
        {
            0f, 0.05f, 15f, 0.12f, 30f, 0.3f, 45f, 0.62f, 60f, 0.9f, 75f, 1.08f, 90f, 1.15f
        };
        private const float LiftNorm = 1.2f; // peak of LiftKeys, so ideal trim ~= 1.0 of the vanilla force scale

        private Ship _ship;
        private ZNetView _nview;
        private Rigidbody _body;

        public float SheetAngle { get; private set; }
        /// <summary>False = the current pilot opted out, so this ship sails vanilla for everyone.</summary>
        public bool ManualMode { get; private set; } = true;
        /// <summary>Player ID of the crew member holding the sheet, or 0 when the pilot trims.</summary>
        public long SheetHand { get; private set; }
        /// <summary>True while the boat has been heeled past MastStrainAngle for longer than the grace period.</summary>
        public bool IsMastStraining { get; private set; }
        private float _strainTimer, _strainDamageAcc, _strainDamageTimer;
        /// <summary>0 = open water, 1 = fully in the lee of land upwind.</summary>
        public float ShadowFactor { get; private set; }
        public float WindFromAngle { get; private set; }
        public float AngleOfAttack { get; private set; }
        public bool IsLuffing { get; private set; }
        /// <summary>Wind on the wrong side of the sail (e.g. in irons with the yard square): drag only, pushes downwind.</summary>
        public bool IsBackwinded { get; private set; }
        public bool IsStalled { get; private set; }
        public bool IsSweetSpot => !IsLuffing && AngleOfAttack >= 15f && AngleOfAttack <= 30f;
        /// <summary>Current gust (+) / lull (-) as a fraction of wind strength, for the readout.</summary>
        public float GustFactor { get; private set; }
        /// <summary>Best achievable sheet angle for the current wind angle (about 22 deg AoA, clamped to the sheet range).</summary>
        public float IdealSheet { get; private set; } = 45f;

        public enum TrimState { Trimmed, UnderTrimmed, OverTrimmed, Luffing, Stalled, Backwinded, NoseDiving }
        /// <summary>Readout state, computed from wave-smoothed wind angle and AoA with hysteresis so it does not flicker.</summary>
        public TrimState State { get; private set; }
        /// <summary>Wind-from angle smoothed over a couple of seconds (waves yaw the boat every second or so).</summary>
        public float SmoothWindFromAngle { get; private set; }
        public float SmoothAoA { get; private set; }
        /// <summary>Yard direction in the boat frame for the HUD: x = starboard, y = bow.</summary>
        public Vector2 YardUi { get; private set; } = new Vector2(1f, 0f);
        /// <summary>Direction the sail bellies toward, boat frame, for the HUD.</summary>
        public Vector2 BellyUi { get; private set; } = new Vector2(0f, 1f);
        /// <summary>Bow-down angle in degrees (0 when level or bow up).</summary>
        public float BowDown => _ship != null ? Mathf.Max(0f, -Mathf.Asin(Mathf.Clamp(_ship.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg) : 0f;
        /// <summary>True while the bow is buried deep enough to ship water and lose drive.</summary>
        public bool IsNoseDiving => BowDown > 0.8f * Plugin.MaxPitchAngle.Value;
        /// <summary>
        /// Displacement hull speed from waterline length (1.34 * sqrt(LWL ft) = 2.43 * sqrt(LWL m) knots),
        /// scaled by config. Raft ~8, Karve ~9.6, longship ~12.5, drakkar ~14.4 kn at the default scale.
        /// </summary>
        public float HullSpeedKnots
        {
            get
            {
                float len = _ship != null && _ship.m_floatCollider != null ? _ship.m_floatCollider.size.z : 10f;
                return 2.43f * Mathf.Sqrt(Mathf.Max(1f, len)) * Plugin.HullSpeedScale.Value;
            }
        }
        /// <summary>Forward speed in knots.</summary>
        public float SpeedKnots => _ship != null ? Mathf.Max(0f, _ship.GetSpeed()) * 1.94384f : 0f;
        /// <summary>Speed as a fraction of hull speed; above 1 the boat is being pushed past its limit.</summary>
        public float SpeedRatio => SpeedKnots / Mathf.Max(0.1f, HullSpeedKnots);
        /// <summary>Current roll in degrees, positive = heeled to port, for the readout.</summary>
        public float HeelAngle => _ship != null ? Mathf.Asin(Mathf.Clamp(_ship.transform.right.y, -1f, 1f)) * Mathf.Rad2Deg : 0f;

        private int _boomSide = 1;          // +1 = yard tip to starboard, -1 = to port (leeward side)
        private Vector3 _lastForce;         // last computed sail force (per-step velocity change units, like vanilla)
        private Vector3 _heelForce;         // same, but before the heel-costs-drive reduction (so heel never damps itself)
        private float _lastWindStrength;    // apparent wind strength used for the last force computation
        private float _noseDiveDamageAcc;   // accumulated hull damage from a buried bow, applied once a second
        private float _noseDiveDamageTimer;
        private float _lastSendTime;
        private float _lastSentSheet = float.NaN;
        private int _lastSentMode = -1;     // -1 = nothing sent yet, else 0/1
        private bool _sideInitialised;
        private bool _gybeFlag;
        private float _gybeScale;
        private float _lastGybeTime = -999f;
        private float _gybeSwingUntil = -1f;
        private bool _initialised;
        private Vector2 _smoothWindVec = new Vector2(0f, 1f);
        private bool _smoothInit;

        public static SailTrimShip Get(Ship ship)
        {
            return ship ? ship.GetComponent<SailTrimShip>() : null;
        }

        internal void Init(Ship ship)
        {
            _ship = ship;
            _nview = ship.m_nview;
            _body = ship.m_body;
            SheetAngle = Plugin.DefaultSheetAngle.Value;
        }

        /// <summary>Called from the Ship.Start postfix: RPC registration and initial value from the ZDO.</summary>
        internal void OnShipStart()
        {
            if (_nview == null || !_nview.IsValid()) return;
            _nview.Register<float>(RpcName, RPC_Sheet);
            _nview.Register<bool>(RpcModeName, RPC_Mode);
            _nview.Register<long, bool>(RpcSheetHandName, RPC_SheetHand);
            SheetHand = _nview.GetZDO().GetLong(ZdoSheetHandHash, 0L);
            SheetAngle = Mathf.Clamp(_nview.GetZDO().GetFloat(ZdoSheetHash, Plugin.DefaultSheetAngle.Value), 0f, 90f);
            ManualMode = _nview.GetZDO().GetBool(ZdoModeHash, true);
            _initialised = true;
            if (_ship.m_floatCollider != null && _body != null)
                Plugin.Log.LogInfo($"SailTrim: {_ship.name} float collider {_ship.m_floatCollider.size}, mass {_body.mass:0}, sailForceFactor {_ship.m_sailForceFactor}, sailForceOffset {_ship.m_sailForceOffset}");
            if (_ship.m_mastObject != null)
            {
                // Which interactables live on the mast (vanilla may have its own hold-fast component there).
                var hovs = _ship.m_mastObject.GetComponentsInChildren<MonoBehaviour>(true);
                var names = new System.Collections.Generic.List<string>();
                foreach (var mb in hovs) if (mb is Hoverable || mb is Interactable) names.Add(mb.GetType().Name + "@" + mb.gameObject.name);
                Plugin.Log.LogInfo($"SailTrim: {_ship.name} mast interactables: " + (names.Count > 0 ? string.Join(", ", names) : "none"));
                var cols = _ship.m_mastObject.GetComponentsInChildren<Collider>(true);
                Plugin.Log.LogInfo($"SailTrim: {_ship.name} mast object '{_ship.m_mastObject.name}' has {cols.Length} collider(s)" +
                    (cols.Length > 0 ? ": " + string.Join(", ", System.Array.ConvertAll(cols, c => c.name + "(" + c.GetType().Name + (c.isTrigger ? ",trigger" : "") + ")")) : ""));
            }
        }

        // Sent by the pilot to the ship OWNER (ZNetView.InvokeRPC(string, ...) targets the ZDO owner).
        // The owner mirrors it into the ZDO, which is how every other client learns the value.
        private void RPC_Sheet(long sender, float value)
        {
            SheetAngle = Mathf.Clamp(value, 0f, 90f);
            if (_nview != null && _nview.IsValid() && _nview.IsOwner())
                _nview.GetZDO().Set(ZdoSheetHash, SheetAngle);
        }

        private void RPC_Mode(long sender, bool manual)
        {
            ManualMode = manual;
            if (_nview != null && _nview.IsValid() && _nview.IsOwner())
                _nview.GetZDO().Set(ZdoModeHash, ManualMode);
        }

        // Crew member takes or releases the sheet (sent to the owner; the owner mirrors it into the ZDO).
        private void RPC_SheetHand(long sender, long playerId, bool take)
        {
            if (take) SheetHand = playerId;
            else if (SheetHand == playerId) SheetHand = 0L;
            if (_nview != null && _nview.IsValid() && _nview.IsOwner())
                _nview.GetZDO().Set(ZdoSheetHandHash, SheetHand);
        }

        internal void CrewSetSheetHand(bool take)
        {
            var p = Player.m_localPlayer;
            if (p == null || _nview == null || !_nview.IsValid()) return;
            long id = p.GetPlayerID();
            if (take) SheetHand = id; else if (SheetHand == id) SheetHand = 0L; // optimistic, confirmed by the owner
            _nview.InvokeRPC(RpcSheetHandName, id, take);
        }

        /// <summary>Who may move the sheet: the pilot, and a crew member holding fast on the mast. Both at once.</summary>
        internal bool IsLocalSheetAuthority()
        {
            var p = Player.m_localPlayer;
            if (p == null) return false;
            if (SheetHand != 0L && SheetHand == p.GetPlayerID()) return true;
            return IsLocalPilot();
        }

        /// <summary>True while this client has changed the sheet very recently, so it should not be overwritten by the synced value.</summary>
        private bool RecentlyTrimmed => Time.time - _lastSendTime < 0.6f;

        /// <summary>Pilot pushes their opt-in/opt-out choice to the ship whenever it differs from what was last sent.</summary>
        internal void PilotSetMode(bool manual)
        {
            ManualMode = manual;
            int m = manual ? 1 : 0;
            if (m == _lastSentMode || _nview == null || !_nview.IsValid()) return;
            _lastSentMode = m;
            _nview.InvokeRPC(RpcModeName, manual);
        }

        private float _lastClaimTime = -999f;
        private float _shadowTarget, _shadowSmoothed, _shadowNextSample;
        private float _rollPhase;
        private static readonly float[] ShadowDistances = { 25f, 50f, 90f, 150f, 250f };

        /// <summary>Pilot's client claims the ship's network ownership (vanilla API) if it does not have it.</summary>
        internal void EnsurePilotOwnsShip()
        {
            if (!Plugin.PilotOwnsShip.Value || _nview == null || !_nview.IsValid() || _nview.IsOwner()) return;
            if (Time.time - _lastClaimTime < 1f) return;
            _lastClaimTime = Time.time;
            _nview.ClaimOwnership();
            Plugin.Log.LogInfo("SailTrim: claimed ownership of " + _ship.name + " for the pilot.");
        }

        // ------------------------------------------------------------------
        // Pilot input (runs on the pilot's client, every physics step)
        // ------------------------------------------------------------------
        internal void PilotTrimInput(float moveZ, float dt)
        {
            bool takeInput = Player.m_localPlayer != null && Player.m_localPlayer.TakeInput();
            bool ease = false, haul = false;
            if (Plugin.MoveKeysTrimSheet.Value)
            {
                // Forward (W) hauls the sheet in, Backward (S) eases it out, like pulling a rope toward you.
                haul = moveZ > 0.5f;
                ease = moveZ < -0.5f;
                if (Plugin.InvertSheetKeys.Value) { bool t = haul; haul = ease; ease = t; }
            }
            if (takeInput)
            {
                if (Plugin.EaseKey.Value != KeyCode.None && ZInput.GetKey(Plugin.EaseKey.Value, false)) ease = true;
                if (Plugin.SheetInKey.Value != KeyCode.None && ZInput.GetKey(Plugin.SheetInKey.Value, false)) haul = true;
            }
            if (ease == haul) { MaybeSend(force: false); return; }

            float delta = Plugin.SheetRate.Value * dt * (ease ? 1f : -1f);
            SheetAngle = Mathf.Clamp(SheetAngle + delta, 0f, Plugin.MaxSheetAngle.Value);
            MaybeSend(force: false);
        }

        /// <summary>Crew member on the sheet (runs on their client every frame).</summary>
        internal void CrewTrimInput(bool haul, bool ease, float dt)
        {
            if (!IsLocalSheetAuthority()) return;
            if (Plugin.InvertSheetKeys.Value) { bool t = haul; haul = ease; ease = t; }
            if (ease == haul) { MaybeSend(force: false); return; }
            // A dedicated hand on the sheet works it faster than a pilot doing two jobs.
            float delta = Plugin.SheetRate.Value * (1f + Plugin.CrewTrimBonus.Value) * dt * (ease ? 1f : -1f);
            SheetAngle = Mathf.Clamp(SheetAngle + delta, 0f, Plugin.MaxSheetAngle.Value);
            MaybeSend(force: false);
        }

        private void MaybeSend(bool force)
        {
            if (_nview == null || !_nview.IsValid()) return;
            bool changed = float.IsNaN(_lastSentSheet) || Mathf.Abs(_lastSentSheet - SheetAngle) > 0.05f;
            if (!changed) return;
            if (!force && Time.time - _lastSendTime < SendInterval) return;
            _lastSendTime = Time.time;
            _lastSentSheet = SheetAngle;
            _nview.InvokeRPC(RpcName, SheetAngle);
        }

        /// <summary>
        /// Owner mirrors the sheet into the ZDO every step (same place vanilla syncs speed and rudder).
        /// Everyone else reads it back, except the pilot, who is the source of truth for their own input.
        /// </summary>
        internal void SyncControls()
        {
            if (_nview == null || !_nview.IsValid() || !_initialised) return;
            if (_nview.IsOwner())
            {
                // A crew member who left the boat drops the sheet.
                if (SheetHand != 0L && !_ship.IsPlayerInBoat(SheetHand)) SheetHand = 0L;
                _nview.GetZDO().Set(ZdoSheetHash, SheetAngle);
                _nview.GetZDO().Set(ZdoModeHash, ManualMode);
                _nview.GetZDO().Set(ZdoSheetHandHash, SheetHand);
            }
            else
            {
                var zdo = _nview.GetZDO();
                long hand = zdo.GetLong(ZdoSheetHandHash, SheetHand);
                var lp = Player.m_localPlayer;
                // Keep our optimistic claim for a moment until the owner confirms it.
                if (!(lp != null && SheetHand == lp.GetPlayerID() && hand != SheetHand && Time.time - _lastSendTime < 1f))
                    SheetHand = hand;
                // Pilot and mast hand share the sheet: whoever pulled last wins, the other follows the synced value.
                if (!IsLocalSheetAuthority() || !RecentlyTrimmed)
                    SheetAngle = Mathf.Clamp(zdo.GetFloat(ZdoSheetHash, SheetAngle), 0f, 90f);
                if (!IsLocalPilot())
                    ManualMode = zdo.GetBool(ZdoModeHash, ManualMode);
                else
                    PilotSetMode(Plugin.ManualTrim.Value); // local pilot's preference is authoritative
            }
        }

        internal bool IsLocalPilot()
        {
            var p = Player.m_localPlayer;
            return p != null && _ship.m_shipControlls != null && _ship.m_shipControlls.GetUser() == p.GetPlayerID();
        }

        // ------------------------------------------------------------------
        // Geometry shared by physics and visuals
        // ------------------------------------------------------------------
        private struct Aero
        {
            public Vector3 fwd, right, up;
            public Vector3 windTo;        // apparent wind direction the air moves toward (horizontal, unit)
            public float windStrength;    // vanilla-style 0.25..1 factor scaled by apparent speed
            public float trueFromDot;     // dot(true wind FROM direction, bow): vanilla's no-go input
            public Vector3 yardDir;       // unit vector along the yard, toward its leeward tip
            public Vector3 leewardNormal; // sail normal pointing away from the wind side
            public float aoa;             // effective angle of attack, -90..90
            public bool valid;
        }

        private Aero ComputeAero()
        {
            var a = new Aero();
            if (EnvMan.instance == null || _ship == null) return a;
            var t = _ship.transform;
            a.up = t.up;
            a.fwd = t.forward; a.fwd.y = 0f;
            if (a.fwd.sqrMagnitude < 1e-4f) return a;
            a.fwd.Normalize();
            a.right = Vector3.Cross(Vector3.up, a.fwd);

            Vector3 trueWind = EnvMan.instance.GetWindDir(); trueWind.y = 0f;
            if (trueWind.sqrMagnitude < 1e-4f) trueWind = a.fwd; else trueWind.Normalize();
            float intensity = EnvMan.instance.GetWindIntensity();
            float trueStrength = Mathf.Lerp(0.25f, 1f, intensity);
            GustFactor = ComputeGust();
            trueStrength *= Mathf.Max(0.1f, 1f + GustFactor);
            trueStrength *= 1f - Plugin.WindShadowMax.Value * _shadowSmoothed;

            // Apparent wind = true wind - boat velocity (scaled by config)
            float refSpeed = Plugin.WindSpeedReference.Value;
            Vector3 trueVel = trueWind * (trueStrength * refSpeed);
            Vector3 boatVel = _body != null ? _body.linearVelocity : Vector3.zero; boatVel.y = 0f;
            Vector3 app = trueVel - boatVel * Plugin.ApparentWindFactor.Value;
            float appSpeed = app.magnitude;
            if (appSpeed < 0.05f) { a.windTo = trueWind; a.windStrength = 0f; }
            else { a.windTo = app / appSpeed; a.windStrength = appSpeed / refSpeed; }

            Vector3 from = -a.windTo;
            float beta = Mathf.Atan2(Vector3.Dot(from, a.right), Vector3.Dot(from, a.fwd)) * Mathf.Rad2Deg; // + = from starboard
            WindFromAngle = beta;
            a.trueFromDot = Vector3.Dot(-trueWind, a.fwd);

            // Boom/yard tip goes to leeward. Keep the previous side near dead ahead / dead astern (hysteresis).
            float absBeta = Mathf.Abs(beta);
            int prevSide = _boomSide;
            if (absBeta > 8f && absBeta < 172f) _boomSide = beta > 0f ? -1 : 1;

            // The yard crossing while the wind is astern is a gybe. Crossing near the bow is a tack: harmless.
            if (_sideInitialised && _boomSide != prevSide && absBeta >= 150f && _ship.IsSailUp()
                && Plugin.GybesEnabled.Value && Time.time - _lastGybeTime > 5f)
            {
                _lastGybeTime = Time.time;
                float sail = _ship.m_speed == Ship.Speed.Full ? 1f : 0.5f;
                // Sheeted in before the crossing = controlled gybe: the yard barely travels.
                _gybeScale = Mathf.Lerp(0.15f, 1f, SheetAngle / 90f) * Mathf.Clamp01(trueStrength) * sail;
                _gybeFlag = true;
                _gybeSwingUntil = Time.time + 1.5f;
            }
            _sideInitialised = true;

            float s = SheetAngle * Mathf.Deg2Rad;
            a.yardDir = -a.fwd * Mathf.Cos(s) + a.right * (_boomSide * Mathf.Sin(s));
            Vector3 n = Vector3.Cross(Vector3.up, a.yardDir);
            float side = Vector3.Dot(n, a.right * _boomSide);
            if (Mathf.Abs(side) < 1e-3f) side = Vector3.Dot(n, a.fwd); // yard square: leeward is forward
            a.leewardNormal = side >= 0f ? n : -n;

            float raw = absBeta - SheetAngle;
            a.aoa = raw <= 90f ? raw : 180f - raw;
            a.valid = true;
            return a;
        }

        /// <summary>
        /// Rare gusts and lulls, deterministic from world time so every client agrees. Each period-long slot
        /// rolls once for an event; most of the time only a tiny ambient wobble remains.
        /// </summary>
        private static float ComputeGust()
        {
            float ambientAmp = Plugin.AmbientVariation.Value;
            double time = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.timeAsDouble;
            float ambient = ambientAmp * (0.6f * Mathf.Sin((float)(time * (2.0 * Math.PI / 23.0)))
                                         + 0.4f * Mathf.Sin((float)(time * (2.0 * Math.PI / 41.0))));
            if (Plugin.GustStrength.Value <= 0f) return ambient;

            float period = Mathf.Max(60f, Plugin.GustPeriodMinutes.Value * 60f);
            float duration = Mathf.Min(Plugin.GustDuration.Value, period * 0.3f);
            long slot = (long)Math.Floor(time / period);
            uint h = Hash(unchecked((uint)slot) ^ 0x9E3779B9u);
            float rOccur = (h & 0xFFFF) / 65535f; h = Hash(h);
            if (rOccur > Plugin.GustChance.Value) return ambient;
            float rCenter = (h & 0xFFFF) / 65535f; h = Hash(h);
            float rSign = (h & 0xFFFF) / 65535f; h = Hash(h);
            float rStrength = (h & 0xFFFF) / 65535f;

            float center = Mathf.Lerp(0.15f, 0.85f, rCenter) * period;
            float local = (float)(time - slot * (double)period) - center;
            float x = Mathf.Abs(local) / (duration * 0.5f); // 0 at the peak, 1 at the edges
            if (x >= 1f) return ambient;
            float env = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - 0.4f) / 0.6f));
            float sign = rSign < Plugin.LullFraction.Value ? -1f : 1f;
            float strength = Mathf.Lerp(0.5f, 1f, rStrength) * Plugin.GustStrength.Value;
            return ambient + sign * strength * env;
        }

        private static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
            }
            return x;
        }

        private static float Sample(float[] keys, float x)
        {
            if (x <= keys[0]) return keys[1];
            for (int i = 2; i < keys.Length; i += 2)
            {
                if (x <= keys[i])
                {
                    float t = (x - keys[i - 2]) / (keys[i] - keys[i - 2]);
                    return Mathf.Lerp(keys[i - 1], keys[i + 1], t);
                }
            }
            return keys[keys.Length - 1];
        }

        // ------------------------------------------------------------------
        // Physics: replaces Ship.GetSailForce on the ZDO owner
        // ------------------------------------------------------------------
        internal Vector3 ComputeSailForce(float sailSize, float dt)
        {
            var a = ComputeAero();
            Vector3 target = Vector3.zero;
            if (a.valid && sailSize > 0f && a.windStrength > 0f)
            {
                AngleOfAttack = a.aoa;
                float aoa = a.aoa;
                IsBackwinded = aoa < -Plugin.LuffAngle.Value;
                IsLuffing = !IsBackwinded && aoa <= Plugin.LuffAngle.Value;
                IsStalled = !IsLuffing && !IsBackwinded && aoa > Plugin.StallAngle.Value;

                float cl, cd;
                if (IsLuffing) { cl = 0f; cd = 0.05f; }
                else if (IsBackwinded)
                {
                    // Sail aback: pressed against the mast, no lift, just drag pushing the boat downwind
                    // (astern when in irons). That is how a square-rigger backs out of irons.
                    cl = 0f;
                    cd = Sample(DragKeys, -aoa) * Plugin.DragScale.Value;
                }
                else
                {
                    cl = Sample(LiftKeys, aoa) * Plugin.LiftScale.Value;
                    cd = Sample(DragKeys, aoa) * Plugin.DragScale.Value;
                }

                // No-go zone: exactly vanilla's cut-off (Ship.GetWindAngleFactor): lift is gone within
                // ~37 deg of the true wind and fully back by ~41 deg. The vanilla ship HUD shows this zone.
                float noGoScale = 1f - Utils.LerpStep(0.75f, 0.8f, a.trueFromDot);
                cl *= noGoScale;

                // Lift is perpendicular to the apparent wind, toward the sail's leeward side.
                Vector3 liftDir = Vector3.Cross(Vector3.up, a.windTo);
                if (Vector3.Dot(liftDir, a.leewardNormal) < 0f) liftDir = -liftDir;

                Vector3 f = (liftDir * cl + a.windTo * cd) / LiftNorm;

                // Keep a filled sail sailable, however badly trimmed (not when aback: that push is meant to be backwards).
                if (!IsLuffing && !IsBackwinded)
                {
                    float drive = Vector3.Dot(f, a.fwd);
                    float floor = Plugin.MinFilledDrive.Value * noGoScale;
                    if (drive < floor) f += a.fwd * (floor - drive);
                }

                float scale = _ship.m_sailForceFactor * sailSize * a.windStrength * Plugin.ForceMultiplier.Value;
                _lastWindStrength = a.windStrength;
                Vector3 heelTarget = f * scale;

                // A little heel lifts wetted surface off a round-bilged hull: a small bonus peaking near 8 deg
                // and gone by ~15 deg. Beyond that the sail presents less area and the hull drags: lying over is slow.
                float rollRad = Mathf.Asin(Mathf.Clamp(_ship.transform.right.y, -1f, 1f));
                float rollDeg = Mathf.Abs(rollRad) * Mathf.Rad2Deg;
                float cosRoll = Mathf.Cos(rollRad);
                float loss = Mathf.Lerp(1f, cosRoll * cosRoll, Plugin.HeelDriveLoss.Value);
                float bonus = Plugin.HeelSweetSpotBonus.Value * Mathf.Sin(Mathf.Clamp01(rollDeg / 16f) * Mathf.PI);
                scale *= loss + bonus;

                // A buried bow drags: pressing on with too much sail downwind is self-limiting.
                float bury = Mathf.Clamp01((BowDown - 3f) / Mathf.Max(1f, Plugin.MaxPitchAngle.Value - 3f));
                scale *= 1f - Plugin.NoseDiveLoss.Value * bury;

                target = f * scale;
                _heelForce = Vector3.Lerp(_heelForce, heelTarget, Mathf.Clamp01(dt / Mathf.Max(0.05f, Plugin.ForceSmoothTime.Value)));
            }
            else
            {
                IsLuffing = false;
                IsStalled = false;
                IsBackwinded = false;
                if (a.valid) AngleOfAttack = a.aoa;
                _heelForce = Vector3.Lerp(_heelForce, Vector3.zero, Mathf.Clamp01(dt / Mathf.Max(0.05f, Plugin.ForceSmoothTime.Value)));
            }

            _ship.m_sailForce = Vector3.SmoothDamp(_ship.m_sailForce, target, ref _ship.m_windChangeVelocity,
                Plugin.ForceSmoothTime.Value, 99f);
            _lastForce = _ship.m_sailForce;
            return _ship.m_sailForce;
        }

        /// <summary>
        /// Extra heel, applied after Ship.CustomFixedUpdate on the owner. Implemented as a damped spring
        /// toward a target roll angle so it stays stable whatever the prefab's mass/inertia are.
        ///
        /// The target comes from the sail's sideways force, which already scales with sail size
        /// (Half = 0.5, Full = 1.0) and wind strength: a reefed sail heels half as much and light air barely
        /// heels at all. Rowing/furled states never reach here (IsSailUp), so sculling with the sheet hauled
        /// in adds no heel. With little or no side force (luffing) the spring fades out entirely, leaving
        /// natural wave roll alone.
        /// </summary>
        internal void ApplyHullEffects(float dt)
        {
            if (_body == null || _ship == null) return;
            if (!_ship.IsSailUp()) return;
            if (!_nview.IsOwner()) return;

            // Only while the hull is actually in the water (same test vanilla uses before applying forces).
            Vector3 com = _body.worldCenterOfMass;
            float water = Floating.GetWaterLevel(com, ref _ship.m_previousCenter);
            if (com.y - water - _ship.m_waterLevelOffset > _ship.m_disableLevel) return;

            var t = _ship.transform;
            Vector3 fwd = t.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // Sail forces as fractions of the ship's full-scale sail force (1.0 = full sail, max wind).
            float latRef = Mathf.Max(1e-4f, _ship.m_sailForceFactor);
            float lateralRaw = Vector3.Dot(_lastForce, right);
            float lateral = Vector3.Dot(_heelForce, right) / latRef;
            float driveN = Vector3.Dot(_heelForce, fwd) / latRef;
            float roll = Mathf.Asin(Mathf.Clamp(t.right.y, -1f, 1f)) * Mathf.Rad2Deg; // + = starboard up (heeled to port)
            float maxHeel = Plugin.MaxHeelAngle.Value;
            float stall = Plugin.StallAngle.Value;

            // Weather helm: a heeled boat wants to round up into the wind, so you have to hold rudder against it.
            // Heeled to port (roll > 0, wind from starboard) -> yaw to starboard (+ about up).
            if (Plugin.WeatherHelm.Value > 0f && Mathf.Abs(roll) > 1f)
            {
                // Bigger hulls carry more rudder authority and a longer keel: less weather helm per degree of heel.
                float hb = _ship.m_floatCollider != null ? _ship.m_floatCollider.size.x * 0.5f : 2f;
                float beamScale = Mathf.Pow(2f / Mathf.Max(0.3f, hb), 2f);
                // Quadratic in heel: a modest 10 degrees barely tugs, 20 is the reference, past that it really rounds up.
                float yawAccel = roll * (Mathf.Abs(roll) / 20f) * Plugin.WeatherHelm.Value * beamScale; // deg/s^2
                float yawInertia = RollInertia(t.up);
                _body.AddTorque(t.up * (yawInertia * yawAccel * Mathf.Deg2Rad * dt), ForceMode.Impulse);
            }

            // Leeway: a stalled sail or a boat on its ear slides sideways instead of tracking straight.
            if (Plugin.LeewayFactor.Value > 0f)
            {
                float leeway = 0f;
                if (AngleOfAttack > stall) leeway += Mathf.Clamp01((AngleOfAttack - stall) / Mathf.Max(1f, 90f - stall));
                leeway += Mathf.Clamp01(Mathf.Abs(roll) / maxHeel);
                leeway = Mathf.Clamp01(leeway) * Plugin.LeewayFactor.Value;
                if (leeway > 0f)
                    _body.AddForce(right * (lateralRaw * leeway * _body.mass), ForceMode.Impulse);
            }

            // Wave-making resistance: past hull speed the hull climbs its own bow wave and drag rises steeply.
            float over = SpeedRatio - 1f;
            if (over > 0f && Plugin.HullSpeedDrag.Value > 0f)
            {
                float decel = Plugin.HullSpeedDrag.Value * over * over; // m/s^2
                _body.AddForce(-fwd * (decel * _body.mass * dt), ForceMode.Impulse);
            }

            ApplyPitch(dt, t, fwd, driveN);

            if (Plugin.HeelTorque.Value <= 0f) return;

            // Heeling moment: the side force, plus a share of the drive (the rig pulls high above the hull's
            // resistance) that fades as the wind goes aft, so running dead downwind rolls rather than heels.
            float sinBeta = Mathf.Abs(Mathf.Sin(WindFromAngle * Mathf.Deg2Rad));
            float heelMag = Mathf.Abs(lateral) + Plugin.HeelDriveShare.Value * Mathf.Abs(driveN) * sinBeta;
            float fill = Mathf.Clamp01(heelMag * 2f);
            if (fill <= 0.001f) return;

            float boost = 1f;
            if (AngleOfAttack > stall)
                boost += Plugin.StallHeelBoost.Value * Mathf.Clamp01((AngleOfAttack - stall) / Mathf.Max(1f, 90f - stall));

            // Storms lay you over: emphasise wind strength beyond the linear force scaling.
            float windEmphasis = Mathf.Pow(Mathf.Clamp(_lastWindStrength, 0.05f, 1.5f), Mathf.Max(0f, Plugin.HeelWindPower.Value - 1f));

            // A plain torque, like a real rig. Vanilla's own buoyancy is the righting moment, so the boat
            // settles where the two balance instead of being dragged to a target angle. Torque is scaled by
            // hull beam (Karve float collider = 4 m wide, the reference) so a narrow hull lies over more than a beamy one.
            float halfBeam = _ship.m_floatCollider != null ? _ship.m_floatCollider.size.x * 0.5f : 2f;
            float beamNorm = Mathf.Pow(Mathf.Max(0.3f, halfBeam) / 2f, 2.5f);
            float torque = heelMag * windEmphasis * boost * Plugin.HeelTorque.Value * _body.mass * beamNorm; // N*m

            // Positive torque about +forward lifts the starboard side (roll = asin(right.y) > 0).
            // The boat heels away from the wind: wind from starboard (WindFromAngle > 0) -> heel to port -> positive roll.
            float dir = WindFromAngle >= 0f ? 1f : -1f;

            // Safety: fade the torque out as the boat nears the heel cap in the direction it is being pushed.
            float fade = 1f;
            if (Mathf.Sign(roll) == dir) fade = 1f - Utils.LerpStep(maxHeel - 10f, maxHeel, Mathf.Abs(roll));

            // Water damping on roll rate, so it settles instead of wallowing. Scaled by fill so a furled or
            // luffing sail leaves natural wave roll alone.
            float rollRateRad = Vector3.Dot(_body.angularVelocity, t.forward);
            float inertia = RollInertia(t.forward);
            float damping = -Plugin.RollDamping.Value * rollRateRad * inertia * fill;

            float impulse = (dir * torque * fade + damping) * dt;
            _body.AddTorque(t.forward * impulse, ForceMode.Impulse);

            ApplyDownwindRolling(dt, t, roll, maxHeel, beamNorm, halfBeam, inertia);
        }

        /// <summary>
        /// Running dead downwind with the sail up, a square-rigger rolls rhythmically: the sail's drive has no
        /// steadying side component. A gentle periodic roll torque that grows with wind and sail area and fades
        /// as you head up past ~30 degrees off dead downwind, or reef. Damped so it never builds on itself.
        /// </summary>
        private void ApplyDownwindRolling(float dt, Transform t, float roll, float maxHeel, float beamNorm, float halfBeam, float inertia)
        {
            if (Plugin.DownwindRolling.Value <= 0f || !_ship.IsSailUp()) return;
            float downwind = Mathf.Clamp01((Mathf.Abs(WindFromAngle) - 150f) / 30f);
            if (downwind <= 0f) { _rollPhase = 0f; return; }

            float sail = _ship.m_speed == Ship.Speed.Full ? 1f : 0.5f;
            float wind = Mathf.Clamp(_lastWindStrength, 0f, 1.5f);
            float period = Plugin.RollPeriod.Value * Mathf.Sqrt(Mathf.Max(0.3f, halfBeam) / 2f);
            _rollPhase += dt / Mathf.Max(1f, period);
            float amp = Plugin.DownwindRolling.Value * _body.mass * beamNorm * sail * wind * wind * downwind;
            float torque = amp * Mathf.Sin(_rollPhase * 2f * Mathf.PI);
            float fade = 1f - Utils.LerpStep(maxHeel - 10f, maxHeel, Mathf.Abs(roll));
            float rollRateRad = Vector3.Dot(_body.angularVelocity, t.forward);
            float damping = -Plugin.RollDamping.Value * rollRateRad * inertia * downwind;
            _body.AddTorque(t.forward * ((torque * fade + damping) * dt), ForceMode.Impulse);
        }

        /// <summary>
        /// Bow-down trim from the drive force (the rig pulls high, the hull resists low), as a plain torque
        /// balanced by vanilla buoyancy, like the heel. Faded out at MaxPitchAngle so it buries the bow but
        /// never pitchpoles. A buried bow also ships water: a little hull damage per second until you ease
        /// out or reef.
        /// </summary>
        private void ApplyPitch(float dt, Transform t, Vector3 fwd, float driveN)
        {
            if (Plugin.PitchTorque.Value <= 0f || driveN <= 0f) { ResetNoseDiveDamage(); return; }

            // Bow burying is a strong-wind problem: scale harder with wind than heel does.
            float windEmphasis = Mathf.Pow(Mathf.Clamp(_lastWindStrength, 0.05f, 1.5f), Mathf.Max(0f, Plugin.PitchWindPower.Value - 1f));
            float maxPitch = Plugin.MaxPitchAngle.Value;
            float fill = Mathf.Clamp01(driveN * 2f);

            float halfLen = _ship.m_floatCollider != null ? _ship.m_floatCollider.size.z * 0.5f : 5f;
            float lenNorm = Mathf.Pow(Mathf.Max(0.5f, halfLen) / 5f, 2.5f);
            float torque = driveN * windEmphasis * Plugin.PitchTorque.Value * _body.mass * lenNorm; // N*m, bow down
            // Driven past hull speed the bow digs in hard: a small hull buries first.
            torque *= 1f + Plugin.OverSpeedPitchBoost.Value * Mathf.Max(0f, SpeedRatio - 1f);

            // Positive torque about +right pushes the bow down. Fade out near the cap.
            float fade = 1f - Utils.LerpStep(maxPitch - 5f, maxPitch, BowDown);

            float pitchRateRad = Vector3.Dot(_body.angularVelocity, t.right);
            float inertia = RollInertia(t.right);
            float damping = -Plugin.RollDamping.Value * pitchRateRad * inertia * fill;

            float impulse = (torque * fade + damping) * dt;
            _body.AddTorque(t.right * impulse, ForceMode.Impulse);

            // Shipping water over the bow.
            if (Plugin.NoseDiveDamagePerSecond.Value > 0f && IsNoseDiving)
            {
                var wnt = _ship.GetComponent<WearNTear>();
                if (wnt != null)
                {
                    _noseDiveDamageAcc += wnt.m_health * (Plugin.NoseDiveDamagePerSecond.Value / 100f) * dt;
                    _noseDiveDamageTimer += dt;
                    if (_noseDiveDamageTimer >= 1f)
                    {
                        var hit = new HitData();
                        hit.m_damage.m_blunt = _noseDiveDamageAcc;
                        hit.m_point = t.position + fwd * 3f;
                        hit.m_dir = Vector3.down;
                        wnt.Damage(hit);
                        _noseDiveDamageAcc = 0f;
                        _noseDiveDamageTimer = 0f;
                    }
                }
            }
            else ResetNoseDiveDamage();
        }

        private void ResetNoseDiveDamage()
        {
            _noseDiveDamageAcc = 0f;
            _noseDiveDamageTimer = 0f;
        }

        /// <summary>Moment of inertia of the rigidbody about a world-space axis through its centre of mass.</summary>
        private float RollInertia(Vector3 worldAxis)
        {
            Vector3 local = _body.transform.InverseTransformDirection(worldAxis).normalized;
            Vector3 a = Quaternion.Inverse(_body.inertiaTensorRotation) * local;
            Vector3 i = _body.inertiaTensor;
            return Mathf.Max(1f, i.x * a.x * a.x + i.y * a.y * a.y + i.z * a.z * a.z);
        }

        // ------------------------------------------------------------------
        // Visuals: replaces the mast rotation in Ship.UpdateSail while the sail is up (all clients)
        // ------------------------------------------------------------------
        internal void UpdateYard(float dt)
        {
            var mast = _ship.m_mastObject;
            if (mast == null) return;
            var a = ComputeAero();
            if (!a.valid) return;

            // Non-owners never run ComputeSailForce, so refresh the readout state here.
            if (!_nview.IsOwner())
            {
                AngleOfAttack = a.aoa;
                IsBackwinded = a.aoa < -Plugin.LuffAngle.Value;
                IsLuffing = !IsBackwinded && a.aoa <= Plugin.LuffAngle.Value;
                IsStalled = !IsLuffing && !IsBackwinded && a.aoa > Plugin.StallAngle.Value;
            }

            UpdateWindShadow(a, dt);
            UpdateReadout(a, dt);
            UpdateMastStrain(dt);

            // Vanilla points the mast object's forward DOWNWIND (the sail bellies away from mast.forward's
            // back face), and builds the rotation in the hull plane so the rig heels with the hull.
            // Aback: the wind is on the other face, so the sail bellies the other way.
            Vector3 facing = Vector3.ProjectOnPlane(IsBackwinded && _ship.IsSailUp() ? -a.leewardNormal : a.leewardNormal, a.up);
            if (facing.sqrMagnitude < 1e-4f) return;
            facing.Normalize();
            if (IsLuffing && _ship.IsSailUp() && Plugin.FlapAmplitude.Value > 0f)
            {
                float wobble = Mathf.Sin(Time.time * Mathf.PI * 2f * Plugin.FlapFrequency.Value)
                               * Plugin.FlapAmplitude.Value * Mathf.Clamp01(a.windStrength + 0.25f);
                facing = Quaternion.AngleAxis(wobble, a.up) * facing;
            }

            if (_gybeFlag)
            {
                _gybeFlag = false;
                OnGybe();
            }

            float turnRate = Plugin.YardTurnRate.Value;
            if (Time.time < _gybeSwingUntil) turnRate *= 3f; // the yard slams across
            Quaternion to = Quaternion.LookRotation(facing, a.up);
            mast.transform.rotation = Quaternion.RotateTowards(mast.transform.rotation, to, turnRate * dt);
        }

        /// <summary>Rig strain: every client tracks it for the readout; only the owner applies damage.</summary>
        private void UpdateMastStrain(float dt)
        {
            bool over = _ship.IsSailUp() && Mathf.Abs(HeelAngle) > Plugin.MastStrainAngle.Value;
            _strainTimer = over ? _strainTimer + dt : Mathf.Max(0f, _strainTimer - dt * 2f);
            IsMastStraining = _strainTimer > Plugin.MastStrainGrace.Value;

            if (!IsMastStraining || Plugin.MastStrainDamagePerSecond.Value <= 0f || _nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                _strainDamageAcc = 0f; _strainDamageTimer = 0f;
                return;
            }
            var wnt = _ship.GetComponent<WearNTear>();
            if (wnt == null) return;
            _strainDamageAcc += wnt.m_health * (Plugin.MastStrainDamagePerSecond.Value / 100f) * dt;
            _strainDamageTimer += dt;
            if (_strainDamageTimer >= 1f)
            {
                var hit = new HitData();
                hit.m_damage.m_blunt = _strainDamageAcc;
                hit.m_point = _ship.m_mastObject != null ? _ship.m_mastObject.transform.position : _ship.transform.position;
                hit.m_dir = Vector3.down;
                wnt.Damage(hit);
                _strainDamageAcc = 0f; _strainDamageTimer = 0f;
            }
        }

        /// <summary>
        /// Lee of the land: sample terrain height upwind at a few distances. Land that subtends more than
        /// WindShadowOnset degrees above the water starts to shelter the boat; the effect is capped at
        /// WindShadowMax so a river between banks is slower, never becalmed. Same terrain on every client.
        /// </summary>
        private void UpdateWindShadow(Aero a, float dt)
        {
            if (Plugin.WindShadowMax.Value <= 0f) { _shadowSmoothed = 0f; ShadowFactor = 0f; return; }
            if (Time.time >= _shadowNextSample)
            {
                _shadowNextSample = Time.time + 0.3f;
                Vector3 from = -a.windTo;
                Vector3 pos = _ship.transform.position;
                float water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
                float baseY = Mathf.Max(water, pos.y);
                float worst = 0f;
                foreach (float d in ShadowDistances)
                {
                    Vector3 pnt = pos + from * d;
                    if (!Heightmap.GetHeight(pnt, out float h)) continue;
                    float rise = h - baseY;
                    if (rise <= 0f) continue;
                    float ang = Mathf.Atan2(rise, d) * Mathf.Rad2Deg;
                    float sh = Mathf.Clamp01((ang - Plugin.WindShadowOnset.Value) / Mathf.Max(1f, Plugin.WindShadowRange.Value));
                    if (sh > worst) worst = sh;
                }
                _shadowTarget = worst;
            }
            _shadowSmoothed = Mathf.MoveTowards(_shadowSmoothed, _shadowTarget, dt * 0.5f);
            ShadowFactor = _shadowSmoothed;
        }

        /// <summary>
        /// Wave-smoothed numbers and a hysteretic state for the HUD. Waves yaw and roll the boat every
        /// second or so, which swings the instantaneous apparent wind; averaging over ~1.5 s gives the trim
        /// that is right on average, which is what the sailor should be steering for.
        /// </summary>
        private void UpdateReadout(Aero a, float dt)
        {
            float tau = Mathf.Max(0.1f, Plugin.ReadoutSmoothing.Value);
            float k = 1f - Mathf.Exp(-dt / tau);
            float b = WindFromAngle * Mathf.Deg2Rad;
            Vector2 v = new Vector2(Mathf.Sin(b), Mathf.Cos(b));
            if (!_smoothInit) { _smoothWindVec = v; SmoothAoA = a.aoa; _smoothInit = true; }
            _smoothWindVec = Vector2.Lerp(_smoothWindVec, v, k);
            if (_smoothWindVec.sqrMagnitude > 1e-4f)
                SmoothWindFromAngle = Mathf.Atan2(_smoothWindVec.x, _smoothWindVec.y) * Mathf.Rad2Deg;
            SmoothAoA = Mathf.Lerp(SmoothAoA, a.aoa, k);

            float absBeta = Mathf.Abs(SmoothWindFromAngle);
            IdealSheet = Mathf.Clamp(absBeta - 22f, 0f, Plugin.MaxSheetAngle.Value);
            float diff = SheetAngle - IdealSheet;

            // HUD vectors (boat frame: x = starboard, y = bow).
            YardUi = new Vector2(Vector3.Dot(a.yardDir, a.right), Vector3.Dot(a.yardDir, a.fwd));
            Vector3 belly = IsBackwinded ? -a.leewardNormal : a.leewardNormal;
            BellyUi = new Vector2(Vector3.Dot(belly, a.right), Vector3.Dot(belly, a.fwd));

            // State with hysteresis: hard states win, otherwise compare sheet to the best reachable sheet.
            float luff = Plugin.LuffAngle.Value;
            TrimState next;
            if (IsNoseDiving) next = TrimState.NoseDiving;
            else if (SmoothAoA < -luff - 2f || (State == TrimState.Backwinded && SmoothAoA < -luff + 2f)) next = TrimState.Backwinded;
            else if (SmoothAoA <= luff || (State == TrimState.Luffing && SmoothAoA <= luff + 3f)) next = TrimState.Luffing;
            else
            {
                bool wasTrimmed = State == TrimState.Trimmed;
                float band = wasTrimmed ? 11f : 6f;
                if (Mathf.Abs(diff) <= band) next = TrimState.Trimmed;
                else if (diff < 0f) next = SmoothAoA > Plugin.StallAngle.Value ? TrimState.Stalled : TrimState.OverTrimmed;
                else next = TrimState.UnderTrimmed;
            }
            State = next;
        }

        /// <summary>
        /// Runs on every client the step the yard crosses with the wind astern. Everyone gets the message and
        /// the bang; the owner also applies the roll kick and hull damage.
        /// </summary>
        private void OnGybe()
        {
            var t = _ship.transform;
            bool controlled = _gybeScale < 0.4f;
            var local = Player.m_localPlayer;
            if (local != null && _ship.IsPlayerInBoat(local))
                local.Message(MessageHud.MessageType.Center, controlled ? "Controlled gybe" : "GYBE!");

            Vector3 mastPos = _ship.m_mastObject != null ? _ship.m_mastObject.transform.position : t.position;
            if (!controlled) _ship.m_waterImpactEffect.Create(t.position, t.rotation);

            if (!_nview.IsOwner()) return;

            if (Plugin.GybeRollRate.Value > 0f && _body != null)
            {
                // New boom side +1 = yard tip now to starboard -> boat gets thrown to starboard (negative roll).
                float rate = Plugin.GybeRollRate.Value * _gybeScale * Mathf.Deg2Rad;
                _body.AddTorque(t.forward * (-_boomSide * RollInertia(t.forward) * rate), ForceMode.Impulse);
            }

            if (Plugin.GybeDamagePercent.Value > 0f)
            {
                var wnt = _ship.GetComponent<WearNTear>();
                if (wnt != null)
                {
                    var hit = new HitData();
                    hit.m_damage.m_blunt = wnt.m_health * (Plugin.GybeDamagePercent.Value / 100f) * _gybeScale;
                    hit.m_point = mastPos;
                    hit.m_dir = Vector3.up;
                    wnt.Damage(hit);
                }
            }
        }
    }
}
