# Valheim Sail Trim mod: handoff brief

> **Read `HANDOFF-settings-tab.md` first.** This file is the original brief from before anything was written and
> is kept for the intent behind the sailing model. The handoff file carries the current state: what is released,
> what is still open, how to build, deploy and publish from a fresh machine, and the reasoning behind the parts
> that were hard. As of 2026-09-21 the released version is **1.8.0**, with **1.9.0** built and awaiting a
> play-test, and the mod has grown a cleat, a buoy and a
> gangway on top of the sail trimming described below.


Drop this file into the mod's project folder as `CLAUDE.md` (or paste it as the first prompt in Claude Code).

## Goal

A BepInEx plugin for Valheim 1.0 that adds sail trimming as a real part of sailing. Originally no new items, prefabs, or models; since 1.6.0 (2026-09-19, at Max's request) there is one build piece, the Cleat, built at runtime from primitives with the game's bronze material (still no asset bundle; see `Cleat.cs`). Everyone on the private server installs it (client-side mod, sheet angle synced through the ship's ZDO so other players see the yard move).

## Controls (on the rudder)

- Tap E: raise sail one step (what W does in vanilla: Stop > Slow > Half > Full).
- Hold E about 0.5 s: release the helm (vanilla E behaviour). Tap must not also release.
- Q: lower sail one step (what S does in vanilla, including reverse/rowing).
- W held: ease the sheet out continuously (yard swings out at a configurable deg/s).
- S held: sheet in continuously.
- A/D: rudder, unchanged.
- All keys and rates in BepInEx config.

## Physics intent

Sheet angle is the yard's angle relative to the hull centerline (0 = hauled fully in, ~90 = fully eased). Compare it to the apparent wind angle to get an angle of attack, then replace the vanilla sail force with a curve:

- Luffing (sheet eased past the wind, AoA near 0 or negative): near zero drive, sail flaps (drive the cloth/yard with a small oscillation).
- Sweet spot (roughly 15 to 30 deg AoA): full drive, faster than vanilla auto-trim.
- Over-sheeted (AoA past ~45 deg, sail stalled): drive drops off but stays positive, drag rises, and heel increases a lot. Boat must remain sailable when badly over- or under-sheeted, just slow.

Vanilla `Ship` already computes sail force from the wind angle and already rotates the mast/sail object toward the wind every frame in `UpdateSail`; the mod takes that over with the manual sheet angle.

## Implementation notes

- Project: net462 class library, references BepInEx core, 0Harmony, `assembly_valheim.dll`, `UnityEngine.*` from `valheim_Data/Managed`. Publicize assembly_valheim (BepInEx.AssemblyPublicizer or Krafs.Publicizer) so private fields are reachable.
- First step: decompile `Ship`, `ShipControlls`, and `ZInput` usage from the 1.0 assembly (ilspycmd) and patch the real signatures. Do not trust older versions of `GetSailForce` / `ApplyControlls`; they have changed across updates.
- Patches: `ShipControlls` input handling for the key remap and hold-E timer; `Ship.GetSailForce` (or wherever 1.0 computes sail force) for the trim curve; `Ship.FixedUpdate` or the heel/roll section for extra heel when stalled; `Ship.UpdateSail` for yard rotation and luff flapping.
- Sync: store the sheet angle in the ship's ZDO (e.g. `zdo.Set("sailtrim_sheet", angle)`) from the pilot, read it on other clients for visuals. Physics runs on the ZDO owner only.
- Verify with a reflection check that every Harmony patch target exists in the loaded assembly before shipping.

## Deliverables

- `SailTrim.dll` for `BepInEx/plugins` on every client.
- Short install/config notes for the friends on the server.
