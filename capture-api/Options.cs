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
    /// <summary>Address for links people open (QR codes, "open in Part-DB"), e.g. https://db.rmlabs.ch.
    /// Empty: same as BaseUrl (the tailnet address the API itself uses).</summary>
    public string PublicUrl { get; set; } = "";
}
