// Types and calls for the capture API (capture-api/). Field names are snake_case like the API.

export interface KeyParameter {
  name: string;
  value: string;
  unit: string | null;
}

export interface IdentifiedItem {
  name: string;
  category_path: string[];
  mpn: string | null;
  manufacturer: string | null;
  package: string | null;
  key_parameters: KeyParameter[];
  visible_markings: string[];
  quantity_estimate: number | null;
  search_terms: string[];
  confidence: number;
  notes: string | null;
}

export interface StockMatch {
  id: number;
  name: string;
  mpn: string | null;
  category: string | null;
  total_amount: number;
  locations: string[];
  url: string;
  matched_term: string;
  same: boolean; // the same part, not just a similar one (ESP32-S3 vs ESP32-C3)
}

export interface IdentifyResult {
  source_type: string;
  items: { item: IdentifiedItem; in_stock: StockMatch[] }[];
  needs_better_photo: boolean;
  photo_advice: string | null;
  usage: { model: string; seconds: number; input_tokens: number; output_tokens: number };
}

export interface CreatePartRequest {
  name: string;
  category_path: string[];
  description?: string | null;
  mpn?: string | null;
  manufacturer?: string | null;
  package?: string | null;
  key_parameters?: KeyParameter[];
  visible_markings?: string[];
  notes?: string | null;
  quantity?: number | null;
  storage_location_path?: string[];
}

export interface CreatedPart {
  id: number;
  name: string;
  url: string;
  scan_url: string;
  photos: number;
  location: string | null;
  location_id: number | null;
  new_bin: boolean; // nothing was stored in the location before: its bin needs a label
}

async function check(response: Response): Promise<Response> {
  if (response.ok) return response;
  let message = `${response.status} ${response.statusText}`;
  try {
    const problem = await response.json();
    message = problem.detail ?? problem.title ?? message;
  } catch {
    // not JSON; keep the status text
  }
  throw new Error(message);
}

export async function identify(photos: Blob[], hint: string): Promise<IdentifyResult> {
  const form = new FormData();
  photos.forEach((p, i) => form.append("images", p, `photo-${i + 1}.jpg`));
  if (hint.trim()) form.append("hint", hint.trim());
  const response = await check(await fetch("/api/identify", { method: "POST", body: form }));
  return response.json();
}

export async function createPart(part: CreatePartRequest, photos: Blob[]): Promise<CreatedPart> {
  const form = new FormData();
  form.append("part", JSON.stringify(part));
  photos.forEach((p, i) => form.append("photos", p, `photo-${i + 1}.jpg`));
  const response = await check(await fetch("/api/parts", { method: "POST", body: form }));
  return response.json();
}

export interface StockAdded {
  id: number;
  name: string;
  url: string;
  added: number;
  total_amount: number;
  location: string | null;
  new_lot: boolean; // a location the part wasn't in yet
  location_id: number | null;
  new_bin: boolean;
}

export async function addStock(id: number, quantity: number, location: string[]): Promise<StockAdded> {
  const response = await check(
    await fetch(`/api/parts/${id}/stock`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ quantity, storage_location_path: location }),
    }),
  );
  return response.json();
}

/** subtitle overrides the label's second line (default: the part's first storage location). */
export async function printLabel(id: number, subtitle?: string): Promise<void> {
  await check(
    await fetch(`/api/parts/${id}/label`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(subtitle ? { subtitle } : {}),
    }),
  );
}

export const labelPreviewUrl = (id: number, subtitle?: string) =>
  `/api/parts/${id}/label.png` + (subtitle ? `?subtitle=${encodeURIComponent(subtitle)}` : "");

export interface StorageLocation {
  id: number;
  name: string; // e.g. "D2-A1", unique on its own
  path: string; // e.g. "Helmer / D2 / D2-A1"
}

export async function storageLocations(): Promise<StorageLocation[]> {
  const response = await check(await fetch("/api/storage-locations"));
  return response.json();
}

/** A bin's label: QR code to the parts in it, the bin name as text. */
export async function printLocationLabel(id: number): Promise<void> {
  await check(await fetch(`/api/storage-locations/${id}/label`, { method: "POST" }));
}

export const locationLabelPreviewUrl = (id: number) => `/api/storage-locations/${id}/label.png`;

/** Where a new part should go, from its category (docs/what-goes-where.md). */
export interface BinSuggestion {
  drawers: string[];
  reason: string;
  bin: string | null; // the bin being filled, else the first empty one
  filling: boolean;
  empty_bins: string[]; // in plan order, for "bin full → next bin"
}

export async function suggestBin(category: string[], bagged: boolean): Promise<BinSuggestion> {
  const q = new URLSearchParams(category.map((c) => ["category", c]));
  q.set("bagged", String(bagged));
  const response = await check(await fetch(`/api/bins/suggest?${q}`));
  return response.json();
}
