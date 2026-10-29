import { useEffect, useRef, useState } from "react";
import {
  createPart,
  identify,
  labelPreviewUrl,
  printLabel,
  storageLocations,
  type CreatedPart,
  type IdentifiedItem,
  type IdentifyResult,
  type StorageLocation,
} from "./api";
import { shrinkPhoto } from "./image";

type Screen =
  | { kind: "capture" }
  | { kind: "result"; result: IdentifyResult }
  | { kind: "review"; result: IdentifyResult; index: number }
  | { kind: "saved"; result: IdentifyResult; part: CreatedPart };

const PATH_SEPARATOR = " › ";
const splitPath = (s: string) => s.split(/\s*[›>/→]\s*/).map((p) => p.trim()).filter(Boolean);

export default function App() {
  const [screen, setScreen] = useState<Screen>({ kind: "capture" });
  const [photos, setPhotos] = useState<Blob[]>([]);

  const restart = () => {
    setPhotos([]);
    setScreen({ kind: "capture" });
  };

  return (
    <main>
      <header>
        <h1 onClick={restart}>Shelfie</h1>
      </header>
      {screen.kind === "capture" && (
        <Capture photos={photos} setPhotos={setPhotos} onResult={(result) => setScreen({ kind: "result", result })} />
      )}
      {screen.kind === "result" && (
        <Result
          result={screen.result}
          onAdd={(index) => setScreen({ kind: "review", result: screen.result, index })}
          onRestart={restart}
        />
      )}
      {screen.kind === "review" && (
        <Review
          item={screen.result.items[screen.index].item}
          photos={photos}
          onBack={() => setScreen({ kind: "result", result: screen.result })}
          onSaved={(part) => setScreen({ kind: "saved", result: screen.result, part })}
        />
      )}
      {screen.kind === "saved" && (
        <Saved
          part={screen.part}
          hasMoreItems={screen.result.items.length > 1}
          onBack={() => setScreen({ kind: "result", result: screen.result })}
          onRestart={restart}
        />
      )}
    </main>
  );
}

// ------------------------------------------------------------------ 1. capture

