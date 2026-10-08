#!/bin/sh
# Build the Windows game and run the unattended suites in order: Tools/qa.sh <outdir> [suite ...]
# Suites: career legacy uishots qa (the default set, the one a PR reports), then audio tour physics regression gpu.
# Each suite's process exits 0 when clean, 1 on failed checks or logged errors, 2 when it did not finish.
# The script stops at the first nonzero exit and returns it. Reports land in <outdir>/<suite>/.
# QA_NOBUILD=1 reuses the last build. QA_EXE=<path> runs another player (default Builds/Windows/FogOfWar.exe).
# QA_UIRES=<w>x<h>,... sets the UI shot sizes. UNITY=<path> names the editor (default from ProjectVersion.txt).
# Holds the shared Unity lock (space sim/UNITY.lock.d) for the whole run: other sessions build and run suites too.
set -u
cd "$(dirname "$0")/.."
OUT="${1:?usage: Tools/qa.sh <outdir> [suite ...]}"; shift
SUITES="${*:-career legacy uishots qa}"
PROJ="$(pwd -W)"
VER="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt | tr -d '\r')"
UNITY="${UNITY:-/d/Unity/Editors/$VER/Editor/Unity.exe}"
EXE="${QA_EXE:-./Builds/Windows/FogOfWar.exe}"
UIRES="${QA_UIRES:-1920x1080,1440x2560}"
LOCK="/c/Users/nasse/OneDrive/Desktop/Projects/space sim/UNITY.lock.d"
mkdir -p "$OUT" Logs

until mkdir "$LOCK" 2>/dev/null; do
  # stale after 45 minutes
  if [ -f "$LOCK/owner.txt" ] && [ $(( $(date +%s) - $(stat -c %Y "$LOCK/owner.txt") )) -gt 2700 ]; then
    echo "removing stale lock: $(cat "$LOCK/owner.txt")"; rm -rf "$LOCK"; continue
  fi
  sleep 15
done
echo "qa.sh $PROJ $(date -u +%FT%TZ)" > "$LOCK/owner.txt"
trap 'rm -rf "$LOCK"' EXIT

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
    career)     player career  career.txt  -fowcareer ;;
    legacy)     player legacy  career.txt  -fowcareer -fowlegacyui ;;
    uishots)    player uishots uishots.txt "-batchmode -fowuishots" -fowuires "$UIRES" ;;
    qa)         player qa      qa.txt      -fowqa ;;
    audio)      player audio   audioqa.txt -fowaudioqa ;;
    tour)       player tour    tour.txt    -fowtour ;;
    physics)    editor physics    FogOfWarPhysicsCheck.Run    Logs/physics-benchmarks.json -nographics ;;
    regression) editor regression FogOfWarRegressionCheck.Run Logs/issue-regressions.txt   -nographics ;;
    gpu)        editor gpu        FogOfWarGpuCheck.Run        Logs/gpu-benchmarks.json     -force-d3d11 ;;
    *) echo "unknown suite: $s"; exit 2 ;;
  esac
done
echo "== all suites passed $(date +%H:%M)$SUMMARY"
