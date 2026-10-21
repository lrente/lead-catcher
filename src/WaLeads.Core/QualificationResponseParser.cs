using System.Text.Json;
using FluentValidation;

namespace WaLeads.Core;

public sealed class QualificationResponseParser(IValidator<QualificationResponse> validator)
{
    private static readonly HashSet<string> RequiredProperties =
        ["intent", "score", "missing_info", "next_action", "suggested_reply", "confidence"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public async Task<QualificationParseResult> ParseAsync(
        string content,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = StripCodeFence(content);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return QualificationParseResult.Invalid(["The response must be a JSON object."]);
            }

            var properties = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            if (properties.Length != RequiredProperties.Count || !RequiredProperties.SetEquals(properties))
            {
                return QualificationParseResult.Invalid(
                    ["The JSON object must contain exactly: intent, score, missing_info, next_action, suggested_reply, confidence."]);
            }

            var response = JsonSerializer.Deserialize<QualificationResponse>(json, JsonOptions);
            if (response is null)
            {
                return QualificationParseResult.Invalid(["The response JSON could not be read."]);
            }

            var validation = await validator.ValidateAsync(response, cancellationToken);
            return validation.IsValid
                ? QualificationParseResult.Valid(response)
                : QualificationParseResult.Invalid(validation.Errors.Select(error => error.ErrorMessage).ToArray());
        }
        catch (JsonException)
        {
            return QualificationParseResult.Invalid(["The response is not valid JSON matching the required fields."]);
        }
    }

    private static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstLineEnd = trimmed.IndexOf('\n');
        var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstLineEnd < 0 || closingFence <= firstLineEnd)
        {
            return trimmed;
        }

        return trimmed[(firstLineEnd + 1)..closingFence].Trim();
    }
}

public sealed record QualificationParseResult(
    QualificationResponse? Response,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Response is not null && Errors.Count == 0;

    public static QualificationParseResult Valid(QualificationResponse response) => new(response, []);

    public static QualificationParseResult Invalid(IReadOnlyList<string> errors) => new(null, errors);
}
