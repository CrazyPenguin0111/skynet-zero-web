#!/usr/bin/env python3
"""Consistent SQLite backup plus cookie-encryption keys. Output is private (0600)."""
import argparse
import datetime
import os
from pathlib import Path
import sqlite3
import tarfile
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("data", type=Path, help="Platform data directory")
parser.add_argument("destination", type=Path, help="Private backup directory")
args = parser.parse_args()
os.umask(0o077)
data = args.data.resolve(strict=True)
args.destination.mkdir(parents=True, exist_ok=True)
stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
archive = args.destination / f"skynetzero-{stamp}.tar.gz"
with tempfile.TemporaryDirectory(prefix="skynet-backup-") as temporary:
    snapshot = Path(temporary) / "platform.db"
    with sqlite3.connect((data / "platform.db").as_uri() + "?mode=ro", uri=True) as source:
        with sqlite3.connect(snapshot) as target:
            source.backup(target)
            if target.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
                raise RuntimeError("Database integrity check failed")
    with tarfile.open(archive, "x:gz") as output:
        output.add(snapshot, arcname="platform.db")
        if (data / "keys").is_dir():
            output.add(data / "keys", arcname="keys")
print(archive)
