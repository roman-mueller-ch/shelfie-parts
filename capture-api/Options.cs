namespace CaptureApi;

public sealed class VisionOptions
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-5-mini";
    /// <summary>minimal, low, medium or high.</summary>
    public string ReasoningEffort { get; set; } = "low";
}

public sealed class PartDbOptions
{
    public string BaseUrl { get; set; } = "";
    public string ApiToken { get; set; } = "";
}
