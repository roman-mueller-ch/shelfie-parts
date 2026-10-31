#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["fonttools"]
# ///
"""Drawer-front labels for the Helmer: a coloured plate with the drawer name raised in black.

    uv run storage/drawer_labels.py            # all drawers
    uv run storage/drawer_labels.py D1         # just D1 (test print)

Writes storage/out/labels/<id>-label.3mf: one object with two parts, "plate" (drawer colour)
and "text" (black), plus the same parts as STL. Sized for the label holder on the drawer front
(45 x 25 mm, stuck on with double-sided tape). Plain Python + fontTools, no CAD needed.

Text font: Space Grotesk Bold (SIL Open Font License, storage/fonts/OFL.txt).
"""
from __future__ import annotations

import argparse
import json
import math
import struct
import zipfile
from pathlib import Path
from xml.sax.saxutils import escape

from fontTools.pens.basePen import BasePen
from fontTools.ttLib import TTFont

HERE = Path(__file__).resolve().parent
FONT = HERE / "fonts" / "SpaceGrotesk-Bold.ttf"
OUT = HERE / "out" / "labels"

WIDTH, HEIGHT = 45.0, 25.0   # mm, outer size of the plate
CORNER = 2.0                 # mm corner radius
PLATE = 1.2                  # mm plate thickness (6 layers at 0.2 mm)
RELIEF = 0.6                 # mm raised text (3 layers at 0.2 mm)
CAP = 15.0                   # mm cap height of the text
MAX_TEXT_W = WIDTH - 2 * 4.0 # mm, keep 4 mm free left and right
CURVE_STEPS = 10

Pt = tuple[float, float]


# ------------------------------------------------------------------ outlines

class FlattenPen(BasePen):
    def __init__(self, glyphset):
        super().__init__(glyphset)
        self.contours: list[list[Pt]] = []

    def _moveTo(self, p):
        self.contours.append([p])

    def _lineTo(self, p):
        self.contours[-1].append(p)

    def _curveToOne(self, p1, p2, p3):
        p0 = self.contours[-1][-1]
        for i in range(1, CURVE_STEPS + 1):
            t = i / CURVE_STEPS
            u = 1 - t
            self.contours[-1].append((
                u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0],
                u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1]))

    def _qCurveToOne(self, p1, p2):
        p0 = self.contours[-1][-1]
        for i in range(1, CURVE_STEPS + 1):
            t = i / CURVE_STEPS
            u = 1 - t
            self.contours[-1].append((u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0],
                                      u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1]))

    def _closePath(self):
        c = self.contours[-1]
        if len(c) > 1 and math.dist(c[0], c[-1]) < 1e-9:
            c.pop()


def text_contours(text: str) -> list[list[Pt]]:
    font = TTFont(FONT)
    glyphs = font.getGlyphSet()
    cmap = font.getBestCmap()
    scale = CAP / font["OS/2"].sCapHeight
    contours, x = [], 0.0
    for ch in text:
        name = cmap[ord(ch)]
        pen = FlattenPen(glyphs)
        glyphs[name].draw(pen)
        contours += [[(x + px, py) for px, py in c] for c in pen.contours if len(c) >= 3]
        x += glyphs[name].width
    xs = [p[0] for c in contours for p in c]
    ys = [p[1] for c in contours for p in c]
    scale = min(scale, MAX_TEXT_W / (max(xs) - min(xs)))
    cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    return [[((px - cx) * scale + WIDTH / 2, (py - cy) * scale + HEIGHT / 2) for px, py in c]
            for c in contours]


def rounded_rect(w: float, h: float, r: float, steps: int = 8) -> list[Pt]:
    pts = []
    for cx, cy, a0 in ((w - r, r, -90), (w - r, h - r, 0), (r, h - r, 90), (r, r, 180)):
        for i in range(steps + 1):
            a = math.radians(a0 + 90 * i / steps)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


# ------------------------------------------------------------ triangulation

def area(c: list[Pt]) -> float:
    return sum(c[i][0] * c[i - 1][1] - c[i - 1][0] * c[i][1] for i in range(len(c))) / -2


