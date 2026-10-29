# /// script
# requires-python = ">=3.10,<3.13"
# dependencies = [
#   "cqgridfinity>=0.5.7",
# ]
# ///
"""Generate Gridfinity baseplates and bins for the Shelfie storage plan.

Reads storage/plan.json (drawer size, grid and every bin), validates it and
renders one STL per distinct part into storage/out/, plus a print list.

    uv run storage/generate.py                 # render everything
    uv run storage/generate.py --only base     # only files whose name contains "base"
    uv run storage/generate.py --step          # also write STEP files
    python3 storage/generate.py --check        # validate + print layouts, no CAD needed
    python3 storage/generate.py --locations    # Part-DB storage location names

Orientation: X runs across the drawer (columns A-E), Y from front to back
(rows 1-9). A bin size [2, 3] is 2 units across and 3 units deep.
"""

from __future__ import annotations

import argparse
import json
import string
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path

HERE = Path(__file__).resolve().parent
PLAN = HERE / "plan.json"
OUT = HERE / "out"

GRU = 42.0            # Gridfinity grid unit (mm)
HEIGHT_U = 7.0        # height unit (mm)
LIP_MM = 4.4          # stacking lip on top of a bin
BASEPLATE_BUDGET = 5  # conservative allowance for the baseplate under a bin
MIN_CLEARANCE = 10    # warn below this much headroom in a drawer
BED_MM = 256          # Bambu A1 build plate


# --------------------------------------------------------------------------- plan

@dataclass(frozen=True)
class BinSpec:
    across: int
    deep: int
    height: int
    lite: bool
    labels: bool
    scoops: bool
    compartments: tuple[int, int] = (1, 1)

    @property
    def filename(self) -> str:
        name = f"bin_{self.across}x{self.deep}x{self.height}u"
        if self.compartments != (1, 1):
            name += f"_{self.compartments[0]}x{self.compartments[1]}comp"
        if self.lite:
            name += "_lite"
        if self.scoops:
            name += "_scoop"
        if self.labels:
            name += "_label"
        return name

    @property
    def total_height_mm(self) -> float:
        return self.height * HEIGHT_U + LIP_MM + BASEPLATE_BUDGET


@dataclass
class PlacedBin:
    drawer: str
    at: str
    spec: BinSpec
    colour: str

    @property
    def name(self) -> str:
        return f"{self.drawer}-{self.at}"

    @property
    def compartment_names(self) -> list[str]:
        cx, cy = self.spec.compartments
        if cx * cy == 1:
            return []
        # left to right, then front to back
        return [f"{self.name}.{i + 1}" for i in range(cx * cy)]


@dataclass
class Plan:
    raw: dict
    bins: list[PlacedBin] = field(default_factory=list)
    wall: list[tuple[dict, BinSpec]] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)


def parse_cell(at: str) -> tuple[int, int]:
    """'C4' -> (2, 3): zero-based column and row."""
    col = string.ascii_uppercase.index(at[0].upper())
    row = int(at[1:]) - 1
    return col, row


def make_spec(entry: dict, defaults: dict) -> BinSpec:
    get = lambda k: entry.get(k, defaults.get(k))
    across, deep = entry["size"]
    comps = tuple(entry.get("compartments", (1, 1)))
    return BinSpec(across, deep, entry["height"], bool(get("lite")), bool(get("labels")), bool(get("scoops")), comps)


