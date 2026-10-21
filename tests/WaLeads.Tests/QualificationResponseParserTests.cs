using FluentValidation;
using WaLeads.Core;

namespace WaLeads.Tests;

public sealed class QualificationResponseParserTests
{
    private readonly QualificationResponseParser _parser =
        new(new QualificationResponseValidator());

    [Fact]
    public async Task RejectsBadEnum()
    {
        var result = await _parser.ParseAsync(Json(intent: "lead"), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("intent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsScoreAboveMaximum()
    {
        var result = await _parser.ParseAsync(Json(score: 101), CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task RejectsMissingRequiredReply()
    {
        var result = await _parser.ParseAsync(Json(suggestedReply: null), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task ParsesFenceWrappedJson()
    {
        var result = await _parser.ParseAsync(
            $"```json\n{Json()}\n```",
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("info", result.Response!.Intent);
    }

    private static string Json(
        string intent = "info",
        int score = 50,
        string? suggestedReply = "Como posso ajudar?") =>
        $$"""{"intent":"{{intent}}","score":{{score}},"missing_info":[],"next_action":"ask_question","suggested_reply":{{(suggestedReply is null ? "null" : System.Text.Json.JsonSerializer.Serialize(suggestedReply))}},"confidence":0.9}""";
}
