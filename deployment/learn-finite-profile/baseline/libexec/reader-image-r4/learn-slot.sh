#!/bin/sh
# C6 cooperative slot gate (spec rev 10 §2: processes 32, CLR 10; cooperative - a direct fork/exec bypasses it).
# usage: slot <pool> <slots> <wait-seconds> -- cmd...   Runs cmd holding one free flock slot of <pool>; if no slot frees up
# within <wait-seconds>, refuses with exit 75 (no blocking beyond that, avoiding nested-dotnet deadlock).
# r2 (review 4516#6345725040): the command runs with the lock fd CLOSED ("$@" 9>&-), so only this slot shell holds the
# lock and a background grandchild cannot keep the slot; an empty command is refused (exit 64).
set -u
pool=$1; n=$2; wait_s=$3; shift 3; [ "${1:-}" = "--" ] && shift
[ "$#" -gt 0 ] || { echo "learn-slot: empty command" >&2; exit 64; }
dir=/work/.slots/$pool; mkdir -p "$dir" || exit 75
end=$(( $(date +%s) + wait_s ))
while :; do
  i=1
  while [ "$i" -le "$n" ]; do
    exec 9>>"$dir/$i"
    if flock -n 9; then "$@" 9>&-; rc=$?; exec 9>&-; exit "$rc"; fi
    exec 9>&-; i=$((i + 1))
  done
  [ "$(date +%s)" -ge "$end" ] && { echo "learn-slot: pool $pool full ($n), refused" >&2; exit 75; }
  sleep 0.2
done
