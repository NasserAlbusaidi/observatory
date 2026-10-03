#!/bin/sh
# Build the black hole review copy and render the offscreen black hole shots: Tools/bh-build-shots.sh <outdir> [extra args]
set -e
cd "$(dirname "$0")/.."
OUT="$1"; shift
"/d/Unity/Editors/6000.6.4f1/Editor/Unity.exe" -batchmode -quit -projectPath "$(pwd -W)" -executeMethod FogOfWar.EditorTools.FogOfWarBhBuild.Build -logFile "$(pwd -W)/Logs/bhbuild.log" || { grep -E "error" Logs/bhbuild.log | head -20; exit 1; }
grep -i -E "Shader error" Logs/bhbuild.log | head
mkdir -p "$OUT"
./Builds/BlackHole/FogOfWarBH.exe -batchmode -fowbhshots "$OUT" -logFile "$(pwd -W)/Logs/bhshots.log" "$@"
cat "$OUT/bhshots.txt" | grep -v ICU
