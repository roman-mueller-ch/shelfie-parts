namespace CaptureApi;

public static class ImageTypes
{
    /// <summary>
    /// Media type from the file's magic bytes. Images are sent as they are: the model downscales
    /// them itself (so a full-size photo costs no more tokens), and the PWA resizes before upload.
    /// </summary>
    public static string? Sniff(ReadOnlySpan<byte> b) => b switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => "image/png",
        [(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..] => "image/gif",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "image/webp",
        _ => null,
    };
}

public sealed class UnsupportedImageException(string message) : Exception(message);