def load_plan(path: Path) -> Plan:
    raw = json.loads(path.read_text())
    plan = Plan(raw)
    d = raw["drawer"]
    cols, rows = d["grid"]["columns"], d["grid"]["rows"]
    inner = d["inner_mm"]
    clearance = d.get("fit_clearance_mm", 1.0)
    defaults = raw.get("defaults", {})

    # Grid vs drawer
    for axis, units, mm in (("width", cols, inner["width"]), ("depth", rows, inner["depth"])):
        slack = mm - units * GRU - clearance
        if slack < 0:
            plan.errors.append(f"{units} units ({units * GRU:.0f} mm) don't fit the drawer {axis} of {mm} mm "
                               f"with {clearance} mm clearance")
    if sum(b["rows"] for b in d["baseplates"]) != rows:
        plan.errors.append(f"baseplates cover {sum(b['rows'] for b in d['baseplates'])} rows, grid has {rows}")

    side_pad = side_padding(raw)
    for b in d["baseplates"]:
        w, l = cols * GRU + 2 * side_pad, b["rows"] * GRU
        if max(w, l) > BED_MM:
            plan.errors.append(f"baseplate {b['name']} ({w:.0f} × {l:.0f} mm) is larger than the {BED_MM} mm bed")

    # Bins
    for drawer in raw["drawers"]:
        occupied: dict[tuple[int, int], str] = {}
        for entry in drawer["bins"]:
            spec = make_spec(entry, defaults)
            placed = PlacedBin(drawer["id"], entry["at"], spec, drawer.get("colour", ""))
            plan.bins.append(placed)
            col, row = parse_cell(entry["at"])
            if spec.compartments[0] > spec.across or spec.compartments[1] > spec.deep * 4:
                plan.warnings.append(f"{placed.name}: unusual compartment split {spec.compartments}")
            if spec.across == 1 and spec.deep == 1:
                plan.warnings.append(f"{placed.name}: 1×1 bin (plan says to avoid them)")
            if spec.total_height_mm > inner["height"]:
                plan.errors.append(f"{placed.name}: {spec.height}u needs ~{spec.total_height_mm:.0f} mm, "
                                   f"drawer has {inner['height']} mm")
            elif inner["height"] - spec.total_height_mm < MIN_CLEARANCE:
                plan.warnings.append(f"{placed.name}: {spec.height}u leaves only "
                                     f"{inner['height'] - spec.total_height_mm:.0f} mm headroom")
            for dx in range(spec.across):
                for dy in range(spec.deep):
                    cell = (col + dx, row + dy)
                    if not (0 <= cell[0] < cols and 0 <= cell[1] < rows):
                        plan.errors.append(f"{placed.name} ({spec.across}×{spec.deep}) runs outside the {cols}×{rows} grid")
                        break
                    if cell in occupied:
                        msg = f"{placed.name} overlaps {occupied[cell]}"
                        if msg not in plan.errors:
                            plan.errors.append(msg)
                    occupied[cell] = placed.name
        empty = [f"{string.ascii_uppercase[c]}{r + 1}" for r in range(rows) for c in range(cols) if (c, r) not in occupied]
        if empty:
            plan.warnings.append(f"{drawer['id']}: {len(empty)} empty cells ({', '.join(empty)})")

    names = [b.name for b in plan.bins]
    dupes = {n for n in names if names.count(n) > 1}
    if dupes:
        plan.errors.append(f"duplicate bin names: {', '.join(sorted(dupes))}")

    for entry in raw.get("wall", []):
        plan.wall.append((entry, make_spec(entry, defaults)))
    return plan


def side_padding(raw: dict) -> float:
    d = raw["drawer"]
    margin = d["inner_mm"]["width"] - d["grid"]["columns"] * GRU - d.get("fit_clearance_mm", 1.0)
    return max(margin, 0) / 2


# ------------------------------------------------------------------------ reports

def layout_text(plan: Plan, drawer_id: str) -> str:
    d = plan.raw["drawer"]["grid"]
    cols, rows = d["columns"], d["rows"]
    grid = [["" for _ in range(cols)] for _ in range(rows)]
    for b in plan.bins:
        if b.drawer != drawer_id:
            continue
        col, row = parse_cell(b.at)
        for dx in range(b.spec.across):
            for dy in range(b.spec.deep):
                if 0 <= row + dy < rows and 0 <= col + dx < cols:
                    grid[row + dy][col + dx] = b.at
    width = 5
    lines = []
    for r in reversed(range(rows)):  # back row on top, like looking into the drawer
        lines.append(f"  {r + 1:>2} " + "".join(f"{(grid[r][c] or '·'):^{width}}" for c in range(cols)))
    lines.append("     " + "".join(f"{string.ascii_uppercase[c]:^{width}}" for c in range(cols)) + "   (front)")
    return "\n".join(lines)


