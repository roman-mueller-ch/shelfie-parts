using System.Text.Json;
using CaptureApi;
using CaptureApi.Labels;
using CaptureApi.PartDb;
using CaptureApi.Vision;

var builder = WebApplication.CreateBuilder(args);

// snake_case everywhere: the vision schema uses it, so the whole API does too.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
};
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
builder.Services.AddSingleton(json);

builder.Services.Configure<VisionOptions>(builder.Configuration.GetSection("Vision"));
builder.Services.Configure<PartDbOptions>(builder.Configuration.GetSection("PartDb"));
builder.Services.AddSingleton<VisionClient>();
builder.Services.Configure<PrintServerOptions>(builder.Configuration.GetSection("PrintServer"));
builder.Services.AddHttpClient<PartDbClient>();
builder.Services.AddHttpClient<LabelPrinter>();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
// The PWA (capture-web/, built into wwwroot/ by the Dockerfile).
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// Photo(s) in, identification + matching parts already in stock out.
app.MapPost("/api/identify", async (IFormFileCollection images, [Microsoft.AspNetCore.Mvc.FromForm] string? hint,
    VisionClient vision, PartDbClient partDb, ILogger<Program> log, CancellationToken ct) =>
{
    if (images.Count is 0 or > 4)
        return Results.Problem("Send 1 to 4 images in the 'images' form field.", statusCode: 400);
    if (images.Any(f => f.Length > MaxImageBytes))
        return Results.Problem($"Images must be smaller than {MaxImageBytes / 1024 / 1024} MB each.", statusCode: 413);

    var streams = images.Select(f => f.OpenReadStream()).ToList();
    try
    {
        var (result, usage) = await vision.IdentifyAsync(streams, hint, ct);
        var items = new List<object>();
        foreach (var item in result.Items)
            items.Add(new { item, in_stock = await partDb.SearchAsync(item, ct) });
        log.LogInformation("Identified {Count} item(s) in {Seconds} s ({In}/{Out} tokens)",
            result.Items.Count, usage.Seconds, usage.InputTokens, usage.OutputTokens);
        return Results.Ok(new { result.SourceType, items, result.NeedsBetterPhoto, result.PhotoAdvice, usage });
    }
    catch (VisionException e)
    {
        return Results.Problem(e.Message, statusCode: 502);
    }
    catch (UnsupportedImageException e)
    {
        return Results.Problem(e.Message, statusCode: 415);
    }
    finally
    {
        foreach (var s in streams) await s.DisposeAsync();
    }
}).DisableAntiforgery();

// Reviewed part in, created Part-DB part out. Multipart: "part" (JSON, see CreatePartRequest) + optional "photos".
app.MapPost("/api/parts", async (HttpRequest http, PartDbClient partDb, JsonSerializerOptions json, CancellationToken ct) =>
{
    if (!http.HasFormContentType)
        return Results.Problem("Send multipart/form-data with a 'part' JSON field and optional 'photos'.", statusCode: 415);
    var form = await http.ReadFormAsync(ct);

    CreatePartRequest? request;
    try { request = JsonSerializer.Deserialize<CreatePartRequest>(form["part"].ToString(), json); }
    catch (JsonException e) { return Results.Problem($"Invalid 'part' JSON: {e.Message}", statusCode: 400); }
    if (request is null) return Results.Problem("Missing 'part' field.", statusCode: 400);

    var files = form.Files.GetFiles("photos");
    if (files.Count > 4 || files.Any(f => f.Length > MaxImageBytes))
        return Results.Problem("Send up to 4 photos, each smaller than 20 MB.", statusCode: 413);
    if (request.Quantity < 0)
        return Results.Problem("Quantity can't be negative.", statusCode: 400);
    var photos = new List<(string, BinaryData)>();
    foreach (var f in files)
    {
        await using var s = f.OpenReadStream();
        var data = await BinaryData.FromStreamAsync(s, ct);
        if (ImageTypes.Sniff(data.ToMemory().Span) is null)
            return Results.Problem($"'{f.FileName}' is not a JPEG, PNG, WebP or GIF image.", statusCode: 415);
        photos.Add((Path.GetFileName(f.FileName), data));
    }

    try
    {
        var created = await partDb.CreatePartAsync(request, photos, ct);
        return Results.Created(created.Url, created);
    }
    catch (PartDbException e)
    {
        return Results.Problem(e.Message, statusCode: 502);
    }
}).DisableAntiforgery();

