# Changelog

## Unreleased (branch gangway)

- The Gangway: a plank hinged at the rail that you walk over between the deck and the dock, so a load of metal no longer has to be climbed over the side. Craft one at the workbench (10 fine wood, 4 iron nails), so it comes in about when the longship does and interact with the rail of any hull but the raft to fit it, one to a side; it stays with the boat. Stowed it lies flat along the rail; lowering swings it out over the side and down onto whatever is under it, dock, shore or log, and keeps resting there as the boat works in the swell. While it is down the boat holds its spot the way a moored boat does, though unlike a cleat it does not mend the hull, and lowering it within reach of a cleat with no boat on it ties that cleat on as well. You work it from on board. Taking the helm raises it after two seconds.

## 1.7.1

- Fixed: stepping off a boat furled the sail to look at but not in fact. The mod kept its own sail amount, so an empty boat sat there apparently furled and then sailed off the moment you stepped back aboard, with the cloth snapping back to where it had been. An empty boat now furls for real and has to be set again, as in vanilla. Loading cargo is no longer a chase.

## 1.7.0

- Crew weight. Anyone standing on deck shifts the boat: weight to windward stands her up, weight forward puts the bow down. Sitting crew, anyone holding the mast and the helmsman do not count, since they are braced and already part of the hull. Nothing is scaled by hand for ship size; a body that visibly stands a karve up barely troubles a longship, because the longship's inertia says so. A boat sailing flatter loses less drive, so a crew that works the rail is faster than the same boat sailed alone, and faster than vanilla.
- Crew on deck are told which rail to stand on while the boat is over, and told when they are already in the right place (`CrewWeightHints`). `CrewWeight` and `CrewMass` are in the Heel section and the server owns them.

## 1.6.2

- Fixed: boats came untied when the world was reloaded. Both the cleat and the boat took "the other one's data is not here yet" to mean it had gone, and in the first seconds after a world loads nothing is here yet. Neither side lets go now until it actually hears otherwise, and a cleat that has really been broken releases its boat after half a minute.

## 1.6.1

- Fixed: a buoy broke the moment it was placed. It counted as a piece with nothing holding it up, and the game wears those away at once. Buoys and cleats now take no damage at all: not from wear, rain or ash, not from a ship ramming them, and not from waves setting a buoy down on the sea floor. Remove them with the hammer as usual.
- The server needs 1.6.1 too (the version check wants the same version everywhere).

## 1.6.0

- The Cleat: a bronze horn cleat for the dock, under the hammer's Misc tab (one bronze). It sits on top of planks and floors only and does not snap into beams. With a boat within 10 m (`CleatRange`), press E to tie it up: the rope is made fast round the horn with a cleat hitch and runs to the nearest point of the hull, hanging and draping over the dock edge instead of passing through it. A tied boat holds its spot and heading the way an empty boat does in vanilla, crew aboard or not, and creatures and waves cannot shove it away (`MooringHold`). Press E again to untie, or take the helm: after a second (`CastOffDelay`) the boat casts off by itself. If the boat sinks or the cleat is broken, the other side lets go.
- A tied-up boat mends itself, a few percent of its health per minute (`MoorRepairPerMinute`, 5), so the dock knocking it about between tides costs nothing.
- The Buoy: a barrel float with a staff, a banner and a lantern, put together from the game's own barrel, pole, banner and lantern. Hammer, Misc tab, 6 wood and 2 resin, no workbench. Place it on open water like a boat; it floats on the waves and keeps the spot it was set at, and its lantern glows at night. It stays in the world out to the game's distant area, hundreds of metres. Press E to change the banner's colour (red, green, yellow, white, blue, orange, black); every loaded buoy shows on the map as a disc in its colour (`BuoyPins`). A boat that hits one shoves it aside for a moment; it works its way back.
- Both pieces have icons rendered from their models.
- The sailing keys stay on screen left of the speed gauge while you are at the helm in manual trim, as currently bound (`ShowControls`, on; also a toggle in Settings > SailTrim). The one-time key hint under the HUD steps aside when the list is on.

## 1.5.0

- Tack animation (visual only, sailing performance unchanged): when the yard changes sides the sail's tension is let go and its foot streams out downwind, the yard sweeps round through square, and the sail is tensioned again. The yard can no longer turn the wrong way round or snap between two equivalent positions.
- The sail itself luffs: the cloth ripples when luffing, and the yard no longer shakes. Hauled in hard, a drawing sail looks taut with a fair curve; eased out it moves freely.
- Fixed: crew trimming worked from the benches near the mast on the Karve and from the longship's bow hold-fast. Only the hold-fast at the mast takes the sheet now.

