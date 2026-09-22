# Changelog

## 1.10.0

- **Boats carry their way off instead of stopping dead.** Step off a boat under sail and vanilla takes nine tenths of her speed every physics step, which at fifty steps a second is a wall; she now loses her way evenly over `EmptyCoast` (2.5 seconds) and comes to rest like a boat rather than like a boat hitting one. The same when a line goes on or a gangway goes down: she settles over `MooringSettle` rather than stopping where she stands, and is held at the spot she was made fast at, not wherever she drifted to.
- **A gangway stays where it was set down.** It used to feel for the ground again every frame and take whatever it found, so half a metre of swell had the tip reading the top of a beam one frame and the ground beside it the next, and the plank climbed in and out of the timber. It now takes the spot once, as it goes down, and keeps it in the frame of whatever it landed on: a dock stays put, another boat's rail rides with that boat, and only the angle needed to reach it changes as the hull works.
- **Tying up and lowering a gangway want her nearly still** (`MooringMaxSpeed`, 3.5 knots). Both stop the boat, and a plank aimed from a moving boat is aimed at something it will no longer be over.
- **Moorings survive a restart.** A boat tied to a cleat came back untied, while a gangway came back as it was left. A ZDOID does not survive a world load -- the game hands every object a fresh one -- so anything saved that points at another object by ZDOID points at nothing when the world returns, and the cleat and the boat each held the other's. Both ends now also keep a tag that does survive, and the link is put back from it. Boats lashed alongside keep their raft the same way.
- **The HUD can be arranged to suit whatever else is on your screen.** In the SailTrim settings page: pick a piece (or "everything"), then move and size it with sliders while you watch it move behind the page, which goes see-through while you are there. OK keeps it, Back puts it as it was, and there is a Reset. For playing beside mods that move or enlarge the ship's dials.
- Fixed: a gangway swinging out could throw another boat on its beam ends. It only spared the boat it had lashed to, which left the shove in place on the way out, on a boat it landed on without lashing, and on every boat when `GangwayLashShips` was off.
- Fixed: a cleat made its rope fast to the boat's own gangway. A lowered plank reaches out towards the dock, so it was nearer the cleat than the hull was.
- Fixed: the place to interact with a gangway was the plank itself, which moves, so it was never where the brackets are; and it was solid and invisible while nothing was fitted, which put a block on the rail to walk into. The brackets carry it now, on the layer the game's own ladders use, so you walk through it rather than into it.
- Fixed: the gangway's ironwork was plain timber, its planks fought with themselves for every pixel, and its folded leaves stood further apart than they needed to.
- A boat with no gangway fitted no longer builds one. Every hull but the raft carries the brackets, and they were building the walkway, the brow and all their meshes whether anyone had crafted a gangway or not.

## 1.9.0

- A gangway can come down on another boat. If the plank reaches a deck rather than the shore, it lands on it, and the two boats are then lashed alongside: both hold where they lie and both stop, exactly as a gangway onto a dock holds the one boat. Walk across, load the other hold, raft up for the night. Taking the helm of either boat raises the plank and frees them both. `GangwayLashShips` turns it off.

## 1.8.0

- **The Gangway.** A plank hinged at the rail that you walk over between the deck and the dock, so a load of metal no longer has to be climbed over the side, encumbered you cannot jump at all, which is the whole reason it exists. Craft one at the workbench (10 fine wood, 4 iron nails), so it arrives about when the longship does, and fit it to the brackets on the rail of any hull but the raft, one to a side; it stays with the boat. It is three boards hinged end to end, planked in the hull's own timber and strapped with iron beams: stowed they fold back on one another and stand lashed against the rail, and lowering unfolds them, swings the plank out over the side and sets it down on whatever is under it, dock, shore, log, or a beam halfway out, and keeps it there as the boat works in the swell. An inboard ramp swings down off the rail with it so you can walk up from the deck, and folds away inside the planking when it is not wanted, taking no deck at all.
- While the gangway is down the boat holds its spot the way a moored boat does, though unlike a cleat it does not mend the hull. Lowering it within reach of a cleat with no boat on it ties that cleat on as well, so one press does the dock. You work it from on board, and taking the helm raises it after two seconds.
- Every hull is measured rather than guessed at: the mount follows the boat's own boarding ladder, and the rail, the deck beside it and the height of the climb are read off a section taken across the beam in the hull's own frame, so the fitting sits properly on a Karve, a longship and a Drakkar alike.

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
