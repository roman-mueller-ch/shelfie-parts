# Storage: Gridfinity bins in an IKEA Helmer and on the wall

Physical layout of the inventory. Every bin is a storage location in Part-DB, with a QR label on the P700.

## The cabinet

- **IKEA Helmer**, 6 drawers, numbered **D1 (top) to D6 (bottom)**.
- **Inside of a drawer: 235 × 365 × 85 mm** (width × depth × height), measured 2026-10-29.
- **Grid per drawer: 5 × 8 units** (210 × 336 mm), 5 across the front, 8 deep.
- **Margin:** 25 mm in width, 29 mm in depth. Fill it with padding on the baseplates (see Print list): 12.5 mm left and right, 29 mm at the back.

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

- **Columns A–E** from left to right, **rows 1–8** from front to back.
- **A bin is named after its front-left cell:** the 2×4 bin covering A1–B4 in drawer 2 is **`D2-A1`**.
- **Compartments** of a divided bin get a suffix, counted left to right, then front to back: `D1-A1.1` to `D1-A1.8` in a 2×4 bin with eight compartments.
- **In Part-DB** the storage location tree is `Helmer › D2 › D2-A1`. The leaf name is unique on its own, which keeps the 9 mm labels short.

Names describe positions, not contents. When a bin's contents change, only Part-DB changes; the label stays.

## Drawer plan

A starting allocation. Swap drawers around as the collection grows; the layouts are independent.

| Drawer | Contents | Bins |
|---|---|---|
| D1 | Small loose parts: ICs, crystals, LEDs, buttons | 4 × 2×4 3u (8 compartments), 2 × 1×4 3u (4 compartments) |
| D2 | Bagged parts | 4 × 2×4 9u, 2 × 1×4 9u |
| D3 | Bagged parts | 4 × 2×4 9u, 2 × 1×4 9u |
| D4 | Modules and sensors | 8 × 2×2 6u, 2 × 1×4 6u |
| D5 | Dev boards and displays | 4 × 2×4 6u, 2 × 1×4 6u |
| D6 | Cables, wire, tools | 2 × 3×4 9u, 2 × 2×4 9u |

**No 1×1 bins.** The smallest bins are 2×2 and 1×4. Small loose parts go into compartments of larger bins instead: D1 offers 40 compartments in only 6 bins. Use 1×1 bins only for something that really needs its own box.

Most AliExpress stock arrives in small zip bags, so two drawers are reserved for them. Keep parts in their bags and stand the bags upright in the 2×4 bins like index cards, front to back. One 2×4 bin holds roughly 20–35 bags.

### Layouts

Front of the drawer at the bottom of each grid.

**D1, D2, D3 and D5** share one layout. D1 uses it at 3u with compartments (eight per 2×4 bin, four per 1×4 bin), D2 and D3 at 9u for bags, D5 at 6u for dev boards.
```
row 5–8 │  A5 (2×4)  │  C5 (2×4)  │ E5 (1×4) │
row 1–4 │  A1 (2×4)  │  C1 (2×4)  │ E1 (1×4) │
             A    B       C    D       E
```

**D4: modules and sensors**
```
row 7–8 │  A7 (2×2) │  C7 (2×2) │ E5 (1×4) │
row 5–6 │  A5 (2×2) │  C5 (2×2) │ rows 5–8 │
row 3–4 │  A3 (2×2) │  C3 (2×2) │ E1 (1×4) │
row 1–2 │  A1 (2×2) │  C1 (2×2) │ rows 1–4 │
             A    B      C    D       E
```

**D6: cables, wire, tools**
```
row 5–8 │   A5 (3×4)    │  D5 (2×4)  │
row 1–4 │   A1 (3×4)    │  D1 (2×4)  │
             A   B   C      D    E
```

## Print list

### Baseplates

Per drawer **two 5×4 baseplates** (each 210 × 168 mm before padding, well inside the A1's 256 × 256 mm bed). Most generators can add padding to a baseplate; use it instead of separate spacers:

| Piece | Grid | Padding | Outer size |
|---|---|---|---|
| Front | 5×4 | 12.5 mm left and right | 235 × 168 mm |
| Back | 5×4 | 12.5 mm left and right, 29 mm at the back | 235 × 197 mm |

Magnets aren't needed; bins in a drawer don't slide. For all six drawers: 6 front and 6 back pieces.

### Bins

| Size | Height | Quantity | Drawers |
|---|---|---|---|
| 2×4, 8 compartments | 3u | 4 | D1 |
| 1×4, 4 compartments | 3u | 2 | D1 |
| 2×2 | 6u | 8 | D4 |
| 1×4 | 6u | 4 | D4, D5 |
| 2×4 | 6u | 4 | D5 |
| 2×4 | 9u | 10 | D2, D3, D6 |
| 1×4 | 9u | 4 | D2, D3 |
| 3×4 | 9u | 2 | D6 |

38 bins in total for all six drawers.

**Start with a test print:** one front baseplate and one 2×4 bin at 9u with a label tab. Check that the bin drops in and lifts out easily, that the drawer closes with a bag standing in the bin, and that a 9 mm label fits the tab. Then print the rest of D2, then D1, then the other drawers as needed.

**Settings:** a generator such as Gridfinity Rebuilt (OpenSCAD) or the parametric Gridfinity models on MakerWorld; label tabs on, stacking lip on, magnet holes off. Choose a label tab that fits **9 mm tape**, the size currently loaded in the P700. For D1, set the compartment dividers in the generator (2 × 4 for the 2×4 bins, 1 × 4 for the 1×4 bins); scoop-shaped compartment floors make tiny parts easier to pick out.

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

- [x] Measure a drawer: 235 × 365 × 85 mm → 5×8 grid, max 9u
- [ ] Create the storage location trees in Part-DB (`Helmer › D1…D6 › bins`, `Wall › W-INBOX / W-PROJECT`)
- [ ] Bin labels: the capture API prints part labels today; storage-location labels are still missing
- [ ] PWA: "scan bin, then photograph part" flow
