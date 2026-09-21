# SailTrim pick-up notes

## Status, 2026-09-21: 1.9.0 is built, installed and unpublished

**Lashing alongside** is the one change in 1.9.0. A gangway lowered onto another boat's deck now lands on it and
the two boats are lashed: both hold where they lie and both stop, as a gangway onto a dock stops the one boat.
Taking the helm of either raises the plank and frees them both. `GangwayLashShips` (default true) turns it off.
Built, packaged at `releases/SailTrim-1.9.0.zip` and copied into Max's Flotilla profile. **Not published to
Hexium and not play-tested**: it waits on Max sailing two boats together, exactly as 1.7.0's crew weight waits
on a crew.

How it is put together, because none of it is obvious from one file:

- Landing was already possible. `GangwayMount.AngleOnto` discards the plank's own boat and accepts anything else
  as ground, so another hull always answered the probe; what was missing was any notion of whose deck it was.
  It now carries the `Ship` the winning hit belongs to out through `FindRest` to `Lower`.
- The record lives on both boats' ZDOs: `SailTrim_LashPort` / `SailTrim_LashStbd` on the boat the plank belongs
  to, `SailTrim_LashedBy` on the boat underneath. **Neither side trusts its own copy.** `Gangway.LashedFrom`
  reads the other boat's record before it holds, so a plank that has come up releases the boat underneath even
  if the message never arrived, and a world reload leaves the raft made. The same half-minute grace as the
  cleat covers "that boat's data is not loaded yet", which is not the same thing as "that boat is gone".
- Holding is the mooring's, not a new one: `Mooring.FixedStep` treats "a plank is across me" exactly as it
  treats "my own gangway is down", through `Gangway.LashedAlongside`.
- **The plank must not lean on the other boat.** Ours is a kinematic body, and a kinematic body overlapping a
  floating one shoves it with everything it has: the same thing that put every hull on its beam ends until the
  boat's own colliders were struck out in `IgnoreShip`. `UpdateLashIgnore` strikes out the pairs with the boat
  it is lying on while it is down, and puts them back when it comes up, or the two hulls would pass through
  each other afterwards.
- The `SailTrim_Gangway` RPC now carries the target: `Register<int, int, ZDOID>`. **ZDO keys for ZDOIDs are
  strings in this build**, not stable hashes: `zdo.Set(int, ZDOID)` does not exist and the compiler's complaint
  about int-to-string is what that looks like.

## Previously, 2026-09-21: 1.8.0 is released

**The Gangway** is finished, merged and published. `main` is the released 1.8.0, tagged `v1.8.0`; the `gangway`
branch is merged and kept. Hexium: `Max/SailTrim 1.8.0` (package 1207). Sailing physics is unchanged from 1.7.1,
`SailTrimShip.cs` byte-identical to the previous main.

**Read the "Gangway" sections at the bottom of this file before touching that feature.** They are written as
causes, not as a list of changes, because most of the time went into the same few mistakes wearing different
hats: something of ours counted as part of the boat, a figure assumed instead of measured, or a value that had
already been written down somewhere and no longer followed the code.

### Still open

- **The dedicated server is on 1.6.2** and will now be refused by 1.9.0 clients (the version check wants an exact
  match). `releases/SailTrim-1.9.0.zip` is committed ready for it; 1.6.2 and 1.8.0 are there too.
- **Lashing alongside is untested in game.** Two things to watch when it is: whether the two hulls fend each
  other off while both are held, and whether the plank keeps its footing on a deck that is itself rising on the
  swell (the probe median smooths the ground, which was written for ground that holds still).
- **Crew weight (1.7.0) has never been tested with a real crew.** It shipped inside 1.8.0 because 1.7.0 and
  1.7.1 were never published separately. Nothing in it should touch solo play.
- `SurveyKey` ships unbound, but Max's own config still holds `F10` from when that was the default. That is
  deliberate for him and harmless; the point to remember is that **a default changed in code does not change a
  value already written to a config file**. That cost two play-test rounds once already, when a switch left true
  in the config painted the hull's timber over the gangway's ironwork.

## Picking this up on another machine

- Repo `C:\Users\maxst\dev\SailTrim`, game `C:\Program Files (x86)\Steam\steamapps\common\Valheim`.
- Build: `dotnet build SailTrim/SailTrim.csproj -c Release`. Output lands at `SailTrim/bin/Release/SailTrim.dll`
  with no target-framework subfolder. Deploy by copying it over `BepInEx\plugins\SailTrim.dll`; the game locks
  that file while it is running.
- **Check the build succeeded before copying.** Piping `dotnet build` through `tail` hides a failure's exit code,
  and a stale DLL then gets deployed and play-tested as though it were the fix. That has happened here.
- Publishing: `publish-mod.ps1 -Zip <absolute path> -Author Max -Categories vehicles,mechanics,user-interface`.
  It wants `hexium-token.txt` beside it (gitignored): copy it from
  `C:\Users\maxst\OneDrive\Documents\Hexium_API_Key.txt`, run, delete. Never print the key. The manifest
  description has a hard **256 character** limit and a longer one is refused at submission.
- Game APIs: decompile rather than remember, with
  `ilspycmd -t <Type> "<Valheim>/valheim_Data/Managed/assembly_valheim.dll"`.
- **Prefab names: read them out of the bundles, do not guess.** They are on disk:
  `grep -ria -o "<stem>[a-z_0-9]*" valheim_Data/StreamingAssets/SoftRef/Bundles/ | sed 's/.*://' | sort -u`.
  Two play-test rounds went on guessing at the Wood Iron Beam, which this answers in seconds. Note also that a
  piece's build-menu label and its prefab name are different things: `piece_woodironbeam` is the label,
  `woodiron_beam` is the prefab.

### The survey tool (`SailTrim/Survey.cs`)

Set `11. Development / SurveyKey` to a key and press it near some boats. It writes into
`BepInEx\cache\SailTrim\survey\`:

- `survey.txt` - per boat the float collider and hull bounds, and per mount the rail found, the deck drop and the
  rest angle, plus a **section across the beam** (`profile`), and a **swing** table that walks the gangway
  through its travel running `Physics.ComputePenetration` against the hull at each step.
- `hierarchy.txt` - every transform of every boat and of our own rig, with mesh, vertex count, own size, scale,
  material, shader, probe usage, motion vectors, shadows, layer and body/interpolation, then a **coincidence
  report** naming any two of our own meshes that share a place.
- Images of each boat from several angles, and a frame at each step of the swing.

This is what makes the work possible without being at the keyboard, and it is worth extending rather than
squinting at screenshots. Nearly every fix below came from a number in one of those two files. Some examples of
what that looked like in practice: the same Karve reading its rail at 0.92 m on one side and 1.56 m on the other
said the section was being cut in world space on a boat that is never level; `coincident pairs: 0` said the
flicker was not geometry at all and saved days of moving things apart; `mat=ship_wood` on an iron beam said the
material lookup was right and something downstream was painting over it.

# Pick-up notes (history)

**Status 2026-09-17 (later): everything below is released as 1.5.0** (Hexium `1.5.0.zip`, branch `spill-tack` merged into
`main`). 1.5.0 = visual tack motion, cloth luffing/tautness, mast-only crew seat; sailing physics unchanged from 1.4.0.
**The dedicated server needs 1.5.0** (exact version-string match). Earlier: 1.4.0 (main `83e72a9`), branches
`settings-tab` and `square-rig` merged.
Kept for the reasoning and test history.


Written 2026-09-16 ~00:30 Central on Max's home PC, right before switching computers. Read this first on the new
machine. `main` is the released **1.2.1**; this branch carries the unfinished settings tab on top of it.

## State of things

### Released and done
- **1.2.1** fixed the tap-E-releases-the-helm bug. Root cause was **Jotunn** (`com.jotunn.jotunn`): its postfix on
  `ZInput.GetButtonDown` overwrites `__result` via a reverse-patched call to the unpatched original, which undid
  SailTrim's prefix swallow of the Use press. Fix = a second postfix in `Patches.cs`, `[HarmonyPriority(Priority.Last)]`
  + `[HarmonyAfter("com.jotunn.jotunn")]`, re-applying the swallow. Verified by Max in single-player.
- 1.2.1 is on GitHub `main` (`0c7a3d6`) and uploaded to Hexium (`https://cdn.hexium.gg/upload/1207/1.2.1.zip`, DLL
  byte-identical to the build). Hexium's site/API were still *listing* 1.2.0 as newest at 02:30 UTC; 1.2.0 itself took
  a few hours to list, so expect 1.2.1 to appear on its own. Do not re-upload; Hexium rejects duplicate versions.
