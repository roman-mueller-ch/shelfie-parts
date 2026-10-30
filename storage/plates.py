#!/usr/bin/env python3
"""Pack the bins that still need printing onto Bambu Lab A1 plates and write 3MF files.

Reads storage/plan.json, leaves out every bin listed under "printed", groups the
rest by colour (one filament per plate) and fills plates as fully as possible.
Identical plates are written once, with how many times to print them.

    python3 storage/plates.py --check     # show the plates, no CAD or STL files needed
    uv run storage/plates.py              # write storage/out/plates/*.3mf (+ plates.md)

Meshes come from the STLs in storage/out/ (rendered by generate.py). Missing ones
are rendered first, which needs CadQuery, hence `uv run`.

After printing, add the bin names to "printed" in plan.json and run it again:
the next set of plates only contains what's left.

Plate layout: the A1 bed (256 × 256 mm) is used as a 5 × 5 grid of Gridfinity
cells with ~6.5 mm gaps and a 10 mm margin, which keeps clear of the purge line
at the front. A 6th row or column would leave no gap at all.
"""
# /// script
# requires-python = ">=3.10,<3.13"
# dependencies = [
#   "cqgridfinity>=0.5.7",
# ]
# ///

from __future__ import annotations

import argparse
import struct
import sys
import zipfile
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import generate  # noqa: E402  (same folder)

BED = 256.0
CELLS = 5
MARGIN = 10.0
GAP = (BED - 2 * MARGIN - CELLS * generate.GRU) / (CELLS - 1)
PITCH = generate.GRU + GAP
TOLERANCE = 0.5  # a bin is n × 42 mm minus 0.5 mm


# ------------------------------------------------------------------ what to print

@dataclass(frozen=True)
class Item:
    name: str          # D2-A1, W-PROJECT
    spec: generate.BinSpec
    colour: str


def remaining_items(plan: generate.Plan) -> tuple[list[Item], list[str]]:
    printed = Counter(plan.raw.get("printed", []))
    items: list[Item] = [Item(b.name, b.spec, b.colour) for b in plan.bins]
    for entry, spec in plan.wall:
        items += [Item(entry["id"], spec, entry.get("colour", ""))] * entry.get("count", 1)

    known = Counter(i.name for i in items)
    unknown = [n for n in printed if n not in known]
    left: list[Item] = []
    for item in items:
        if printed[item.name] > 0:
            printed[item.name] -= 1
        else:
            left.append(item)
    return left, unknown


# ---------------------------------------------------------------------- packing

Placement = tuple[int, int, int, int, Item]  # col, row, across, deep, item


