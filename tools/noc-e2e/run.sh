#!/usr/bin/env bash
# T21 orchestration (CI only, nightly workflow noc-e2e.yml). Run from the repository root.
#
#   run.sh load  <server_dir> <label> <clients> <ramp_s> <hold_s>   -> $OUT/<label>.txt = sustainedConnectedClients
#   run.sh state <server_dir> <clients>                              -> grant, graceful restart, verify (exit 1 on mismatch)
#   run.sh compare <base.txt> <head.txt> <tolerance_pct>             -> exit 1 when |head-base| > tolerance% of base
#
# <server_dir> holds the built Server.dll and its Config/ directory; the server runs there (Chars/ is created there).
set -uo pipefail

OUT="${OUT:-$PWD/e2e-out}"
PORT=1337
mkdir -p "$OUT"
SERVER_PID=""

start_server() { # dir logfile
  (cd "$1" && exec dotnet Server.dll) >"$2" 2>&1 &
  SERVER_PID=$!
  for _ in $(seq 1 120); do
    if (exec 3<>"/dev/tcp/127.0.0.1/$PORT") 2>/dev/null; then echo "server up (pid $SERVER_PID)"; return 0; fi
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then echo "::error::server exited during startup"; tail -n 40 "$2"; return 1; fi
    sleep 0.5
  done
  echo "::error::server did not open port $PORT within 60 s"; tail -n 40 "$2"; return 1
}

# SIGTERM (not SIGINT: background jobs of a non-interactive shell ignore SIGINT) triggers ApplicationStopping -> persist all players.
stop_server() {
  [ -n "$SERVER_PID" ] || return 0
  kill -TERM "$SERVER_PID" 2>/dev/null
  for _ in $(seq 1 60); do kill -0 "$SERVER_PID" 2>/dev/null || { SERVER_PID=""; return 0; }; sleep 0.5; done
  echo "::warning::server did not stop within 30 s, killing it"; kill -KILL "$SERVER_PID" 2>/dev/null; SERVER_PID=""
}
trap stop_server EXIT

cmd="${1:-}"; shift || true
case "$cmd" in
  load)
    dir="$1"; label="$2"; clients="$3"; ramp="$4"; hold="$5"
    start_server "$dir" "$OUT/$label.server.log" || exit 1
    # The simulator ends on its own at the first unexpected disconnect; otherwise SIGINT after ramp+hold makes it report the clients still connected.
    (cd tools && timeout --signal=INT "$((ramp + hold))" node --import tsx ./client-simulator.ts --ip 127.0.0.1 --port "$PORT" --clients "$clients" --rampUpTime "$ramp") >"$OUT/$label.sim.log" 2>&1
    stop_server
    n=$(grep -o 'sustainedConnectedClients=[0-9]*' "$OUT/$label.sim.log" | tail -n1 | cut -d= -f2)
    if [ -z "$n" ]; then echo "::error::no sustainedConnectedClients in $label simulator output"; tail -n 30 "$OUT/$label.sim.log"; exit 1; fi
    echo "$n" >"$OUT/$label.txt"
    grep -m1 -E 'First disconnect|stopped by' "$OUT/$label.sim.log" || true
    echo "$label: sustainedConnectedClients=$n (requested $clients)"
    ;;
  state)
    dir="$1"; clients="$2"
    # nocthemar.debug must be true for NocGrantXpRequest; edits the copy in the build output, never the repository file.
    python3 - "$dir/Config/Settings.json" <<'PY'
import json, sys
p = sys.argv[1]; d = json.load(open(p)); d.setdefault("nocthemar", {})["debug"] = True; json.dump(d, open(p, "w"), indent=2)
PY
    rm -rf "$dir/Chars"
    state="$OUT/state.json"
    start_server "$dir" "$OUT/state-1.server.log" || exit 1
    (cd tools && node --import tsx ./noc-e2e/reconnect.ts --mode grant --state "$state" --clients "$clients" --ip 127.0.0.1 --port "$PORT") | tee "$OUT/state-grant.log"
    rc1=${PIPESTATUS[0]}
    stop_server
    echo "shutdown save lines: $(grep -c 'Shutdown save:' "$OUT/state-1.server.log") ; character files: $(ls "$dir/Chars" 2>/dev/null | wc -l)"
    [ "$rc1" -eq 0 ] || { echo "::error::grant phase failed"; exit 1; }
    start_server "$dir" "$OUT/state-2.server.log" || exit 1
    (cd tools && node --import tsx ./noc-e2e/reconnect.ts --mode verify --state "$state" --ip 127.0.0.1 --port "$PORT") | tee "$OUT/state-verify.log"
    rc2=${PIPESTATUS[0]}
    stop_server
    [ "$rc2" -eq 0 ] || { echo "::error::state differs after server restart"; exit 1; }
    ;;
  compare)
    python3 - "$1" "$2" "$3" <<'PY'
import sys
base, head, tol = int(open(sys.argv[1]).read()), int(open(sys.argv[2]).read()), float(sys.argv[3])
if base <= 0:
    print(f"::error::base metric is {base}"); sys.exit(1)
delta = (head - base) / base * 100
verdict = "OK" if abs(delta) <= tol else "FAIL"
print(f"{verdict}: head={head} base={base} delta={delta:+.1f}% (tolerance ±{tol:g}%)")
sys.exit(0 if verdict == "OK" else 1)
PY
    ;;
  *) echo "usage: run.sh load|state|compare ..."; exit 2 ;;
esac
