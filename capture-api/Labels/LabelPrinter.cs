using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CaptureApi.Labels;

public sealed class PrintServerOptions
{
    public string BaseUrl { get; set; } = "";
}

/// <summary>Optional overrides; by default the label is built from the part.</summary>
public sealed record LabelRequest(string? Title = null, string? Subtitle = null, int Copies = 1);

/// <summary>What goes on the label: QR code with the part's scan URL, a title and a subtitle.</summary>
public sealed record LabelContent(string Title, string? Subtitle, string Url, int Copies);

/// <summary>Client for print-server/ on the Pi (FastAPI + ptouch-print).</summary>
public sealed class LabelPrinter
{
    private readonly HttpClient _http;

    public LabelPrinter(HttpClient http, IOptions<PrintServerOptions> options)
    {
        if (string.IsNullOrEmpty(options.Value.BaseUrl))
            throw new InvalidOperationException("PrintServer:BaseUrl must be configured.");
        _http = http;
        _http.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(60); // printing a long label takes a while
    }

    public async Task<JsonDocument> PrintAsync(LabelContent label, CancellationToken ct)
    {
        using var response = await _http.PostAsync("labels", Body(label), ct);
        await EnsureSuccessAsync(response, ct);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    public async Task<byte[]> PreviewAsync(LabelContent label, CancellationToken ct)
    {
        using var response = await _http.PostAsync("labels/preview", Body(label), ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static StringContent Body(LabelContent l) => new(
        JsonSerializer.Serialize(new { title = l.Title, subtitle = l.Subtitle, url = l.Url, copies = l.Copies }),
        System.Text.Encoding.UTF8, "application/json");

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(ct);
        try { detail = JsonDocument.Parse(detail).RootElement.GetProperty("detail").ToString(); } catch (Exception) { }
        throw new PrintServerException($"Print server {(int)response.StatusCode}: {detail}");
    }
}

public sealed class PrintServerException(string message) : Exception(message);
