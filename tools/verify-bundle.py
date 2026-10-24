#!/usr/bin/env python3
"""Check the pinned engine/model bundle before compiling or publishing."""
from pathlib import Path
import hashlib
import json

root = Path(__file__).resolve().parents[1]
manifest = json.loads((root / "engine/bundle-sha256.json").read_text())
for name, expected in manifest.items():
    actual = hashlib.sha256((root / name).read_bytes()).hexdigest()
    if actual != expected:
        raise SystemExit(f"Bundle changed: {name}; review and update the manifest deliberately.")
info = json.loads((root / "engine/Model/provenance.json").read_text())
assert manifest["engine/Model/weights.json"] == info["exportSha256"]
assert manifest["engine/Resources/evaluation-tables.bin"] == info["learnedTablesSha256"]
assert (root / "engine/Model/provenance.json").read_bytes() == (root / "engine/wwwroot/model-info.json").read_bytes()
print(f"PASS {len(manifest)} bundled engine, model and piece checksums")
