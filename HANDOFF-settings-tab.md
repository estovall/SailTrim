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
