#pragma warning disable OPENAI001 // Official SDK 2.14.0 marks its Responses surface as experimental.

using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using Redeemer.SocialFlow.Application.AI;

namespace Redeemer.SocialFlow.Infrastructure.AI;

public sealed class OpenAIContentGenerator(
    ResponsesClient client,
    IOptions<OpenAIContentGeneratorOptions> options) : IContentGenerator
{
    private const int MaximumAttempts = 3;
    private static readonly HashSet<string> OutputProperties =
        ["Title", "Content", "CallToAction", "VisualBrief", "Warnings"];

    public async Task<GeneratedContent> GenerateAsync(
        GenerateContentRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var model = options.Value.Model;
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var brief = JsonSerializer.Serialize(new
        {
            request.Subject,
            request.Objective,
            request.Audience,
            Platform = request.Platform.ToString()
        });

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var responseOptions = new CreateResponseOptions
            {
                Model = model,
                Instructions = RedeemerEditorialPolicy.Instructions,
                StoredOutputEnabled = false,
                TextOptions = new ResponseTextOptions
                {
                    TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                        "redeemer_editorial_content",
                        BinaryData.FromString(RedeemerEditorialPolicy.Schema),
                        jsonSchemaIsStrict: true)
                }
            };
            responseOptions.InputItems.Add(ResponseItem.CreateUserMessageItem(brief));
            if (attempt > 1)
                responseOptions.InputItems.Add(ResponseItem.CreateUserMessageItem(
                    "The previous generation was invalid or incomplete. Generate a fresh, complete " +
                    "JSON object matching the schema, with non-empty Title and Content. " +
                    "Keep following every permanent editorial rule. Do not bypass a safety refusal."));

            // Transport/authentication failures and cancellation propagate; only invalid
            // generations are retried here. No generated text or API key is logged.
            ResponseResult response;
            try
            {
                response = await client.CreateResponseAsync(responseOptions, cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // SDK transport cleanup can mask cancellation with a disposal error.
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var refused = response.OutputItems.OfType<MessageResponseItem>()
                .SelectMany(message => message.Content)
                .Any(part => part.Kind == ResponseContentPartKind.Refusal);

            if (response.Status == ResponseStatus.Completed && response.Error is null && !refused &&
                TryParse(response.GetOutputText(), out var generated))
                return generated!;
        }

        throw new ContentGenerationException();
    }

    private static bool TryParse(string text, out GeneratedContent? generated)
    {
        generated = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length != OutputProperties.Count || !OutputProperties.SetEquals(names)) return false;

            var title = root.GetProperty("Title");
            var content = root.GetProperty("Content");
            if (title.ValueKind != JsonValueKind.String || content.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(title.GetString()) || string.IsNullOrWhiteSpace(content.GetString()))
                return false;

            var cta = root.GetProperty("CallToAction");
            var visual = root.GetProperty("VisualBrief");
            if (cta.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                visual.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return false;

            var warnings = root.GetProperty("Warnings");
            if (warnings.ValueKind != JsonValueKind.Array ||
                warnings.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)) return false;

            generated = new GeneratedContent(
                title.GetString()!.Trim(), content.GetString()!.Trim(),
                OptionalText(cta), OptionalText(visual),
                warnings.EnumerateArray().Select(item => item.GetString()!).ToArray());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? OptionalText(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null || string.IsNullOrWhiteSpace(value.GetString())
            ? null : value.GetString()!.Trim();
}
