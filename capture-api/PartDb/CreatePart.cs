using CaptureApi.Vision;

namespace CaptureApi.PartDb;

/// <summary>
/// A part to create, usually an <see cref="IdentifiedItem"/> after the user reviewed it.
/// Category, storage location, manufacturer and footprint are found by name and created if missing.
/// </summary>
public sealed record CreatePartRequest(
    string Name,
    IReadOnlyList<string> CategoryPath,
    string? Description = null,
    string? Mpn = null,
    string? Manufacturer = null,
    string? Package = null,
    IReadOnlyList<KeyParameter>? KeyParameters = null,
    IReadOnlyList<string>? VisibleMarkings = null,
    string? Notes = null,
    /// <summary>Null means "in stock, amount unknown".</summary>
    double? Quantity = null,
    /// <summary>E.g. ["Shelf A", "Box 4"]. Empty: no stock lot location.</summary>
    IReadOnlyList<string>? StorageLocationPath = null);

public sealed record CreatedPart(int Id, string Name, string Url, string ScanUrl, int Photos);

/// <summary>Stock to add to an existing part. Without a location, the part's first stock lot gets it.</summary>
public sealed record AddStockRequest(double Quantity, IReadOnlyList<string>? StorageLocationPath = null);

public sealed record StockAdded(int Id, string Name, string Url, double Added, double TotalAmount, string? Location);