- **The dedicated server was still on 1.2.0.** It needs 1.2.1 (exact version-string match). A note for the server-side
  session was written to the home PC's `Downloads\SAILTRIM-1.2.1-SERVER-NOTE.md`; the gist is above.

### In progress: "SailTrim" tab in the vanilla Settings menu (this branch)
Goal from Max: keybinds (and the key-behaviour toggles like invert sheet) editable in the game's own Settings screen,
as a tab for the mod. **Physics must not be exposed anywhere in the menu**; only keys and key toggles.

Implementation: `SailTrim/SettingsTab.cs` (new), hooked by a prefix on `Settings.Awake` in `Patches.cs`
(`Settings_Awake` -> `SettingsTab.Install`), with `Settings.Awake` added to `VerifyPatchTargets` in `Plugin.cs`.
No Jotunn dependency, no asset bundle: it clones vanilla widgets from the same Settings instance.

How it works:
- Finds the *main* `TabHandler` (the one whose pages carry `ISettingsTab`; the Controller page has its own nested
  sub-tab handler, which is what the first attempt wrongly picked up). Clones the Gameplay tab's button, builds a
  fresh page, adds a `TabHandler.Tab`, and the game initialises our `ISettingsTab` like its own.
- Rows: key rows are clones of `KeyboardMouseSettings.m_keys[0].m_keyTransform` (an empty 0x0 container named
  `Attack` with a `Button` child and a `Label` child); toggles are clones of `GameplaySettings.m_toggleRun`
  (`ToggleSprint`, 20x20, with a `Label[300x20]` child hanging to its left).
- Contents: keys ToggleKey (manual trim on/off), LowerSailKey, RaiseSailKey, EaseKey, SheetInKey; toggles
  MoveKeysTrimSheet, InvertSheetKeys. Click a key -> capture next key via `ZInput.GetKeyDown(kc,false)` over a
  KeyCode list (Esc cancels, Delete clears, Mouse0/1 and joystick buttons excluded). OK writes the `ConfigEntry`s and
  `Config.Save()`; Back discards. Reads happen every frame in `Plugin.Update`, so changes apply immediately.

Test history (main menu -> Settings, Flotilla Gale profile):
1. Tab missing: wrong TabHandler. Fixed.
2. Tab present, toggles correct and styled right. Problems: key rows collapsed onto each other (template row has zero
   height, sized by the vanilla grid at runtime), headers rendered in a white default font, and a stray white
   "SailTrim" text above the tab bar (came from relabelling every `TMP_Text` under the cloned tab button).
3. **Current build (untested):** explicit geometry for key rows (20x32 anchor in the toggle column, 140x32 button to
   the right, 320-wide right-aligned label to the left), headers cloned from the toggle's label, tab-button relabel
   limited to the button's own label and its `Selected` overlay. It also logs `Describe(...)` dumps of the tab-button
   template and the built list (with font name + colour) to `BepInEx\LogOutput.log` for the next adjustment.
   Version string is still 1.2.1 on this branch on purpose (dev build).

Known cosmetic issue: with seven tabs the "Keyboard & Mouse" tab label wraps to two lines. Options: shorter tab
label, smaller tab font, or accept it.

## Next steps
1. Build this branch, install the DLL into the Gale profile, open Settings from the main menu, screenshot.
   Read the `SailTrim: settings ...` lines in `BepInEx\LogOutput.log` if anything looks off.
