"""Prepare a signed environment pointer to immutable Docker archives."""
import hashlib
import json
from pathlib import Path
import re
import sys

sha, environment = sys.argv[1:3]
if not re.fullmatch(r"[a-f0-9]{40}", sha) or environment not in ("dev", "production"):
    raise ValueError("Invalid release")
assets = {}
for name in ("api", "web"):
    file = Path("out") / (name + ".tar.gz")
    with file.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    assets[name] = {"file": file.name, "sha256": digest, "image": f"luma-{name}:{sha}"}
Path("out/manifest.json").write_text(json.dumps({"schema": 1, "environment": environment, "sha": sha, "assets": assets}, separators=(",", ":")))
