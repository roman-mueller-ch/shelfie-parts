# Storage: Gridfinity bins in an IKEA Helmer and on the wall

Physical layout of the inventory. Every bin is a storage location in Part-DB, with a QR label on the P700.

## The cabinet

- **IKEA Helmer**, 6 drawers, numbered **D1 (top) to D6 (bottom)**.
- **Inside of a drawer: 235 × 380 × 85 mm** (width × depth × height), measured 2026-10-29.
- **Grid per drawer: 5 × 9 units** (210 × 378 mm), 5 across the front, 9 deep.
- **Margin:** 25 mm in width, filled with padding on the baseplates (12 mm left and right, leaving 1 mm so they slide in). In depth only **2 mm** are left, so the grid fills the drawer front to back.

> **2 mm is tight.** Before printing the second baseplate, measure the depth again at the floor along both side walls, and check the corners: if the drawer's corners are rounded or have a seam, the baseplate's corner needs to clear it. If 378 mm doesn't fit, fall back to 8 rows and 44 mm of padding at the back.

### Height budget

Gridfinity bins are 7 mm per height unit (u), plus about 4.4 mm for the stacking lip. Budget about 5 mm for the baseplate.

| Bin height | Bin incl. lip | + baseplate | Clearance in 85 mm |
|---|---|---|---|
| 3u | 25 mm | ~30 mm | 55 mm |
| 6u | 46 mm | ~51 mm | 34 mm |
| 9u | 67 mm | ~72 mm | 13 mm, **tallest recommended** |
| 10u | 74 mm | ~79 mm | 6 mm, only if the test print shows the drawer closes reliably |
| 11u | 81 mm | ~86 mm | ❌ doesn't fit |

Whatever sticks out of a bin counts too: a bag standing upright on the bin floor must be **at most 7 cm tall** (about 10 mm of baseplate and bin floor plus 70 mm stays under 85 mm). In a 9u bin such a bag sticks out a little above the rim, which makes it easy to grab.

Bins that should also work on the wall stay at 9u or lower.

## Naming

- **Columns A–E** from left to right, **rows 1–9** from front to back.
- **A bin is named after its front-left cell:** the 2×3 bin covering A1–B3 in drawer 2 is **`D2-A1`**.
- **Compartments** of a divided bin get a suffix, counted left to right, then front to back: `D1-A1.1` to `D1-A1.6` in a 2×3 bin with six compartments.
- **In Part-DB** the storage location tree is `Helmer › D2 › D2-A1`. The leaf name is unique on its own, which keeps the 9 mm labels short.

Names describe positions, not contents. When a bin's contents change, only Part-DB changes; the label stays.

## Drawer plan

A starting allocation. Swap drawers around as the collection grows; the layouts are independent.

| Drawer | Contents | Bins |
|---|---|---|
| D1 | Small loose parts: ICs, crystals, LEDs, buttons | 6 × 2×3 3u (6 compartments), 3 × 1×3 3u (3 compartments) |
| D2 | Bagged parts | 6 × 2×3 9u, 3 × 1×3 9u |
| D3 | Bagged parts | 6 × 2×3 9u, 3 × 1×3 9u |
| D4 | Modules and sensors | 8 × 2×2 6u, 3 × 1×3 6u, 2 × 2×1 6u |
| D5 | Dev boards and displays | 6 × 2×3 6u, 3 × 1×3 6u |
| D6 | Cables, wire, tools | 3 × 3×3 9u, 3 × 2×3 9u |

**No 1×1 bins.** The smallest bins are 2×1 and 1×3. Small loose parts go into compartments of larger bins instead: D1 offers 45 compartments, the same as 45 single bins, but only 9 bins to print and handle. Use 1×1 bins only for something that really needs its own box.

Most AliExpress stock arrives in small zip bags, so two drawers are reserved for them. Keep parts in their bags and stand the bags upright in the 2×3 bins like index cards, front to back. One 2×3 bin holds roughly 15–25 bags.

### Layouts

