# Shared by the Tools/*.sh scripts: where the Unity editor is, and the lock that keeps one build or suite running at a
# time on this machine. Source it from the repository root.
#   UNITY           the editor exe. Default: $UNITY_EDITORS/<ProjectVersion>/Editor/Unity.exe
#   UNITY_EDITORS   the Unity Hub editors folder. Default: C:/Program Files/Unity/Hub/Editor
#   FOW_UNITY_LOCK  the lock directory. Default: /tmp/fow-unity.lock.d. Put it where every checkout on the machine sees it.
# Set them in the environment, or in Tools/unity.local (gitignored), one `NAME=value` a line. The environment wins.
env_unity="${UNITY:-}"; env_editors="${UNITY_EDITORS:-}"; env_lock="${FOW_UNITY_LOCK:-}"
[ -f Tools/unity.local ] && . ./Tools/unity.local
VER="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt | tr -d '\r')"
UNITY="${env_unity:-${UNITY:-${env_editors:-${UNITY_EDITORS:-/c/Program Files/Unity/Hub/Editor}}/$VER/Editor/Unity.exe}}"
[ -x "$UNITY" ] || { echo "no Unity $VER at $UNITY: set UNITY or UNITY_EDITORS (see Tools/unity-env.sh)"; exit 1; }
LOCK="${env_lock:-${FOW_UNITY_LOCK:-${TMPDIR:-/tmp}/fow-unity.lock.d}}"

# unity_lock <owner>: wait for the lock, take it, release it on exit. A lock older than 45 minutes is stale.
unity_lock() {
  until mkdir "$LOCK" 2>/dev/null; do
    if [ -f "$LOCK/owner.txt" ] && [ $(( $(date +%s) - $(stat -c %Y "$LOCK/owner.txt") )) -gt 2700 ]; then
      echo "removing stale lock: $(cat "$LOCK/owner.txt")"; rm -rf "$LOCK"; continue
    fi
    sleep 15
  done
  echo "$1 $(date -u +%FT%TZ)" > "$LOCK/owner.txt"
  trap 'rm -rf "$LOCK"' EXIT
}
