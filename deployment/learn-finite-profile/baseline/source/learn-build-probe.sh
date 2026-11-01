#!/bin/sh
# LEARN #4745 probe build (admitted step). Runs as eugen (no privilege). Records compiler/libc versions and the binary sha256.
# Reproducibility: same source + same gcc/glibc versions + same flags give identical bytes (no timestamps embedded with
# -Wl,--build-id=none and SOURCE_DATE_EPOCH=0); any version change is visible in the recorded versions.
set -eu
SRC=$(dirname "$0")/learn-probe.c; OUT=$(dirname "$0")/probe
export LC_ALL=C SOURCE_DATE_EPOCH=0
echo "source-sha256 $(sha256sum "$SRC" | cut -c1-64)"
echo "gcc $(gcc --version | head -1)"
echo "glibc $(pacman -Q glibc 2>/dev/null || echo unknown)"
gcc -static -O2 -Wall -Wextra -Werror -fno-ident -Wl,--build-id=none -o "$OUT.tmp" "$SRC"
strip --strip-all "$OUT.tmp"
mv "$OUT.tmp" "$OUT"; chmod 0755 "$OUT"
file "$OUT" | sed 's/^[^:]*: /file /'
echo "probe-sha256 $(sha256sum "$OUT" | cut -c1-64)"
