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

app.Run();

partial class Program
{
    // Azure OpenAI accepts up to 20 MB per image.
    private const long MaxImageBytes = 20 * 1024 * 1024;
}