// Restock an existing part. JSON: {"quantity": 5, "storage_location_path": ["D2-A1"]} (location optional).
app.MapPost("/api/parts/{id:int}/stock", async (int id, AddStockRequest request, PartDbClient partDb, CancellationToken ct) =>
{
    if (request.Quantity <= 0) return Results.Problem("Quantity must be positive.", statusCode: 400);
    try
    {
        var added = await partDb.AddStockAsync(id, request, ct);
        return added is null ? Results.Problem($"Part {id} not found.", statusCode: 404) : Results.Ok(added);
    }
    catch (PartDbException e)
    {
        return Results.Problem(e.Message, statusCode: 502);
    }
});

// Print the part's QR label on the P-touch. Body optional: {"title", "subtitle", "copies"}.
app.MapPost("/api/parts/{id:int}/label", (int id, LabelRequest? overrides,
    PartDbClient partDb, LabelPrinter printer, CancellationToken ct) =>
    PrintLabel(partDb.GetLabelAsync(id, overrides ?? new(), ct), $"Part {id}", printer, ct));

// The same label as PNG, without printing (for the PWA).
app.MapGet("/api/parts/{id:int}/label.png", (int id, string? title, string? subtitle,
    PartDbClient partDb, LabelPrinter printer, CancellationToken ct) =>
    PreviewLabel(partDb.GetLabelAsync(id, new(title, subtitle), ct), $"Part {id}", printer, ct));

app.MapGet("/api/storage-locations", (PartDbClient partDb, CancellationToken ct) => partDb.StorageLocationsAsync(ct));

// Where a new part should go: ?category=Modules&category=Sensor&bagged=true (rules in docs/what-goes-where.md).
app.MapGet("/api/bins/suggest", async (string[]? category, bool? bagged, PartDbClient partDb, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await partDb.SuggestBinAsync(category ?? [], bagged ?? true, ct));
    }
    catch (PartDbException e)
    {
        return Results.Problem(e.Message, statusCode: 502);
    }
});

// Create a storage location with any missing parents; existing ones are returned as they are.
// JSON: {"path": ["Helmer", "D2", "D2-A1"]}. storage/generate.py --locations lists the whole plan.
app.MapPost("/api/storage-locations", async (EnsureLocationRequest request, PartDbClient partDb, CancellationToken ct) =>
{
    if (request.Path is not { Count: > 0 } || request.Path.Any(string.IsNullOrWhiteSpace))
        return Results.Problem("path must be a list of non-empty names.", statusCode: 400);
    try
    {
        return Results.Ok(await partDb.EnsureStorageLocationAsync(request.Path, ct));
    }
    catch (PartDbException e)
    {
        return Results.Problem(e.Message, statusCode: 502);
    }
});

// A bin's label: QR code to the parts in it, the bin name as text. Body optional, as for parts.
app.MapPost("/api/storage-locations/{id:int}/label", (int id, LabelRequest? overrides,
    PartDbClient partDb, LabelPrinter printer, CancellationToken ct) =>
    PrintLabel(partDb.GetLocationLabelAsync(id, overrides ?? new(), ct), $"Storage location {id}", printer, ct));

app.MapGet("/api/storage-locations/{id:int}/label.png", (int id, string? title, string? subtitle,
    PartDbClient partDb, LabelPrinter printer, CancellationToken ct) =>
    PreviewLabel(partDb.GetLocationLabelAsync(id, new(title, subtitle), ct), $"Storage location {id}", printer, ct));

// Unknown /api paths are 404s; everything else is the PWA.
app.MapFallback("/api/{**path}", () => Results.Problem("Not found.", statusCode: 404));
app.MapFallbackToFile("index.html");

app.Run();

static async Task<IResult> PrintLabel(Task<LabelContent?> content, string what, LabelPrinter printer, CancellationToken ct)
{
    var label = await content;
    if (label is null) return Results.Problem($"{what} not found.", statusCode: 404);
    try
    {
        using var result = await printer.PrintAsync(label, ct);
        return Results.Ok(new { label, printer = result.RootElement.Clone() });
    }
    catch (Exception e) when (e is PrintServerException or HttpRequestException or TaskCanceledException)
    {
        return Results.Problem($"Printing failed: {e.Message}", statusCode: 502);
    }
}

static async Task<IResult> PreviewLabel(Task<LabelContent?> content, string what, LabelPrinter printer, CancellationToken ct)
{
    var label = await content;
    if (label is null) return Results.Problem($"{what} not found.", statusCode: 404);
    try
    {
        return Results.File(await printer.PreviewAsync(label, ct), "image/png");
    }
    catch (Exception e) when (e is PrintServerException or HttpRequestException or TaskCanceledException)
    {
        return Results.Problem($"Preview failed: {e.Message}", statusCode: 502);
    }
}

sealed record EnsureLocationRequest(IReadOnlyList<string>? Path);

partial class Program
{
    // Azure OpenAI accepts up to 20 MB per image.
    private const long MaxImageBytes = 20 * 1024 * 1024;
}
