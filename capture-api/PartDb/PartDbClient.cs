using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaptureApi.Labels;
using CaptureApi.Vision;
using Microsoft.Extensions.Options;

namespace CaptureApi.PartDb;

public sealed record StockMatch(
    int Id,
    string Name,
    string? Mpn,
    string? Category,
    double TotalAmount,
    IReadOnlyList<string> Locations,
    string Url,
    string MatchedTerm,
    /// <summary>The same part, not just a similar one (ESP32-S3 vs ESP32-C3); only these are offered for restocking.</summary>
    bool Same);

public sealed record StorageLocation(int Id, string Name, string Path);

/// <summary>Talks to Part-DB's REST API (API Platform) with a personal API token.</summary>
public sealed class PartDbClient
{
    private const int MaxMatches = 10;
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public PartDbClient(HttpClient http, IOptions<PartDbOptions> options)
    {
        var o = options.Value;
        if (string.IsNullOrEmpty(o.BaseUrl) || string.IsNullOrEmpty(o.ApiToken))
            throw new InvalidOperationException("PartDb:BaseUrl and PartDb:ApiToken must be configured.");
        _baseUrl = o.BaseUrl.TrimEnd('/');
        _http = http;
        _http.BaseAddress = new Uri(_baseUrl + "/api/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiToken);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Parts whose name or MPN contains one of the item's search terms (case-insensitive),
    /// in the order of the terms, which the model lists most specific first.
    /// </summary>
    public async Task<IReadOnlyList<StockMatch>> SearchAsync(IdentifiedItem item, CancellationToken ct)
    {
        var terms = item.SearchTerms.Prepend(item.Mpn).OfType<string>()
            .Select(t => t.Trim()).Where(t => t.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase);

        var matches = new Dictionary<int, StockMatch>();
        foreach (var term in terms)
        {
            var pattern = "%" + EscapeLike(term) + "%";
            foreach (var field in new[] { "manufacturer_product_number", "name" })
            {
                using var doc = await GetAsync($"parts?{field}={Uri.EscapeDataString(pattern)}&itemsPerPage={MaxMatches}", ct);
                foreach (var part in doc.RootElement.EnumerateArray())
                {
                    var match = ToMatch(part, term, item);
                    matches.TryAdd(match.Id, match);
                }
            }
            if (matches.Count >= MaxMatches) break;
        }
        return matches.Values.OrderByDescending(m => m.Same).Take(MaxMatches).ToList();
    }

    /// <summary>
    /// Whether a stocked part is the identified one, not just similar: equal MPNs when both have one,
    /// otherwise every word of the most specific search term (e.g. "ESP32-S3 N16R8") in its name or MPN.
    /// </summary>
    internal static bool IsSamePart(IdentifiedItem item, string partName, string? partMpn)
    {
        if (!string.IsNullOrWhiteSpace(item.Mpn) && !string.IsNullOrWhiteSpace(partMpn))
            return Words(item.Mpn).SetEquals(Words(partMpn));
        var key = Words(item.SearchTerms.FirstOrDefault() ?? item.Mpn ?? item.Name);
        return key.Count > 0 && key.IsSubsetOf(Words($"{partName} {partMpn}"));
    }

    /// <summary>"ESP32-S3 dev board (N16R8)" → {ESP32S3, DEV, BOARD, N16R8}.</summary>
    private static HashSet<string> Words(string s) =>
        Regex.Split(s, @"[\s(),;/]+")
            .Select(w => Regex.Replace(w.ToUpperInvariant(), "[^A-Z0-9]", ""))
            .Where(w => w.Length > 0).ToHashSet();

    /// <summary>
    /// Creates the part with its stock lot, parameters and photos (the first becomes the preview picture).
    /// If any step fails, the half-created part is deleted again.
    /// </summary>
    public async Task<CreatedPart> CreatePartAsync(CreatePartRequest request, IReadOnlyList<(string FileName, BinaryData Data)> photos, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new PartDbException("Name is required.");
        if (request.CategoryPath is not { Count: > 0 }) throw new PartDbException("Category path is required.");

        var part = new Dictionary<string, object?>
        {
            ["name"] = request.Name.Trim(),
            ["description"] = request.Description ?? "",
            ["manufacturer_product_number"] = request.Mpn ?? "",
            ["comment"] = Comment(request),
            ["category"] = await ResolvePathAsync("categories", request.CategoryPath, ct),
        };
        if (!string.IsNullOrWhiteSpace(request.Manufacturer))
            part["manufacturer"] = await ResolvePathAsync("manufacturers", [request.Manufacturer], ct);
        if (!string.IsNullOrWhiteSpace(request.Package))
            part["footprint"] = await ResolvePathAsync("footprints", [request.Package], ct);

        var id = (await PostAsync("parts", part, ct)).RootElement.GetProperty("id").GetInt32();
        var partIri = $"/api/parts/{id}";
        StorageLocation? location = null;
        var newBin = false;
        try
        {
            var lot = new Dictionary<string, object?>
            {
                ["part"] = partIri,
                ["amount"] = request.Quantity ?? 0,
                ["instock_unknown"] = request.Quantity is null,
            };
            if (request.StorageLocationPath is { Count: > 0 })
            {
                var iri = await ResolvePathAsync("storage_locations", request.StorageLocationPath, ct);
                lot["storage_location"] = iri;
                newBin = !(await LotLocationsAsync(ct)).Contains(iri);
                using var loc = await GetAsync(iri["/api/".Length..], ct);
                location = ToLocation(loc.RootElement);
            }
            (await PostAsync("part_lots", lot, ct)).Dispose();

            foreach (var p in request.KeyParameters ?? [])
                (await PostAsync("parameters", Parameter(partIri, p), ct)).Dispose();

            if (photos.Count > 0)
            {
                var photoType = await ResolvePathAsync("attachment_types", ["Photo"], ct, new() { ["filetype_filter"] = "image/*" });
                foreach (var (fileName, data) in photos)
                    (await PostAsync("attachments", new Dictionary<string, object?>
                    {
                        ["_type"] = "Part",
                        ["element"] = partIri,
                        ["attachment_type"] = photoType,
                        ["name"] = "Photo",
                        ["upload"] = new { data = Convert.ToBase64String(data.ToArray()), filename = fileName },
                    }, ct)).Dispose();
            }
        }
        catch
        {
            using var _ = await _http.DeleteAsync($"parts/{id}", CancellationToken.None);
            throw;
        }

        return new CreatedPart(id, request.Name.Trim(), $"{_baseUrl}/en/part/{id}", $"{_baseUrl}/scan/part/{id}", photos.Count,
            location?.Name, location?.Id, newBin);
    }

    /// <summary>
    /// Adds stock to an existing part: to the lot in the given location (a new lot if the part has
    /// none there), or without a location to the part's first lot. A lot with unknown stock becomes known.
    /// </summary>
    public async Task<StockAdded?> AddStockAsync(int id, AddStockRequest request, CancellationToken ct)
    {
        if (request.Quantity <= 0) throw new PartDbException("Quantity must be positive.");
        using var response = await _http.GetAsync($"parts/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new PartDbException($"Part-DB {(int)response.StatusCode} for GET parts/{id}");
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var part = doc.RootElement;
        var lots = part.TryGetProperty("partLots", out var l) && l.ValueKind == JsonValueKind.Array ? l.EnumerateArray().ToList() : [];

        string? locationIri = request.StorageLocationPath is { Count: > 0 }
            ? await ResolvePathAsync("storage_locations", request.StorageLocationPath, ct) : null;
        var lot = locationIri is null
            ? lots.FirstOrDefault()
            : lots.FirstOrDefault(x => LocationIri(x) == locationIri);

        var newLot = lot.ValueKind != JsonValueKind.Object;
        var newBin = newLot && locationIri is not null && !(await LotLocationsAsync(ct)).Contains(locationIri);
        if (!newLot)
        {
            var unknown = lot.TryGetProperty("instock_unknown", out var u) && u.ValueKind == JsonValueKind.True;
            var current = !unknown && lot.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble() : 0;
            (await PatchAsync($"part_lots/{lot.GetProperty("id").GetInt32()}",
                new { amount = current + request.Quantity, instock_unknown = false }, ct)).Dispose();
        }
        else
        {
            var body = new Dictionary<string, object?> { ["part"] = $"/api/parts/{id}", ["amount"] = request.Quantity };
            if (locationIri is not null) body["storage_location"] = locationIri;
            (await PostAsync("part_lots", body, ct)).Dispose();
        }

        // Read back for the new total and the location name.
        using var after = await GetAsync($"parts/{id}", ct);
        var updated = after.RootElement;
        var location = updated.GetProperty("partLots").EnumerateArray()
            .Where(x => locationIri is null || LocationIri(x) == locationIri)
            .Select(x => x.TryGetProperty("storage_location", out var s) && s.ValueKind == JsonValueKind.Object ? Str(s, "name") : null)
            .FirstOrDefault();
        return new StockAdded(id, Str(updated, "name") ?? "", $"{_baseUrl}/en/part/{id}", request.Quantity,
            updated.TryGetProperty("total_instock", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDouble() : 0,
            location, newLot,
            locationIri is null ? null : int.Parse(locationIri["/api/storage_locations/".Length..]), newBin);
    }

    // Embedded in a part, a lot's storage_location is an object; from /api/part_lots it can be an IRI.
    private static string? LocationIri(JsonElement lot) =>
        !lot.TryGetProperty("storage_location", out var s) ? null : s.ValueKind switch
        {
            JsonValueKind.Object => $"/api/storage_locations/{s.GetProperty("id").GetInt32()}",
            JsonValueKind.String => s.GetString(),
            _ => null,
        };

    /// <summary>The location IRI of every stock lot, newest first.</summary>
    private async Task<List<string?>> LotLocationsAsync(CancellationToken ct)
    {
        using var doc = await GetAsync("part_lots?itemsPerPage=10000&order%5BaddedDate%5D=desc", ct);
        return doc.RootElement.EnumerateArray().Select(LocationIri).ToList();
    }

    /// <summary>
    /// Where a new part should go (see <see cref="BinSuggestion"/>). Bins are the leaves under the drawers
    /// (in D1 the compartments), in plan order: storage/sync_locations.py creates them in that order.
    /// </summary>
    public async Task<BinSuggestion> SuggestBinAsync(IReadOnlyList<string> categoryPath, bool bagged, CancellationToken ct)
    {
        var rule = DrawerRules.For(categoryPath, bagged);
        var all = await StorageLocationsAsync(ct);
        int DrawerIndex(StorageLocation l) =>
            rule.Drawers.ToList().FindIndex(d => l.Path.StartsWith($"Helmer / {d} / ", StringComparison.OrdinalIgnoreCase));
        var bins = all
            .Where(l => DrawerIndex(l) >= 0 && !all.Any(c => c.Path.StartsWith(l.Path + " / ", StringComparison.Ordinal)))
            .OrderBy(DrawerIndex).ThenBy(l => l.Id).ToList();
        var iri = bins.ToDictionary(b => b, b => $"/api/storage_locations/{b.Id}");

        var lots = await LotLocationsAsync(ct);
        var used = lots.OfType<string>().ToHashSet();
        var empty = bins.Where(b => !used.Contains(iri[b])).ToList();
        var filling = rule.OnePartPerBin ? null
            : lots.Select(l => bins.FirstOrDefault(b => iri[b] == l)).FirstOrDefault(b => b is not null);

        return new BinSuggestion(rule.Drawers, rule.Reason, (filling ?? empty.FirstOrDefault())?.Name, filling is not null,
            empty.Select(b => b.Name).ToList());
    }

    /// <summary>All storage locations as name + full path (e.g. "D2-A1", "Helmer / D2 / D2-A1"), for suggestions in the PWA.</summary>
    public async Task<IReadOnlyList<StorageLocation>> StorageLocationsAsync(CancellationToken ct)
    {
        using var doc = await GetAsync("storage_locations?itemsPerPage=1000", ct);
        return doc.RootElement.EnumerateArray()
            .Select(ToLocation)
            .Where(l => l.Name != "")
            .OrderBy(l => l.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The storage location at <paramref name="path"/> (e.g. ["Helmer", "D2", "D2-A1"]), creating missing levels.</summary>
    public async Task<StorageLocation> EnsureStorageLocationAsync(IReadOnlyList<string> path, CancellationToken ct)
    {
        var iri = await ResolvePathAsync("storage_locations", path, ct);
        using var doc = await GetAsync(iri["/api/".Length..], ct);
        return ToLocation(doc.RootElement);
    }

    /// <summary>Label for a bin: its name (e.g. "D2-A1") as title; the QR code opens the parts in it.</summary>
    public async Task<LabelContent?> GetLocationLabelAsync(int id, LabelRequest overrides, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"storage_locations/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new PartDbException($"Part-DB {(int)response.StatusCode} for GET storage_locations/{id}");
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        return new LabelContent(
            NullIfEmpty(overrides.Title) ?? Str(doc.RootElement, "name") ?? $"Location {id}",
            NullIfEmpty(overrides.Subtitle),
            $"{_baseUrl}/scan/location/{id}",
            Math.Clamp(overrides.Copies, 1, 20));
    }

    private static StorageLocation ToLocation(JsonElement e) =>
        new(e.GetProperty("id").GetInt32(), Str(e, "name") ?? "", Str(e, "full_path")?.Replace(" → ", " / ") ?? "");

    /// <summary>Label text for a part: MPN (or name) as title, storage location name (e.g. "D2-A1") or part name as subtitle.</summary>
    public async Task<LabelContent?> GetLabelAsync(int id, LabelRequest overrides, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"parts/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new PartDbException($"Part-DB {(int)response.StatusCode} for GET parts/{id}");
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var part = doc.RootElement;

        var name = Str(part, "name") ?? $"Part {id}";
        var mpn = NullIfEmpty(Str(part, "manufacturer_product_number"));
        var location = part.TryGetProperty("partLots", out var lots) && lots.ValueKind == JsonValueKind.Array
            ? lots.EnumerateArray()
                .Select(l => l.TryGetProperty("storage_location", out var s) && s.ValueKind == JsonValueKind.Object ? Str(s, "name") : null)
                .OfType<string>().FirstOrDefault()
            : null;

        return new LabelContent(
            NullIfEmpty(overrides.Title) ?? mpn ?? name,
            NullIfEmpty(overrides.Subtitle) ?? location ?? (mpn is null ? null : name),
            $"{_baseUrl}/scan/part/{id}",
            Math.Clamp(overrides.Copies, 1, 20));
    }

    /// <summary>
    /// IRI of the element at <paramref name="path"/> in a Part-DB tree (categories, storage locations, ...),
    /// creating missing levels. Names match case-insensitively.
    /// </summary>
    private async Task<string> ResolvePathAsync(string resource, IReadOnlyList<string> path, CancellationToken ct,
        Dictionary<string, object?>? extraOnCreate = null)
    {
        // A single name that exists exactly once anywhere in the tree means that element,
        // so "D2-A1" finds "Helmer → D2 → D2-A1" (bin names are unique on their own).
        var names = path.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (names.Count == 1)
        {
            using var doc = await GetAsync($"{resource}?name={Uri.EscapeDataString(EscapeLike(names[0]))}", ct);
            var matches = doc.RootElement.EnumerateArray().ToList();
            if (matches.Count == 1) return $"/api/{resource}/{matches[0].GetProperty("id").GetInt32()}";
        }

        string? parent = null;
        foreach (var raw in path)
        {
            var name = raw.Trim();
            if (name.Length == 0) continue;
            using var doc = await GetAsync($"{resource}?name={Uri.EscapeDataString(EscapeLike(name))}", ct);
            var existing = doc.RootElement.EnumerateArray().FirstOrDefault(e =>
                (e.TryGetProperty("parent", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null) == parent);

            if (existing.ValueKind == JsonValueKind.Object)
            {
                parent = $"/api/{resource}/{existing.GetProperty("id").GetInt32()}";
                continue;
            }
            var body = new Dictionary<string, object?>(extraOnCreate ?? []) { ["name"] = name, ["parent"] = parent };
            using var created = await PostAsync(resource, body, ct);
            parent = $"/api/{resource}/{created.RootElement.GetProperty("id").GetInt32()}";
        }
        return parent ?? throw new PartDbException($"Empty path for {resource}.");
    }

    private static string Comment(CreatePartRequest r)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Notes)) lines.Add(r.Notes.Trim());
        if (r.VisibleMarkings is { Count: > 0 }) lines.Add("Markings: " + string.Join(" | ", r.VisibleMarkings));
        lines.Add("Added with Shelfie.");
        return string.Join("\n\n", lines);
    }

    /// <summary>Numeric values go into value_typical so Part-DB can filter and sort by them.</summary>
    private static Dictionary<string, object?> Parameter(string partIri, KeyParameter p)
    {
        var body = new Dictionary<string, object?>
        {
            ["_type"] = "Part",
            ["element"] = partIri,
            ["name"] = p.Name,
            ["unit"] = p.Unit ?? "",
        };
        if (double.TryParse(p.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
            body["value_typical"] = number;
        else
            body["value_text"] = p.Value;
        return body;
    }

    private async Task<JsonDocument> PostAsync(string path, object body, CancellationToken ct)
    {
        // Buffered with Content-Length: Part-DB (PHP behind Apache) can lose chunked request bodies.
        using var content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(path, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new PartDbException($"Part-DB {(int)response.StatusCode} for POST {path}: {detail[..Math.Min(detail.Length, 500)]}");
        }
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private async Task<JsonDocument> PatchAsync(string path, object body, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/merge-patch+json");
        using var response = await _http.PatchAsync(path, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new PartDbException($"Part-DB {(int)response.StatusCode} for PATCH {path}: {detail[..Math.Min(detail.Length, 500)]}");
        }
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
            throw new PartDbException($"Part-DB {(int)response.StatusCode} for GET {path}");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private StockMatch ToMatch(JsonElement part, string term, IdentifiedItem item)
    {
        var id = part.GetProperty("id").GetInt32();
        var lots = part.TryGetProperty("partLots", out var l) && l.ValueKind == JsonValueKind.Array
            ? l.EnumerateArray().ToList() : [];
        var name = part.GetProperty("name").GetString() ?? "";
        var mpn = NullIfEmpty(Str(part, "manufacturer_product_number"));
        return new StockMatch(
            id,
            name,
            mpn,
            part.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.Object ? Str(c, "full_path") : null,
            part.TryGetProperty("total_instock", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDouble() : 0,
            lots.Select(lot => lot.TryGetProperty("storage_location", out var s) && s.ValueKind == JsonValueKind.Object ? Str(s, "name") : null)
                .OfType<string>().Distinct().ToList(),
            $"{_baseUrl}/en/part/{id}",
            term,
            IsSamePart(item, name, mpn));
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

public sealed class PartDbException(string message) : Exception(message);
