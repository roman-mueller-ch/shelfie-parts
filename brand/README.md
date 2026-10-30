# Shelfie brand

The icon: a 3 × 3 grid of Gridfinity bins with a camera lens in the centre cell, inside camera viewfinder corners: point the camera at your bins and know what's in them.

| | |
|---|---|
| Ink (background) | `#17233B` |
| Accent (viewfinder, lens) | `#F2B33D`, label-tape yellow |
| Paper (bins) | `#F6F3EC` |
| Wordmark | "Shelfie" in Space Grotesk Bold, letter-spacing −0.02em |
| Tagline | Snap a part. Know what you have. |

## Files

| File | Use |
|---|---|
| `shelfie-icon.svg` | Master icon, rounded corners: favicon, README, anywhere |
| `shelfie-icon-square.svg` | Square corners, full-size mark: iOS home screen (iOS rounds it) |
| `shelfie-icon-fullbleed.svg` | Square, mark scaled into the 80 % safe zone: Android "maskable" icon |
| `shelfie-icon-mono.svg` | Black only, no background: P-touch labels and 1-bit printing |
| `png/` | Rendered PNGs (192, 512, maskable 512, apple-touch 180, favicon 32, mono 256) |

`node brand/render.mjs` re-renders the PNGs from the SVGs with Playwright/Chromium and copies the PWA's files into `capture-web/public/`. Edit the SVGs, never the PNGs.

Design canvas with the explored directions (A, B, C, C1–C3; C1 was chosen): kept in Claude, not in the repo.
