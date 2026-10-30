namespace CaptureApi.PartDb;

/// <summary>
/// Where a new part should go: the drawer(s) from its category (docs/what-goes-where.md), the bin
/// currently being filled there (or the first empty one) and the empty bins to move on to when it's full.
/// </summary>
public sealed record BinSuggestion(
    IReadOnlyList<string> Drawers,
    string Reason,
    string? Bin,
    bool Filling,
    IReadOnlyList<string> EmptyBins);

/// <summary>Category → drawer defaults from docs/what-goes-where.md. Keep the two in sync.</summary>
public static class DrawerRules
{
    /// <param name="OnePartPerBin">D1 compartments hold one part type each, so there is no bin "being filled".</param>
    public sealed record Rule(IReadOnlyList<string> Drawers, string Reason, bool OnePartPerBin = false);

    public static Rule For(IReadOnlyList<string> categoryPath, bool bagged)
    {
        var top = categoryPath.ElementAtOrDefault(0)?.Trim() ?? "";
        var sub = categoryPath.ElementAtOrDefault(1)?.Trim() ?? "";
        bool Is(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

        if (Is(top, "Dev boards") || (Is(top, "Modules") && Is(sub, "Display")))
            return new(["D5"], "Dev boards and displays go to D5");
        if (Is(top, "Modules"))
            return new(["D4"], "Modules and sensors go to D4");
        if (Is(top, "Cables") || Is(top, "Power supplies") || Is(top, "Tools") || Is(top, "Mechanical"))
            return new(["D6"], "Cables, power supplies, tools and mechanical parts go to D6");
        return bagged
            ? new(["D2", "D3"], "Bagged parts go to D2, then D3")
            : new(["D1"], "Loose small parts get their own D1 compartment", OnePartPerBin: true);
    }
}
