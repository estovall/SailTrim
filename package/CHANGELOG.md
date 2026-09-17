# Changelog

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
