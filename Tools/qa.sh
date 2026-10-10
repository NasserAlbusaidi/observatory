#!/bin/sh
# Build the Windows game and run the unattended suites in order: Tools/qa.sh <outdir> [suite ...]
# Suites: uishots qa (the default set, the one a PR reports), then tour bhshots physics gpu.
# Each suite's process exits 0 when clean, 1 on failed checks or logged errors, 2 when it did not finish.
# The script stops at the first nonzero exit and returns it. Reports land in <outdir>/<suite>/.
# QA_NOBUILD=1 reuses the last build. QA_EXE=<path> runs another player (default Builds/Windows/FogOfWar.exe).
# QA_UIRES=<w>x<h>,... sets the UI shot sizes. The editor and the lock come from Tools/unity-env.sh.
# Holds the shared Unity lock for the whole run: other sessions build and run suites too.
set -u
cd "$(dirname "$0")/.."
OUT="${1:?usage: Tools/qa.sh <outdir> [suite ...]}"; shift
SUITES="${*:-uishots qa}"
PROJ="$(pwd -W)"
EXE="${QA_EXE:-./Builds/Windows/FogOfWar.exe}"
UIRES="${QA_UIRES:-1920x1080,1440x2560}"
. ./Tools/unity-env.sh
mkdir -p "$OUT" Logs
unity_lock "qa.sh $PROJ"

if [ -z "${QA_NOBUILD:-}" ]; then
  echo "== build $(date +%H:%M)"
  "$UNITY" -batchmode -quit -projectPath "$PROJ" -executeMethod FogOfWar.EditorTools.FogOfWarBuild.BuildWindows -logFile "$PROJ/Logs/qa-build.log"
  code=$?
  grep -E "error CS|Build Finished|Fog of War: .*build" Logs/qa-build.log | head -20
  if [ $code -ne 0 ] || grep -q "error CS" Logs/qa-build.log; then echo "BUILD FAILED (exit $code): Logs/qa-build.log"; exit 1; fi
fi

SUMMARY=""
finish() { # name code report
  line="$(grep -E '^done|AUDIO QA|failed checks' "$3" 2>/dev/null | tail -1)"
  SUMMARY="$SUMMARY
$1: exit $2  ${line:-no report}"
  if [ "$2" -ne 0 ]; then
    echo "FAILED $1 (exit $2): $3"
    grep -E 'FAIL|TIMEOUT|ERROR|\[Error\]|\[Exception\]|aborted' "$3" 2>/dev/null | grep -v 'PERF PASS' | head -20
    echo "$SUMMARY"
    exit "$2"
  fi
}

player() { # name report flag [extra args]: the player writes its report to <outdir>/<name>/
  name=$1; report=$2; flag=$3; shift 3
  dir="$OUT/$name"; mkdir -p "$dir"
  echo "== $name $(date +%H:%M)"
  # the player prints only its boot config to stdout; the report is the file
  "$EXE" $flag "$(cygpath -w "$dir")" "$@" -logFile "$(cygpath -w "$dir/player.log")" > /dev/null
  finish "$name" $? "$dir/$report"
}

editor() { # name method report [extra args]: an editor check, batch mode, writes under Logs/
  name=$1; method=$2; report=$3; shift 3
  echo "== $name $(date +%H:%M)"
  "$UNITY" -batchmode "$@" -projectPath "$PROJ" -executeMethod "FogOfWar.EditorTools.$method" -logFile "$PROJ/Logs/qa-$name.log"
  finish "$name" $? "$report"
}

for s in $SUITES; do
  case $s in
    uishots)    player uishots uishots.txt "-batchmode -fowuishots" -fowuires "$UIRES" ;;
    qa)         player qa      qa.txt      -fowqa ;;
    tour)       player tour    tour.txt    -fowtour ;;
    bhshots)    player bhshots bhshots.txt "-batchmode -fowbhshots" ;;
    physics)    editor physics    FogOfWarPhysicsCheck.Run    Logs/physics-benchmarks.json -nographics ;;
    gpu)        editor gpu        FogOfWarGpuCheck.Run        Logs/gpu-benchmarks.json     -force-d3d11 ;;
    *) echo "unknown suite: $s"; exit 2 ;;
  esac
done
echo "== all suites passed $(date +%H:%M)$SUMMARY"