def best_plate(items: list[Item]) -> list[Placement]:
    """Fill one 5×5 plate as fully as possible from `items` (depth-first search).

    Ties go to plates with larger bins, so the awkward big ones don't pile up at the end.
    """
    by_spec: dict[generate.BinSpec, list[Item]] = defaultdict(list)
    for it in items:
        by_spec[it.spec].append(it)
    specs = sorted(by_spec, key=lambda s: -s.across * s.deep)
    counts = [len(by_spec[s]) for s in specs]
    shapes = []
    for s in specs:
        o = {(s.across, s.deep), (s.deep, s.across)}
        shapes.append([wd for wd in o if wd[0] <= CELLS and wd[1] <= CELLS])

    best: dict = {"score": (-1, -1), "layout": []}
    grid = [[False] * CELLS for _ in range(CELLS)]

    def fits(c, r, w, d):
        return c + w <= CELLS and r + d <= CELLS and all(not grid[r + y][c + x] for y in range(d) for x in range(w))

    def mark(c, r, w, d, v):
        for y in range(d):
            for x in range(w):
                grid[r + y][c + x] = v

    def dfs(cell, filled, weight, layout, free_left):
        score = (filled, weight)
        if score > best["score"]:
            best["score"], best["layout"] = score, list(layout)
        if filled + free_left <= best["score"][0] and filled + free_left < CELLS * CELLS:
            if filled + free_left < best["score"][0] or weight >= best["score"][1]:
                return
        while cell < CELLS * CELLS and grid[cell // CELLS][cell % CELLS]:
            cell += 1
        if cell >= CELLS * CELLS:
            return
        r, c = divmod(cell, CELLS)
        for i, spec in enumerate(specs):
            if not counts[i]:
                continue
            for w, d in shapes[i]:
                if fits(c, r, w, d):
                    mark(c, r, w, d, True)
                    counts[i] -= 1
                    layout.append((c, r, w, d, i))
                    dfs(cell + 1, filled + w * d, weight + (w * d) ** 2, layout, free_left - w * d)
                    layout.pop()
                    counts[i] += 1
                    mark(c, r, w, d, False)
        grid[r][c] = True  # leave this cell empty
        dfs(cell + 1, filled, weight, layout, free_left - 1)
        grid[r][c] = False

    dfs(0, 0, 0, [], CELLS * CELLS)

    pools = {s: list(v) for s, v in by_spec.items()}
    return [(c, r, w, d, pools[specs[i]].pop(0)) for c, r, w, d, i in best["layout"]]


def plan_colour(items: list[Item]) -> list[list[Placement]]:
    """Plates for one colour: identical plates if the bins split evenly, else greedy."""
    left = list(items)
    greedy: list[list[Placement]] = []
    while left:
        plate = best_plate(left)
        if not plate:
            raise SystemExit(f"can't place {left[0].name}: {left[0].spec.across}×{left[0].spec.deep} is larger than the plate")
        used = {id(p[4]) for p in plate}
        left = [i for i in left if id(i) not in used]
        greedy.append(plate)

    # Try the same plate N times (easier to reprint): split every bin type evenly.
    n = len(greedy)
    per_spec = Counter(i.spec for i in items)
    if n > 1 and all(c % n == 0 for c in per_spec.values()):
        by_spec: dict = defaultdict(list)
        for it in items:
            by_spec[it.spec].append(it)
        uniform = []
        for k in range(n):
            share = [it for spec, its in by_spec.items() for it in its[k::n]]
            plate = best_plate(share)
            if len(plate) != len(share):
                break
            uniform.append(plate)
        else:
            return uniform
    return greedy


def signature(plate: list[Placement]) -> tuple:
    return tuple(sorted((c, r, w, d, p.spec.filename) for c, r, w, d, p in plate))


@dataclass
class PlateJob:
    label: str
    colour: str
    layout: list[Placement]   # first run's layout (others are identical)
    runs: list[list[Placement]]


def plan_plates(items: list[Item]) -> list[PlateJob]:
    by_colour: dict[str, list[Item]] = defaultdict(list)
    for it in items:
        by_colour[it.colour or "any"].append(it)
    jobs: list[PlateJob] = []
    for colour in sorted(by_colour):
        grouped: dict[tuple, list] = {}
        for plate in plan_colour(by_colour[colour]):
            grouped.setdefault(signature(plate), []).append(plate)
        for k, runs in enumerate(sorted(grouped.values(), key=len, reverse=True)):
            jobs.append(PlateJob(f"{colour}-{chr(ord('A') + k)}", colour, runs[0], runs))
    return jobs


# ---------------------------------------------------------------------- reports

def plate_ascii(layout: list[Placement]) -> str:
    grid = [["·"] * CELLS for _ in range(CELLS)]
    for n, (c, r, w, d, _) in enumerate(layout):
        ch = chr(ord("a") + n)
        for y in range(d):
            for x in range(w):
                grid[r + y][c + x] = ch
    return "\n".join("    " + " ".join(row) for row in reversed(grid)) + "\n    (front of the bed)"


def describe(job: PlateJob) -> list[str]:
    lines = [f"Plate {job.label}: print ×{len(job.runs)}"]
    for n, (c, r, w, d, item) in enumerate(job.layout):
        s = item.spec
        rot = " (rotated)" if (w, d) != (s.across, s.deep) else ""
        lines.append(f"  {chr(ord('a') + n)}  {s.across}×{s.deep} {s.height}u{rot}   {s.filename}")
    lines.append(plate_ascii(job.layout))
    for k, run in enumerate(job.runs, 1):
        lines.append(f"  run {k}: " + ", ".join(sorted(p[4].name for p in run)))
    return lines


def write_markdown(jobs: list[PlateJob], path: Path) -> None:
    total_plates = sum(len(j.runs) for j in jobs)
    total_bins = sum(len(r) for j in jobs for r in j.runs)
    out = ["# Print plates", "",
           "Generated by `storage/plates.py`. Bins already listed under `printed` in `plan.json` are left out.", "",
           f"{total_bins} bins on {total_plates} plates.", "",
           "| Plate | Colour | Print | Bins per plate | File |", "|---|---|---|---|---|"]
    for j in jobs:
        out.append(f"| {j.label} | {j.colour} | ×{len(j.runs)} | {len(j.layout)} | `plate_{j.label}.3mf` |")
    out.append("")
    for j in jobs:
        out += ["```", *describe(j), "```", ""]
    path.write_text("\n".join(out))


# ------------------------------------------------------------------------ meshes

Mesh = tuple[list[tuple[float, float, float]], list[tuple[int, int, int]]]


def read_stl(path: Path) -> Mesh:
    data = path.read_bytes()
    tris: list[tuple[tuple[float, float, float], ...]] = []
    if len(data) >= 84 and 84 + 50 * struct.unpack_from("<I", data, 80)[0] == len(data):
        (n,) = struct.unpack_from("<I", data, 80)
        for k in range(n):
            v = struct.unpack_from("<12f", data, 84 + 50 * k)
            tris.append((v[3:6], v[6:9], v[9:12]))
    else:  # ASCII
        verts = [tuple(map(float, line.split()[1:4])) for line in data.decode(errors="replace").splitlines()
                 if line.strip().startswith("vertex")]
        tris = [tuple(verts[i:i + 3]) for i in range(0, len(verts) - 2, 3)]
    index: dict[tuple, int] = {}
    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, int, int]] = []
    for tri in tris:
        ids = []
        for v in tri:
            key = (round(v[0], 4), round(v[1], 4), round(v[2], 4))
            if key not in index:
                index[key] = len(vertices)
                vertices.append(key)
            ids.append(index[key])
        if len(set(ids)) == 3:
            faces.append(tuple(ids))
    return vertices, faces