2. Fix layout as needed; then remove the `Describe` log lines (or keep them at Debug level).
3. Decide whether ManualTrim itself should also be a toggle on the tab (it is the H key's saved state).
4. Gamepad navigation on the tab is not wired (`OnTabOpen` is empty); mouse works. Optional.
5. Bump to **1.3.0** (Plugin.VERSION, csproj `<Version>`, package/manifest.json), CHANGELOG entry, merge to `main`,
   build, package (`package\*` + `SailTrim.dll` at zip root), publish with `publish-mod.ps1`, push, then the server
   needs 1.3.0 too (exact version match).

## Environment you need on the new PC
- **.NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8`). Build:
  `dotnet build SailTrim\SailTrim.csproj -c Release` -> `dist\SailTrim.dll`. csproj defaults to the Steam Valheim
  path for references; override with `-p:ValheimDir=...` if different.
- **Decompiler** (optional but very useful): `dotnet tool install -g ilspycmd --version 8.2.0.7535`, then run with
  `DOTNET_ROLL_FORWARD=Major` (it targets .NET 6). Example:
  `ilspycmd -t Settings -r "<Managed>" "<Managed>\assembly_valheim.dll"`. Useful types for this work: `Settings`,
  `TabHandler`, `Valheim.SettingsGui.KeyboardMouseSettings`, `Valheim.SettingsGui.GameplaySettings`,
  `Valheim.SettingsGui.ISettingsTab`, `ZInput` (in `assembly_utils.dll`), `Player`, `ShipControlls`.
- **Hexium token**: `hexium-token.txt` next to `publish-mod.ps1` in the repo root. It is gitignored and NOT in GitHub;
  copy it from the old PC (`C:\Users\maxst\source\SailTrim\hexium-token.txt` or `Downloads\hexium-token.txt`).
- **Gale** (`%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>`): the profile's plugin files are hard-linked
  to Gale's cache, so `cp` over `BepInEx\plugins\Max-SailTrim\SailTrim.dll` also rewrites the cached copy. Delete
  the profile file first, then copy, to keep the cache honest. The DLL is locked while Valheim runs.
- Logs: mod lines in `<profile>\BepInEx\LogOutput.log`; vanilla helm events (`Doodad controlls set` /
  `Stop doodad controlls`) in `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\Player.log`.
- Original bug handoff from the server-side session: home PC `Downloads\SAILTRIM-BUGFIX-HANDOFF.md` (server facts,
  connection details, the server's build setup). Not in the repo.

## Update 2026-09-17: branch `square-rig` (1.4.0 candidate), built on top of `settings-tab`

Max's asks, all implemented but untested in game when written:
- Square-rig trim: new lift/drag tables in `SailTrimShip.cs`, `IdealSheet` = argmax of drive from those tables
  (`BestSheetFor`), drag regime abaft `SquareRunAngle` (110, `3. Physics`, synced) where nothing counts as stalled.
  Pointing (no-go) and the 0–90 sheet range deliberately unchanged.
- Continuous reef: `SailAmount` (0..1) on `SailTrimShip`, synced via RPC `SailTrim_Sail` + ZDO `sailtrim_sail`;
  hold E / `RaiseSailKey` lets out, hold Q takes in, at `SailSetRate` per second (`3. Physics`, synced). The vanilla
  speed setting is driven by `PilotFixedStep` through RPC `SailTrim_Speed` (Full while any sail, Slow/Back rowing,
  Stop). `UpdateSailSizeManual` replaces vanilla `UpdateSailSize` for the cloth. E no longer releases the helm
  (Jump does, as vanilla); `ReleaseHoldTime` config removed.
- Rowing: `RowForwardKey` (LeftShift), `RowBackKey` (LeftControl), `RowKeysToggle`, `StowHoldTime`; logic in
  `Plugin.UpdateRowKeys`. Rowing refused with sail set; holding a row key ≥ StowHoldTime stows at 2x rate.
- `RudderSelfCenter` (postfix on `ShipControlls.ApplyControlls`).
- Settings tab: rows for the row keys, the two options, and a read-only "Game keys" list from
  `Localization.GetBoundKeyString`.

Test plan (single-player is enough): take helm, H on; hold E → sail % climbs on the HUD and the cloth follows;
hold Q → drops; Shift with sail set → "Hold to stow" message, keep holding → sail stows then rowing starts
(paddle animation, "Rowing" on the HUD); release → stops (hold mode). Space lets go. Then multiplayer: a second
client should see the partial sail and the paddling. Watch `LogOutput.log` for errors from `UpdateSailSizeManual`.
1.3.0 (settings tab + HUD-after-rejoin fix) is still on `settings-tab`, verified visually, not yet merged/published.

## Update 2026-09-17 (later): branch `spill-tack`, untested in game when written

Max's ask, after a long talk about how a real square rig tacks ("let go and haul"): when tacking with the sheet
hauled in, the clews are let go and the sail is a loose rag while the yard is braced round; eased out further, a
wind from ahead still puts the sail aback as before.

- `SailTrimShip.UpdateSpillState` (called from `UpdateYard`, every client, once per step): spill starts when the sail
  would go aback (`aoa < -LuffAngle`) while `SheetAngle <= SpillSheetAngle` (40, `3. Physics`, synced) and the wind is
  forward of the beam. It ends when the wind is back on the right face, the visible yard is within 15 deg of its
  target, and `TackHaulTime` (1.5 s) has passed (0.4 s if the yard never changed sides). Easing the sheet past the
  limit ends the spill at once, and the sail then goes aback: that is the deliberate way to back out of irons.
- Physics (`ComputeSailForce`): spilled = no lift, cd 0.06, no `MinFilledDrive` floor, so no heel and no sternway push.
- Visuals: `TackThroughSquare` routes the mast rotation through square during a spilled tack (sheets under 15 deg keep
  the short way, which avoids the half-turn spin fixed in 1.4.0); yard shake is stronger; `UpdateSailSizeManual`
  lifts and thrashes `m_sailBottomTransform` (the cloth's foot) scaled by `SpillFlog` (`5. Visuals`).
- HUD: `TrimState.Spilled`, "Tacking – sail spilled" / "Sail spilled – bear away to fill".
- Version string left at 1.4.0 on purpose so the test build still matches the server. Bump to 1.5.0 at release.

Test: close-hauled (sheet ~25), helm down through the wind: expect luff, then "Tacking – sail spilled", yard sweeping
through square, a dead patch, then fill on the new tack. Then in irons ease the sheet past 40: expect "Aback" and
sternway. Watch the loose foot: if the cloth misbehaves, set `SpillFlog = 0`.

### spill-tack, second pass (2026-09-17, after Max's first test)

- Tack animation was intermittent: Max sails close-hauled at about 6 deg of sheet, below the old 15 deg cut-off for the
  through-square sweep, and spill only began if a physics step caught the narrow aback window. Now spill also starts
  when the yard changes sides outright, the sweep always goes through square, and it takes `TackSwingTime` (1.6 s)
  whatever the sheet. The mast is turned as explicit yaw about the hull's up axis (`SweepCrosses` picks the way
  round), so a near half-turn cannot tumble the rig.
- Rag: foot lifted 30%, thrash scaled by the sail's drop, and the foot streams downwind (`_visWindTo`) like a flag.
- Seats bug: benches within 2.5 m of the mast (Karve) and the bow hold-fast (`$ship_holdfast@front`, longship) were
  hooked for crew trimming. Rule is now name contains "hold" AND within 2.5 m of the mast.

### spill-tack, third pass (2026-09-17): Max: "the animation looks pretty terrible", luffing must not shake the yard

What the sail actually is: `Ship.m_sailCloth` is a MagicaCloth2 blown by a global `MagicaWindZone` on EnvMan
(`SetWindDirection(GetWindDir())`, `main = intensity^2 * 100`). The cloth's foot is pinned to
`m_sailBottomTransform`, so jittering or sliding that anchor only waggles a stiff sheet (that was the ugly part).

- Yard wobble removed for luffing and spill. `FlapAmplitude` is now unused (binding kept for old cfg files).
- `UpdateClothFlutter` (called from `UpdateSailSizeManual` just before `SetParameterChange`): blends the cloth's own
  `SerializeData.wind` from the prefab's values toward turbulence 2, frequency 2, synchronization 0.05, influence
  x1.35, by 0.65 when luffing and 1.0 when spilled, scaled by `LuffFlutter` (`5. Visuals`). Originals captured once.
- Spilled look: the foot is smoothly clewed up `SpillClewUp` (0.4) of the way toward the furled position, which
  leaves the full-length cloth slack so the wind flogs it. No anchor jitter, no downwind slide. `SpillFlog` removed.
- Through-square sweep only when `SheetAngle >= TackSquareMinSheet` (25, sweep <= 130 deg); flatter sheets take the
  short way as in 1.4.0. Yard motion eases in and out (`_yardSpeed`).

If the cloth still looks wrong, next things to try: `SerializeData.gravity`, `damping`, and
`distanceConstraint`/`tetherConstraint` stiffness while spilled. Max tests on the Karve with a very flat sheet (~6 deg).

### spill-tack, fourth pass (2026-09-17)

- Max: clewing the foot straight up made the slack sail parachute, not flutter. The released foot now swings out
  downwind on an arc from the yard (`SpillStreamAngle`, 55 deg in strong wind, radius 0.9 of the hang so the cloth has a
  little slack), smoothly, with a slow sway only. Stream direction = 0.6 wind + 0.4 of the visible sail normal on the
  downwind side, projected square to the hang, so a sail streaming aft in a tack clears the mast. `SpillClewUp` removed.
- Max: hauled in hard, the sail should look tight: fair curve, little flutter. `UpdateClothFlutter` now starts from a
  "calm" set that depends on the sheet (`taut = 1 - sheet/60`, scaled by `CloseHauledTautness`): turbulence x0.15,
  frequency x0.5, synchronization to 1, influence x0.9, damping +0.2; luff/spill flutter blends on top of that.

### spill-tack, fifth pass (2026-09-17): physics reverted, tack is visual only

Max: tacking had become nearly impossible (stuck in irons), the animation often did not play, the yard ended on the
wrong side or snapped. He wants: tension loosens -> yard spins round to the other side -> sail tensions, fluid, with
NO effect on sailing performance. Lesson: do not change sail physics for a visual request.

- Physics: the spilled state is gone. `ComputeSailForce`, `ComputeAero`, `ApplyHullEffects`, `DriveFor` verified
  byte-identical to `main` (1.4.0). Config `SpillSheetAngle`, `TackHaulTime`, `TackThroughSquare`,
  `TackSquareMinSheet` removed.
- Yard: steered as ONE number, `_yardYaw` = yaw of the sail normal from the bow in the hull plane, clamped to +-90
  (square = 0, starboard tack negative, port positive). `delta = target - yaw` with no wrap, so the only path between
  tacks is through square. No second "flipped" facing, no shortest-way choice, nothing to snap to. This is Max's
  "restrict it so it cannot go the wrong way round" idea. Eased speed (`_yardYawSpeed`).
- `UpdateTackAnimation`: triggered by `_boomSide` changing with the wind forward of 100 deg. Phase 1 loosen (0.3 s,
  yard held), phase 2 sweep (`TackSwingTime`, rate from the measured sweep), phase 3 tension (0.9 s). `_tackAnim` 0..1
  drives the downwind-streaming foot and the cloth flutter. `TackAnimation` (`5. Visuals`) turns it off.
- `TrimState.Spilled` is never set now; `IsSpilled` is a constant false kept so the HUD compiles.

### spill-tack, sixth pass (2026-09-17): tack motion tied to the bow's swing

Max: slower, start sooner; the cloth was getting dragged through the mast.
- The timer phases are gone. Inside `TackStartAngle` (25 deg of apparent wind off the bow) `_tackZone` =
  smoothstep(absBeta / zone) and the yard target is `rawSide * (90 - sheet) * _tackZone`, with the side taken from the
  sign of `WindFromAngle` (no hysteresis). The target passes through zero head to wind, so the sweep is continuous and
  runs at the pace of the turn; `TackMaxYardRate` (55 deg/s) caps it. `TackSwingTime` removed.
- `_tackAnim` (looseness) = 1 - `_tackZone`, held up while the yard is still more than 10 deg from home.
- Likely cause of the cloth-through-mast: the foot's stream direction used the sail normal, whose sign flipped as the
  yard passed square, yanking the pinned foot across the mast. Now `_streamDir` is the apparent wind, turned at most
  45 deg/s, and the swing-out is reduced to 30% while the stream points fore-and-aft (at the mast).
- Physics re-verified identical to `main`.

## Update 2026-09-21: 1.6.x and 1.7.x

Published on Hexium: **1.6.2** (`https://cdn.hexium.gg/upload/1207/1.6.2.zip`). The server zip is also committed
at `releases/SailTrim-1.6.2.zip`, and the server needs it: the cleat holds a boat when no player is nearby.
The note for the server session is `Downloads\SAILTRIM-1.6.0-SERVER-NOTE.md` (still accurate except the version).

**Not published, awaiting Max's sailing test: 1.7.0 and 1.7.1.** Both are built, committed and installed in his
Gale profile. Publish them together once he has sailed with crew aboard.

### 1.6.0 to 1.6.2, shipped
- **Cleat** (`Cleat.cs`): bronze horn cleat, hammer Misc tab, procedural mesh on a copy of a game Standard
  material. Tie a boat within `CleatRange` (10 m); the rope is a verlet line that collides with the world and
  ends at the nearest point of the hull; a cleat-hitch mesh appears on the horn while tied. A moored boat holds
  its spot and heading (`Mooring`, ZDO keys `SailTrim_Cleat` / `MoorPos` / `MoorYaw`, RPC `SailTrim_Moor`),
  mends itself (`MoorRepairPerMinute`), and casts off a second after you take the helm (`CastOffDelay`).
- **Buoy** (`Buoy.cs`): built from the game's own barrel, wood pole, banner (re-dyed) and lantern; water piece,
  `m_distant`, floats to its anchor (`SailTrim_Anchor`), seven colours (`SailTrim_Color`) with map pins.
- Buoys and cleats take no damage at all (`WearNTear.Damage` / `RPC_Damage` / `ApplyDamage` prefixes). Note
  `m_noSupportWear` and `m_noRoofWear` are inverted: true turns that wear **on**, which is what broke buoys.
- **1.6.2**: moorings survive a world reload. Neither side unties because the other's ZDO has not loaded yet;
  a cleat that is really gone frees the boat after 30 s.
- Models and icons are rendered from the game's own prefabs (`Models.cs`) into `BepInEx\cache\SailTrim\*.png`.
  Dropping a `preview.request` file there (one prefab name per line) renders candidates on the next world load.
  `Shader.Find("Standard")` renders **magenta** in this build: clone a game material instead
  (`Models.StandardTemplate`). The main-menu ObjectDB has no items, so models and icons build from a world's.

### 1.7.0 and 1.7.1, built and unpublished
- **Crew weight**: anyone standing on deck shifts the boat (`SailTrimShip.ApplyCrewWeight`). Sitting crew, the
  mast hold-fast and the helmsman are excluded via `Player.IsAttached()`. Applied as a real moment, so ship size
  scales itself through inertia. `CrewWeight` (0.35) and `CrewMass` (90) are in `4. Heel`, server-synced.
  Crew on deck are told which rail to stand on (`SailTrimHud.CrewWeightHint`, `CrewWeightHints`).
  Design rule Max set: **no change may only nerf sailing**; each must give skill an upside that beats vanilla.
  Here the upside is that a flatter boat loses less drive.
- **1.7.1**: an empty boat furls for real (`SailTrimShip.StowIfEmpty`). The mod's own sail amount used to survive
  the boat being left, so it looked furled and then sailed off the moment you stepped back aboard.

### Ideas discussed, not started
Tow lines (a craftable tow rope between two boats), an anchor, loaded boats riding lower, rudder authority that
depends on water flow, swell that you surf down and pay for climbing, coastal set and drift. Max wants all of it
to feel vanilla and official, and each idea must carry its own upside.

## Update 2026-09-21 (later): branch `gangway`, built and untested in game

Max's ask, agreed in full: a wooden gangway hinged at the rail that drops until it meets something and lets you
walk between deck and dock while encumbered (a full load of metal cannot jump). His terms: every hull but the
raft; a craftable fitting you place to upgrade a boat, one a side; auto-retract at the helm but after 2 s, not the
cleat's 1 s; while down the boat holds its spot like a moored one but does NOT self-heal; you work it from on
board (separate from the cleat); lowering it within reach of a free cleat ties that cleat on as well.

- `Gangway.cs`. State is one int in the boat's ZDO (`SailTrim_Gangway`, bits: 1 port fitted, 2 stbd fitted,
  4 port down, 8 stbd down), written by the owner on the `SailTrim_Gangway` RPC (side, action 0 fit / 1 lower /
  2 raise), the same shape as the mooring.
