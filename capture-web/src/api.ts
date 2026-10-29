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

export async function printLabel(id: number): Promise<void> {
  await check(await fetch(`/api/parts/${id}/label`, { method: "POST" }));
}

export const labelPreviewUrl = (id: number) => `/api/parts/${id}/label.png`;

export async function storageLocations(): Promise<string[]> {
  const response = await check(await fetch("/api/storage-locations"));
  return response.json();
}