def print_list(plan: Plan) -> list[tuple[str, int, list[str], str, str]]:
    """(file, quantity, where, colours, description) per distinct part."""
    rows = []
    d = plan.raw["drawer"]
    cols = d["grid"]["columns"]
    pad = side_padding(plan.raw)
    n_drawers = len(plan.raw["drawers"])
    for b in d["baseplates"]:
        rows.append((baseplate_filename(cols, b["rows"], pad, b["round_corners"]), n_drawers,
                     [f"{b['name']} of every drawer"], "",
                     f"{cols}×{b['rows']} baseplate, {pad:.1f} mm side padding, "
                     f"{cols * GRU + 2 * pad:.0f} × {b['rows'] * GRU:.0f} mm"))
    groups: dict[BinSpec, list[PlacedBin]] = defaultdict(list)
    for b in plan.bins:
        groups[b.spec].append(b)
    for entry, spec in plan.wall:
        groups[spec].append(PlacedBin("W", entry["id"], spec, entry.get("colour", "")))
        for _ in range(entry.get("count", 1) - 1):
            groups[spec].append(PlacedBin("W", entry["id"], spec, entry.get("colour", "")))
    for spec, placed in sorted(groups.items(), key=lambda kv: (kv[0].height, kv[0].across * kv[0].deep)):
        where = [p.at if p.drawer == "W" else p.name for p in placed]
        colours = ", ".join(sorted({p.colour for p in placed if p.colour}))
        desc = f"{spec.across}×{spec.deep} bin, {spec.height}u"
        if spec.compartments != (1, 1):
            desc += f", {spec.compartments[0] * spec.compartments[1]} compartments"
        rows.append((spec.filename, len(placed), where, colours, desc))
    return rows


def write_print_list(plan: Plan, path: Path) -> None:
    lines = ["# Print list", "", "Generated by `storage/generate.py` from `storage/plan.json`. Do not edit by hand.", "",
             "| File | Qty | Colour | What | Where |", "|---|---|---|---|---|"]
    total = 0
    for file, qty, where, colour, desc in print_list(plan):
        lines.append(f"| `{file}.stl` | {qty} | {colour} | {desc} | {', '.join(where)} |")
        total += qty
    lines += ["", f"{total} parts in total.", ""]
    path.write_text("\n".join(lines))


def locations(plan: Plan) -> list[str]:
    out = []
    for drawer in plan.raw["drawers"]:
        out.append(f"Helmer › {drawer['id']}")
        for b in (b for b in plan.bins if b.drawer == drawer["id"]):
            out.append(f"Helmer › {drawer['id']} › {b.name}")
            out += [f"Helmer › {drawer['id']} › {b.name} › {c}" for c in b.compartment_names]
    out.append("Wall")
    out += [f"Wall › {entry['id']}" for entry, _ in plan.wall]
    return out


# ------------------------------------------------------------------------- render

def baseplate_filename(cols: int, rows: int, pad: float, corners: str) -> str:
    return f"baseplate_{cols}x{rows}_pad{pad:g}mm_{corners}"


def render_baseplate(cols: int, rows: int, pad: float, corners: str):
    """A standard open Gridfinity baseplate, widened by `pad` mm on the left and right.

    Same pocket geometry as cqgridfinity's GridfinityBaseplate, but the outer block is
    wider and only the corners that sit in a drawer corner are rounded ("front",
    "back" or "all"); the edges where two baseplates meet stay square.
    """
    import cadquery as cq
    from cqgridfinity import GridfinityBaseplate
    from cqgridfinity.constants import GR_BASE_HEIGHT, GR_BASE_PROFILE, GR_RAD, GRU2, GRU_CUT
    from cqkit.cq_helpers import composite_from_pts, recentre, rotate_x, rounded_rect_sketch

    ref = GridfinityBaseplate(cols, rows)
    pocket = ref.extrude_profile(rounded_rect_sketch(GRU_CUT, GRU_CUT, GR_RAD), GR_BASE_PROFILE)
    pocket = rotate_x(pocket, 180).translate((GRU2, GRU2, GR_BASE_HEIGHT))
    pockets = recentre(composite_from_pts(pocket, ref.grid_centres), "XY")

    plate = cq.Workplane("XY").rect(ref.length + 2 * pad, ref.width).extrude(GR_BASE_HEIGHT)
    edges = plate.edges("|Z")
    if corners == "front":
        edges = edges.edges("<Y")
    elif corners == "back":
        edges = edges.edges(">Y")
    plate = edges.fillet(GR_RAD)
    return plate.cut(pockets)


