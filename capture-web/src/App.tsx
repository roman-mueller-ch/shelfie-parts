import { useEffect, useRef, useState } from "react";
import {
  addStock,
  createPart,
  identify,
  labelPreviewUrl,
  locationLabelPreviewUrl,
  printLabel,
  printLocationLabel,
  storageLocations,
  type CreatedPart,
  type IdentifiedItem,
  type IdentifyResult,
  type StockMatch,
  type StorageLocation,
} from "./api";
import { shrinkPhoto } from "./image";

type Screen =
  | { kind: "capture" }
  | { kind: "result"; result: IdentifyResult }
  | { kind: "review"; result: IdentifyResult; index: number }
  | { kind: "restock"; result: IdentifyResult; index: number; match: StockMatch }
  | { kind: "saved"; result: IdentifyResult; done: Done }
  | { kind: "bins" };

/** What the last screen shows: a new part or restocked one. */
interface Done {
  id: number;
  url: string;
  message: string;
  /**
   * New parts and stock in a new bin get a label right away. Restocking an existing bin
   * only offers one (its box is labelled already, unless the parts move to another bin).
   */
  label: "show" | "offer";
  /** Bin name for the label's second line, e.g. "D2-A1". */
  bin?: string;
}

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
        {screen.kind !== "bins" && <button className="link" onClick={() => setScreen({ kind: "bins" })}>Bin labels</button>}
      </header>
      {screen.kind === "capture" && (
        <Capture photos={photos} setPhotos={setPhotos} onResult={(result) => setScreen({ kind: "result", result })} />
      )}
      {screen.kind === "result" && (
        <Result
          result={screen.result}
          onAdd={(index) => setScreen({ kind: "review", result: screen.result, index })}
          onRestock={(index, match) => setScreen({ kind: "restock", result: screen.result, index, match })}
          onRestart={restart}
        />
      )}
      {screen.kind === "review" && (
        <Review
          item={screen.result.items[screen.index].item}
          photos={photos}
          onBack={() => setScreen({ kind: "result", result: screen.result })}
          onSaved={(part: CreatedPart) =>
            setScreen({ kind: "saved", result: screen.result, done: { id: part.id, url: part.url, message: `Saved ${part.name}`, label: "show" } })}
        />
      )}
      {screen.kind === "restock" && (
        <Restock
          match={screen.match}
          suggested={screen.result.items[screen.index].item.quantity_estimate}
          onBack={() => setScreen({ kind: "result", result: screen.result })}
          onDone={(done) => setScreen({ kind: "saved", result: screen.result, done })}
        />
      )}
      {screen.kind === "saved" && (
        <Saved
          done={screen.done}
          hasMoreItems={screen.result.items.length > 1}
          onBack={() => setScreen({ kind: "result", result: screen.result })}
          onRestart={restart}
        />
      )}
      {screen.kind === "bins" && <Bins onBack={restart} />}
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

function Result(props: {
  result: IdentifyResult;
  onAdd: (index: number) => void;
  onRestock: (index: number, match: StockMatch) => void;
  onRestart: () => void;
}) {
  const { result, onAdd, onRestock, onRestart } = props;
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
              {in_stock.map((m) => (
                <div key={m.id} className="match">
                  <span>
                    <a href={m.url} target="_blank" rel="noreferrer">{m.name}</a>: {m.total_amount} pcs
                    {m.locations.length > 0 && <> in {m.locations.join(", ")}</>}
                  </span>
                  <button className="primary small" onClick={() => onRestock(i, m)}>+ Add to stock</button>
                </div>
              ))}
            </div>
          ) : (
            <div className="stock no">Not in stock yet</div>
          )}
          {item.notes && <p className="note">{item.notes}</p>}
          {in_stock.length ? (
            <button className="link" onClick={() => onAdd(i)}>It's a different part: add as new</button>
          ) : (
            <button className="primary" onClick={() => onAdd(i)}>Add to inventory</button>
          )}
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

// ------------------------------------------------------------------ 3b. restock

