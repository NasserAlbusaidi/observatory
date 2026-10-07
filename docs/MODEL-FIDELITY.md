# Models and limits

## Arrival and contact policy

Airless bodies use local surface-relative contact speed. Contact at up to 20 m/s is supported; faster contact destroys the career ship and reloads its save. Sandbox mode explicitly forgives impacts. Landed position and attitude remain fixed relative to the body's modeled rotation, while the pilot can turn; departure inherits its translational and rotational velocity.

Earth, Venus, Mars and Titan retain an assisted surface-arrival abstraction. Gas/ice giants retain assisted cloud-top hold, never a physical ground landing. The same 20 m/s contact rule applies at the model boundary; it is a game arrival constraint, not a pressure or heating calculation. Assistance holds the contact location without consuming fuel. An outward burn or RCS command releases it. There is no atmospheric-flight regime hidden behind the warning.

Assisted arrival counts as docking only at an existing port on that body, using the same contract-completion and autosave rules as airless contact. No new giant ports are added. Unsafe contact uses the existing destruction/reload recovery; dry ships retain the existing tender/port fuel options. Save/load keeps a body-fixed contact point and the saved orientation; older saves derive that point at their saved epoch and adopt the surface velocity. Time compression follows the body analytically while supported and does not waive impact handling during approach.
