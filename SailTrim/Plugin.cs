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
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.maxst.sailtrim";
        public const string NAME = "SailTrim";
        public const string VERSION = "1.6.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        private Harmony _harmony;

        // ---- Config: server ----
        internal static ConfigEntry<ServerEnforcement> Enforcement;
        internal static ConfigEntry<bool> LockConfig;
        /// <summary>Settings the server pushes to every client while connected (gameplay-affecting ones).</summary>
        internal static readonly List<ConfigEntryBase> ServerSyncedEntries = new List<ConfigEntryBase>();

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
        internal static ConfigEntry<bool> PilotOwnsShip;
        internal static ConfigEntry<bool> CrewCanTrim;
        internal static ConfigEntry<float> CrewTrimBonus;
        internal static ConfigEntry<bool> PassengerHud;
        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<KeyCode> RowForwardKey;
        internal static ConfigEntry<KeyCode> RowBackKey;
        internal static ConfigEntry<bool> RowKeysToggle;
        internal static ConfigEntry<bool> RudderSelfCenter;
        internal static ConfigEntry<float> StowHoldTime;
        internal static ConfigEntry<float> SailSetRate;
        internal static ConfigEntry<float> SquareRunAngle;
        internal static ConfigEntry<bool> TackAnimation;
        internal static ConfigEntry<float> TackStartAngle;
        internal static ConfigEntry<float> TackMaxYardRate;
        internal static ConfigEntry<float> SpillStreamAngle;
        internal static ConfigEntry<float> LuffFlutter;
        internal static ConfigEntry<float> CloseHauledTautness;
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
        internal static ConfigEntry<float> MastStrainAngle;
        internal static ConfigEntry<float> MastStrainGrace;
        internal static ConfigEntry<float> MastStrainDamagePerSecond;

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
        internal static ConfigEntry<float> WindShadowMax;
        internal static ConfigEntry<float> WindShadowOnset;
        internal static ConfigEntry<float> WindShadowRange;
        internal static ConfigEntry<float> DownwindRolling;
        internal static ConfigEntry<float> RollPeriod;

        // ---- Config: mooring ----
        internal static ConfigEntry<bool> CleatEnabled;
        internal static ConfigEntry<float> CleatRange;
        internal static ConfigEntry<int> CleatCost;
        internal static ConfigEntry<float> MooringHold;
        internal static ConfigEntry<float> CastOffDelay;

        // ---- Config: visuals ----
        internal static ConfigEntry<float> YardTurnRate;
        internal static ConfigEntry<float> FlapAmplitude;
        internal static ConfigEntry<float> FlapFrequency;

        // ---- Crew-on-the-sheet state (local player holding fast on a mast) ----
        internal static bool CrewActive => _crewShip != null;
        private static Ship _crewShip;
        internal static Ship CrewShip => _crewShip;

        /// <summary>The ship the local player is aboard, whether standing, holding the mast or at the rudder.</summary>
        internal static Ship GetShipAboard(Player player)
        {
            if (player == null) return null;
            if (_crewShip != null && _crewShip.IsPlayerInBoat(player)) return _crewShip;
            var standing = player.GetStandingOnShip();
            if (standing != null) return standing;
            var local = Ship.GetLocalShip();
            return local != null && local.IsPlayerInBoat(player) ? local : null;
        }

        private static bool _releasing;

        internal static void TakeSheet(Ship ship)
        {
            var st = SailTrimShip.Get(ship);
            if (st == null) return;
            _crewShip = ship;
            st.CrewSetSheetHand(true);
            Log.LogInfo("SailTrim: took the sheet on " + ship.name);
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "You have the sheet: W sheet in, S ease out");
        }

        internal static void ReleaseSheet()
        {
            if (_crewShip == null) return;
            var ship = _crewShip;
            _crewShip = null;
            SailTrimShip.Get(ship)?.CrewSetSheetHand(false);
            var p = Player.m_localPlayer;
            _releasing = true;
            try { if (p != null && p.IsAttached()) p.AttachStop(); }
            finally { _releasing = false; }
            Log.LogInfo("SailTrim: released the sheet on " + ship.name);
            p?.Message(MessageHud.MessageType.Center, "Sheet released");
        }

        /// <summary>Something other than us detached the crew member: drop the sheet without calling AttachStop again.</summary>
        internal static void OnCrewDetached()
        {
            if (_releasing || _crewShip == null) return;
            var ship = _crewShip;
            _crewShip = null;
            SailTrimShip.Get(ship)?.CrewSetSheetHand(false);
            Log.LogInfo("SailTrim: crew detached from the mast on " + ship.name);
        }

        private void UpdateCrew(Player player, bool piloting)
        {
            if (_crewShip == null) return;
            var cst = SailTrimShip.Get(_crewShip);
            bool stillValid = player != null && player.IsAttached() && _crewShip.IsPlayerInBoat(player)
                              && !piloting && cst != null && cst.ManualMode && CrewCanTrim.Value;
            if (!stillValid)
            {
                // Drop the sheet only. If they are still seated on the mast (e.g. the captain switched to
                // auto-trim) they stay there as a plain vanilla seat; jumping stands them up as usual.
                Log.LogInfo($"SailTrim: dropping the sheet (attached {player?.IsAttached()}, aboard {_crewShip.IsPlayerInBoat(player)}, piloting {piloting}, manual {cst?.ManualMode})");
                OnCrewDetached();
                return;
            }
            if (player.TakeInput() && !Hud.InRadial())
                cst.CrewTrimInput(ZInput.GetButton("Forward"), ZInput.GetButton("Backward"), Time.deltaTime);
        }

        // ---- Input state for tap/hold on the Use button ----
        private bool _wasPiloting;
        private bool _requireUseRelease;

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
            Enforcement = Config.Bind("0. Server", "Enforcement", ServerEnforcement.Warn,
                "Only read on the server (dedicated or the hosting player). Off = anyone may join. Warn = players without a matching SailTrim may join but get a warning on screen (and the server logs it). Require = players without the same SailTrim version are refused with an 'incompatible version' message.");
            LockConfig = Config.Bind("0. Server", "LockConfig", true,
                "Only read on the server. true = the server's physics, heel, gust, pitch and hull-speed settings are pushed to every client on connect, so the whole server sails by the same rules. Personal settings (keys, HUD, camera, opt-in) always stay per player.");

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
            PilotOwnsShip = Config.Bind("2. Controls", "PilotOwnsShip", true,
                "When you take the rudder with manual trim on, your client takes over simulating the ship (the game's normal ownership hand-off). Guarantees the boat sails by your trim even if a passenger without the mod boarded first.");
            CrewCanTrim = Config.Bind("2. Controls", "CrewCanTrim", true,
                "A passenger with the mod can hold fast on the mast (interact with it) to take the sheet and trim with W/S while the pilot steers. While they hold it the pilot's W/S do nothing.");
            CrewTrimBonus = Config.Bind("2. Controls", "CrewTrimBonus", 0.2f,
                new ConfigDescription("Extra sheet speed when a dedicated crew member is on the sheet (0.2 = 20% faster than the pilot trimming alone).",
                    new AcceptableValueRange<float>(0f, 1f)));
            PassengerHud = Config.Bind("1. General", "PassengerHud", true,
                "Show the ship HUD (wind circle, sail icon, speed gauge, trim state) to passengers with the mod, not just the pilot.");
            ToggleKey = Config.Bind("2. Controls", "ToggleKey", KeyCode.H,
                "Key that switches ManualTrim on/off in game (saved to this config).");
            LowerSailKey = Config.Bind("2. Controls", "LowerSailKey", KeyCode.Q,
                "Hold to take in sail (any amount, down to furled). The game's Use button (E) held lets sail out again.");
            RaiseSailKey = Config.Bind("2. Controls", "RaiseSailKey", KeyCode.None,
                "Optional extra key: hold to let out sail. Holding the game's Use button (E) always does this too.");
            RowForwardKey = Config.Bind("2. Controls", "RowForwardKey", KeyCode.LeftShift,
                "Row forward (vanilla's paddling speed). Only with the sail furled: with sail set, hold it for StowHoldTime to stow the sail first.");
            RowBackKey = Config.Bind("2. Controls", "RowBackKey", KeyCode.LeftControl,
                "Row astern. Same rules as RowForwardKey.");
            RowKeysToggle = Config.Bind("2. Controls", "RowKeysToggle", false,
                "false = row only while the key is held. true = press once to start rowing, again to stop.");
            StowHoldTime = Config.Bind("2. Controls", "StowHoldTime", 1f,
                new ConfigDescription("Seconds a row key must be held with sail set before the sail starts coming in, so a stray press does nothing.",
                    new AcceptableValueRange<float>(0.2f, 3f)));
            RudderSelfCenter = Config.Bind("2. Controls", "RudderSelfCenter", false,
                "The rudder drifts back to centre when you are not steering. Comfortable, but weather helm then needs constant attention.");
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

            SailSetRate = Config.Bind("3. Physics", "SailSetRate", 0.25f,
                new ConfigDescription("How much sail is let out or taken in per second while a sail key is held (0.25 = furled to full in 4 s). Stowing for rowing goes twice as fast.",
                    new AcceptableValueRange<float>(0.05f, 2f)));
            SquareRunAngle = Config.Bind("3. Physics", "SquareRunAngle", 110f,
                new ConfigDescription("Apparent wind angle off the bow beyond which the square sail is a drag device: the yard goes square and the sail never counts as stalled.",
                    new AcceptableValueRange<float>(90f, 180f)));
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
            MastStrainAngle = Config.Bind("4. Heel", "MastStrainAngle", 35f,
                new ConfigDescription("Heel angle beyond which the rig is under strain. Hold it longer than MastStrainGrace and the hull takes damage until you ease out or reef.",
                    new AcceptableValueRange<float>(15f, 60f)));
            MastStrainGrace = Config.Bind("4. Heel", "MastStrainGrace", 4f,
                new ConfigDescription("Seconds of heel past MastStrainAngle before damage starts (a gust knockdown you recover from quickly costs nothing).",
                    new AcceptableValueRange<float>(0f, 30f)));
            MastStrainDamagePerSecond = Config.Bind("4. Heel", "MastStrainDamagePerSecond", 0.5f,
                new ConfigDescription("Hull damage per second, as a percent of max health, while the rig is straining. 0 disables.",
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
            WindShadowMax = Config.Bind("6. Realism", "WindShadowMax", 0.4f,
                new ConfigDescription("Most wind you can lose in the lee of land upwind (0.4 = down to 60% of the wind). Deliberately mild so rivers and fjords stay sailable. 0 disables.",
                    new AcceptableValueRange<float>(0f, 0.9f)));
            WindShadowOnset = Config.Bind("6. Realism", "WindShadowOnset", 10f,
                new ConfigDescription("Land upwind must rise more than this many degrees above the water, seen from the boat, before it starts to shelter you. Low river banks stay below it.",
                    new AcceptableValueRange<float>(2f, 45f)));
            WindShadowRange = Config.Bind("6. Realism", "WindShadowRange", 25f,
                new ConfigDescription("Degrees above the onset at which the shelter reaches WindShadowMax (a cliff or mountain right upwind).",
                    new AcceptableValueRange<float>(5f, 60f)));
            DownwindRolling = Config.Bind("6. Realism", "DownwindRolling", 0.6f,
                new ConfigDescription("Rhythmic roll when running within ~30 degrees of dead downwind with the sail up, growing with wind and sail area. Heading up or reefing cures it. 0 disables.",
                    new AcceptableValueRange<float>(0f, 5f)));
            RollPeriod = Config.Bind("6. Realism", "RollPeriod", 4f,
                new ConfigDescription("Seconds per roll cycle for a Karve-sized hull; wider hulls roll slower.",
                    new AcceptableValueRange<float>(1.5f, 12f)));
            GybeDamagePercent = Config.Bind("6. Realism", "GybeDamagePercent", 5f,
                new ConfigDescription("Hull damage from an uncontrolled gybe, as a percent of max health (full sail, strong wind, sheet fully eased). Half sail, lighter wind or a hauled-in sheet reduce it.",
                    new AcceptableValueRange<float>(0f, 50f)));
            GybeRollRate = Config.Bind("6. Realism", "GybeRollRate", 30f,
                new ConfigDescription("Roll kick from an uncontrolled gybe, in degrees per second.",
                    new AcceptableValueRange<float>(0f, 120f)));

            YardTurnRate = Config.Bind("5. Visuals", "YardTurnRate", 90f,
                new ConfigDescription("How fast the mast/yard visually rotates toward the commanded angle, degrees per second.",
                    new AcceptableValueRange<float>(10f, 360f)));
            TackAnimation = Config.Bind("5. Visuals", "TackAnimation", true,
                "Play the tack as one continuous motion tied to the bow's swing: the sail's tension eases as the bow comes up to the wind, the yard passes through square as the bow passes through the wind, and the sail is tensioned again on the new side. Visual only; the boat sails exactly the same with it off.");
            TackStartAngle = Config.Bind("5. Visuals", "TackStartAngle", 25f,
                new ConfigDescription("How far off the wind (degrees of apparent wind off the bow) the tack motion begins and ends. Larger = starts sooner and the yard's sweep is spread over more of the turn.",
                    new AcceptableValueRange<float>(8f, 60f)));
            TackMaxYardRate = Config.Bind("5. Visuals", "TackMaxYardRate", 55f,
                new ConfigDescription("Fastest the yard may swing during a tack, degrees per second. If you throw the bow through the wind quicker than this the yard follows at its own pace and arrives a moment later. Lower = calmer, and kinder to the cloth.",
                    new AcceptableValueRange<float>(10f, 360f)));
            SpillStreamAngle = Config.Bind("5. Visuals", "SpillStreamAngle", 55f,
                new ConfigDescription("During the tack animation, how far the loosened sail's foot swings out downwind from under the yard, in degrees from hanging straight down, in a strong wind (less in light air). It streams like a flag from the yard. 0 = the foot stays put.",
                    new AcceptableValueRange<float>(0f, 85f)));
            CloseHauledTautness = Config.Bind("5. Visuals", "CloseHauledTautness", 1f,
                new ConfigDescription("How much a hard-sheeted, drawing sail tightens up: less turbulence, the cloth moving as one, a little more damping, while keeping its curve. Fades out as the sheet is eased toward 60 degrees. 0 = the prefab's own lively cloth at every trim.",
                    new AcceptableValueRange<float>(0f, 1f)));
            LuffFlutter = Config.Bind("5. Visuals", "LuffFlutter", 1f,
                new ConfigDescription("How strongly the sail cloth itself flutters when luffing or spilled (cloth turbulence and gust rate). The yard no longer shakes. 0 = off.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FlapAmplitude = Config.Bind("5. Visuals", "FlapAmplitude", 3f,
                new ConfigDescription("No longer used: the yard does not shake; the cloth flutters instead (LuffFlutter). Kept so old config files load cleanly.",
                    new AcceptableValueRange<float>(0f, 15f)));
            FlapFrequency = Config.Bind("5. Visuals", "FlapFrequency", 4f,
                new ConfigDescription("Flap oscillation rate in Hz while luffing.",
                    new AcceptableValueRange<float>(0.5f, 12f)));

            CleatEnabled = Config.Bind("8. Mooring", "CleatEnabled", true,
                "Adds the Cleat build piece (hammer, Misc). Interact with it to tie up a boat within CleatRange: the boat holds its spot and heading, crew aboard or not, until untied.");
            CleatRange = Config.Bind("8. Mooring", "CleatRange", 10f,
                new ConfigDescription("How far from the cleat a boat can be to tie it up, metres.", new AcceptableValueRange<float>(2f, 30f)));
            CleatCost = Config.Bind("8. Mooring", "CleatCost", 1,
                new ConfigDescription("Bronze per cleat.", new AcceptableValueRange<int>(1, 20)));
            CastOffDelay = Config.Bind("8. Mooring", "CastOffDelay", 1f,
                new ConfigDescription("Seconds at the helm of a tied-up boat before it casts off by itself.", new AcceptableValueRange<float>(0f, 10f)));
            MooringHold = Config.Bind("8. Mooring", "MooringHold", 1f,
                new ConfigDescription("How firmly a moored boat is pulled back to where it was tied (heading too). 0 = only the vanilla empty-boat damping.", new AcceptableValueRange<float>(0f, 5f)));

            // Gameplay-affecting settings the server owns when LockConfig is on.
            ServerSyncedEntries.Clear();
            foreach (var kv in Config)
            {
                string sec = kv.Key.Section, key = kv.Key.Key;
                bool synced = sec == "3. Physics" || sec == "4. Heel" || sec == "6. Realism" || sec == "7. Pitch"
                              || (sec == "1. General" && (key == "Enabled" || key == "HullSpeedScale"))
                              || (sec == "2. Controls" && (key == "MaxSheetAngle" || key == "SheetRate" || key == "DefaultSheetAngle"));
                if (synced) ServerSyncedEntries.Add(kv.Value);
            }
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
                new MethodTarget(typeof(Settings), "Awake", Type.EmptyTypes),
                new MethodTarget(typeof(Ship), "Start", Type.EmptyTypes),
                new MethodTarget(typeof(ZNetScene), "Awake", Type.EmptyTypes),
                new MethodTarget(typeof(ObjectDB), "Awake", Type.EmptyTypes),
                new MethodTarget(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB), new[] { typeof(ObjectDB) }),
                new MethodTarget(typeof(Ship), "GetSailForce", new[] { typeof(float), typeof(float) }),
                new MethodTarget(typeof(Ship), "UpdateSail", new[] { typeof(float) }),
                new MethodTarget(typeof(Ship), "UpdateSailSize", new[] { typeof(float) }),
                new MethodTarget(typeof(Ship), "UpdateControlls", new[] { typeof(float) }),
                new MethodTarget(typeof(Ship), nameof(Ship.CustomFixedUpdate), new[] { typeof(float) }),
                new MethodTarget(typeof(ZInput), nameof(ZInput.GetButtonDown), new[] { typeof(string) }),
                new MethodTarget(typeof(GameCamera), "ApplyCameraTilt", new[] { typeof(Player), typeof(float), typeof(Quaternion).MakeByRefType() }),
                new MethodTarget(typeof(ZNet), "OnNewConnection", new[] { typeof(ZNetPeer) }),
                new MethodTarget(typeof(Player), nameof(Player.SetControls), new[] { typeof(Vector3), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }),
                new MethodTarget(typeof(Player), nameof(Player.GetStandingOnShip), Type.EmptyTypes),
                new MethodTarget(typeof(Player), nameof(Player.AttachStart), new[] { typeof(Transform), typeof(GameObject), typeof(bool), typeof(bool), typeof(bool), typeof(string), typeof(Vector3), typeof(Transform) }),
                new MethodTarget(typeof(Player), nameof(Player.AttachStop), Type.EmptyTypes),
                new MethodTarget(typeof(Player), nameof(Player.IsAttached), Type.EmptyTypes),
                new MethodTarget(typeof(Hud), "UpdateShipHud", new[] { typeof(Player), typeof(float) }),
                new MethodTarget(typeof(Chair), nameof(Chair.Interact), new[] { typeof(Humanoid), typeof(bool), typeof(bool) }),
                new MethodTarget(typeof(Chair), nameof(Chair.GetHoverText), Type.EmptyTypes),
                new MethodTarget(typeof(Heightmap), nameof(Heightmap.GetHeight), new[] { typeof(Vector3), typeof(float).MakeByRefType() }),
                new MethodTarget(typeof(ZNet), "SendPeerInfo", new[] { typeof(ZRpc), typeof(string) }),
                new MethodTarget(typeof(ZNet), "RPC_PeerInfo", new[] { typeof(ZRpc), typeof(ZPackage) }),
                new MethodTarget(typeof(ZNet), "Update", Type.EmptyTypes),
                new MethodTarget(typeof(ZNet), "OnDestroy", Type.EmptyTypes),
                new MethodTarget(typeof(ZNet), nameof(ZNet.Disconnect), new[] { typeof(ZNetPeer) }),
                new MethodTarget(typeof(FejdStartup), "ShowConnectError", new[] { typeof(ZNet.ConnectionStatus) }),
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
                new FieldTarget(typeof(Ship), "m_speed"), new FieldTarget(typeof(Ship), "m_rudderValue"), new FieldTarget(typeof(Ship), "m_rudderSpeed"),
                new FieldTarget(typeof(Ship), "m_hasSail"), new FieldTarget(typeof(Ship), "m_sailPosition"), new FieldTarget(typeof(Ship), "m_sailWasInPosition"),
                new FieldTarget(typeof(Ship), "m_sailBottomTransform"), new FieldTarget(typeof(Ship), "m_sailFurledPosition"), new FieldTarget(typeof(Ship), "m_sailMidfurledPosition"),
                new FieldTarget(typeof(Ship), "m_sailUnfurledPosition"), new FieldTarget(typeof(Ship), "m_sailCloth"), new FieldTarget(typeof(Ship), "m_sailBlendWeightCurve"),
                new FieldTarget(typeof(Ship), "m_changeSailPosEffect"), new FieldTarget(typeof(Ship), "m_floatCollider"),
                new FieldTarget(typeof(ZNetScene), "m_prefabs"), new FieldTarget(typeof(ZNetScene), "m_namedPrefabs"),
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
            SailTrimNet.ClientUpdate();
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

            bool pilotingNow = IsLocalPlayerPiloting(out var ship);
            UpdateCrew(player, pilotingNow);

            if (!pilotingNow || !ManualTrim.Value)
            {
                _wasPiloting = false;
                _rowHold = 0f; _rowArmed = 0; _rowWarned = false; _rowPrevHeld = 0;
                return;
            }
            var st = SailTrimShip.Get(ship);
            if (st == null) return;
            if (!_wasPiloting)
            {
                // We just took the rudder. The Use press that started control may still be held; it must
                // not start letting sail out, so demand a release first.
                _wasPiloting = true;
                _requireUseRelease = true;
                _rowHold = 0f; _rowArmed = 0; _rowWarned = false; _rowPrevHeld = 0;
            }

            bool takeInput = player.TakeInput() && !Hud.InRadial();
            bool useHeld = takeInput && ZInput.GetButton("Use");
            if (_requireUseRelease)
            {
                if (!useHeld) _requireUseRelease = false;
                useHeld = false;
            }

            // Sail: hold E (or RaiseSailKey) to let out, hold Q to take in; a tap moves it a little.
            // Letting go of the helm is the game's Jump, exactly like vanilla.
            float step = SailSetRate.Value * Time.deltaTime;
            bool setMore = useHeld || (takeInput && RaiseSailKey.Value != KeyCode.None && ZInput.GetKey(RaiseSailKey.Value, false));
            bool takeIn = takeInput && LowerSailKey.Value != KeyCode.None && ZInput.GetKey(LowerSailKey.Value, false);
            if (setMore != takeIn)
            {
                st.PilotSetSail(setMore ? step : -step);
                SailTrimHud.NoteSailKeyUsed();
            }

            UpdateRowKeys(st, player, takeInput, step);
        }

        // ---- Rowing keys ----
        private float _rowHold;      // how long the current row key press has lasted
        private int _rowArmed;       // toggle mode: the row direction to start once the sail is stowed
        private bool _rowWarned;     // "hold to stow" shown for this press
        private int _rowPrevHeld;    // row key held last frame (+1/-1/0)

        private void UpdateRowKeys(SailTrimShip st, Player player, bool takeInput, float step)
        {
            int held = 0;
            if (takeInput)
            {
                if (RowForwardKey.Value != KeyCode.None && ZInput.GetKey(RowForwardKey.Value, false)) held = 1;
                else if (RowBackKey.Value != KeyCode.None && ZInput.GetKey(RowBackKey.Value, false)) held = -1;
            }
            bool sailSet = st.SailAmount > 0.001f;

            if (held != 0)
            {
                _rowHold += Time.deltaTime;
                if (sailSet)
                {
                    // Sail set: a short press does nothing (probably a slip); holding stows the sail first.
                    if (_rowHold >= StowHoldTime.Value) { st.PilotSetSail(-2f * step); _rowArmed = held; }
                    else if (!_rowWarned) { player.Message(MessageHud.MessageType.Center, "Hold to stow the sail first"); _rowWarned = true; }
                    if (st.RowDir != 0) st.PilotSetRowing(0);
                }
                else if (!RowKeysToggle.Value)
                {
                    st.PilotSetRowing(held);                          // hold mode: row while the key is down
                }
                else if (_rowArmed == held)
                {
                    st.PilotSetRowing(held);                          // toggle mode: the stow just finished, start rowing
                    _rowArmed = 0;
                }
                else if (_rowPrevHeld == 0)
                {
                    st.PilotSetRowing(st.RowDir == held ? 0 : held);  // toggle mode: a fresh press flips it
                }
            }
            else
            {
                if (!RowKeysToggle.Value && st.RowDir != 0) st.PilotSetRowing(0);
                _rowHold = 0f; _rowArmed = 0; _rowWarned = false;
            }
            _rowPrevHeld = held;
        }

        private void LateUpdate()
        {
            SailTrimHud.Update();
        }

    }
}
