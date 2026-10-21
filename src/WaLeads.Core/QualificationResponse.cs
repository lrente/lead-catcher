using System.Text.Json.Serialization;

namespace WaLeads.Core;

public sealed record QualificationResponse(
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("missing_info")] IReadOnlyList<string> MissingInfo,
    [property: JsonPropertyName("next_action")] string NextAction,
    [property: JsonPropertyName("suggested_reply")] string? SuggestedReply,
    [property: JsonPropertyName("confidence")] decimal Confidence);

public sealed record QualificationMessage(
    string Direction,
    string Type,
    string Body);

public sealed record QualificationRequest(
    string BusinessName,
    string BusinessContext,
    IReadOnlyList<QualificationMessage> Messages,
    IReadOnlyList<string> ValidationErrors);

public sealed record LeadQualifierResponse(
    string Content,
    string Model,
    int? InputTokens,
    int? OutputTokens,
    int LatencyMs);

public interface ILeadQualifier
{
    Task<LeadQualifierResponse> QualifyAsync(
        QualificationRequest request,
        CancellationToken cancellationToken);
}