- `Gangway.OnShipAwake` (from the `Ship.Awake` postfix) hangs a mount at each rail: x = float collider half-beam,
  z = 20% aft of amidships to clear the mast and shrouds, y raised to the deck by a one-off downward `RaycastAll`
  filtered to the boat's own colliders (`EnsureDeck`; Awake is too early, so it runs on first use). The mount's
  local rotation mirrors port, so inside a mount +X is always outboard. The plank is the mount's child and its
  own transform is the hinge: `localRotation = Euler(0,0,-angle)`.
- `GangwayMount` carries the `Hoverable`/`Interactable` and the `BoxCollider`, because the game routes hovering to
  a component on the hit collider's own object. It is **never deactivated** (its own Update watches the boat's
  state); unfitted it shrinks to a small patch of rail and hides the visual.
- Resting angle: `FindRest` sweeps from -20 deg (tip above the hinge, for a dock higher than the rail) down to
  `GangwayMaxAngle` (35) and takes the first thing the far end meets, which is what a plank let down on its hinge
  does. It re-runs every 0.1 s while down, so the plank rides the swell instead of hanging in the air or sinking
  into the dock. Nothing found = refused, because you could not walk up it carrying a load anyway.
- Walking across needs no work: `Character` adds `m_lastGroundBody.GetPointVelocity(feet)` to your own velocity,
  which is what makes the deck walkable, and the plank is a child of the boat so it shares the hull's rigidbody.
  The collider takes the hull's layer for the same reason.
- The hold: `Mooring.FixedStep` now runs for `byCleat || byGangway`, and the hull repair is gated on `byCleat`
  alone. `Mooring.HoldHere(ship)` records the spot when a gangway goes down. Taking the helm raises a gangway
  after `GangwayRetractDelay` (2) via `Gangway.PilotAtHelm`, next to the cleat's `CastOffDelay` (1).
- Auto-tie: `Cleat.AutoTie(ship)` finds the nearest `CleatPiece` within `CleatRange` with no boat and ties it;
  `CleatPiece.TieTo`/`HasNoBoat`/`DistanceToHull` are the new internals, and `Tie` now tolerates a null user.
