using System.Net.Http.Headers;
using System.Text.Json;
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