def render_bin(spec: BinSpec):
    from cqgridfinity import GridfinityBox

    cx, cy = spec.compartments
    box = GridfinityBox(
        spec.across, spec.deep, spec.height,
        lite_style=spec.lite,
        labels=spec.labels,
        scoops=spec.scoops,
        length_div=cx - 1,
        width_div=cy - 1,
    )
    return box.render()


def export(obj, path: Path, step: bool) -> None:
    import cadquery as cq

    cq.exporters.export(obj, str(path.with_suffix(".stl")), tolerance=0.01, angularTolerance=0.1)
    if step:
        cq.exporters.export(obj, str(path.with_suffix(".step")))


def render_all(plan: Plan, only: str | None, step: bool) -> None:
    OUT.mkdir(exist_ok=True)
    d = plan.raw["drawer"]
    cols, pad = d["grid"]["columns"], side_padding(plan.raw)

    jobs = []
    for b in d["baseplates"]:
        name = baseplate_filename(cols, b["rows"], pad, b["round_corners"])
        jobs.append((name, lambda b=b: render_baseplate(cols, b["rows"], pad, b["round_corners"])))
    specs = {p.spec for p in plan.bins} | {s for _, s in plan.wall}
    for spec in sorted(specs, key=lambda s: s.filename):
        jobs.append((spec.filename, lambda s=spec: render_bin(s)))

    jobs = [(n, f) for n, f in jobs if not only or only in n]
    if not jobs:
        sys.exit(f"Nothing matches --only {only!r}")
    for i, (name, make) in enumerate(jobs, 1):
        print(f"[{i}/{len(jobs)}] {name}.stl", flush=True)
        export(make(), OUT / name, step)

    write_print_list(plan, OUT / "print-list.md")
    print(f"\nWrote {len(jobs)} parts and print-list.md to {OUT}")


# --------------------------------------------------------------------------- main

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--plan", type=Path, default=PLAN, help="plan file (default: storage/plan.json)")
    parser.add_argument("--check", action="store_true", help="validate and show layouts and the print list; no CAD")
    parser.add_argument("--locations", action="store_true", help="list Part-DB storage locations; no CAD")
    parser.add_argument("--only", help="render only parts whose file name contains this text")
    parser.add_argument("--step", action="store_true", help="also write STEP files")
    args = parser.parse_args()

    plan = load_plan(args.plan)
    for w in plan.warnings:
        print(f"warning: {w}", file=sys.stderr)
    for e in plan.errors:
        print(f"error: {e}", file=sys.stderr)
    if plan.errors:
        sys.exit(1)

    if args.locations:
        print("\n".join(locations(plan)))
        return

    if args.check:
        for drawer in plan.raw["drawers"]:
            print(f"\n{drawer['id']}: {drawer['contents']}")
            print(layout_text(plan, drawer["id"]))
        print("\nPrint list:")
        total = 0
        for file, qty, where, colour, desc in print_list(plan):
            print(f"  {qty:>3} × {file}.stl   {desc}" + (f"   [{colour}]" if colour else ""))
            total += qty
        print(f"  {total} parts in total")
        return

    try:
        import cadquery  # noqa: F401
    except ImportError:
        sys.exit("CadQuery is not installed. Run this with: uv run storage/generate.py")
    render_all(plan, args.only, args.step)


if __name__ == "__main__":
    main()
