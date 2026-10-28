using System.Net.Http.Headers;
using System.Text.Json;
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
    string MatchedTerm);

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
                    var match = ToMatch(part, term);
                    matches.TryAdd(match.Id, match);
                }
            }
            if (matches.Count >= MaxMatches) break;
        }
        return matches.Values.Take(MaxMatches).ToList();
    }

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
        try
        {
            var lot = new Dictionary<string, object?>
            {
                ["part"] = partIri,
                ["amount"] = request.Quantity ?? 0,
                ["instock_unknown"] = request.Quantity is null,
            };
            if (request.StorageLocationPath is { Count: > 0 })
                lot["storage_location"] = await ResolvePathAsync("storage_locations", request.StorageLocationPath, ct);
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

        return new CreatedPart(id, request.Name.Trim(), $"{_baseUrl}/en/part/{id}", $"{_baseUrl}/scan/part/{id}", photos.Count);
    }

    /// <summary>Label text for a part: MPN (or name) as title, storage location (or name) as subtitle.</summary>
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
                .Select(l => l.TryGetProperty("storage_location", out var s) && s.ValueKind == JsonValueKind.Object ? Str(s, "full_path") : null)
                .OfType<string>().FirstOrDefault()?.Replace(" → ", " / ")
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

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
            throw new PartDbException($"Part-DB {(int)response.StatusCode} for GET {path}");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private StockMatch ToMatch(JsonElement part, string term)
    {
        var id = part.GetProperty("id").GetInt32();
        var lots = part.TryGetProperty("partLots", out var l) && l.ValueKind == JsonValueKind.Array
            ? l.EnumerateArray().ToList() : [];
        return new StockMatch(
            id,
            part.GetProperty("name").GetString() ?? "",
            NullIfEmpty(Str(part, "manufacturer_product_number")),
            part.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.Object ? Str(c, "full_path") : null,
            part.TryGetProperty("total_instock", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDouble() : 0,
            lots.Select(lot => lot.TryGetProperty("storage_location", out var s) && s.ValueKind == JsonValueKind.Object ? Str(s, "full_path") : null)
                .OfType<string>().Distinct().ToList(),
            $"{_baseUrl}/en/part/{id}",
            term);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

public sealed class PartDbException(string message) : Exception(message);