Front of the drawer at the bottom of each grid.

**D1, D2, D3 and D5** share one layout. D1 uses it at 3u with compartments (six per 2×3 bin, three per 1×3 bin), D2 and D3 at 9u for bags, D5 at 6u for dev boards.
```
row 7–9 │  A7 (2×3)  │  C7 (2×3)  │ E7 (1×3) │
row 4–6 │  A4 (2×3)  │  C4 (2×3)  │ E4 (1×3) │
row 1–3 │  A1 (2×3)  │  C1 (2×3)  │ E1 (1×3) │
             A    B       C    D       E
```

**D4: modules and sensors**
```
columns A–D                          column E
row 9   │  A9 (2×1) │  C9 (2×1) │    rows 7–9  │ E7 (1×3) │
row 7–8 │  A7 (2×2) │  C7 (2×2) │    rows 4–6  │ E4 (1×3) │
row 5–6 │  A5 (2×2) │  C5 (2×2) │    rows 1–3  │ E1 (1×3) │
row 3–4 │  A3 (2×2) │  C3 (2×2) │
row 1–2 │  A1 (2×2) │  C1 (2×2) │
```

**D6: cables, wire, tools**
```
row 7–9 │   A7 (3×3)    │  D7 (2×3)  │
row 4–6 │   A4 (3×3)    │  D4 (2×3)  │
row 1–3 │   A1 (3×3)    │  D1 (2×3)  │
             A   B   C      D    E
```

## Print list

All STL files come from **`storage/generate.py`**, which reads **`storage/plan.json`**: the drawer size, grid and every bin. Change the plan there, not here, then regenerate:

```bash
python3 storage/generate.py --check     # validate, show layouts and print list (no CAD needed)
uv run storage/generate.py              # render everything to storage/out/ (first run installs CadQuery)
uv run storage/generate.py --only base  # just the baseplates, e.g. for the test print
```

`storage/out/print-list.md` lists every file with quantity, colour and which bins it's for. The validator refuses plans where bins overlap, run outside the grid or are too tall for the drawer, and warns about empty cells, 1×1 bins and tight headroom.

### Baseplates

Per drawer one **5×5** and one **5×4** baseplate, both with **12 mm padding left and right**: the 25 mm margin minus 1 mm clearance so they slide in. Only the corners that sit in a drawer corner are rounded; the edges where the two pieces meet stay square. No separate spacers needed.

| Piece | Grid | Outer size |
|---|---|---|
| Front | 5×5 | 234 × 210 mm |
| Back | 5×4 | 234 × 168 mm |

Both fit the A1's 256 × 256 mm bed. Magnets aren't needed; bins in a drawer don't slide. For all six drawers: 6 front and 6 back pieces.

### Bins

| Size | Height | Quantity | Drawers |
|---|---|---|---|
| 2×3, 6 compartments | 3u | 6 | D1 |
| 1×3, 3 compartments | 3u | 3 | D1 |
| 2×1 | 6u | 2 | D4 |
| 2×2 | 6u | 8 | D4 |
| 1×3 | 6u | 6 | D4, D5 |
| 2×3 | 6u | 6 | D5 |
| 2×3 | 9u | 15 | D2, D3, D6 |
| 1×3 | 9u | 6 | D2, D3 |
| 3×3 | 9u | 3 | D6 |

55 bins in total for all six drawers.

**Start with a test print:** the front 5×5 baseplate and one 2×3 bin at 9u with a label tab. Check that the baseplate sits flat between the side walls, that the bin drops in and lifts out easily, that the drawer closes with a bag standing in the bin, and that a 9 mm label fits the tab. Then the back baseplate, which shows whether the 2 mm of depth really fit, then the rest of D2, then D1.

**What the generator makes:** bins with a stacking lip and a label shelf (12 mm wide, fits the 9 mm tape), no magnet holes. Bins are "lite" style (thin shell, no raised floor) to save filament, except D1, which has a solid floor with compartments and scoop-shaped floors so tiny parts are easy to pick out. Per-bin options (`lite`, `labels`, `scoops`, `compartments`) live in `plan.json`.

