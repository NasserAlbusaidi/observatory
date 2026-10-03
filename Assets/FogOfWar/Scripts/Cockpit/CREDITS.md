# Cockpit interior: credits

Everything in the cockpit interior is made in code for this project. It uses no third-party models, textures, fonts or sounds.

- Cabin, couch, console, MFD bezels: `CockpitMesh.cs` (procedural boxes, vertex colours).
- Panel seams and wear: `Resources/Shaders/Cockpit.shader` (procedural, `vnoise` from `FOWCommon.cginc`).
- MFD pages: `CockpitMfd.cs` (UI Toolkit). The pages use the game's existing UI theme and body font.

If you add an outside asset here, it must be CC0. List it in this file with its source URL and licence.
