# Pick-up notes: in-game settings tab (branch `settings-tab`)

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
