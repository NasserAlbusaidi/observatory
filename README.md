<p align="center">
  <img src="docs/media/blackhole.webp" width="100%" alt="Skimming the accretion disc of a ray-traced black hole">
</p>

<p align="center">
  <img alt="Platform: Windows" src="https://img.shields.io/badge/PLATFORM-WINDOWS-FFB547?style=for-the-badge&labelColor=0b0d14">
  <img alt="Engine: Unity 6" src="https://img.shields.io/badge/ENGINE-UNITY%206-FFB547?style=for-the-badge&labelColor=0b0d14">
  <img alt="Code licence: GPL-3.0" src="https://img.shields.io/badge/CODE-GPL--3.0-FFB547?style=for-the-badge&labelColor=0b0d14">
  <img alt="Built with AI" src="https://img.shields.io/badge/BUILT%20WITH-AI-FF5C5C?style=for-the-badge&labelColor=0b0d14">
</p>

## ⚠️ BUILT WITH AI, NOT BY HAND

> [!WARNING]
> **I did not write this by hand.** I made this project with heavy use of AI coding assistants, including Claude Code and OpenAI Codex. Read the code and the docs with that in mind.
>
> This is a hobby project. I built it to play with physics and learn more of it. It's not for sale and I don't distribute builds. I share the source for anyone with the same interest: clone it, build it, explore it.

## OBSERVATORY

**Eight interactive physics models**, each with its assumptions explained.

<table>
<tr>
<td align="center" width="25%"><img src="docs/media/obs-1-wormhole.jpg"><br><b>WORMHOLE</b><br><sub>Fly through to another sky</sub></td>
<td align="center" width="25%"><img src="docs/media/obs-2-blackhole.jpg"><br><b>BLACK HOLE</b><br><sub>Lensing, spin, tidal disruption</sub></td>
<td align="center" width="25%"><img src="docs/media/obs-3-warp.jpg"><br><b>WARP BUBBLE</b><br><sub>Shrink space ahead, stretch it behind</sub></td>
<td align="center" width="25%"><img src="docs/media/obs-4-journey.jpg"><br><b>1G TO ANYWHERE</b><br><sub>Cross the galaxy in 24 ship-years</sub></td>
</tr>
<tr>
<td align="center"><img src="docs/media/obs-5-negmass.jpg"><br><b>NEGATIVE MASS</b><br><sub>Free acceleration forever</sub></td>
<td align="center"><img src="docs/media/obs-6-tides.jpg"><br><b>MOON 1,000 KM CLOSER</b><br><sub>Tides, month length, Roche limit</sub></td>
<td align="center"><img src="docs/media/obs-7-mars.jpg"><br><b>TERRAFORM MARS</b><br><sub>The average is a liar</sub></td>
<td align="center"><img src="docs/media/obs-8-meteors.jpg"><br><b>RADIO METEORS</b><br><sub>Catch meteor echoes with an SDR from Muscat</sub></td>
</tr>
</table>

<p align="center"><sub>The black hole uses geodesic-based lensing with numerical tracing and lens tables. Moving emission uses snapshot time; the disk is an idealized model. Each scene's limits are in <a href="docs/MODEL-FIDELITY.md">Models and limits</a>.</sub></p>

## ORIGIN

These scenes started in the game [Fog of War](https://github.com/NasserAlbusaidi/fog-of-war-observatory) and moved here with their full git history on 2026-10-10 ([the decision](https://github.com/NasserAlbusaidi/fog-of-war-observatory/blob/main/docs/adr/0001-open-world-game.md)). The code that both projects used was copied; the two copies now change apart.

## SYSTEM REQUIREMENTS

<table>
<tr><td><b>OS</b></td><td>Windows 10 (21H1 or newer) or 11, 64-bit</td></tr>
<tr><td><b>Tested on</b></td><td>AMD Ryzen 9 7900X · 32 GB RAM · NVIDIA GeForce RTX 4080</td></tr>
<tr><td><b>Minimum</b></td><td>Not measured yet. The ray-traced scenes have Fast, Balanced and Sharp quality.</td></tr>
</table>

<details>
<summary><b>CONTROLS</b></summary>

<br>

<table>
<tr><td><b>Pick a scene</b></td><td>1–8</td></tr>
<tr><td><b>Orbit · Zoom</b></td><td>Drag · Scroll</td></tr>
<tr><td><b>Photo mode · Clip · Screenshot</b></td><td>F10 · F9 · F12</td></tr>
<tr><td><b>Hide panels · Full screen</b></td><td>F2 or H · F11</td></tr>
<tr><td><b>All controls</b></td><td>F1</td></tr>
<tr><td><b>Menu</b></td><td>Esc</td></tr>
</table>

Rebind keys in **Esc → Settings → Controls**. 1–8, Esc, F2, H, F11 and the photo-mode camera keys are fixed.

</details>

## BUILD IT

There's no download and no store build. Build it from source:

1. Install Unity **6000.6.4f1** with Windows Build Support (Mono).
2. Clone the repo: `git clone https://github.com/NasserAlbusaidi/observatory.git`
3. Open the folder in Unity. The first import takes a few minutes.
4. To try it in the editor: **Fog of War → 1. Create and open the sim scene**, then press Play.
5. To build: **Fog of War → 2. Build Windows game**. The program lands in `Builds/Windows/FogOfWar.exe`.

The menu and the program still carry the old "Fog of War" name. A later change renames them.

## TEST IT

`Tools/qa.sh <dir>` builds the program and runs the menu shots (`uishots`) and the scene checks (`qa`). Add `tour`, `bhshots`, `physics` or `gpu` for the scene tour, the black hole shots, the geodesic checks and the GPU-against-reference check.

Full steps, every test flag and the project layout are in the **[developer guide](docs/DEVELOPING.md)**.

---

<sub>
Maps: NASA Visible Earth, NASA's Scientific Visualization Studio, USGS Astrogeology and PDS. Stars: Gaia DR2 (ESA/Gaia/DPAC) and the Yale Bright Star Catalogue. Fonts: Barlow and JetBrains Mono (SIL OFL). Full credits are in the <a href="docs/DEVELOPING.md#credits-and-licence">developer guide</a>.<br><br>
Code © 2026 Nasser Albusaidi, licensed under the <a href="LICENSE">GNU General Public License v3.0</a>, with an additional permission to link with Unity. The project's own art, models and text: © 2026 Nasser Albusaidi, licensed under <a href="https://creativecommons.org/licenses/by/4.0/">CC BY 4.0</a>. Third-party maps, data and fonts keep their own licences, and the Gaia-based sky is for non-commercial use only. Details are in the <a href="docs/DEVELOPING.md#credits-and-licence">developer guide</a>.
</sub>
