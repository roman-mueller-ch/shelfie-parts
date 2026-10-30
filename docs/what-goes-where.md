# What goes where

How parts are sorted into the IKEA Helmer and the Multiboard wall. Use this when choosing a storage location for a part, in code (e.g. suggesting a bin in the capture app) or by hand.

Layout and print details: [storage.md](storage.md). Machine-readable source of truth: `storage/plan.json` (if this file and the plan disagree, the plan wins; update this file).

## The basics

- **6 drawers**, D1 at the top to D6 at the bottom. Each drawer holds a 5 × 9 Gridfinity grid: columns **A–E** left to right, rows **1–9** front to back.
- **A bin is named after its front-left cell:** `D2-A4` = drawer 2, cell A4. In Part-DB: `Helmer › D2 › D2-A4`. D1's compartments add a suffix: `D1-A1.1` to `D1-A1.6`, left to right, then front to back.
- **Names are positions, not contents.** When a bin's contents change, only Part-DB changes; the label stays.
- **Rule of thumb:** heavy and rarely used at the bottom, small and frequent at the top.

## Drawers

| Drawer | Contents | Bins | Colour |
|---|---|---|---|
| **D1** | **Small loose parts**: bare ICs, crystals, LEDs, buttons, transistors, diodes, small passives | 6 × 2×3 (6 compartments) + 3 × 1×3 (3 compartments), 3u: 45 compartments | white |
| **D2** | **Bagged parts**: AliExpress zip bags standing upright like index cards | 6 × 2×3 + 3 × 1×3, 9u | blue |
| **D3** | **More bagged parts**: overflow from D2 | same as D2 | blue |
| **D4** | **Modules and sensors**: breakout boards (BME280, TP4056, HX711, relay and motor driver boards) | 8 × 2×2 + 2 × 2×1 + 3 × 1×3, 6u | green |
| **D5** | **Dev boards and displays**: ESP32, Pi Pico, Arduino, OLED/TFT displays | 6 × 2×3 + 3 × 1×3, 6u | green |
| **D6** | **Bulky items**: cables, jumper wires, wire spools, power supplies, tools | 3 × 3×3 + 3 × 2×3, 9u | grey |

## Wall (Multiboard, next to the desk)

- **`W-INBOX`:** new parcels until they're captured with the app. A queue, not storage: parts captured without time to sort go here and are moved later.
- **`W-PROJECT`** (2 bins): parts pulled for the current project. Move them in Part-DB when taken out and when put back.

## Deciding where a part goes

1. **Still in its bag?** → D2, then D3 when D2 is full. Most AliExpress orders; the bag keeps parts and their label together.
2. **Loose and tiny?** → a D1 compartment.
3. **A board with pins?** → D4 for a module or sensor, D5 for a dev board or display.
4. **Too big for a 9u bin (about 7 cm standing), a cable or a tool?** → D6.
5. **No time to sort?** → `W-INBOX`.

Within a drawer, keep similar parts together (e.g. in D4: sensors front left, power modules front right, motor drivers at the back). Exact positions don't matter; the app shows which bin a part is in.

### From the vision model's category to a drawer

The capture API's vision model returns one of the fixed `category_path` values from `vision/prompt.md`. Default drawer per category:

| Category | Default |
|---|---|
| Active › ICs › … (bare chips), Passive › …, Discrete › …, Electromechanical › Switches / Buttons | D1 if loose, D2/D3 if bagged |
| Modules › … | D4 |
| Dev boards, Modules › Display | D5 |
| Electromechanical › Relays / Motors / Buzzers / Speakers | D4 if a board, D2/D3 if bagged |
| Connectors | D2/D3 if bagged, D1 if loose |
| Cables, Power supplies, Tools, Mechanical | D6 |

"Bagged" vs "loose" isn't something the model reports reliably; when in doubt, the bag drawers (D2/D3) are the default for anything from AliExpress.

## Status (2026-10-30)

- Baseplates printed; first 2×3×9u bin printed in white and listed as `D2-A1` under `printed` in `plan.json` (D2 is planned in blue).
- 57 bins left; `storage/plates.py` packs them onto 14 A1 plates.
- All 109 locations exist in Part-DB (`storage/sync_locations.py`); capturing into D2 can start.
