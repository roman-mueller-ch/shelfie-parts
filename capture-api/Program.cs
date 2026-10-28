using System.Text.Json;
using CaptureApi;
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
builder.Services.AddHttpClient<PartDbClient>();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();

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

app.Run();

partial class Program
{
    // Azure OpenAI accepts up to 20 MB per image.
    private const long MaxImageBytes = 20 * 1024 * 1024;
}
