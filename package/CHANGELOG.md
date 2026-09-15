# Changelog

## 1.2.0

- Crew trimming: a passenger with the mod presses B to take the sheet and trims with W/S while the pilot steers. Press B again to let go.
- Passenger readout: everyone aboard with the mod sees the sail icon, speed gauge and trim state.
- Apparent-wind arrow on the wind circle: the wind the sail actually feels, shifted forward by your own speed. Trim to this one.
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
