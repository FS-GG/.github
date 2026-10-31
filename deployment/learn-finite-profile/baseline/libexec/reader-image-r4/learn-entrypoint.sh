#!/bin/sh
# C5 entrypoint (spec rev 10 §1 status contract S2; R14 is enforced by the finalizer, not here).
# 1. pre-allocate /out/.status (exactly 4096 B, fsync) before any work; refuse to start if that fails;
# 2. run the reader through the cooperative process slots (C6);
# 3. overwrite .status IN PLACE (dd conv=notrunc at offset 0, same 4096 B, fsync) - works with /out full;
# 4. write /out/.done (0 B); 5. block until stopped (SIGTERM ends the block; never exits on its own).
# r2 (review 4516#6345725040): dd iflag=fullblock (no partial block from a pipe short read); an empty command is refused
# (status reason gate-refused, exit 64) instead of reporting ok.
# Reader contract: the proc cap counts only children launched through `slot proc` (the reader itself holds one of the 32
# slots, so 31 remain for its children); a reader's own rc 75 is indistinguishable from a slot refusal (both gate-refused);
# PID 1 ignores TERM until the trap is set at the end - a stop escalates to KILL then, which is harmless.
# Cooperative bypasses: direct fork/exec, and the dotnet muxer exports DOTNET_HOST_PATH=<real dotnet>, so SDK-spawned dotnet
# children skip the shim.
set -u
S=/out/.status
status() { printf '%-4096.4096s' "$1" | dd of="$S" bs=4096 count=1 iflag=fullblock conv=notrunc,fsync status=none; }
printf '%-4096.4096s' '{"schema":"learn-op-status/1","reason":"running"}' | dd of="$S" bs=4096 count=1 iflag=fullblock conv=fsync status=none || exit 70
[ "$(wc -c < "$S")" -eq 4096 ] || exit 70
mkdir -p /work/home /work/tmp || exit 71
if [ "$#" -eq 0 ]; then
  status '{"schema":"learn-op-status/1","exit":64,"reason":"gate-refused","detail":"empty command"}' || true
  : > /out/.done
  trap 'exit 0' TERM INT
  while :; do sleep 3600 & wait $!; done
fi
/usr/local/libexec/learn/slot proc 32 10 -- "$@"
rc=$?
avail=$(stat -f -c '%a %S' /out 2>/dev/null | awk '{print $1*$2}')
if [ "$rc" -eq 0 ]; then reason=ok
elif [ "$rc" -eq 75 ]; then reason=gate-refused
elif [ -n "$avail" ] && [ "$avail" -lt 1048576 ]; then reason=enospc
else reason=error; fi
status "{\"schema\":\"learn-op-status/1\",\"exit\":$rc,\"reason\":\"$reason\",\"detail\":\"rc=$rc outAvail=${avail:-unknown}\"}" || true
: > /out/.done
trap 'exit 0' TERM INT
while :; do sleep 3600 & wait $!; done