function Restock(props: {
  match: StockMatch;
  suggested: number | null;
  onBack: () => void;
  onDone: (done: Done) => void;
}) {
  const { match, suggested, onBack, onDone } = props;
  const [quantity, setQuantity] = useState(suggested?.toString() ?? "1");
  const [location, setLocation] = useState(match.locations[0] ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const r = await addStock(match.id, Number(quantity), splitPath(location));
      const where = r.location ? ` in ${r.location}` : "";
      onDone({
        id: r.id,
        url: r.url,
        message: `Added ${r.added} to ${r.name}${where}, now ${r.total_amount} pcs`,
        label: r.new_lot ? "show" : "offer",
        bin: r.location ?? undefined,
      });
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const valid = Number(quantity) > 0;
  return (
    <section>
      <h2>{match.name}</h2>
      <p className="muted">
        Now {match.total_amount} pcs{match.locations.length > 0 && <> in {match.locations.join(", ")}</>}
      </p>
      <div className="row">
        <label>Add pieces
          <input type="number" inputMode="numeric" min={1} value={quantity} autoFocus
            onChange={(e) => setQuantity(e.target.value)} />
        </label>
        <label>Storage location
          <input value={location} placeholder="e.g. D2-A1" onChange={(e) => setLocation(e.target.value)} />
        </label>
      </div>
      <p className="muted">A different location gets its own stock entry for this part.</p>
      <button className="primary" onClick={save} disabled={busy || !valid}>{busy ? "Saving…" : "Add to stock"}</button>
      <button className="secondary" onClick={onBack} disabled={busy}>Back</button>
      {error && <p className="error">{error}</p>}
    </section>
  );
}

// ------------------------------------------------------------------ 4. saved

function Saved(props: { done: Done; hasMoreItems: boolean; onBack: () => void; onRestart: () => void }) {
  const { done, hasMoreItems, onBack, onRestart } = props;
  const [showLabel, setShowLabel] = useState(done.label === "show");
  const [bin, setBin] = useState(done.bin ?? "");
  const [previewBin, setPreviewBin] = useState(bin); // trails typing, so the Pi renders one preview, not one per key
  useEffect(() => {
    const t = setTimeout(() => setPreviewBin(bin.trim()), 600);
    return () => clearTimeout(t);
  }, [bin]);
  const [printing, setPrinting] = useState<"idle" | "busy" | "done">("idle");
  const [error, setError] = useState<string | null>(null);

  const print = async () => {
    setPrinting("busy");
    setError(null);
    try {
      await printLabel(done.id, bin.trim() || undefined);
      setPrinting("done");
    } catch (e) {
      setError((e as Error).message);
      setPrinting("idle");
    }
  };

  return (
    <section>
      <p className="stock yes">✓ {done.message}</p>
      {done.label === "show" && done.bin && <p className="muted">New bin for this part: label its box.</p>}
      {showLabel ? (
        <>
          {done.label === "offer" && (
            <label>Bin on the label<input value={bin} onChange={(e) => setBin(e.target.value)} placeholder="e.g. D2-A1" /></label>
          )}
          <img className="label" src={labelPreviewUrl(done.id, previewBin || undefined)} alt="Label preview" />
          <button className="primary" onClick={print} disabled={printing === "busy"}>
            {printing === "busy" ? "Printing…" : printing === "done" ? "Printed ✓ Print again" : "🏷 Print label"}
          </button>
          {error && <p className="error">{error}</p>}
        </>
      ) : (
        <button className="link" onClick={() => setShowLabel(true)}>Print a label anyway (e.g. moving to a new bin)</button>
      )}
      <a className="button secondary" href={done.url} target="_blank" rel="noreferrer">Open in Part-DB</a>
      {hasMoreItems && <button className="secondary" onClick={onBack}>Back to the other parts</button>}
      <button className="secondary" onClick={onRestart}>Next part</button>
    </section>
  );
}

// ------------------------------------------------------------------ bin labels

// Compartments of a divided bin are named like "D1-A1.3" (docs/storage.md); they rarely need their own label.
const isCompartment = (l: StorageLocation) => /\.\d+$/.test(l.name);

function Bins(props: { onBack: () => void }) {
  const [locations, setLocations] = useState<StorageLocation[] | null>(null);
  const [filter, setFilter] = useState("");
  const [compartments, setCompartments] = useState(false);
  const [selected, setSelected] = useState<number | null>(null);
  const [printed, setPrinted] = useState<Set<number>>(new Set());
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    storageLocations().then(setLocations).catch((e) => setError((e as Error).message));
  }, []);

  const q = filter.trim().toLowerCase();
  const shown = (locations ?? []).filter(
    (l) => (compartments || !isCompartment(l)) && (!q || l.path.toLowerCase().includes(q)),
  );

  const print = async (list: StorageLocation[]) => {
    setError(null);
    for (const [i, l] of list.entries()) {
      setBusy(list.length > 1 ? `Printing ${i + 1} of ${list.length}…` : "Printing…");
      try {
        await printLocationLabel(l.id);
        setPrinted((p) => new Set(p).add(l.id));
      } catch (e) {
        setError(`${l.name}: ${(e as Error).message}`);
        break;
      }
    }
    setBusy(null);
  };

  const printAll = () => {
    if (window.confirm(`Print ${shown.length} labels?`)) print(shown);
  };

  return (
    <section>
      <h2>Bin labels</h2>
      <p className="muted">Scanning a bin's label opens the list of parts in it.</p>
      <label>Filter<input value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="e.g. D2 or Wall" /></label>
      <label className="check">
        <input type="checkbox" checked={compartments} onChange={(e) => setCompartments(e.target.checked)} />
        Show compartments (D1-A1.1 …)
      </label>
      {locations === null && !error && <p className="muted">Loading…</p>}
      <ul className="locations">
        {shown.map((l) => (
          <li key={l.id}>
            <div className="match">
              <button className="link" onClick={() => setSelected(selected === l.id ? null : l.id)}>
                <strong>{l.name}</strong> <span className="muted">{l.path}</span>
              </button>
              {printed.has(l.id) && <span className="muted">✓</span>}
              <button className="small" disabled={busy !== null} onClick={() => print([l])}>🏷 Print</button>
            </div>
            {selected === l.id && <img className="label" src={locationLabelPreviewUrl(l.id)} alt={`Label for ${l.name}`} />}
          </li>
        ))}
      </ul>
      {error && <p className="error">{error}</p>}
      {busy && <p className="muted">{busy}</p>}
      {shown.length > 1 && (
        <button className="primary" disabled={busy !== null} onClick={printAll}>Print all {shown.length} shown</button>
      )}
      <button className="secondary" onClick={props.onBack}>Back to capture</button>
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
