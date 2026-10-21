using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WaLeads.Core;

namespace WaLeads.Infrastructure;

public sealed partial class WhatsAppCloudClient(
    HttpClient httpClient,
    IOptions<WhatsAppCloudOptions> options) : IWhatsAppMessageSender
{
    private readonly WhatsAppCloudOptions _options = options.Value;

    public async Task<string> SendTextAsync(
        string phoneNumberId,
        string recipient,
        string body,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{_options.GraphApiVersion}/{Uri.EscapeDataString(phoneNumberId)}/messages";
        using var response = await httpClient.PostAsJsonAsync(
            endpoint,
            new WhatsAppTextRequest("whatsapp", "individual", recipient, "text", new WhatsAppText(body)),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array
            || messages.GetArrayLength() == 0
            || !messages[0].TryGetProperty("id", out var id)
            || string.IsNullOrWhiteSpace(id.GetString()))
        {
            throw new JsonException("WhatsApp response did not contain a message ID.");
        }

        return id.GetString()!;
    }

    public static bool IsGraphApiVersion(string value) =>
        GraphApiVersionPattern().IsMatch(value);

    [GeneratedRegex("^v[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GraphApiVersionPattern();

    private sealed record WhatsAppTextRequest(
        [property: JsonPropertyName("messaging_product")] string MessagingProduct,
        [property: JsonPropertyName("recipient_type")] string RecipientType,
        [property: JsonPropertyName("to")] string Recipient,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] WhatsAppText Text);

    private sealed record WhatsAppText(
        [property: JsonPropertyName("body")] string Body);
}