## 1.4.0

- Square-rig trim: the force curve is now a low-aspect square sail (lift peaks near 30-35 deg and falls off gently), the yard wants roughly half the apparent wind angle instead of a jib-like 22 deg angle of attack, and abaft about 110 deg the sail is a drag device: running with the yard square is "Trimmed", never "Stalled". The trim hint is computed from the force curves themselves, so it always matches the physics. Pointing ability and the 0-90 sheet range are unchanged on purpose.
- Reef to any amount: hold E (or RaiseSailKey) to let sail out, hold Q to take it in, from furled to full in about 4 s (`SailSetRate`). The HUD shows the sail percentage. Vanilla's Half/Full steps are gone in manual mode.
- Rowing: Shift rows forward, Ctrl rows astern (hold, or toggle with `RowKeysToggle`). Rowing is refused while any sail is set; holding a row key for a second (`StowHoldTime`) stows the sail first, then rowing starts. Letting go of the helm is Jump, like vanilla; E no longer releases it.
- `RudderSelfCenter` (off by default): the rudder drifts back to centre when you are not steering.
- Settings tab: new rows for the row keys and the two options, plus a read-only list of the game's own keys (let out sail, sheet, steer, let go) as currently bound.

## 1.3.0

- SailTrim tab in the game's Settings menu (main menu and pause menu): rebind the manual-trim, lower/raise sail, ease and sheet-in keys, and flip the W/S-trim and invert-sheet toggles, without editing the config file. Physics and server settings stay out of the menu.
- Fixed: the trim HUD stayed hidden (vanilla ship UI only) after logging out to the main menu and joining again, until the game was restarted. The HUD is now rebuilt for the new session, and a HUD build failure is retried instead of being remembered all session.

## 1.2.1

- Fixed: with manual trim on, tapping E at the rudder let go of the helm instead of raising the sail whenever Jotunn was installed (single-player and servers alike). Jotunn re-evaluates the Use button after SailTrim swallows it; SailTrim now re-applies the swallow after Jotunn. Tap E = raise, hold E = release works again.

## 1.2.0

- Crew trimming: a passenger with the mod uses the ship's "Hold fast" spot on the mast to take the sheet and trims with W/S while the pilot steers, 20% faster than the pilot could alone. Jump to let go. The pilot can trim at the same time; nobody else aboard can.
- Passenger readout: everyone aboard with the mod sees the ship HUD with the sail icon, speed gauge and trim state.
- Mast strain: heeling past 35 degrees for more than a few seconds damages the hull until you ease out or reef.
- Wind shadow: land upwind takes some wind away (at most 40%, so rivers and fjords stay sailable).
- Downwind rolling: running near dead downwind with the sail up rolls the boat rhythmically; head up a little or reef to settle it.

## 1.1.1

- The pilot's client now takes ownership of the ship when they hold the rudder with manual trim on (`PilotOwnsShip`), so the boat always sails by the pilot's trim even if a passenger without the mod boarded first. Passengers without the mod see the boat's motion correctly; only their yard angle is drawn vanilla-style.

## 1.1.0

- Server support. Install on the dedicated server (or the hosting player) as well as clients.
- `Enforcement` server setting: Off, Warn (default: players without a matching SailTrim can join but see a warning) or Require (they are refused with an incompatible-version message).
- `LockConfig` server setting (default on): the server's physics, heel, gust, pitch and hull-speed settings are pushed to every client, so the whole server sails by the same rules. Keys, HUD, camera and the H opt-in stay personal.
- Camera tilt setting, per-hull speed gauge, first-time key hint, vanilla sailing as the first-run default.

## 1.0.0

Initial release.

- Manual sheet control at the rudder: W sheets in, S eases, tap E raises sail, Q lowers, hold E lets go.
- Lift/drag sail model with luffing, stall, aback and a vanilla no-go zone.
- Heel, weather helm, leeway and bow-down trim from the sail forces, balanced by the hull's own buoyancy.
- Rare gusts and lulls, per-hull speed limits, speed gauge in knots and a trim readout on the ship HUD.
- Vanilla sailing is the default; press H at the helm to switch to manual trim (saved per player).
