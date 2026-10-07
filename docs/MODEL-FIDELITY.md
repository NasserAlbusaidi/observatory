# Models and limits

## Spacecraft decisions

These assumptions are retained. Changing one requires a separate mechanics change with planner, economy, cargo and low/zero-fuel checks.

- **Acceleration regulation:** the selected g level commands acceleration, not a fixed force divided by current mass. The drive is assumed to have sufficient thrust reserve for the ship's current dry mass, cargo and propellant. More cargo does not reduce commanded acceleration; it increases the force required and the propellant needed for a given delta-v. As propellant is spent, required force decreases. Fuel follows the ideal rocket equation with the current total mass.
- **Shared RCS technology:** translational RCS deliberately uses the same effective exhaust velocity as the torch drive, `10,000 km/s`. Its acceleration limit is `0.1 g`. Main thrust and RCS both consume reaction mass; the model is not a chemical-RCS system.
- **Electric attitude control:** rotation uses reaction wheels and consumes no reaction mass. Fixed angular acceleration and rate limits remain `1.5 rad/s²` and `0.6 rad/s`. Wheel momentum capacity, saturation/desaturation, electrical draw and cargo-dependent moments of inertia are omitted. A dry ship can still turn.
- **Power and heat:** reactor power, exhaust energy supply, radiator temperature/area and waste-heat rejection impose no limits in the game. The exhaust-power readout uses the ideal nonrelativistic relation `P = ½ m a vₑ`, with mass in kg, applied main-drive acceleration in m/s² and exhaust velocity in m/s, displayed in TW. This is exhaust kinetic power, not onboard waste heat; the latter needs an efficiency and energy-partition model that this game does not supply. No engine feasibility or numerical waste-heat claim is made.
- **High g:** sustained crew tolerance is fictionalized; passenger contracts enforce a gameplay comfort limit. No human physiological simulation is claimed.

## Arrival and contact policy

Airless bodies use local surface-relative contact speed. Contact at up to 20 m/s is supported; faster contact destroys the career ship and reloads its save. Sandbox mode explicitly forgives impacts. Landed position and attitude remain fixed relative to the body's modeled rotation, while the pilot can turn; departure inherits its translational and rotational velocity.

Earth, Venus, Mars and Titan retain an assisted surface-arrival abstraction. Gas/ice giants retain assisted cloud-top hold, never a physical ground landing. The same 20 m/s contact rule applies at the model boundary; it is a game arrival constraint, not a pressure or heating calculation. Assistance holds the contact location without consuming fuel. An outward burn or RCS command releases it. There is no atmospheric-flight regime hidden behind the warning.

Assisted arrival counts as docking only at an existing port on that body, using the same contract-completion and autosave rules as airless contact. No new giant ports are added. Unsafe contact uses the existing destruction/reload recovery; dry ships retain the existing tender/port fuel options. Save/load keeps a body-fixed contact point and the saved orientation; older saves derive that point at their saved epoch and adopt the surface velocity. Time compression follows the body analytically while supported and does not waive impact handling during approach.
