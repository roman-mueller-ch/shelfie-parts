#!/usr/bin/env python3
"""Create the storage plan's locations in Part-DB.

Takes the location tree from `generate.py --locations` (drawers, bins, compartments,
wall) and creates every path Part-DB doesn't have yet, through the capture API
(which holds the Part-DB token). Existing locations are left alone, so it is safe
to run again after changing storage/plan.json. It never deletes or renames: a bin
removed from the plan stays in Part-DB until you delete it there.

    python3 storage/sync_locations.py --dry-run   # show what would be created
    python3 storage/sync_locations.py
    python3 storage/sync_locations.py --api http://localhost:5099
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path

DEFAULT_API = "https://capture.tailf98c37.ts.net"
HERE = Path(__file__).resolve().parent


def split(path: str) -> tuple[str, ...]:
    return tuple(p.strip().casefold() for p in re.split(r"[›/→]", path) if p.strip())


def request(method: str, url: str, body: dict | None = None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        sys.exit(f"{method} {url}: {e.code} {e.read().decode(errors='replace')[:300]}")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--api", default=DEFAULT_API, help=f"capture API base URL (default: {DEFAULT_API})")
    ap.add_argument("--plan", help="plan file, passed on to generate.py")
    ap.add_argument("--dry-run", action="store_true", help="only list the locations that are missing")
    args = ap.parse_args()

    cmd = [sys.executable, str(HERE / "generate.py"), "--locations"] + (["--plan", args.plan] if args.plan else [])
    wanted = [line for line in subprocess.run(cmd, check=True, capture_output=True, text=True).stdout.splitlines() if line.strip()]

    api = args.api.rstrip("/")
    existing = {split(loc["path"]) for loc in request("GET", f"{api}/api/storage-locations")}
    missing = [p for p in wanted if split(p) not in existing]

    print(f"{len(wanted)} locations in the plan, {len(wanted) - len(missing)} already in Part-DB, {len(missing)} missing.")
    for path in missing:
        if args.dry_run:
            print(f"  would create {path}")
            continue
        loc = request("POST", f"{api}/api/storage-locations", {"path": [p.strip() for p in path.split("›")]})
        print(f"  created {loc['path']} (id {loc['id']})")


if __name__ == "__main__":
    main()