def mesh_for(spec: generate.BinSpec, cache: dict) -> Mesh:
    if spec in cache:
        return cache[spec]
    stl = generate.OUT / f"{spec.filename}.stl"
    if not stl.exists():
        try:
            import cadquery  # noqa: F401
        except ImportError:
            sys.exit(f"{stl.name} is missing and CadQuery isn't installed. Run: uv run storage/plates.py")
        generate.OUT.mkdir(exist_ok=True)
        print(f"  rendering {stl.name}", flush=True)
        generate.export(generate.render_bin(spec), generate.OUT / spec.filename, step=False)
    cache[spec] = read_stl(stl)
    return cache[spec]


def write_3mf(job: PlateJob, path: Path, cache: dict) -> None:
    objects, items = [], []
    for n, (c, r, w, d, item) in enumerate(job.layout, 1):
        verts, faces = mesh_for(item.spec, cache)
        xs, ys, zs = zip(*verts)
        rotated = (w, d) != (item.spec.across, item.spec.deep) and w != d
        # Lower-left corner of the bin on the bed, then move the mesh there (rotated 90° if needed).
        x0, y0 = MARGIN + c * PITCH, MARGIN + r * PITCH
        out = []
        for x, y, z in verts:
            if rotated:
                x, y = (max(ys) - y), (x - min(xs))
                out.append((x0 + x, y0 + y, z - min(zs)))
            else:
                out.append((x0 + x - min(xs), y0 + y - min(ys), z - min(zs)))
        v_xml = "".join(f'<vertex x="{x:.4f}" y="{y:.4f}" z="{z:.4f}"/>' for x, y, z in out)
        t_xml = "".join(f'<triangle v1="{a}" v2="{b}" v3="{c3}"/>' for a, b, c3 in faces)
        name = escape(f"{item.name} {item.spec.across}x{item.spec.deep}x{item.spec.height}u", {'"': "&quot;"})
        objects.append(f'<object id="{n}" name="{name}" type="model"><mesh><vertices>{v_xml}</vertices>'
                       f'<triangles>{t_xml}</triangles></mesh></object>')
        items.append(f'<item objectid="{n}"/>')

    model = ('<?xml version="1.0" encoding="UTF-8"?>\n'
             '<model unit="millimeter" xml:lang="en-US" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">'
             f'<metadata name="Title">Shelfie plate {job.label}</metadata>'
             f'<resources>{"".join(objects)}</resources><build>{"".join(items)}</build></model>')
    content_types = ('<?xml version="1.0" encoding="UTF-8"?>\n'
                     '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                     '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                     '<Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>'
                     '</Types>')
    rels = ('<?xml version="1.0" encoding="UTF-8"?>\n'
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            '<Relationship Target="/3D/3dmodel.model" Id="rel0" '
            'Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/></Relationships>')
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", content_types)
        z.writestr("_rels/.rels", rels)
        z.writestr("3D/3dmodel.model", model)


# -------------------------------------------------------------------------- main

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--plan", type=Path, default=generate.PLAN)
    parser.add_argument("--check", action="store_true", help="show the plates only; no meshes, no files")
    parser.add_argument("--colour", help="only plates in this colour")
    args = parser.parse_args()

    plan = generate.load_plan(args.plan)
    if plan.errors:
        for e in plan.errors:
            print(f"error: {e}", file=sys.stderr)
        sys.exit(1)
    items, unknown = remaining_items(plan)
    for name in unknown:
        print(f"warning: printed bin {name!r} isn't in the plan", file=sys.stderr)
    if args.colour:
        items = [i for i in items if i.colour == args.colour]
    if not items:
        print("Nothing left to print.")
        return

    jobs = plan_plates(items)
    total = sum(len(j.runs) for j in jobs)
    print(f"{len(items)} bins left to print → {total} plates ({len(jobs)} different)\n")
    for j in jobs:
        print("\n".join(describe(j)) + "\n")
    if args.check:
        return

    out = generate.OUT / "plates"
    out.mkdir(parents=True, exist_ok=True)
    for old in out.glob("plate_*.3mf"):
        old.unlink()
    cache: dict = {}
    for j in jobs:
        path = out / f"plate_{j.label}.3mf"
        write_3mf(j, path, cache)
        print(f"wrote {path}  (print ×{len(j.runs)})")
    write_markdown(jobs, out / "plates.md")
    print(f"wrote {out / 'plates.md'}")


if __name__ == "__main__":
    main()