def inside(p: Pt, c: list[Pt]) -> bool:
    hit = False
    for i in range(len(c)):
        (x1, y1), (x2, y2) = c[i - 1], c[i]
        if (y1 > p[1]) != (y2 > p[1]) and p[0] < x1 + (p[1] - y1) * (x2 - x1) / (y2 - y1):
            hit = not hit
    return hit


def cross(o: Pt, a: Pt, b: Pt) -> float:
    return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])


def segments_cross(a: Pt, b: Pt, c: Pt, d: Pt) -> bool:
    d1, d2, d3, d4 = cross(c, d, a), cross(c, d, b), cross(a, b, c), cross(a, b, d)
    return (d1 > 0) != (d2 > 0) and (d3 > 0) != (d4 > 0) and abs(d1) > 1e-12 and abs(d2) > 1e-12 \
        and abs(d3) > 1e-12 and abs(d4) > 1e-12


def bridge(outer: list[int], holes: list[list[int]], V: list[Pt]) -> list[int]:
    """Join holes into the outer ring with zero-width bridges (indices into V)."""
    ring = list(outer)
    all_edges = lambda: [(r[i - 1], r[i]) for r in [ring] + holes for i in range(len(r))]
    for hole in sorted(holes, key=lambda h: -max(V[i][0] for i in h)):
        m = max(range(len(hole)), key=lambda k: V[hole[k]][0])
        M = V[hole[m]]
        best = None
        for k, vi in enumerate(ring):
            P = V[vi]
            if any(segments_cross(M, P, V[a], V[b]) for a, b in all_edges()
                   if hole[m] not in (a, b) and vi not in (a, b)):
                continue
            dist = math.dist(M, P)
            if best is None or dist < best[0]:
                best = (dist, k)
        k = best[1]
        loop = hole[m:] + hole[:m] + [hole[m]]
        ring = ring[:k + 1] + loop + ring[k:]
        holes = [h for h in holes if h is not hole]
    return ring


def ear_clip(ring: list[int], V: list[Pt]) -> list[tuple[int, int, int]]:
    ring, tris = list(ring), []
    guard = 0
    while len(ring) > 3 and guard < 100000:
        guard += 1
        n = len(ring)
        for i in range(n):
            a, b, c = ring[i - 1], ring[i], ring[(i + 1) % n]
            A, B, C = V[a], V[b], V[c]
            if cross(A, B, C) <= 1e-12:
                continue
            if any(cross(A, B, V[p]) >= 0 and cross(B, C, V[p]) >= 0 and cross(C, A, V[p]) >= 0
                   for p in ring if V[p] not in (A, B, C)):
                continue
            tris.append((a, b, c))
            ring.pop(i)
            break
        else:
            raise RuntimeError("triangulation failed")
    tris.append(tuple(ring))
    return tris


def extrude(contours: list[list[Pt]], z0: float, z1: float):
    """Closed mesh for polygons with holes (holes found by nesting depth)."""
    depth = [sum(inside(c[0], o) for o in contours if o is not c) for c in contours]
    rings = []
    for c, d in zip(contours, depth):
        ccw = area(c) > 0
        want_ccw = d % 2 == 0
        rings.append(c if ccw == want_ccw else c[::-1])
    verts: list[tuple[float, float, float]] = []
    V2: list[Pt] = []
    bot, top = [], []
    for r in rings:
        b, t = [], []
        for x, y in r:
            V2.append((x, y)); verts.append((x, y, z0)); b.append(len(verts) - 1)
            V2.append((x, y)); verts.append((x, y, z1)); t.append(len(verts) - 1)
        bot.append(b); top.append(t)
    faces = []
    for b, t in zip(bot, top):  # walls
        for i in range(len(b)):
            j = (i + 1) % len(b)
            faces += [(b[i], b[j], t[j]), (b[i], t[j], t[i])]
    for k, d in enumerate(depth):  # caps
        if d % 2:
            continue
        holes = [m for m, dd in enumerate(depth) if dd == d + 1 and inside(rings[m][0], rings[k])]
        ring = bridge(top[k], [top[m] for m in holes], V2)
        tri = ear_clip(ring, V2)
        faces += tri
        to_bot = {t: b for m in [k] + holes for t, b in zip(top[m], bot[m])}
        faces += [(to_bot[a], to_bot[c], to_bot[b]) for a, b, c in tri]
    return verts, faces


