#!/bin/sh
# Build the black hole review copy and render the offscreen black hole shots: Tools/bh-build-shots.sh <outdir> [extra args]
# BH_NOBUILD=1 skips the build. BH_RUNS="name:args;name:args" renders several runs (into <outdir>/<name>) with one build.
# Holds the shared Unity lock (space sim/UNITY.lock.d) for the whole time: other sessions build and run suites too.
set -e
cd "$(dirname "$0")/.."
OUT="$1"; shift
LOCK="/c/Users/nasse/OneDrive/Desktop/Projects/space sim/UNITY.lock.d"
until mkdir "$LOCK" 2>/dev/null; do
  # stale after 45 minutes
  if [ -f "$LOCK/owner.txt" ] && [ $(( $(date +%s) - $(stat -c %Y "$LOCK/owner.txt") )) -gt 2700 ]; then
    echo "removing stale lock: $(cat "$LOCK/owner.txt")"; rm -rf "$LOCK"; continue
  fi
  sleep 15
done
echo "black hole session (bh-build-shots) $(date -u +%FT%TZ)" > "$LOCK/owner.txt"
trap 'rm -rf "$LOCK"' EXIT
if [ -z "$BH_NOBUILD" ]; then
  "/d/Unity/Editors/6000.6.4f1/Editor/Unity.exe" -batchmode -quit -projectPath "$(pwd -W)" -executeMethod FogOfWar.EditorTools.FogOfWarBhBuild.Build -logFile "$(pwd -W)/Logs/bhbuild.log" || { grep -E "error" Logs/bhbuild.log | head -20; exit 1; }
  grep -i -E "Shader error|error CS" Logs/bhbuild.log | head
fi
run() {
  dir="$1"; shift
  mkdir -p "$dir"
  ./Builds/BlackHole/FogOfWarBH.exe -batchmode -fowbhshots "$dir" -logFile "$(pwd -W)/Logs/bhshots.log" "$@"
  grep -v ICU "$dir/bhshots.txt" | grep -v "^$"
}
if [ -n "$BH_RUNS" ]; then
  echo "$BH_RUNS" | tr ';' '\n' | while IFS=: read -r name args; do
    [ -z "$name" ] && continue
    echo "== $name"
    # shellcheck disable=SC2086
    run "$OUT/$name" $args
  done
else
  run "$OUT" "$@"
fi