**Slicer (Bambu Studio):** PLA, 0.2 mm layers, 2–3 walls, 10–15 % infill, no supports. STL units are millimetres.

## Wall: Multiboard work area

A small area on the Multiboard wall next to the desk, **35 × 35 cm**, for things in use. The Helmer stays the main store. Bins on the wall use Gridfinity shelves that click into Multiboard, so the same bins move between wall and drawers.

```
┌──────────────────────── 35 cm ────────────────────────┐
│  W-INBOX                       │  P700 tape cassettes  │  top: parcels can stick out freely
│  2×4 shelf, one 4×2 bin (10u)  │  (item holder)        │
├────────────────────────────────┼───────────────────────┤
│  W-PROJECT                     │  free: second project │  middle
│  2×4 shelf, two 2×2 bins (6u)  │  shelf later          │
├────────────────────────────────┴───────────────────────┤
│  tool holders: flush cutters, tweezers, screwdrivers   │  bottom
└────────────────────────────────────────────────────────┘
```

- **`W-INBOX`:** new AliExpress parcels stand upright here until they're photographed and put away. In Part-DB it's a storage location too: parts captured but not yet sorted are found "in the inbox" instead of nowhere. Keep it nearly empty; it's a queue, not storage.
- **`W-PROJECT`:** parts pulled from the Helmer for the current project. Move them in Part-DB when you take them out, and back when the project is done.
- **Tools** hang on Multiboard hooks and holders; they aren't in the inventory.
- **Labels go on the front of the bins** here: a tab on top is hard to read or scan at eye level.
- **Locations** are named by role, not position: `Wall › W-INBOX` and `Wall › W-PROJECT`.

A 2×4 shelf is about 17 cm wide, so two fit side by side in 35 cm if a second project shelf is needed later. Allow roughly 11–12 cm per shelf row so bins can be lifted out.

### Wall print list

| Part | Quantity |
|---|---|
| Multiboard Gridfinity shelf, 2×4 | 2 |
| Bin 4×2, 10u (inbox) | 1 |
| Bin 2×2, 6u (project) | 2 |
| Item holder for P700 tape cassettes (Multiconnect generator) | 1 |
| Tool holders | as needed |

Ready-made shelves: [Multiboard Gridfinity Shelf](https://www.printables.com/model/866656-multiboard-gridfinity-shelf-multiple-sizes) (1×2 to 2×4). Custom sizes and holders: [Multiconnect Part Generator](https://makerworld.com/en/models/582260-multiconnect-part-generator-master-collection).

## Labels

- One label per bin: QR code linking to the bin's storage location in Part-DB, plus the bin name (`D2-A1`).
- Scanning a bin's label shows everything in it.
- Keep QR contents short. On 9 mm tape a QR module is a single dot; a ~45-character Part-DB URL scanned fine in testing.

## Workflow

1. **Parcel arrives:** it goes into `W-INBOX` on the wall.
2. **Put a part away:** open the capture app, scan the bin's label, photograph the part. The part is created with that bin as its storage location. No time to sort? Capture it into `W-INBOX` and move it later.
3. **Find a part:** search in Part-DB or the capture app; the result names the bin.
4. **Before ordering:** run the "do I already have it?" check; a hit names the bin.

## Open items

- [x] Measure a drawer: 235 × 380 × 85 mm → 5×9 grid, max 9u
- [ ] Confirm the 378 mm grid fits the depth (2 mm slack) with the test baseplates
- [ ] First real run of `storage/generate.py` on the Mac (written and validated without CadQuery; rendering untested)
- [ ] Create the storage location trees in Part-DB (`Helmer › D1…D6 › bins`, `Wall › W-INBOX / W-PROJECT`)
- [ ] Bin labels: the capture API prints part labels today; storage-location labels are still missing
- [ ] PWA: "scan bin, then photograph part" flow