- The item: a clone of FineWood with cloned `SharedData` (it is a plain class, so a reflection field copy), the
  stock renderers disabled, our plank mesh as its model and an icon rendered from it; added to `ObjectDB.m_items`
  + `UpdateRegisters()`, to `ZNetScene`, and a `Recipe` at whatever station an existing workbench recipe uses
  (10 fine wood + 4 iron nails, configurable: a later-game unlock, matching the longship's own nails). `Inventory.GetItem("Gangway")` matches on the shared name.
- Config section `10. Gangway`: Enabled, Length (3), MaxAngle (35), StowAngle (-78), SwingRate (45),
  RetractDelay (2), TiesCleat, WoodCost, NailCost. Unsynced, like `8. Mooring` and `9. Buoy`.
- Version left at 1.7.1 on purpose: the dev build still matches the server.

**Untested in game.** Watch for: the mount position (the log prints beam and z per hull; the rail height comes
from the raycast), how the stow along the rail sits on each hull (see below),
whether the icon and dropped model render, whether the recipe shows at the workbench, and above all whether you
can actually walk up it while encumbered without falling through the join.

**Length 6 m (Max's call), and the stow changed with it.** A six-metre plank stood on end at the rail would be a
spar taller than the mast, among the shrouds. It now stows lying flat along the rail pointing forward, clear of
the mast, the shrouds and the steering oar, and deploying is one blended movement over `GangwaySwingTime` (1.6 s):
the first half swings it out from fore-and-aft to square over the side, the second half lowers it onto its rest.
The full walkable collider only exists past the half-way point; stowed it is a small patch of rail, so six metres
of plank never blocks the deck. `GangwayStowAngle` and `GangwaySwingRate` are gone, replaced by `GangwaySwingTime`.
Watch on the Karve, the shortest hull: the mount is 20% aft of amidships, so six metres forward reaches about the
stem. Note the plank mesh is built once from `GangwayLength` at load, so changing that config wants a restart.

**First in-game try (Max): the item crafts and the icon renders, but there is nothing on the boat to use it on**
("Use Gangway on what?"). Two faults, both in the mount:
1. `EnsureDeck` only ran when you lowered the gangway, so an unfitted mount sat at its guess (`floatCollider`
   centre + 1 m), usually inside the hull. It now runs on the mount's first `Update`, when physics is live, with
   the ray bounded to 1.6 m above the guess so the yard overhead cannot win, and it logs the rail height it found.
2. The unfitted collider was a small box tucked inside the rail. `Player.FindHoverObject` takes the FIRST thing
   its ray meets and stops, so the hull's own collider always won and the mount was never hoverable. The unfitted
   mount is now a small post standing proud of the rail; stowed it covers the inboard 1.5 m of the plank;
   deployed it is the full walkable plank (`SetCollider(0|1|2)` / `RefreshCollider`).

**Second in-game try (Max): it deploys and you can stand on it, but four faults.**
1. *Black plank.* The procedural `Models.Standard` material rendered black on the ship (it was fine in the icon).
   Not chased: the plank now wears the game's own material off the FineWood item, which is lit like everything
   else and is the light timber it should be. The procedural one is only a fallback. Logged on first use.
2. *Mount hung in the air beside the boat.* It was placed at the float collider's half-beam, and that box is
   wider than the deck. `EnsureDeck` now feels inward from outside the hull in 8 cm steps and takes the first
   place a downward ray lands on the boat as the rail edge, then sits a hand's breadth inboard of it.
3. *It rested on the sea.* The water layers are out of the `FindRest` mask now, so a gangway with nothing solid
   under it refuses to go down, which is right.
4. *You had to climb the rail to get on it*, which is the one thing a loaded player cannot do, so the feature did
   nothing. `EnsureStep` builds a ramp from the deck up to the hinge, its height measured per hull (probes inboard
   and takes the lowest top surface, so a bench does not fool it), its slope kept walkable (run >= 2.2x the rise).
`GangwayMountZ` was added so placement along the hull can be tuned without a rebuild; the log now prints the rail
x, y and the deck drop for every mount on every hull.

**Still to do:** Max suggested the plank fold into three for stowing, which would also stop six metres of timber
lying the length of the rail. Left until placement is settled, since folding only changes the stowed look.

5. *It shivered, jumping every frame.* The rest angle came straight from one raycast and was applied as-is, and
   the correction from tip height to degrees used a hard-coded factor of 6 that amplified the noise. Now: the
   correction is derived from the plank's own length (`asin(dy / Length)`), probes go into a five-deep ring and
   the plank steers for the **median**, and `_restAngle` is `SmoothDamp`ed toward it over 0.35 s. Max's call that
   a tip slightly inside the dock beats a shivering plank, so it aims 1 deg past what it reads.

**Third in-game try (Max): the boat heeled over at low tide, the stairs were no good, the plank looked terrible.**
1. *The heel.* The plank was a child collider of the ship, so the physics engine treated it as part of the hull:
   resting the far end on the shore propped the boat up and levered it over as the water fell. The plank now has
   its own **kinematic Rigidbody**, which takes it out of the boat's compound collider. It stays solid to walk on
   and cannot push the boat. `FixedUpdate` hands it the boat's point velocity so anyone standing on it is still
   carried along (a kinematic body has none of its own).
2. *Black timber, and why.* Not the material after all. `Models.MeshPart` puts the mesh on its parent's layer, and
   the plank's parent was on the boat's COLLIDER layer, which the scene's lights do not illuminate. The boat's
   meshes are on a different layer. `Gangway.SetLayer` now puts the visuals on `VisualLayer(ship)`, read off one
   of the boat's own MeshRenderers. Worth remembering for any future part hung on a ship.
3. *Built from real pieces now*, as Max asked and as the buoy does: `BuildWalkway` lays the game's `wood_floor`
   end to end (one piece per 2 m, scaled to width) with a `wood_beam` down each edge, falling back to a plain
   plank only if those prefabs are missing. The inboard ramp is the same walkway, shorter and tilted, so the
   "stairs" are now real timber. The item's own model and its icon come from the same builder.
   Note a game material needs the UVs of the mesh it ships with: our procedural mesh sampled the atlas into mud,
   which is the other half of why it looked wrong. Use CopyVisual for anything that wants a game texture.

**Folding (Max asked for it next).** The ramp is three sections hinged end to end, each a third of `GangwayLength`,
built by `BuildSections`: section 2 hangs off the outboard end of section 1 and section 3 off section 2, so each
folds back over the one before it. Stowed they sit at +-168 deg (a shade under flat, so the stack reads as three
boards rather than one), which stows six metres as two. `Apply` now runs three movements in order with a little
overlap: unfold (0 to 0.4 of the travel), swing out from along the rail (0.35 to 0.7), lower onto its rest (0.7 to
1), over `GangwaySwingTime`, raised from 1.6 to 2.4 s to cover all three. The walkable collider only appears past
0.6 of the travel, and stowed the collider is one section long and 0.5 m tall to match the folded stack.

## Gangway: surveying the boats (F10)

Placement has to be judged by eye, hull by hull, and I cannot see the screen. So the mod now photographs
itself. `SailTrim/Survey.cs` binds **F10** (`11. Development / SurveyKey`, `SurveyWidth` 768): press it with
boats about and every `Ship` within 80 m is rendered from four angles into
`BepInEx\cache\SailTrim\survey\`, named `<Hull>_<state>_<view>.png` where state is bare / stowed / down and
view is beam, quarter, astern, mount. Beside them `survey.txt` records, per boat, the float collider and hull
bounds, and per mount the rail position the raycast actually found, the per-hull correction applied, the deck
drop, the deploy fraction and the rest angle. A picture on its own says "that looks wrong"; the numbers say by
how much.

The camera is `Camera.CopyFrom` the game's own, so fog, layers and the rendering path match what the player
sees; it is moved to frame the subject's renderer bounds from a direction in the ship's own frame and rendered
once to a `RenderTexture`. Nothing runs unless the key is pressed.

`Gangway.PlacementFor(Ship)` is the correction table: `Z` (how far aft of amidships, as a fraction of the half
length), `Inset` (extra metres inboard of the measured rail edge) and `Rise` (extra metres above the rail top).
It starts at the config default for every hull with the three hull branches empty, waiting for the pictures.
The rail itself is still measured by ray — these are only the adjustments on top.

**Before release, set `SurveyKey` back to `KeyCode.None`.** F10 is a convenience for this iteration, not
something a published mod should be taking from players.

## Gangway: what the first survey showed

All three hulls were on their beam ends. The plank has its own kinematic body so that it cannot prop the hull
up on the dock; that also makes it a separate object wedged inside the boat, and a kinematic body overlapping a
floating one depenetrates with everything it has. `IgnoreShip()` now strikes out every collider pair between our
fittings and the ship, redone for twenty seconds because a boat goes on assembling itself after `Awake`.

Setting velocity on a kinematic body is refused in Unity 6 and logs a warning every fixed step, so the passenger
handoff in `FixedUpdate` never did anything. It is gone. In its place `Character.UpdateGroundContact` gets a
postfix: `GangwayFooting` marks a part you can stand on and names its boat, and the patch swaps our body for the
boat's, so `GetPointVelocity` and `GetStandingOnShip` both answer as they do on the deck. Keeping our colliders
out of the ship's compound matters for more than propping: `Ship` never sets `centerOfMass` or `inertiaTensor`,
so anything added to its body would move both, and a gangway must not change how a boat sails.

### Reading a hull from its section

`EnsureDeck` samples the whole section across the beam and writes it into the survey notes. Taking the first
thing a ray downward met put the Drakkar's mount out in the air, because an oar stands further out than the rail.
It now takes the outermost timber within 7 cm of the highest.

The deck beside the rail is the first level run of at least seven samples inboard of it, and the lowest point of
that run. Probing at fixed fractions of the beam found the Drakkar's main deck a metre and a half down, when what
is alongside the rail there is a side walkway a hand's breadth below it — the stowed plank ended up buried in the
hull. Checked against all three sections: Karve 0.71 m, Longship 0.69 m, Drakkar 0.11 m.


## Gangway: measuring instead of squinting

Screenshots showed that something was wrong but never what. Three things were added so the work can be done on
numbers:

- **`hierarchy.txt`** — every transform of every surveyed boat and of what we bolted to it, in the boat's own
  frame, with mesh name and vertex count, renderer bounds and collider type/layer. Two meshes in the same place
  is a thing you can read off a list; it is not a thing you can see in a screenshot of a plank at dusk.
- **The swing** — `GangwayMount.PoseFor` puts the rig at a point of its travel and holds it there. The survey
  walks 0, 0.2, 0.4, 0.6, 0.8, 1.0, photographing each and, at each, running `Physics.ComputePenetration`
  between our colliders and the hull's. "It clips through the ship on the way out" becomes
  `0.4  0.38 m (plank in Karve_hull)`, which can be fixed without being at the keyboard.
- **Resting on anything, not just the tip** — `FindRest` used to look only under the far end, so a beam halfway
  out was something the plank passed through. It now samples every 0.4 m along the plank, works out the angle at
  which each point would come down on what is under it, and takes the shallowest.

`FindLadder` anchors the mount to the boat's own boarding ladder. Every hull but the raft has one, and the beam
it hangs on is the one place the builders left clear of benches, shrouds and mast; `Placement.ZOffset` nudges
from there. Guessing a fraction of the length aft of amidships put the Drakkar's mount nowhere near it.

## Gangway: where the z-fighting actually was

Not duplicate models. `hierarchy.txt` showed exactly one deck and two kerbs per section. The kerbs were placed
at `width/2 - Kerb/2`, which puts the kerb's outer face in *exactly* the same plane as the deck's edge face, for
the whole two metres of the section — and the renderer has no way to choose between two coincident faces. The
sections met the same way, face to face in one plane at each joint. Kerbs are now set in by their own width, and
each section is built a hair short so its joints are joints.

The lesson is that coincident faces, not duplicate meshes, are what "two models inside each other" usually looks
like, and a hierarchy dump proves which it is in seconds.

### Steps, not a ramp

The sloped ramp along the rail was about 1.6 m long, and there is nowhere on a Karve to put 1.6 m of anything:
the survey caught it 0.29 m inside the Longship's `sit_box` and 0.63 m inside the Karve's planking. It is now two
or three treads hard against the rail running inboard, 0.8 m of deck in the corner the rail already wastes, with
a single sloped collider over them so it walks smoothly with a full load.

### The stack picks its own end of the boat

Every hull puts its furniture somewhere different: a rule that stowed forward suited the Karve (mast at z 0)
and put the Longship's stack through `sit_box` at z -3.03. `ChooseStowSide` poses the stack both ways once,
measures each with `ComputePenetration` against the hull, and keeps the clearer one. It logs which it chose.

## Gangway: the step, redesigned

A ramp is the wrong shape for a boat. Whatever its slope, it is long, and a Karve has nowhere to put anything
long: the survey caught the ramp 0.63 m inside the planking and, moved inboard to clear that, sitting in the
mast. A staircase of treads fixed to the deck was no better — it is still something you walk round for the rest
of the voyage.

The step is now one or two treads **hinged on the inside of the rail**. They swing down with the gangway and
fold flat back against the rail when it is stowed, so they occupy no deck at all except while being used. Each
gets its own kinematic body and its collider is off while folded. Height is probed just inside the rail, which
is the only place worth measuring on a hull with no flat deck; one tread up to 0.45 m of climb, two above that.

## Gangway: finding it

Nothing marked the spot. You had to know that one particular stretch of one particular rail would answer a
keypress, which nobody was going to work out. Three changes:

- **Brackets on the rail**, timber, present whether a gangway is fitted or not. The boat now shows where its
  gangway goes.
- **The hover text names them** — "Gangway brackets (starboard)" — and when you have no gangway it says where to
  get one rather than only that you lack it.
- **A message the first time you board a boat carrying a gangway**, once per boat: "Gangway: fit it to the
  brackets on either rail."

## Gangway: the z-fighting was in the meshes we copied

The kerbs were one cause and fixing them changed nothing, because the bigger one was underneath. The game's
pieces are statically batched: `wood_floor`'s mesh holds its vertices in the world coordinates of whatever scene
baked it — around fifty units from its own origin — and the renderer carries a matching negative translation to
put it back. `hierarchy.txt` shows it plainly: every part we built read `_Combined Mesh [high] @(52.38,-9.01,0.89)`
while its renderer bounds were at `(0.73,0.52,0.88)`. Reusing that mesh reproduces the offset, so every surface
we draw is the difference of two large numbers, in every pass. It affects every piece we build, which is why it
looked like everything was fighting.

`Models.CopyVisual(..., bake: true)` now rewrites the vertices into the part's own space around its own origin,
and keeps only the triangles inside that renderer's own bounds so nothing of a neighbour in the same batch comes
with it. It falls back to the old path if the mesh is not readable — **check `hierarchy.txt` for mesh names
ending `_SailTrim` to confirm the bake actually ran.**

## Gangway: a ramp, because Valheim has no step-up

The folding tread was the wrong idea and the reason is worth writing down: **Valheim characters do not step up
onto ledges, they jump** — and a loaded player cannot jump, which is the entire reason this feature exists. A
0.35 m tread is therefore something to stand in front of, not on.

The step is now a **brow**: a ramp at a walkable 34 degrees, hinged just inside the rail, that folds in two
against the rail when stowed so it takes no deck except while being walked on. Two leaves, because a ramp long
enough to walk up is too long to stand against a rail unfolded.

## Gangway: the hull's section must be read in the hull's frame

`TopOfShip` cast straight down in world space. A boat at anchor is never level, so the "section across the beam"
was a section through a slanted column of hull, and it changed with however the boat happened to be lying. The
same Karve read its rail at 0.92 m on one side and 1.56 m on the other; the next Karve read 1.80 m. Every number
downstream — rail position, deck drop, the length of the brow — was built on that. It now casts along
`-ship.transform.up` and converts the hit with `InverseTransformPoint`, so the section is a real section.

This is worth remembering for anything else that measures a boat: a raycast in world space asks a question about
the world, not about the boat.

## Gangway: the flashing is being narrowed down, not yet solved

Two candidates were removed this round and one test added:

- **Shadow casting is off** on every baked copy. A thin plank self-shadowing at a grazing angle is acne, and the
  cascades shift with the camera every frame, which would read as the surface flashing between lit and dark.
- **The bake now keeps every channel** — uv2, vertex colour and tangents, not just position, normal and uv. A
  game shader reads vertex colour for wear and snow and the tangent for its normal map; without them it lights
  the surface from nowhere in particular.
- **`Coincident()`** reports any two of our own meshes whose bounds centres are within 3 cm, by path, in
  `hierarchy.txt`. If two surfaces really are in one plane this names them; if it reports zero, the cause is
  material or lighting and not geometry at all.

## Gangway: the flashing is not geometry

`Coincident()` reported **0 pairs on all six mounts**. Nothing of ours shares a place with anything else of ours,
so it is not z-fighting, not a duplicated model, and no amount of moving things apart will help. It is how the
surface is lit.

Two differences between our renderers and the boat's own were closed:

- **Scale is baked into the mesh** (`Models.ScaleInto`), so every part keeps an identity scale. A non-uniform
  scale needs the inverse transpose for its normals; Unity does that on the GPU but *not* when it merges small
  movers into a dynamic batch, and whether a frame merges them is not predictable. The lighting then alternates
  between right and wrong every frame on geometry that never moves — which is exactly the reported symptom.
- **Renderer settings are copied from the source**, not left at Unity's defaults: light probe usage, reflection
  probe usage, motion vectors, occlusion, receive-shadows, rendering layer mask.

If it still flashes, `hierarchy.txt` now prints each renderer's scale, material, shader, probe usage, motion
vector mode, shadow mode and lightmap index — **ours and the boat's own, in the same file, to compare** — and
`11. Development / GangwayPlainTimber` builds the whole thing from plain planks of our own with our own material,
which separates "the copied model is wrong" from "the game's shader dislikes what we feed it".

## Gangway: the Karve, fixed

With the section read in the hull's frame, both Karves now report rail 1.24 m and deck drop 0.71 m on **all four**
mounts, and the Longship is symmetric too. Compare the previous run: 0.92, 1.24, 1.56 and 1.80 on the same hull.

## Gangway: narrowing the flashing, round two

The renderer comparison in `hierarchy.txt` did its job and ruled out most of what was suspected:

| | ours | the boat's own |
|---|---|---|
| scale | (1,1,1) | (0.15,0.19,2.68) — **non-uniform, and it does not flash** |
| shader | Custom/Piece | Custom/Piece |
| probes | BlendProbes / BlendProbes | BlendProbes / BlendProbes |
| motion vectors | Object | Object |
| lightmap | -1 | -1 |

So non-uniform scale was **not** the cause (baking it in was still worth doing), shadow acne was not the cause
(it flashed with shadow casting off), and it is not geometry (`coincident pairs: 0`). Everything we could match,
we matched, and it still flashes. Two differences were left, and both are now closed:

- **The material was the game's own asset**, shared with every wood floor in the world. Anything the game sets
  on that shared material — snow, wear, damage — lands on ours too, and unlike a real piece we have no
  `MaterialPropertyBlock` to override it with. Baked copies now get our own instance of the material, made once
  and reused.
- **Our body was not smoothed the way the boat's is.** The plank and the brow carry their own kinematic bodies
  and had `interpolation = None` while `Ship.m_body` has whatever Valheim gives it. A hull interpolated toward
  the next physics step carries our visuals with it as children, while our own body writes its pose only on the
  step itself — the two disagree by a fraction of a frame, every frame, and a normal-mapped surface that shifts
  by a millimetre relights itself completely. They now take the ship's own interpolation setting.

`hierarchy.txt` now also prints each renderer's **layer** and each body's **kinematic/interpolation**, so the
next run shows whether the interpolation actually matches and whether our visuals are on the boat's mesh layer.

## Gangway: the flashing was a building's material on a moving object

Max worked this out: a building piece's material varies itself by **where it stands**, so that two walls side by
side do not look stamped from one mould. That variation is a function of world position — a constant for a
house, and a different number every frame for a boat. The gangway was built from copies of `wood_floor` and wore
`woodwall`, a wall's material, so it re-rolled its own shading every frame as the hull moved under it.

It explains everything the measurements had already ruled out: not geometry (`coincident pairs: 0`), not
non-uniform scale (the boat's own planks have one and are steady), not shadow acne (it flashed with casting
off), not probes or motion vectors (matched, still flashed). A material cannot be diffed against another
material by looking at its name.

`Gangway.ShipTimber(ship)` now takes the material the **hull itself** wears, per hull, and puts it on everything
we build. A ship's material cannot depend on world position, because ships move. `Tame()` additionally switches
any `triplanar local` style property it finds to local space and **logs the shader's full property list**, so if
anything still varies there is a list of names to work from rather than a guess.

`10. Gangway / GangwayShipTimber` turns it off, for comparison.

**Correction:** taking *each* hull's own timber was the tidier idea and it only worked on one of them — the
Longship looked right, the Karve and the Drakkar came out broken. Our planks carry the wooden floor's uvs, and
only the Longship's material reads them as timber. `ShipTimber` now pulls `VikingShip` out of `ZNetScene` once
and uses that material on every hull, falling back to the boat at hand only if the prefab cannot be found. One
material that looks right on all three beats three that do not.

## Gangway: lashed down when stowed

Two rope lashings round the folded bundle, so a stowed gangway looks stowed rather than balanced on the rail.
They reuse what the cleat already had — `CleatPiece.RopeMaterial()` for the world's own rope and
`Models.MeshBuilder.Sweep` for the tube — and are built in the **plank's own frame**, so they sit correctly on
the bundle however it happens to be leaning without any pose maths of their own. They are cast off the instant
the gangway starts to travel: a lashing on a plank swinging out over the side would be a lashing holding
nothing. `RopeMaterial()` returns null before the world's prefabs are up, so the build is retried each frame
until it takes.

## Gangway: `_MoveableObject` is the whole answer

The shader property list that `Tame()` logged settles it:

```
Custom/Piece: _TriplanarFoldout, _TriplanarMap, _TriplanarLocalPos, _TriplanarScale, _ColorFoldout,
_MainTex, _Color, ... _NoiseTex, _ValueNoise, _RippleDistance, _RippleFreq, _ValueNoiseVertex,
_MiscFoldout, _Cull, _AddRain, _AddSnow, _MoveableObject
```

**`_MoveableObject`.** The game has a flag for exactly this. A piece that stands still may vary itself by where
it stands; a piece that moves may not, and Valheim says which is which with that float. It is why `ship_wood`
never flickered and `woodwall` did — nothing to do with the material being "a ship's", only with the flag.

`Tame()` now sets it on every material we use, which means **we are no longer restricted to the hull's timber**:
any material the game has can be worn by something that moves. Forcing the hull's plain timber on everything is
now just an option (`GangwayShipTimber`, off by default).

## Gangway: iron-strapped timber

It costs fine wood and iron nails and used to look like a piece of somebody's floor. It is built from
`darkwood_beam` now — Valheim's iron-banded timber — and `BuildWalkway` lays the piece out **at its own size in
both directions** rather than stretching one of it to fit: a beam stretched to the width of a walkway is a beam
with its ironwork smeared across it. Falls back through `wood_beam` to `wood_floor` if darkwood is not found.

The lashings stand 8.5 cm clear of the bundle instead of 4.5, are twice the thickness, and get their own
instance of the rope material darkened to 55%. Hemp against fresh timber is nearly the same colour, and the same
colour at the same depth is no lashing at all.

**On taking the mesh with the material:** yes, and `CopyVisual` always did — it copies the prefab's meshes *and*
their materials together. The ironwork on a darkwood beam is where it is because the beam's uvs put it there, so
the two cannot be separated. What mattered was the scaling: `BuildWalkway` now tiles the piece at its own size
in both directions (`nx = round(length / src.x)`, `nz = round(width / src.z)`), so each copy is scaled by
something close to 1 and the straps keep their proportions.

`TimberSource()` is now the single place the timber is chosen, used by the walkway, the brow and the brackets
alike, and it falls back to scanning `ZNetScene.m_prefabs` for anything named `darkwood*` in case the specific
names are wrong. It logs what it picked.

The brackets were the worst offender — a 2 m floor squashed to 0.22 x 0.12 x 0.14, which is a beam's ironwork
smeared into a stripe. They are a chunky stub cut from the beam now, near its own cross-section.

**Correction: darkwood was the wrong piece.** What was wanted is the **Wood Iron Beam** (`wood_ibeam`), and only
on the edges. There are two sources now:

- `TimberSource()` — `wood_floor`, the planking you walk on, finished afterwards in the hull's own timber, which
  is the combination that looked right on the water.
- `IronBeamSource()` — `wood_ibeam`, for the edge beams and the rail brackets, keeping its own material.

`SettleMaterials` tells them apart by name (`IsPlanking`: anything under a `deck*` object), so the hull's timber
goes on the planking and the beam keeps its ironwork. `GangwayShipTimber` now means "the hull's timber over the
ironwork too".

The edge beams keep the beam's **own cross-section**, shrunk uniformly if it is thicker than 20 cm, rather than
being squashed to a fixed 10 cm kerb on two axes. Squashing one axis and not the other is what smears straps and
rivets into stripes — the same mistake as the brackets, in a different place.

## Gangway: `piece_woodironbeam`

The log said it plainly — `gangway ironwork cut from wood_beam` — so there was never any ironwork, only plain
timber, and no amount of looking at it would have said why. The name is **`piece_woodironbeam`** (variants `_26`
and `_45`, mesh `woodiron_beam`), found by grepping the game's own bundles under
`valheim_Data/StreamingAssets/SoftRef/Bundles/`. That is the way to settle a prefab name: the bundles are on
disk and `grep -ria` reads them.

The search was also built wrong. `wood_beam` sat in the same list as the names being looked for, so
`FindFirst` succeeded on it and the scene sweep that would have found the right piece never ran. **A fallback
listed beside the thing it is a fallback for is not a fallback.** The ironwork is looked for alone first, then
by sweeping the scene for anything named `*ironbeam*`, and only then does plain timber stand in.

## Gangway: lash what is there, not what was expected

The rope was sized from the leaf thickness — three leaves of 0.22 stacked by the leaf lift — which ignored the
edge beams sitting on top of them, so it ran straight through the ironwork. `BuildLashings` now waits until the
bundle is genuinely folded (`_wasFitted && _deploy <= 0.001`) and measures it: every mesh under the visual, its
eight corners into the plank's own space, encapsulated. The rope is then drawn round *that*, 7 cm proud of it.

**Correction again: `woodiron_beam`.** `piece_woodironbeam` is the *localisation token* — what the build menu
calls it — not the prefab name. Both sit side by side in the bundles and I asked `ZNetScene` for the wrong one,
so it fell through to `wood_beam` a second time. Listing every `woodiron*` string in the bundles settles it:

```
grep -ria -o "woodiron[a-z_0-9]*" valheim_Data/StreamingAssets/SoftRef/Bundles/ | sed 's/.*://' | sort -u
  woodiron_beam   woodiron_pole   woodironbeam   woodironbeam_26   woodironbeam_45
```

The sweep now also matches `woodiron`, and if it finds nothing it **logs every prefab with "iron" in its name**,
so a wrong guess costs a line of log rather than a round of play-testing.

## Gangway: the leaf lift has to be measured too

`coincident pairs` went from 0 to **4** — the brow's two leaves, their edge beams touching. The fold lift was a
fixed 0.17 m, which cleared a bare plank but not a plank with a thick beam down each edge, so folding one leaf
onto the other put the two kerbs in the same place. `MeshHeight()` measures a built leaf and `_leafLift` is set
from it, so the fold clears whatever the leaf actually turned out to be. The stowed collider follows the same
number.

A constant that encodes the size of something else is a constant that goes wrong the moment that thing changes.

## Gangway: a rope round a rectangle

The lashing was an ellipse and the bundle is a rectangle, so it stood off the flats and cut through the corners
— sunk into the timber at four places on every loop. It is a **superellipse** now (`|y|^4 + |z|^4 = 1`, which is
one `sqrt` per axis), so it lies along the flats and turns the corners. 32 segments rather than 20, and the
clearance drops from 7 cm to 5 because it no longer has to bridge the corner.

The rope also went back to the world's own colour. Darkening it to 55% was a guess made to separate it from
fresh timber, and against the finished gangway it only made it a colour nothing else in the game is.

The edge beams come down from 20 cm to 11. Letting them keep the source beam's full cross-section was right in
principle and too much in fact: an edge on a walkway, not a balk of timber laid along it.

## Gangway: a stale config value, not a bad material lookup

`woodiron_beam` was found, the meshes were right, and the beams were still the wrong colour. The survey said why
in one line:

```
edgeR/high  mesh=default_SailTrim/704  mat=ship_wood (SailTrim gangway)/Custom/Piece
```

The iron beam was wearing the *planking's* material. `SettleMaterials` had a `GangwayShipTimber` switch meaning
"the hull's timber over the ironwork too", and the config file on disk still held `GangwayShipTimber = true`
from when that was the default. **Changing a default in code does not change a value already written to a config
file**, so every run since painted the ship timber straight over the ironwork.

The switch is gone. The planking takes the hull's timber, the ironwork keeps its own, and there is no setting to
go stale. A setting nobody needs is a setting that can only ever be wrong.

Worth remembering when a fix "doesn't take": check `BepInEx\config\com.maxst.sailtrim.cfg` before re-reading the
code. The survey's `mat=` field is what caught it.

`coincident pairs` is back to **0** on all six mounts, so the measured leaf lift fixed the brow's leaves.

**Un-reverted:** `Tame()` sets `_TriplanarLocalPos` again alongside `_MoveableObject`. Dropping it was a wrong
guess — the beams were the wrong colour because of the stale config value, not because of that. A triplanar
sampled in world space is the same trap as `_MoveableObject` by another route, so a moving object wants both.

## Gangway: burying itself in the swell

The rest angle was smoothed toward its target with a 0.35 s `SmoothDamp`. The smoothing was added to settle a
noisy ray and it was applied to the wrong quantity: **what the plank rests on is a dock or a rock and holds
still; the thing that moves is the hinge**, a metre at a time on the swell. Smoothing the angle meant the plank
held the angle that suited the last wave, so each time the boat dropped it drove its far end into the beach.

`FindRest` now reports *where* the binding contact is — the ground height and how far out along the plank — and
the median buffer steadies the **ground reading** against ray noise. The angle is then worked out afresh from
wherever the hinge is this frame:

```csharp
want = asin((mount.position.y - ProbeMedian()) / _restDist) * Rad2Deg;
```

No lag on the boat's motion, which is real and must be tracked exactly, and no jitter from the ray, which is
noise and must not be. The overshoot past contact drops from 1 degree to 0.3 now that it no longer has to cover
for the lag.

## Gangway: the shader varies itself two ways, not one

The iron beams flickered while the planking stayed steady, and the log proved both materials had been through
`Tame()` — `_MoveableObject` and `_TriplanarLocalPos` both set. So the triplanar is not the only way
`Custom/Piece` varies itself by where a thing is. Its property list also carries `_ValueNoise`,
`_ValueNoiseVertex`, `_RippleDistance` and `_RippleFreq`, and `_MoveableObject` evidently does not reach them.

`Tame()` turns those off and **logs the value it found**, so the next run says whether this was the difference:
the beam's material should report a non-zero value noise and the hull's timber should report nothing at all,
which is exactly why one flickered and the other did not.

If the log shows nothing turned off, the cause is elsewhere and the next thing to try is `shadowCastingMode`
off on the ironwork alone — the beams carry rivets, which is the sort of geometry that acnes at a grazing angle.

## Gangway: the thin rod beside the bracket

`ScaleInto` returned from inside its loop when it met a mesh it had not baked. Everything before that point was
already scaled and everything after was left at full size, so a stub cut from a two metre beam came out as a
stub **with a two metre rod beside it** — which is what the interaction fittings looked like on the Drakkar.

It checks every mesh before it touches any, and returns a bool. When it declines, the caller falls back to
scaling the transform as before, so a part that cannot be baked is merely un-baked rather than half-sized:

```csharp
bool fitted = Models.ScaleInto(part, scale, ref b);
Models.Place(part, b, anchor, target, fitted ? Vector3.one : scale, rot);
```

All or nothing is the rule for anything that rewrites geometry in place. A partial rewrite leaves something
worse than not doing it at all.

The brackets move from 8 cm inboard of the hinge to 2, and the Karve's mount takes `Inset = -0.07` to sit a hair
further out on its rail.

## Gangway: what you interact with must not be the plank

`hierarchy.txt`, on the Drakkar:

```
SailTrim_Gangway_P @(-5.84,3.60,-0.85)
  plank        @(-5.34,3.93,0.25)  col=BoxCollider
```

The mount is in one place and the collider a metre and ten away. The interaction collider **was** the plank, and
the plank moves: out over the side when deployed, and along the rail to the stow position when not. So the place
to press Use was never where the brackets are. It was also **solid and invisible while nothing was fitted** — a
block on the rail to walk into, which is the second half of the same mistake.

`GangwayHandle` now carries the hover and the interaction, on a collider that sits on the brackets, on
**`piece_nonsolid`** — the layer the game's own ladders and benches use, so it is hoverable and you walk through
it rather than into it. The plank's collider goes back to being only what you stand on, and is **disabled
entirely when nothing is fitted**.

The lesson: the thing you look at and the thing that moves should not be the same collider.

**Also reverted:** `rb.interpolation` back to `None` on our kinematic bodies. Matching the hull was done while
chasing the shading flicker, which turned out to be the material; interpolation makes Unity write the body's own
physics pose over the transform every frame, and that pose never changes because we never call `MovePosition`.

## Gangway: the two folds are not the same fold

A leaf is not symmetrical about its own origin: its planking hangs 0.22 below and its kerbs stand 0.11 above.
Leaf two is turned over when it folds, so it meets **leaf one kerb to kerb** and **leaf three plank to plank**,
and those two gaps need quite different clearances:

```
leaf 1  [-0.22, +0.10]
leaf 2  [lift1 - 0.10, lift1 + 0.22]     turned over
leaf 3  [lift1 + lift2 - 0.22, ... ]     upright again
```

which gives `lift1 >= 2 * hi` (about 0.22) and `lift2 >= -2 * lo` (about 0.46). One figure for both had to be the
larger of them, so the first pair stood a hand's breadth apart for nothing. `MeshSpan` measures a built leaf's
top and bottom and the two lifts are worked out separately.

Measuring the *height* of a thing that folds tells you less than measuring where its top and bottom are.

## Cleat: the boat was being tied to its own gangway

`CleatPiece.HullCollider` accepts any enabled, non-trigger box or convex collider parented to the ship, and that
is how the rope finds the nearest point of the hull to lead to. A lowered gangway is a box collider parented to
the ship that **reaches out toward the dock**, so it is nearer the cleat than the hull is, and the rope was made
fast to it.

`GangwayPart` marks the mount, so everything under it — plank, brow, brackets, treads — answers to
`GetComponentInParent`, and `HullCollider` refuses the lot. It fixes `DistanceToHull` at the same time, which is
what `AutoTie` uses to choose a cleat.

Worth remembering when adding anything else to a ship: several things in this mod ask a boat for "its"
colliders, and they mean the hull.
