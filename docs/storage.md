# Storage: Gridfinity bins in an IKEA Helmer

Physical layout of the inventory. Every bin is a storage location in Part-DB, with a QR label on the P700.

## The cabinet

- **IKEA Helmer**, 6 drawers, numbered **D1 (top) to D6 (bottom)**.
- **Grid per drawer: 5 × 9 units** (210 × 378 mm), 5 across the front, 9 deep.
- **Usable height: 90 mm** from the drawer floor to the underside of whatever is above it.

> **Measure once before printing:** the exact inner width and depth (to size the spacers that fill the margin around the grid), and the opening at the drawer's top rim. Metal drawers often have a folded top edge that is narrower than the floor; baseplates printed in two pieces get past it more easily.

### Height budget

Gridfinity bins are 7 mm per height unit (u), plus about 4.4 mm for the stacking lip. Budget about 5 mm for the baseplate.

| Bin height | Bin incl. lip | + baseplate | Fits in 90 mm? |
|---|---|---|---|
| 3u | 25 mm | ~30 mm | ✅ |
| 6u | 46 mm | ~51 mm | ✅ |
| 10u | 74 mm | ~79 mm | ✅ **tallest recommended** |
| 11u | 81 mm | ~86 mm | ⚠️ only 4 mm clearance |
| 12u | 88 mm | ~93 mm | ❌ |

Whatever sticks out of a bin counts too: a bag standing upright in a 10u bin must be **at most 7 cm tall**.

## Naming

- **Columns A–E** from left to right, **rows 1–9** from front to back.
- **A bin is named after its front-left cell:** the 2×3 bin covering A1–B3 in drawer 2 is **`D2-A1`**.
- **Compartments** of a divided bin get a suffix: `D1-A1.1`, `D1-A1.2`, from left to right.
- **In Part-DB** the storage location tree is `Helmer › D2 › D2-A1`. The leaf name is unique on its own, which keeps the 9 mm labels short.

Names describe positions, not contents. When a bin's contents change, only Part-DB changes; the label stays.

## Drawer plan

A starting allocation. Swap drawers around as the collection grows; the layouts are independent.

| Drawer | Contents | Bins |
|---|---|---|
| D1 | Small loose parts: ICs, crystals, LEDs, buttons | 30 × 1×1 3u, 5 × 1×3 3u |
| D2 | Bagged parts | 6 × 2×3 10u, 3 × 1×3 10u |
| D3 | Bagged parts | 6 × 2×3 10u, 3 × 1×3 10u |
| D4 | Modules and sensors | 8 × 2×2 6u, 4 × 1×2 6u, 5 × 1×1 6u |
| D5 | Dev boards and displays | 6 × 2×3 6u, 3 × 1×3 6u |
| D6 | Cables, wire, tools | 3 × 3×3 10u, 3 × 2×3 10u |

Most AliExpress stock arrives in small zip bags, so two drawers are reserved for them. Keep parts in their bags and stand the bags upright in the 2×3 bins like index cards, front to back. One 2×3 bin holds roughly 15–25 bags.

### Layouts

Front of the drawer at the bottom of each grid.

**D1: small loose parts**
```
row 9  │ A7 │ B7 │ C7 │ D7 │ E7 │   1×3 (rows 7–9)
row 8  │    │    │    │    │    │
row 7  │    │    │    │    │    │
row 6  │ A6 │ B6 │ C6 │ D6 │ E6 │
 ...   │ 1×1 bins, rows 1–6     │
row 1  │ A1 │ B1 │ C1 │ D1 │ E1 │
         A    B    C    D    E
```

**D2 / D3: bagged parts** (D5 uses the same layout at 6u)
```
row 7–9 │  A7 (2×3)  │  C7 (2×3)  │ E7 (1×3) │
row 4–6 │  A4 (2×3)  │  C4 (2×3)  │ E4 (1×3) │
row 1–3 │  A1 (2×3)  │  C1 (2×3)  │ E1 (1×3) │
             A    B       C    D       E
```

**D4: modules and sensors**
```
row 9   │ A9 │ B9 │ C9 │ D9 │ E9 │   1×1
row 7–8 │  A7 (2×2) │  C7 (2×2) │ E7 (1×2) │
row 5–6 │  A5 (2×2) │  C5 (2×2) │ E5 (1×2) │
row 3–4 │  A3 (2×2) │  C3 (2×2) │ E3 (1×2) │
row 1–2 │  A1 (2×2) │  C1 (2×2) │ E1 (1×2) │
```

**D6: cables, wire, tools**
```
row 7–9 │   A7 (3×3)    │  D7 (2×3)  │
row 4–6 │   A4 (3×3)    │  D4 (2×3)  │
row 1–3 │   A1 (3×3)    │  D1 (2×3)  │
             A   B   C      D    E
```

## Print list

### Baseplates

Per drawer one **5×5** and one **5×4** baseplate (both fit the A1's 256 × 256 mm bed), plus spacers for the margin once measured. Magnets aren't needed; bins in a drawer don't slide.

| Part | Quantity |
|---|---|
| Baseplate 5×5 | 6 |
| Baseplate 5×4 | 6 |

### Bins

| Size | Height | Quantity | Drawers |
|---|---|---|---|
| 1×1 | 3u | 30 | D1 |
| 1×3 | 3u | 5 | D1 |
| 1×1 | 6u | 5 | D4 |
| 1×2 | 6u | 4 | D4 |
| 2×2 | 6u | 8 | D4 |
| 2×3 | 6u | 6 | D5 |
| 1×3 | 6u | 3 | D5 |
| 2×3 | 10u | 15 | D2, D3, D6 |
| 1×3 | 10u | 6 | D2, D3 |
| 3×3 | 10u | 3 | D6 |

**Print order:** D2 first (bags are most of the stock), then D1, then the rest as needed. No need to fill every drawer on day one.

**Settings:** a generator such as Gridfinity Rebuilt (OpenSCAD) or the parametric Gridfinity models on MakerWorld; label tabs on, stacking lip on, magnet holes off. Choose a label tab that fits **9 mm tape**, the size currently loaded in the P700.

## Labels

- One label per bin: QR code linking to the bin's storage location in Part-DB, plus the bin name (`D2-A1`).
- Scanning a bin's label shows everything in it.
- Keep QR contents short. On 9 mm tape a QR module is a single dot; a ~45-character Part-DB URL scanned fine in testing.

## Workflow

1. **Put a part away:** open the capture app, scan the bin's label, photograph the part. The part is created with that bin as its storage location.
2. **Find a part:** search in Part-DB or the capture app; the result names the bin.
3. **Before ordering:** run the "do I already have it?" check; a hit names the bin.

## Open items

- [ ] Measure inner width, depth and top-rim opening of a drawer; size spacers
- [ ] Create the storage location tree in Part-DB (`Helmer › D1…D6 › bins`)
- [ ] Bin labels: the capture API prints part labels today; storage-location labels are still missing
- [ ] PWA: "scan bin, then photograph part" flow
