using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SailTrim
{
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInProcess("valheim.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.maxst.sailtrim";
        public const string NAME = "SailTrim";
        public const string VERSION = "1.0.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        private Harmony _harmony;

        // ---- Config: general ----
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowHud;
        internal static ConfigEntry<bool> ControlHints;
        internal static ConfigEntry<float> CameraTilt;
        internal static ConfigEntry<float> HullSpeedScale;
        internal static ConfigEntry<float> HullSpeedDrag;
        internal static ConfigEntry<float> OverSpeedPitchBoost;
        internal static ConfigEntry<float> ReadoutSmoothing;

        // ---- Config: controls ----
        internal static ConfigEntry<bool> ManualTrim;
        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<float> ReleaseHoldTime;
        internal static ConfigEntry<KeyCode> LowerSailKey;
        internal static ConfigEntry<KeyCode> RaiseSailKey;
        internal static ConfigEntry<bool> MoveKeysTrimSheet;
        internal static ConfigEntry<bool> InvertSheetKeys;
        internal static ConfigEntry<KeyCode> EaseKey;
        internal static ConfigEntry<KeyCode> SheetInKey;
        internal static ConfigEntry<float> SheetRate;
        internal static ConfigEntry<float> MaxSheetAngle;
        internal static ConfigEntry<float> DefaultSheetAngle;

        // ---- Config: physics ----
        internal static ConfigEntry<float> ForceMultiplier;
        internal static ConfigEntry<float> LiftScale;
        internal static ConfigEntry<float> DragScale;
        internal static ConfigEntry<float> MinFilledDrive;
        internal static ConfigEntry<float> LuffAngle;
        internal static ConfigEntry<float> StallAngle;
        internal static ConfigEntry<float> ForceSmoothTime;
        internal static ConfigEntry<float> ApparentWindFactor;
        internal static ConfigEntry<float> WindSpeedReference;

        // ---- Config: heel ----
        internal static ConfigEntry<float> HeelTorque;
        internal static ConfigEntry<float> RollDamping;
        internal static ConfigEntry<float> HeelWindPower;
        internal static ConfigEntry<float> HeelDriveShare;
        internal static ConfigEntry<float> StallHeelBoost;
        internal static ConfigEntry<float> MaxHeelAngle;

        // ---- Config: pitch ----
        internal static ConfigEntry<float> PitchTorque;
        internal static ConfigEntry<float> PitchWindPower;
        internal static ConfigEntry<float> MaxPitchAngle;
        internal static ConfigEntry<float> NoseDiveLoss;
        internal static ConfigEntry<float> NoseDiveDamagePerSecond;

        // ---- Config: realism ----
        internal static ConfigEntry<float> GustPeriodMinutes;
        internal static ConfigEntry<float> GustChance;
        internal static ConfigEntry<float> GustDuration;
        internal static ConfigEntry<float> GustStrength;
        internal static ConfigEntry<float> LullFraction;
        internal static ConfigEntry<float> AmbientVariation;
        internal static ConfigEntry<float> WeatherHelm;
        internal static ConfigEntry<float> HeelDriveLoss;
        internal static ConfigEntry<float> HeelSweetSpotBonus;
        internal static ConfigEntry<float> LeewayFactor;
        internal static ConfigEntry<bool> GybesEnabled;
        internal static ConfigEntry<float> GybeDamagePercent;
        internal static ConfigEntry<float> GybeRollRate;

        // ---- Config: visuals ----
        internal static ConfigEntry<float> YardTurnRate;
        internal static ConfigEntry<float> FlapAmplitude;
        internal static ConfigEntry<float> FlapFrequency;

        // ---- Input state for tap/hold on the Use button ----
        private bool _wasPiloting;
        private bool _requireUseRelease;
        private bool _useHeld;
        private float _useHeldTime;
        private bool _helmReleased;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            BindConfig();

            if (!VerifyPatchTargets())
            {
                Log.LogError("SailTrim: one or more Harmony patch targets are missing in this game version. The mod is disabled.");
                return;
            }

            _harmony = new Harmony(GUID);
            _harmony.PatchAll(typeof(Patches));
            Log.LogInfo($"SailTrim {VERSION} loaded. Game version {Version.CurrentVersion}.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private void BindConfig()
        {
            Enabled = Config.Bind("1. General", "Enabled", true,
                "Master switch. When false the boat sails exactly like vanilla (patches stay loaded but pass through).");
            ShowHud = Config.Bind("1. General", "ShowHud", true,
                "Show the trim overlay on the ship HUD: sail icon on the wind circle, speed gauge, state and heel text.");
            ControlHints = Config.Bind("1. General", "ControlHints", true,
                "Show the raise/lower key hint under the ship HUD when you take the helm with the sail furled. It disappears once you have used the keys (until the next game start). false = never show it.");
            CameraTilt = Config.Bind("1. General", "CameraTilt", 1f,
                new ConfigDescription("How much the camera rolls with the ship when it heels. 1 = the game's own behaviour (the vanilla 'ship camera tilt' setting still applies), 0.5 = half as much, 0 = the camera stays level however far the boat heels.",
                    new AcceptableValueRange<float>(0f, 1f)));
            HullSpeedScale = Config.Bind("1. General", "HullSpeedScale", 1.6f,
                new ConfigDescription("Each ship's hull speed is 2.43 * sqrt(waterline length in m) knots times this. Raft ~10, Karve ~12, longship ~16, drakkar ~18 kn at 1.6. The speed gauge turns red past it, drag rises and the bow digs in.",
                    new AcceptableValueRange<float>(0.5f, 3f)));
            HullSpeedDrag = Config.Bind("7. Pitch", "HullSpeedDrag", 3f,
                new ConfigDescription("Extra deceleration past hull speed, in m/s^2 per (speed ratio - 1)^2. Wave-making resistance: a boat pushed 30% past hull speed feels about 0.3 m/s^2 of extra drag. 0 disables.",
                    new AcceptableValueRange<float>(0f, 20f)));
            OverSpeedPitchBoost = Config.Bind("7. Pitch", "OverSpeedPitchBoost", 2f,
                new ConfigDescription("How much harder the bow digs in past hull speed: bow-down torque is multiplied by 1 + this * (speed ratio - 1). A Karve at longship speeds buries its bow long before the longship does.",
                    new AcceptableValueRange<float>(0f, 10f)));
            ReadoutSmoothing = Config.Bind("1. General", "ReadoutSmoothing", 1.5f,
                new ConfigDescription("Seconds of averaging applied to the wind angle and angle of attack behind the trim hint, so wave rocking does not make it flicker. The physics is not smoothed.",
                    new AcceptableValueRange<float>(0.1f, 6f)));

            ManualTrim = Config.Bind("2. Controls", "ManualTrim", false,
                "Your personal opt-in, remembered between sessions. Starts OFF: whenever you hold the rudder the boat sails exactly like vanilla (W/S step the sail, tap E lets go, auto-trim). Press ToggleKey (H) at the helm to switch to manual trim; your choice is saved here. Other players keep their own setting.");
            ToggleKey = Config.Bind("2. Controls", "ToggleKey", KeyCode.H,
                "Key that switches ManualTrim on/off in game (saved to this config).");
            ReleaseHoldTime = Config.Bind("2. Controls", "ReleaseHoldTime", 0.5f,
                new ConfigDescription("Seconds the Use button (E) must be held to let go of the rudder. A shorter tap raises the sail one step instead.",
                    new AcceptableValueRange<float>(0.15f, 2f)));
            LowerSailKey = Config.Bind("2. Controls", "LowerSailKey", KeyCode.Q,
                "Key that lowers the sail one step (Full > Half > Slow > Stop > Back), i.e. what S does in vanilla.");
            RaiseSailKey = Config.Bind("2. Controls", "RaiseSailKey", KeyCode.None,
                "Optional extra key that raises the sail one step. Tapping the game's Use button (E) always does this too.");
            MoveKeysTrimSheet = Config.Bind("2. Controls", "MoveKeysTrimSheet", true,
                "Use the game's Forward/Backward bindings (W/S, or the left stick) to sheet in (W) and ease out (S) while at the rudder.");
            InvertSheetKeys = Config.Bind("2. Controls", "InvertSheetKeys", false,
                "Swap them: W eases out, S sheets in.");
            EaseKey = Config.Bind("2. Controls", "EaseKey", KeyCode.None,
                "Optional extra key that eases the sheet out (yard swings away from the centreline).");
            SheetInKey = Config.Bind("2. Controls", "SheetInKey", KeyCode.None,
                "Optional extra key that sheets in (yard swings toward the centreline).");
            SheetRate = Config.Bind("2. Controls", "SheetRate", 25f,
                new ConfigDescription("How fast the sheet angle changes while a trim key is held, in degrees per second.",
                    new AcceptableValueRange<float>(5f, 120f)));
            MaxSheetAngle = Config.Bind("2. Controls", "MaxSheetAngle", 90f,
                new ConfigDescription("Fully eased sheet angle in degrees (yard square across the hull).",
                    new AcceptableValueRange<float>(45f, 90f)));
            DefaultSheetAngle = Config.Bind("2. Controls", "DefaultSheetAngle", 45f,
                new ConfigDescription("Sheet angle a ship starts with before anyone has trimmed it.",
                    new AcceptableValueRange<float>(0f, 90f)));

            ForceMultiplier = Config.Bind("3. Physics", "ForceMultiplier", 1.6f,
                new ConfigDescription("Overall sail force scale relative to the ship's vanilla sail force factor. At 1.6 a perfectly trimmed sail on a beam reach gives about 2.2x the vanilla push, roughly 50% more speed; a sloppy trim is close to vanilla and a stalled sail is much slower.",
                    new AcceptableValueRange<float>(0.1f, 3f)));
            LiftScale = Config.Bind("3. Physics", "LiftScale", 1f,
                new ConfigDescription("Multiplier on the lift part of the sail force (the part that makes a well-trimmed sail fast).",
                    new AcceptableValueRange<float>(0f, 3f)));
            DragScale = Config.Bind("3. Physics", "DragScale", 1f,
                new ConfigDescription("Multiplier on the drag part of the sail force (dominates when the sail is stalled or running downwind).",
                    new AcceptableValueRange<float>(0f, 3f)));
            MinFilledDrive = Config.Bind("3. Physics", "MinFilledDrive", 0.12f,
                new ConfigDescription("Minimum forward drive (as a fraction of the ideal) whenever the sail is filled at all, so a badly over-sheeted boat stays sailable.",
                    new AcceptableValueRange<float>(0f, 1f)));
            LuffAngle = Config.Bind("3. Physics", "LuffAngle", 4f,
                new ConfigDescription("Angle of attack (degrees) at or below which the sail counts as luffing: no drive, flapping.",
                    new AcceptableValueRange<float>(0f, 20f)));
            StallAngle = Config.Bind("3. Physics", "StallAngle", 45f,
                new ConfigDescription("Angle of attack (degrees) above which the sail counts as stalled/over-sheeted for the heel boost and readout.",
                    new AcceptableValueRange<float>(20f, 90f)));
            ForceSmoothTime = Config.Bind("3. Physics", "ForceSmoothTime", 0.6f,
                new ConfigDescription("Seconds of smoothing applied to sail force changes. Vanilla uses 1.0.",
                    new AcceptableValueRange<float>(0.05f, 3f)));
            ApparentWindFactor = Config.Bind("3. Physics", "ApparentWindFactor", 0.5f,
                new ConfigDescription("How much the boat's own speed shifts the wind you trim to (0 = true wind only, 1 = full apparent wind).",
                    new AcceptableValueRange<float>(0f, 1f)));
            WindSpeedReference = Config.Bind("3. Physics", "WindSpeedReference", 12f,
                new ConfigDescription("Wind speed in m/s that corresponds to full wind intensity, used only for the apparent wind calculation.",
                    new AcceptableValueRange<float>(3f, 40f)));

            HeelTorque = Config.Bind("4. Heel", "HeelTorque", 2.8f,
                new ConfigDescription("Heeling torque per unit of sail heeling force, in N*m per kg of boat. The hull's own buoyancy rights the boat, so the heel angle is wherever the two balance; bigger = lies over further. Half sail halves it; a furled sail or rowing gives none. 0 disables.",
                    new AcceptableValueRange<float>(0f, 10f)));
            RollDamping = Config.Bind("4. Heel", "RollDamping", 0.6f,
                new ConfigDescription("Extra water damping on roll and pitch rate while the sail is drawing (per second), so the boat settles instead of wallowing. Does not touch wave roll with the sail furled.",
                    new AcceptableValueRange<float>(0f, 3f)));
            HeelWindPower = Config.Bind("4. Heel", "HeelWindPower", 1.5f,
                new ConfigDescription("How strongly heel grows with wind. 1 = linear with sail force, 2 = with the square of wind strength (a storm heels four times as much as half wind), 3 = cube.",
                    new AcceptableValueRange<float>(1f, 3f)));
            HeelDriveShare = Config.Bind("4. Heel", "HeelDriveShare", 0.3f,
                new ConfigDescription("Fraction of the forward drive that also counts as heeling moment (fades as the wind goes aft). Gives a well-trimmed boat on a reach a natural 10-15 degrees of heel instead of sailing flat. 0 = only the pure side force heels.",
                    new AcceptableValueRange<float>(0f, 1f)));
            StallHeelBoost = Config.Bind("4. Heel", "StallHeelBoost", 1f,
                new ConfigDescription("Extra heel multiplier reached at 90 degrees angle of attack (fully stalled). Scales in from StallAngle.",
                    new AcceptableValueRange<float>(0f, 5f)));
            MaxHeelAngle = Config.Bind("4. Heel", "MaxHeelAngle", 45f,
                new ConfigDescription("The heeling torque fades out over the last 10 degrees before this angle, so the mod can never knock the boat down.",
                    new AcceptableValueRange<float>(5f, 60f)));

            PitchTorque = Config.Bind("7. Pitch", "PitchTorque", 5f,
                new ConfigDescription("Bow-down torque per unit of drive, in N*m per kg of boat. The rig pulls high and the hull resists low, so drive buries the bow; vanilla buoyancy lifts it back. 0 disables.",
                    new AcceptableValueRange<float>(0f, 15f)));
            PitchWindPower = Config.Bind("7. Pitch", "PitchWindPower", 2f,
                new ConfigDescription("How strongly bow-down trim grows with wind. 2 = with the square of wind strength: moderate wind barely digs the bow, a storm does. 1 = linear with sail force.",
                    new AcceptableValueRange<float>(1f, 3f)));
            MaxPitchAngle = Config.Bind("7. Pitch", "MaxPitchAngle", 15f,
                new ConfigDescription("The bow-down torque fades out over the last 5 degrees before this angle. The mod never pitchpoles the boat.",
                    new AcceptableValueRange<float>(3f, 45f)));
            NoseDiveLoss = Config.Bind("7. Pitch", "NoseDiveLoss", 0.4f,
                new ConfigDescription("Drive lost with the bow fully buried (0.4 = 40% slower at MaxPitchAngle). Makes reefing in a storm the faster choice.",
                    new AcceptableValueRange<float>(0f, 1f)));
            NoseDiveDamagePerSecond = Config.Bind("7. Pitch", "NoseDiveDamagePerSecond", 0.3f,
                new ConfigDescription("Hull damage per second, as a percent of max health, while the bow is buried past 80% of MaxPitchAngle (shipping water). 0 disables.",
                    new AcceptableValueRange<float>(0f, 5f)));

            GustPeriodMinutes = Config.Bind("6. Realism", "GustPeriodMinutes", 6f,
                new ConfigDescription("Length of each gust 'slot' in minutes. Each slot has at most one gust or lull, so the average gap between events is GustPeriodMinutes / GustChance (12 min by default).",
                    new AcceptableValueRange<float>(1f, 60f)));
            GustChance = Config.Bind("6. Realism", "GustChance", 0.5f,
                new ConfigDescription("Probability that a given slot contains a gust or lull. 0 disables events entirely.",
                    new AcceptableValueRange<float>(0f, 1f)));
            GustDuration = Config.Bind("6. Realism", "GustDuration", 40f,
                new ConfigDescription("How long a gust or lull lasts, in seconds, including the ramp in and out.",
                    new AcceptableValueRange<float>(5f, 300f)));
            GustStrength = Config.Bind("6. Realism", "GustStrength", 0.4f,
                new ConfigDescription("Peak change in wind strength during an event, as a fraction (0.4 = up to +40% in a gust, -40% in a lull). 0 disables.",
                    new AcceptableValueRange<float>(0f, 1f)));
            LullFraction = Config.Bind("6. Realism", "LullFraction", 0.35f,
                new ConfigDescription("Share of events that are lulls rather than gusts.",
                    new AcceptableValueRange<float>(0f, 1f)));
            AmbientVariation = Config.Bind("6. Realism", "AmbientVariation", 0.04f,
                new ConfigDescription("Constant gentle wobble in wind strength between events, as a fraction. Keep small; 0 = perfectly steady.",
                    new AcceptableValueRange<float>(0f, 0.3f)));
            WeatherHelm = Config.Bind("6. Realism", "WeatherHelm", 0.04f,
                new ConfigDescription("Yaw acceleration toward the wind per degree of heel at 20 degrees of heel (deg/s^2 per degree), growing with the square of heel so light heel barely tugs. A heeled boat tries to round up and you must hold rudder against it. 0 disables.",
                    new AcceptableValueRange<float>(0f, 1f)));
            HeelDriveLoss = Config.Bind("6. Realism", "HeelDriveLoss", 1f,
                new ConfigDescription("How much heel reduces sail drive (1 = full cos^2 law: about 18% slower at 25 degrees of heel, 0 = none).",
                    new AcceptableValueRange<float>(0f, 1f)));
            HeelSweetSpotBonus = Config.Bind("6. Realism", "HeelSweetSpotBonus", 0.05f,
                new ConfigDescription("Drive bonus from a little heel (less hull in the water), peaking at about 8 degrees and gone by 16. 0.05 = 5% faster at the sweet spot. Beyond that HeelDriveLoss takes over.",
                    new AcceptableValueRange<float>(0f, 0.3f)));
            LeewayFactor = Config.Bind("6. Realism", "LeewayFactor", 0.4f,
                new ConfigDescription("Extra sideways slip when the sail is stalled or the boat is heeled hard, as a fraction of the sail's side force. 0 disables.",
                    new AcceptableValueRange<float>(0f, 2f)));
            GybesEnabled = Config.Bind("6. Realism", "GybesEnabled", false,
                "Off by default: a square yard has no boom to slam, so a Viking ship 'wearing round' was undramatic. On = when the wind crosses the stern with the sail up you get a roll kick and some hull damage unless the sheet was hauled in first.");
            GybeDamagePercent = Config.Bind("6. Realism", "GybeDamagePercent", 5f,
                new ConfigDescription("Hull damage from an uncontrolled gybe, as a percent of max health (full sail, strong wind, sheet fully eased). Half sail, lighter wind or a hauled-in sheet reduce it.",
                    new AcceptableValueRange<float>(0f, 50f)));
            GybeRollRate = Config.Bind("6. Realism", "GybeRollRate", 30f,
                new ConfigDescription("Roll kick from an uncontrolled gybe, in degrees per second.",
                    new AcceptableValueRange<float>(0f, 120f)));

            YardTurnRate = Config.Bind("5. Visuals", "YardTurnRate", 90f,
                new ConfigDescription("How fast the mast/yard visually rotates toward the commanded angle, degrees per second.",
                    new AcceptableValueRange<float>(10f, 360f)));
            FlapAmplitude = Config.Bind("5. Visuals", "FlapAmplitude", 3f,
                new ConfigDescription("Yaw wobble of the yard (degrees) while the sail is luffing.",
                    new AcceptableValueRange<float>(0f, 15f)));
            FlapFrequency = Config.Bind("5. Visuals", "FlapFrequency", 4f,
                new ConfigDescription("Flap oscillation rate in Hz while luffing.",
                    new AcceptableValueRange<float>(0.5f, 12f)));
        }

        /// <summary>
        /// Every method we patch, checked by reflection before any patch is applied.
        /// </summary>
        private struct MethodTarget
        {
            public readonly Type type; public readonly string name; public readonly Type[] args;
            public MethodTarget(Type type, string name, Type[] args) { this.type = type; this.name = name; this.args = args; }
        }

        private struct FieldTarget
        {
            public readonly Type type; public readonly string name;
            public FieldTarget(Type type, string name) { this.type = type; this.name = name; }
        }

        private static bool VerifyPatchTargets()
        {
            var targets = new List<MethodTarget>
            {
                new MethodTarget(typeof(ShipControlls), nameof(ShipControlls.ApplyControlls), new[] { typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool), typeof(bool) }),
                new MethodTarget(typeof(Ship), "Awake", Type.EmptyTypes),
                new MethodTarget(typeof(Ship), "Start", Type.EmptyTypes),
                new MethodTarget(typeof(Ship), "GetSailForce", new[] { typeof(float), typeof(float) }),
                new MethodTarget(typeof(Ship), "UpdateSail", new[] { typeof(float) }),
                new MethodTarget(typeof(Ship), "UpdateSailSize", new[] { typeof(float) }),
                new MethodTarget(typeof(Ship), "UpdateControlls", new[] { typeof(float) }),
                new MethodTarget(typeof(Ship), nameof(Ship.CustomFixedUpdate), new[] { typeof(float) }),
                new MethodTarget(typeof(ZInput), nameof(ZInput.GetButtonDown), new[] { typeof(string) }),
                new MethodTarget(typeof(GameCamera), "ApplyCameraTilt", new[] { typeof(Player), typeof(float), typeof(Quaternion).MakeByRefType() }),
                new MethodTarget(typeof(ZInput), nameof(ZInput.GetButton), new[] { typeof(string) }),
                new MethodTarget(typeof(ZInput), nameof(ZInput.GetKey), new[] { typeof(KeyCode), typeof(bool) }),
                new MethodTarget(typeof(ZInput), nameof(ZInput.GetKeyDown), new[] { typeof(KeyCode), typeof(bool) }),
                new MethodTarget(typeof(Player), "TakeInput", Type.EmptyTypes),
                new MethodTarget(typeof(Player), nameof(Player.GetControlledShip), Type.EmptyTypes),
                new MethodTarget(typeof(Player), nameof(Player.StopDoodadControl), Type.EmptyTypes),
                new MethodTarget(typeof(Ship), nameof(Ship.Forward), Type.EmptyTypes),
                new MethodTarget(typeof(Ship), nameof(Ship.Backward), Type.EmptyTypes),
                new MethodTarget(typeof(EnvMan), nameof(EnvMan.GetWindDir), Type.EmptyTypes),
                new MethodTarget(typeof(EnvMan), nameof(EnvMan.GetWindIntensity), Type.EmptyTypes),
            };
            var fields = new List<FieldTarget>
            {
                new FieldTarget(typeof(Ship), "m_nview"), new FieldTarget(typeof(Ship), "m_body"), new FieldTarget(typeof(Ship), "m_speed"),
                new FieldTarget(typeof(Ship), "m_sailForce"), new FieldTarget(typeof(Ship), "m_windChangeVelocity"),
                new FieldTarget(typeof(Ship), "m_mastObject"), new FieldTarget(typeof(Ship), "m_sailForceFactor"),
                new FieldTarget(typeof(Ship), "m_sailForceOffset"), new FieldTarget(typeof(Ship), "m_shipControlls"),
                new FieldTarget(typeof(Ship), "m_previousCenter"), new FieldTarget(typeof(Ship), "m_waterLevelOffset"),
                new FieldTarget(typeof(Ship), "m_disableLevel"), new FieldTarget(typeof(Ship), "m_players"),
            };

            bool ok = true;
            foreach (var t in targets)
            {
                if (AccessTools.Method(t.type, t.name, t.args) == null)
                {
                    Log.LogError($"Missing method: {t.type.Name}.{t.name}({string.Join(", ", Array.ConvertAll(t.args, a => a.Name))})");
                    ok = false;
                }
            }
            foreach (var f in fields)
            {
                if (AccessTools.Field(f.type, f.name) == null)
                {
                    Log.LogError($"Missing field: {f.type.Name}.{f.name}");
                    ok = false;
                }
            }
            return ok;
        }

        // ------------------------------------------------------------------
        // Per-frame input: tap/hold on Use, plus the lower/raise keys.
        // Runs on the local client only; everything it does goes through the
        // same RPCs vanilla uses, so other players see the result.
        // ------------------------------------------------------------------
        internal static bool IsLocalPlayerPiloting(out Ship ship)
        {
            ship = null;
            var p = Player.m_localPlayer;
            if (p == null) return false;
            ship = p.GetControlledShip();
            return ship != null;
        }

        private void Update()
        {
            if (!Enabled.Value) { _wasPiloting = false; return; }

            var player = Player.m_localPlayer;
            if (player != null && ToggleKey.Value != KeyCode.None && player.TakeInput() && !Hud.InRadial()
                && ZInput.GetKeyDown(ToggleKey.Value, false))
            {
                ManualTrim.Value = !ManualTrim.Value;
                Config.Save();
                player.Message(MessageHud.MessageType.Center,
                    ManualTrim.Value ? "Sail trim: manual (SailTrim)" : "Sail trim: vanilla auto-trim");
            }

            if (!IsLocalPlayerPiloting(out var ship) || !ManualTrim.Value)
            {
                _wasPiloting = false;
                _useHeld = false;
                return;
            }
            if (!_wasPiloting)
            {
                // We just took the rudder. The Use press that started control may
                // still be held, so demand a release before counting a new press.
                _wasPiloting = true;
                _requireUseRelease = true;
                _useHeld = false;
            }

            bool takeInput = player.TakeInput() && !Hud.InRadial();
            bool held = takeInput && ZInput.GetButton("Use");

            if (_requireUseRelease)
            {
                if (!held) _requireUseRelease = false;
            }
            else
            {
                if (held && !_useHeld)
                {
                    _useHeld = true;
                    _useHeldTime = 0f;
                    _helmReleased = false;
                }

                if (held)
                {
                    _useHeldTime += Time.deltaTime;
                    if (!_helmReleased && _useHeldTime >= ReleaseHoldTime.Value)
                    {
                        _helmReleased = true;
                        player.StopDoodadControl();
                        _wasPiloting = false;
                        return;
                    }
                }
                else if (_useHeld)
                {
                    _useHeld = false;
                    if (!_helmReleased) { ship.Forward(); SailTrimHud.NoteSailKeyUsed(); }
                }
            }

            if (!takeInput) return;

            if (LowerSailKey.Value != KeyCode.None && ZInput.GetKeyDown(LowerSailKey.Value, false))
            { ship.Backward(); SailTrimHud.NoteSailKeyUsed(); }
            if (RaiseSailKey.Value != KeyCode.None && ZInput.GetKeyDown(RaiseSailKey.Value, false))
            { ship.Forward(); SailTrimHud.NoteSailKeyUsed(); }
        }

        private void LateUpdate()
        {
            SailTrimHud.Update();
        }

    }
}
