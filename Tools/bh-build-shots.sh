#!/bin/sh
# Build the black hole review copy and render the offscreen black hole shots: Tools/bh-build-shots.sh <outdir> [extra args]
# BH_NOBUILD=1 skips the build. BH_RUNS="name:args;name:args" renders several runs (into <outdir>/<name>) with one build.
# Holds the shared Unity lock (Tools/unity-env.sh) for the whole time: other sessions build and run suites too.
set -e
cd "$(dirname "$0")/.."
OUT="$1"; shift
. ./Tools/unity-env.sh
unity_lock "bh-build-shots $(pwd -W)"
if [ -z "$BH_NOBUILD" ]; then
  "$UNITY" -batchmode -quit -projectPath "$(pwd -W)" -executeMethod FogOfWar.EditorTools.FogOfWarBhBuild.Build -logFile "$(pwd -W)/Logs/bhbuild.log" || { grep -E "error" Logs/bhbuild.log | head -20; exit 1; }
  grep -i -E "Shader error|error CS" Logs/bhbuild.log | head
fi
run() {
  dir="$1"; shift
  mkdir -p "$dir"
  rc=0
  ./Builds/BlackHole/FogOfWarBH.exe -batchmode -fowbhshots "$dir" -logFile "$(pwd -W)/Logs/bhshots.log" "$@" || rc=$?
  if [ -f "$dir/bhshots.txt" ]; then
    grep -v ICU "$dir/bhshots.txt" | grep -v "^$" || :
  else
    echo "ERROR: missing $dir/bhshots.txt"
    [ "$rc" -ne 0 ] || rc=1
  fi
  echo "player exit $rc"
  return "$rc"
}
if [ -n "$BH_RUNS" ]; then
  status=0
  while IFS=: read -r name args; do
    [ -z "$name" ] && continue
    echo "== $name"
    # shellcheck disable=SC2086
    run "$OUT/$name" $args || status=$?
  done <<EOF
$(printf '%s\n' "$BH_RUNS" | tr ';' '\n')
EOF
  exit "$status"
else
  run "$OUT" "$@"
fi