def check_closed(faces) -> None:
    edges: dict[tuple[int, int], int] = {}
    for a, b, c in faces:
        for e in ((a, b), (b, c), (c, a)):
            edges[e] = edges.get(e, 0) + 1
    bad = [e for e, n in edges.items() if n != 1 or edges.get((e[1], e[0])) != 1]
    if bad:
        raise RuntimeError(f"mesh not closed: {len(bad)} bad edges")


# --------------------------------------------------------------------- output

def write_stl(path: Path, mesh) -> None:
    verts, faces = mesh
    with path.open("wb") as f:
        f.write(b"\0" * 80 + struct.pack("<I", len(faces)))
        for tri in faces:
            p = [verts[i] for i in tri]
            f.write(struct.pack("<12fH", 0, 0, 0, *p[0], *p[1], *p[2], 0))


def write_3mf(path: Path, title: str, parts: list[tuple[str, tuple]]) -> None:
    objs, comps = [], []
    for n, (name, (verts, faces)) in enumerate(parts, 1):
        v = "".join(f'<vertex x="{x:.4f}" y="{y:.4f}" z="{z:.4f}"/>' for x, y, z in verts)
        t = "".join(f'<triangle v1="{a}" v2="{b}" v3="{c}"/>' for a, b, c in faces)
        objs.append(f'<object id="{n}" name="{escape(name)}" type="model"><mesh><vertices>{v}</vertices>'
                    f'<triangles>{t}</triangles></mesh></object>')
        comps.append(f'<component objectid="{n}"/>')
    top = len(parts) + 1
    objs.append(f'<object id="{top}" name="{escape(title)}" type="model"><components>{"".join(comps)}'
                f'</components></object>')
    model = ('<?xml version="1.0" encoding="UTF-8"?>\n'
             '<model unit="millimeter" xml:lang="en-US" '
             'xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">'
             f'<metadata name="Title">{escape(title)}</metadata>'
             f'<resources>{"".join(objs)}</resources>'
             f'<build><item objectid="{top}" transform="1 0 0 0 1 0 0 0 1 100 100 0"/></build></model>')
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml",
                   '<?xml version="1.0" encoding="UTF-8"?>\n'
                   '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                   '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                   '<Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>'
                   '</Types>')
        z.writestr("_rels/.rels",
                   '<?xml version="1.0" encoding="UTF-8"?>\n'
                   '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                   '<Relationship Target="/3D/3dmodel.model" Id="rel0" '
                   'Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/></Relationships>')
        z.writestr("3D/3dmodel.model", model)


def build(drawer_id: str):
    plate = extrude([rounded_rect(WIDTH, HEIGHT, CORNER)], 0.0, PLATE)
    text = extrude(text_contours(drawer_id), PLATE, PLATE + RELIEF)
    for mesh in (plate, text):
        check_closed(mesh[1])
    return plate, text


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("drawers", nargs="*", help="drawer ids (default: all in plan.json)")
    args = ap.parse_args()
    plan = json.loads((HERE / "plan.json").read_text())
    colours = {d["id"]: d["colour"] for d in plan["drawers"]}
    OUT.mkdir(parents=True, exist_ok=True)
    for did in args.drawers or list(colours):
        if did not in colours:
            raise SystemExit(f"unknown drawer {did}; known: {', '.join(colours)}")
        plate, text = build(did)
        stem = OUT / f"{did}-label"
        write_3mf(stem.with_suffix(".3mf"), f"{did} label",
                  [(f"{did} plate ({colours[did]})", plate), (f"{did} text (black)", text)])
        write_stl(OUT / f"{did}-label-plate.stl", plate)
        write_stl(OUT / f"{did}-label-text.stl", text)
        print(f"{stem.with_suffix('.3mf').relative_to(HERE.parent)}  plate {colours[did]}, text black")


if __name__ == "__main__":
    main()
