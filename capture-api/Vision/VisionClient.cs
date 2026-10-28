using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace CaptureApi.Vision;

/// <summary>
/// Identifies parts in photos with a vision model over the OpenAI-compatible API.
/// Same request as vision/identify.py, using the same prompt and schema files.
/// </summary>
public sealed class VisionClient
{
    private readonly ChatClient _chat;
    private readonly VisionOptions _options;
    private readonly string _prompt;
    private readonly BinaryData _schema;
    private readonly JsonSerializerOptions _json;

    public VisionClient(IOptions<VisionOptions> options, JsonSerializerOptions json)
    {
        _options = options.Value;
        _json = json;
        if (string.IsNullOrEmpty(_options.BaseUrl) || string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("Vision:BaseUrl and Vision:ApiKey must be configured.");

        _chat = new ChatClient(_options.Model, new ApiKeyCredential(_options.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(_options.BaseUrl) });

        // Copied next to the binaries from ../vision at build time (see CaptureApi.csproj).
        var dir = Path.Combine(AppContext.BaseDirectory, "vision");
        _prompt = File.ReadAllText(Path.Combine(dir, "prompt.md"));
        _schema = BinaryData.FromString(File.ReadAllText(Path.Combine(dir, "schema.json")));
    }

    public async Task<(Identification Result, VisionUsage Usage)> IdentifyAsync(
        IReadOnlyList<Stream> images, string? hint, CancellationToken ct)
    {
        var parts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(string.IsNullOrWhiteSpace(hint)
                ? "Identify the electronic parts in these images." : hint),
        };
        foreach (var image in images)
        {
            var data = await BinaryData.FromStreamAsync(image, ct);
            var mediaType = SniffImageType(data.ToMemory().Span)
                ?? throw new UnsupportedImageException("Unsupported image format; send JPEG, PNG, WebP or GIF.");
            parts.Add(ChatMessageContentPart.CreateImagePart(data, mediaType, ChatImageDetailLevel.High));
        }

#pragma warning disable OPENAI001 // reasoning effort is marked experimental in the SDK
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "shelfie_identification", _schema, jsonSchemaIsStrict: true),
            ReasoningEffortLevel = new ChatReasoningEffortLevel(_options.ReasoningEffort),
            MaxOutputTokenCount = 6000,
        };
#pragma warning restore OPENAI001

        var stopwatch = Stopwatch.StartNew();
        ChatCompletion completion = await _chat.CompleteChatAsync(
            [new SystemChatMessage(_prompt), new UserChatMessage(parts)], options, ct);
        stopwatch.Stop();

        if (!string.IsNullOrEmpty(completion.Refusal))
            throw new VisionException($"Model refused: {completion.Refusal}");
        if (completion.FinishReason == ChatFinishReason.Length)
            throw new VisionException("Model ran out of tokens before finishing; try fewer images.");

        var result = JsonSerializer.Deserialize<Identification>(completion.Content[0].Text, _json)
            ?? throw new VisionException("Model returned an empty result.");
        var usage = new VisionUsage(completion.Model, Math.Round(stopwatch.Elapsed.TotalSeconds, 1),
            completion.Usage.InputTokenCount, completion.Usage.OutputTokenCount);
        return (result, usage);
    }

    /// <summary>
    /// Media type from the file's magic bytes. Images are sent as they are: the model downscales
    /// them itself (so a full-size photo costs no more tokens), and the PWA resizes before upload.
    /// </summary>
    private static string? SniffImageType(ReadOnlySpan<byte> b) => b switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => "image/png",
        [(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..] => "image/gif",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "image/webp",
        _ => null,
    };
}

public sealed class VisionException(string message) : Exception(message);

public sealed class UnsupportedImageException(string message) : Exception(message);
