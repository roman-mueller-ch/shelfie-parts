namespace CaptureApi.Vision;

// Mirrors vision/schema.json. Serialized with snake_case names, like the rest of the API.

public sealed record Identification(
    string SourceType,
    IReadOnlyList<IdentifiedItem> Items,
    bool NeedsBetterPhoto,
    string? PhotoAdvice);

public sealed record IdentifiedItem(
    string Name,
    /// <summary>Text for the printed label, at most 15 characters.</summary>
    string Label,
    IReadOnlyList<string> CategoryPath,
    string? Mpn,
    string? Manufacturer,
    string? Package,
    IReadOnlyList<KeyParameter> KeyParameters,
    IReadOnlyList<string> VisibleMarkings,
    int? QuantityEstimate,
    IReadOnlyList<string> SearchTerms,
    double Confidence,
    string? Notes);

public sealed record KeyParameter(string Name, string Value, string? Unit);

public sealed record VisionUsage(string Model, double Seconds, int InputTokens, int OutputTokens);
