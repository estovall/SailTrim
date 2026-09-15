# SailTrim for Valheim

Manual sail trimming for Valheim 1.0. No new items or models. Everyone on the server installs the same
`SailTrim.dll`; the sheet angle is synced through the ship so other players see the yard swing.

Built against Valheim **1.0.12** (Unity 6000.0.75f1) with BepInEx 5.4.x.

## Install (every player)

1. Install BepInEx for Valheim if you don't have it yet. The easiest way is the *BepInExPack Valheim*
   package from Thunderstore (r2modman / Thunderstore Mod Manager), or the manual zip from
   https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/ extracted into the game folder
   (`...\steamapps\common\Valheim`). Launch the game once so `BepInEx\config` and `BepInEx\plugins` exist.
2. Drop `SailTrim.dll` into `Valheim\BepInEx\plugins\`.
3. Start the game. `BepInEx\config\com.maxst.sailtrim.cfg` is created on first launch. Edit it with the game
   closed (or use a config manager) to change keys and physics.

**Server (recommended).** Put the same `SailTrim.dll` in the dedicated server's `BepInEx\plugins` too (or
just have it installed if a player hosts). Ship physics always runs on a client, so the server copy does
not sail anything; it does two useful things, both configurable in the server's
`BepInEx\config\com.maxst.sailtrim.cfg` under `0. Server`:

| Server setting | Default | Meaning |
| --- | --- | --- |
| `Enforcement` | Warn | `Off`: anyone may join. `Warn`: players without a matching SailTrim can join but get an on-screen warning (and the server logs it). `Require`: they are refused with an "incompatible version" message that names the mod. |
| `LockConfig` | true | The server's physics, heel, gust, pitch and hull-speed settings are pushed to every client on connect, so everyone sails by the same rules. Keys, HUD, camera tilt and the H opt-in stay personal. |

Without a server copy everything still works, but nothing stops a friend from forgetting to install it.
Since 1.1.1 the pilot's client takes over simulating the ship whenever they hold the rudder with manual
trim on, so an unmodded passenger sees the boat move exactly as the pilot sails it; they only see the yard
drawn at vanilla's angle.

## Controls (while holding the rudder)

| Input | Action |
| --- | --- |
| Tap **E** | Raise sail one step (Stop > Slow > Half > Full) — what W did in vanilla |
| Hold **E** ~0.5 s | Let go of the rudder (vanilla E). A tap never releases. |
| **Q** | Lower sail one step (Full > Half > Slow > Stop > Back / rowing) — what S did in vanilla |
| Hold **W** | Sheet in: the yard swings toward the centreline (pull the rope toward you) |
| Hold **S** | Ease the sheet out: the yard swings away from the centreline |
| **A / D** | Rudder, unchanged |
| **H** | Switch *you* between vanilla sailing (the default) and manual trim; saved to your config |
| **E on the mast** (passenger) | Hold fast: take the sheet and trim with W/S while someone else steers |

Gamepad: the left stick forward/back sheets in/eases; the gamepad Use button releases the helm exactly like
vanilla. There are no default gamepad bindings for raise/lower, so use a keyboard for those or bind
`RaiseSailKey` / `LowerSailKey` in the config.

The vanilla ship HUD gets three additions: a **sail icon** on the wind circle that rotates with your sheet
(belly toward the side the wind fills it; colour = trim state), a **speed gauge** in knots to its left (scaled to that hull's speed: gold up to hull speed, red past it), and
two lines of text below: the trim hint (**Trimmed**, **Sheet in**, **Ease out**, **LUFFING**, **STALLED**,
**ABACK**, **BOW BURIED**) and sheet angle / heel / gust. Wind direction and the no-go zone stay on the
vanilla wind icon. The hint is averaged over about 1.5 s (`ReadoutSmoothing`) with a bit of hysteresis, so
wave rocking does not make it flicker; trim for what it says on average, not for every wave. Turn the overlay
off with `ShowHud = false`.

When you take the helm with the sail furled, a gold line under the HUD reminds you that tap-E raises,
Q lowers and hold-E lets go. It disappears once you have used the keys.

### Sailing as a crew

Any passenger with the mod sees the ship HUD like the pilot does (wind circle, sail icon, speed gauge, trim
state). Walk to the mast and press **E** to hold fast: you are held at the mast foot and W/S trim the sail
while the pilot steers, exactly as a longship crew would, and a dedicated hand works the sheet 20% faster than
a pilot doing two jobs. The pilot's W/S do nothing while you hold it, and their readout says so. Press E on the
mast again, jump, or take the rudder yourself to let go.

### Opting out

**Everyone starts in vanilla mode.** The first time you take a rudder with the mod installed, the boat sails
exactly like vanilla (W/S step the sail, tap E lets go, auto-trim, no extra heel) and the HUD says
"Vanilla sailing · H for manual trim". Press **H** at the helm to switch to manual trim; the choice is saved
per player and remembered between sessions, and H flips it back any time. While a vanilla-mode player holds
the rudder the boat sails vanilla for everyone aboard; hand it to a manual-trim player and it switches back.

## How to sail it

* **Sheet angle** 0 = yard hauled fully in along the hull, 90 = fully eased square across the hull.
* **AoA** = angle between the wind and the sail. Aim for about **15–30°**. Roughly: sheet angle ≈ wind angle
  off the bow minus 20°. Beam wind (90°) wants the sheet near 70°, running (180°) wants it fully eased,
  close-hauled (45°) wants it near 25°.
* The yard follows the sheet even with the sail furled, so you can pre-trim before hoisting.
* Eased too far: the sail **luffs** (yard wobbles, almost no drive). Sheet in.
* Wind on the wrong face, for example in irons with the yard square: the sail is **aback** and pushes you
  astern. Square-riggers used this to back out of irons; sheet in or bear away to fill it properly.
* Hauled too far: the sail **stalls**. Drive drops (never to zero, you can still crawl home), and the boat
  **heels** hard. Ease out.
* A perfectly trimmed sail is about 50% faster than vanilla's auto-trim on a reach and much faster upwind;
  10° off the ideal sheet costs about 15% of the push, a sloppy trim is roughly vanilla speed, and a
  stalled sail is much slower. Heel scales with the sail's
  side force, so it grows with wind strength and is halved with the sail at Half; a furled sail or rowing
  adds no heel at all.
* The no-go zone is vanilla's: lift dies within about 37° of the true wind, exactly where the vanilla
  wind indicator turns red.

### Realism extras (all in the `6. Realism` config section, each can be zeroed)

* **Gusts and lulls.** Rare by design: on average one event every 12 minutes, lasting about 40 s, up to
  ±40% wind strength. The readout shows **GUST** / **lull** while one is on. Between events the wind only
  has a tiny wobble. Everyone sees the same gusts (they are derived from world time).
* **Wind shadow.** Land upwind takes wind away: a cliff or forested hill close to windward can cost you up
  to 40% of the breeze, then it fills in as you clear the point. Deliberately mild (`WindShadowMax`), so rivers
  between low banks stay sailable; the readout shows **Lee** when it bites.
* **Downwind rolling.** Running within about 30° of dead downwind with the sail up, the boat rolls
  rhythmically, more in strong wind and at full sail. Head up a few degrees or drop to Half and it settles.
  Gentle by default (`DownwindRolling`).
* **Weather helm.** A heeled boat tries to round up into the wind; hold rudder against it or ease the sheet.
  It grows with the square of heel, so a little heel barely tugs and a lot really rounds you up.
  Heel and weather helm both scale with hull beam, so the Karve is tender and the longship stiff.
* **Heel and speed.** A little heel helps: about 5% faster at 8° (less hull in the water), gone by 16°.
  Past that, lying over costs speed: 25° is about 18% slower than flat. Ease out in a gust.
* **Leeway.** A stalled sail or a hard-heeled boat slides sideways instead of tracking straight.
* **Mast strain.** Heel past 35° for more than about 4 seconds and the rig is straining: the hull takes
  0.5% of its health per second until you ease out or reef. A brief knockdown in a gust costs nothing.
* **Hull speed.** Each hull has a speed it was built for (from its waterline length). Push past it and
  wave-making drag climbs and the bow digs in much harder, so a Karve driven at longship speeds buries its
  bow long before the longship would. The gauge turns red and reads "over".
* **Bow-down trim.** Drive buries the bow, most of all running at full sail in a storm. Past about 12°
  the readout says **BOW BURIED**: you lose up to 40% of your drive and ship a little water (0.3% hull
  per second) until you ease out or drop to Half. Capped at 15°; the mod never pitchpoles the boat.
* **Gybes (off by default).** A square yard is symmetric and has no boom, so a Viking ship wearing round
  was undramatic; nothing visibly swings. If you turn `GybesEnabled` on anyway, letting the wind cross the
  stern with the sail up gives a "GYBE!" roll kick and hull damage (5% of max health at full sail in strong
  wind with the sheet fully eased), softened by half sail, light wind, or sheeting in first.

## Config highlights (`BepInEx\config\com.maxst.sailtrim.cfg`)

| Key | Default | Meaning |
| --- | --- | --- |
| `Enabled` | true | Off = vanilla sailing for everyone (client-side kill switch) |
| `ShowHud` / `ReadoutSmoothing` | true / 1.5 | Overlay on/off, hint averaging |
| `ControlHints` | true | Raise/lower key reminder when you take the helm with the sail furled |
| `CameraTilt` | 1 | How much the camera rolls with the heeling ship: 1 = vanilla, 0 = camera stays level |
| `HullSpeedScale` | 1.6 | Hull speed = 2.43·√(length m) kn × this: raft 10, Karve 12, longship 16, drakkar 18 |
| `HullSpeedDrag` / `OverSpeedPitchBoost` | 3 / 2 | Extra drag and bow-burying past hull speed |
| `ManualTrim` | false | Your personal opt-in, saved when you press H; false = vanilla whenever you steer |
| `ToggleKey` | H | Flips `ManualTrim` in game |
| `ReleaseHoldTime` | 0.5 | Seconds to hold E to release the helm |
| `LowerSailKey` | Q | Lower sail one step |
| `RaiseSailKey` | None | Extra raise key (tap E always works) |
| `MoveKeysTrimSheet` | true | W/S (game Forward/Backward) sheet in / ease out |
| `InvertSheetKeys` | false | Swap so W eases and S sheets in |
| `EaseKey`, `SheetInKey` | None | Extra keys for ease / sheet in |
| `SheetRate` | 25 | Degrees per second the yard moves while a key is held |
| `ForceMultiplier` | 1.6 | Overall sail power vs. the ship's vanilla factor |
| `ApparentWindFactor` | 0.5 | How much boat speed shifts the wind you trim to |
| `HeelTorque` | 2.8 | Heeling torque per unit sail force; the hull's own buoyancy balances it (0 = off) |
| `RollDamping` | 0.6 | Water damping on roll/pitch while the sail draws, so it settles |
| `HeelWindPower` | 1.5 | Heel grows faster than linearly with wind: storms lay you over |
| `HeelDriveShare` | 0.3 | Part of the drive counts as heel too, so a trimmed reach heels 10-15° |
| `StallHeelBoost` | 1 | Extra heel when stalled |
| `MaxHeelAngle` | 45 | Heeling torque fades out over the last 10° before this |
| `FlapAmplitude` / `FlapFrequency` | 3 / 4 | Luffing wobble |
| `GustPeriodMinutes` / `GustChance` | 6 / 0.5 | One event per 6-minute slot with 50% chance (avg every 12 min) |
| `GustDuration` / `GustStrength` | 40 / 0.4 | Seconds per event, peak ±fraction of wind strength |
| `LullFraction` | 0.35 | Share of events that are lulls |
| `AmbientVariation` | 0.04 | Tiny constant wind wobble between events |
| `WeatherHelm` | 0.04 | Round-up yaw per degree of heel at 20° heel, quadratic in heel (0 = off) |
| `HeelDriveLoss` | 1 | Speed lost to heel (0 = off) |
| `HeelSweetSpotBonus` | 0.05 | Speed gained from a little heel, peaking at 8° |
| `LeewayFactor` | 0.4 | Extra sideways slip when stalled/heeled (0 = off) |
| `PitchTorque` / `PitchWindPower` / `MaxPitchAngle` | 5 / 2 / 15 | Bow-down torque per unit drive, its wind scaling, and where it fades out |
| `NoseDiveLoss` / `NoseDiveDamagePerSecond` | 0.4 / 0.3 | Drive lost and hull % per second with the bow buried |
| `WindShadowMax` / `WindShadowOnset` / `WindShadowRange` | 0.4 / 10 / 25 | Max wind lost in the lee of land, and the land angles where it starts and peaks |
| `DownwindRolling` / `RollPeriod` | 0.6 / 4 | Rolling strength when running, and the roll period for a Karve |
| `CrewCanTrim` / `CrewTrimBonus` | true / 0.2 | Crew trimming on/off and its speed bonus |
| `PassengerHud` | true | Ship HUD and readout for passengers |
| `MastStrainAngle` / `MastStrainGrace` / `MastStrainDamagePerSecond` | 35 / 4 / 0.5 | Heel that strains the rig, seconds before damage, hull % per second |
| `GybesEnabled` / `GybeDamagePercent` / `GybeRollRate` | false / 5 / 30 | Gybe on/off (off: a square yard has no boom to slam), hull damage %, roll kick |

Everything else is documented inline in the cfg. While connected to a server with `LockConfig` on, the
server's values replace yours for the physics sections (your file is not changed); they come back when you leave.

## Building from source

Requirements: .NET SDK (8 or 9) and a Valheim install.

```bash
dotnet build SailTrim/SailTrim.csproj -c Release
```

The dll lands in `dist\SailTrim.dll`. If Valheim is somewhere else pass `-p:ValheimDir="D:\Games\Valheim"`,
and `-p:DeployDir="D:\Games\Valheim\BepInEx\plugins"` copies it straight into the game.

The game assemblies are publicized at build time (BepInEx.AssemblyPublicizer) so the plugin can reach
private `Ship` fields. On load the plugin verifies every Harmony target by reflection and disables itself
with an error in `BepInEx\LogOutput.log` if a game update changed a signature.

## What it patches

* `ShipControlls.ApplyControlls` — W/S become ease/sheet-in; rudder passes through.
* `ZInput.GetButtonDown("Use")` — suppressed while piloting so tap-E can't release the helm; the plugin
  re-implements tap = raise, hold = release.
* `Ship.GetSailForce` — lift/drag model from sheet angle vs. apparent wind.
* `Ship.UpdateSail` — yard follows the sheet angle, wobbles when luffing.
* `Ship.CustomFixedUpdate` (postfix) — heel couple on the ship owner.
* `Ship.Start` / `Ship.UpdateControlls` — `SailTrim_Sheet` / `SailTrim_Mode` RPCs (pilot → ship owner) plus
  `sailtrim_sheet` (float) and `sailtrim_manual` (bool) in the ZDO (owner → everyone), mirroring how vanilla
  syncs the rudder.
