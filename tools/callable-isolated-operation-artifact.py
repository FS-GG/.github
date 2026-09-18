#!/usr/bin/env python3
"""Strictly extract one bounded regular file from an Actions artifact archive."""

from __future__ import annotations

import argparse
import pathlib
import sys
import zipfile


MAX_ARCHIVE_BYTES = 4 * 1024 * 1024
MAX_MEMBER_BYTES = 2 * 1024 * 1024


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("extract",))
    parser.add_argument("--archive", required=True)
    parser.add_argument("--member", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    archive = pathlib.Path(args.archive)
    output = pathlib.Path(args.output)
    try:
        if (archive.is_symlink() or not archive.is_file() or archive.stat().st_size > MAX_ARCHIVE_BYTES
                or output.exists() or output.is_symlink() or "/" in args.member or "\\" in args.member):
            raise ValueError("artifact paths or bounds are invalid")
        with zipfile.ZipFile(archive) as bundle:
            entries = bundle.infolist()
            if (len(entries) != 1 or entries[0].filename != args.member or entries[0].is_dir()
                    or entries[0].file_size > MAX_MEMBER_BYTES or entries[0].compress_size > MAX_MEMBER_BYTES
                    or entries[0].external_attr >> 16 & 0o170000 == 0o120000):
                raise ValueError("artifact must contain exactly the expected bounded regular file")
            payload = bundle.read(entries[0])
        output.write_bytes(payload)
        output.chmod(0o600)
        return 0
    except (OSError, ValueError, zipfile.BadZipFile, RuntimeError, KeyError) as error:
        print(f"callable isolated artifact refused: {error}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main())