function Capture(props: { photos: Blob[]; setPhotos: (p: Blob[]) => void; onResult: (r: IdentifyResult) => void }) {
  const { photos, setPhotos, onResult } = props;
  const [hint, setHint] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const cameraInput = useRef<HTMLInputElement>(null);
  const galleryInput = useRef<HTMLInputElement>(null);

  const addFiles = async (files: FileList | null) => {
    if (!files?.length) return;
    const shrunk = await Promise.all([...files].map(shrinkPhoto));
    setPhotos([...photos, ...shrunk].slice(0, 4));
  };

  const run = async () => {
    setBusy(true);
    setError(null);
    try {
      onResult(await identify(photos, hint));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section>
      <p className="muted">Photograph a part, its bag or a product page. Up to 4 photos of the same thing.</p>
      <div className="thumbs">
        {photos.map((p, i) => (
          <Thumb key={i} blob={p} onRemove={() => setPhotos(photos.filter((_, j) => j !== i))} />
        ))}
      </div>
      <input ref={cameraInput} type="file" accept="image/*" capture="environment" hidden
        onChange={(e) => { addFiles(e.target.files); e.target.value = ""; }} />
      <input ref={galleryInput} type="file" accept="image/*" multiple hidden
        onChange={(e) => { addFiles(e.target.files); e.target.value = ""; }} />
      <div className="row">
        <button onClick={() => cameraInput.current?.click()} disabled={busy || photos.length >= 4}>📷 Camera</button>
        <button className="secondary" onClick={() => galleryInput.current?.click()} disabled={busy || photos.length >= 4}>
          🖼 Gallery
        </button>
      </div>
      <label>
        Hint (optional)
        <input value={hint} onChange={(e) => setHint(e.target.value)} placeholder="e.g. from my last AliExpress order" />
      </label>
      <button className="primary" onClick={run} disabled={busy || photos.length === 0}>
        {busy ? "Identifying… (≈10 s)" : "Identify"}
      </button>
      {error && <p className="error">{error}</p>}
    </section>
  );
}

function Thumb({ blob, onRemove }: { blob: Blob; onRemove: () => void }) {
  const [url, setUrl] = useState<string>();
  useEffect(() => {
    const u = URL.createObjectURL(blob);
    setUrl(u);
    return () => URL.revokeObjectURL(u);
  }, [blob]);
  return (
    <div className="thumb">
      {url && <img src={url} alt="" />}
      <button aria-label="Remove photo" onClick={onRemove}>×</button>
    </div>
  );
}

// ------------------------------------------------------------------ 2. result

function Result(props: { result: IdentifyResult; onAdd: (index: number) => void; onRestart: () => void }) {
  const { result, onAdd, onRestart } = props;
  return (
    <section>
      {result.items.length === 0 && <p>No parts recognised.</p>}
      {result.items.map(({ item, in_stock }, i) => (
        <article key={i} className="card">
          <h2>{item.name}</h2>
          <p className="muted">
            {item.category_path.join(PATH_SEPARATOR)}
            {item.quantity_estimate ? ` · ${item.quantity_estimate} pcs` : ""} · {Math.round(item.confidence * 100)}% sure
          </p>
          {in_stock.length > 0 ? (
            <div className="stock yes">
              <strong>Already in stock</strong>
              <ul>
                {in_stock.map((m) => (
                  <li key={m.id}>
                    <a href={m.url} target="_blank" rel="noreferrer">{m.name}</a>: {m.total_amount} pcs
                    {m.locations.length > 0 && <> in {m.locations.join(", ")}</>}
                  </li>
                ))}
              </ul>
            </div>
          ) : (
            <div className="stock no">Not in stock yet</div>
          )}
          {item.notes && <p className="note">{item.notes}</p>}
          <button className={in_stock.length ? "secondary" : "primary"} onClick={() => onAdd(i)}>
            {in_stock.length ? "Add as new part anyway" : "Add to inventory"}
          </button>
        </article>
      ))}
      {result.needs_better_photo && result.photo_advice && <p className="note">📷 {result.photo_advice}</p>}
      <button className="secondary" onClick={onRestart}>New photo</button>
    </section>
  );
}

// ------------------------------------------------------------------ 3. review

function Review(props: { item: IdentifiedItem; photos: Blob[]; onBack: () => void; onSaved: (p: CreatedPart) => void }) {
  const { item, photos, onBack, onSaved } = props;
  const [name, setName] = useState(item.name);
  const [mpn, setMpn] = useState(item.mpn ?? "");
  const [manufacturer, setManufacturer] = useState(item.manufacturer ?? "");
  const [pkg, setPkg] = useState(item.package ?? "");
  const [category, setCategory] = useState(item.category_path.join(PATH_SEPARATOR));
  const [quantity, setQuantity] = useState(item.quantity_estimate?.toString() ?? "");
  const [location, setLocation] = useState(() => localStorageGet("lastLocation") ?? "");
  const [notes, setNotes] = useState(item.notes ?? "");
  const [locations, setLocations] = useState<StorageLocation[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    storageLocations().then(setLocations).catch(() => {});
  }, []);

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const part = await createPart(
        {
          name: name.trim(),
          category_path: splitPath(category),
          mpn: mpn.trim() || null,
          manufacturer: manufacturer.trim() || null,
          package: pkg.trim() || null,
          key_parameters: item.key_parameters,
          visible_markings: item.visible_markings,
          notes: notes.trim() || null,
          quantity: quantity.trim() === "" ? null : Number(quantity),
          storage_location_path: splitPath(location),
        },
        photos,
      );
      if (location.trim()) localStorageSet("lastLocation", location.trim());
      onSaved(part);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const valid = name.trim() !== "" && splitPath(category).length > 0 && !(quantity !== "" && Number(quantity) < 0);

  return (
    <section>
      <label>Name<input value={name} onChange={(e) => setName(e.target.value)} /></label>
      <label>Category<input value={category} onChange={(e) => setCategory(e.target.value)} /></label>
      <div className="row">
        <label>MPN<input value={mpn} onChange={(e) => setMpn(e.target.value)} /></label>
        <label>Package<input value={pkg} onChange={(e) => setPkg(e.target.value)} /></label>
      </div>
      <label>Manufacturer<input value={manufacturer} onChange={(e) => setManufacturer(e.target.value)} /></label>
      <div className="row">
        <label>Quantity
          <input type="number" inputMode="numeric" min={0} value={quantity} placeholder="unknown"
            onChange={(e) => setQuantity(e.target.value)} />
        </label>
        <label>Storage location
          <input value={location} list="locations" placeholder="e.g. D2-A1 or W-INBOX"
            onChange={(e) => setLocation(e.target.value)} />
          <datalist id="locations">
            {locations.map((l) => <option key={l.path} value={l.name} label={l.path} />)}
          </datalist>
        </label>
      </div>
      {item.key_parameters.length > 0 && (
        <p className="muted">
          {item.key_parameters.map((p) => `${p.name}: ${p.value}${p.unit ? " " + p.unit : ""}`).join(" · ")}
        </p>
      )}
      <label>Notes<textarea rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} /></label>
      <button className="primary" onClick={save} disabled={busy || !valid}>
        {busy ? "Saving…" : `Save${photos.length ? ` with ${photos.length} photo${photos.length > 1 ? "s" : ""}` : ""}`}
      </button>
      <button className="secondary" onClick={onBack} disabled={busy}>Back</button>
      {error && <p className="error">{error}</p>}
    </section>
  );
}

// ------------------------------------------------------------------ 4. saved

function Saved(props: { part: CreatedPart; hasMoreItems: boolean; onBack: () => void; onRestart: () => void }) {
  const { part, hasMoreItems, onBack, onRestart } = props;
  const [printing, setPrinting] = useState<"idle" | "busy" | "done">("idle");
  const [error, setError] = useState<string | null>(null);

  const print = async () => {
    setPrinting("busy");
    setError(null);
    try {
      await printLabel(part.id);
      setPrinting("done");
    } catch (e) {
      setError((e as Error).message);
      setPrinting("idle");
    }
  };

  return (
    <section>
      <p className="stock yes">✓ Saved <strong>{part.name}</strong></p>
      <img className="label" src={labelPreviewUrl(part.id)} alt="Label preview" />
      <button className="primary" onClick={print} disabled={printing === "busy"}>
        {printing === "busy" ? "Printing…" : printing === "done" ? "Printed ✓ Print again" : "🏷 Print label"}
      </button>
      {error && <p className="error">{error}</p>}
      <a className="button secondary" href={part.url} target="_blank" rel="noreferrer">Open in Part-DB</a>
      {hasMoreItems && <button className="secondary" onClick={onBack}>Back to the other parts</button>}
      <button className="secondary" onClick={onRestart}>Next part</button>
    </section>
  );
}

// localStorage can be unavailable (private mode); remembering the last location is only a convenience.
function localStorageGet(key: string): string | null {
  try { return localStorage.getItem(key); } catch { return null; }
}
function localStorageSet(key: string, value: string) {
  try { localStorage.setItem(key, value); } catch { /* ignore */ }
}
