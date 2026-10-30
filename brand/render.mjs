// Render the Shelfie icon SVGs to the PNG sizes the PWA needs.
//   node brand/render.mjs
// Needs Playwright with Chromium: npm i -g playwright && npx playwright install chromium
// (a global install is found via NODE_PATH="$(npm root -g)").
// Writes to capture-web/public/ and brand/png/.
import { createRequire } from "node:module";
import { readFile, mkdir, copyFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const { chromium } = createRequire(import.meta.url)("playwright");
const here = dirname(fileURLToPath(import.meta.url));
const pub = join(here, "..", "capture-web", "public");
const png = join(here, "png");

const jobs = [
  // [source svg, output name, size]
  ["shelfie-icon.svg", "icon-192.png", 192],
  ["shelfie-icon.svg", "icon-512.png", 512],
  ["shelfie-icon.svg", "favicon-32.png", 32],
  ["shelfie-icon-fullbleed.svg", "icon-maskable-512.png", 512],
  ["shelfie-icon-square.svg", "apple-touch-icon.png", 180],
  ["shelfie-icon-mono.svg", "icon-mono-256.png", 256],
];

await mkdir(png, { recursive: true });
const browser = await chromium.launch();
const page = await browser.newPage();
for (const [src, out, size] of jobs) {
  const svg = await readFile(join(here, src), "utf8");
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(
    `<html><body style="margin:0;background:transparent">` +
    svg.replace("<svg ", `<svg width="${size}" height="${size}" `) +
    `</body></html>`);
  await page.screenshot({ path: join(png, out), omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
  console.log(`brand/png/${out}  ${size}×${size}`);
}
await browser.close();

for (const f of ["icon-192.png", "icon-512.png", "icon-maskable-512.png", "apple-touch-icon.png", "favicon-32.png"]) {
  await copyFile(join(png, f), join(pub, f));
}
await copyFile(join(here, "shelfie-icon.svg"), join(pub, "icon.svg"));
console.log("copied to capture-web/public/");
