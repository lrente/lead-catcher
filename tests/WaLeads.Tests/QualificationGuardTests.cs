using WaLeads.Core;

namespace WaLeads.Tests;

public sealed class QualificationGuardTests
{
    private readonly QualificationGuard _guard = new();

    [Fact]
    public void LowConfidenceForcesHandoff()
    {
        var result = Apply(Response(confidence: 0.59m));

        Assert.True(result.ForcedHandoff);
        Assert.Equal("low_confidence", result.ForcedReason);
        Assert.Equal("handoff_human", result.Response.NextAction);
        Assert.Null(result.Response.SuggestedReply);
    }

    [Theory]
    [InlineData("45€")]
    [InlineData("€45")]
    [InlineData("45 euros")]
    [InlineData("45,50 EUR")]
    public void UnverifiedCurrencyAmountForcesHandoff(string reply)
    {
        var result = Apply(Response(suggestedReply: reply));

        Assert.True(result.ForcedHandoff);
        Assert.Equal("unverified_price", result.ForcedReason);
    }

    [Theory]
    [InlineData("O valor é 45,50 €.", "O valor é 45.50 EUR.")]
    [InlineData("O valor é €45.", "O valor é 45 euros.")]
    public void PriceInBusinessContextIsAllowed(string reply, string context)
    {
        var result = Apply(Response(suggestedReply: reply), context);

        Assert.False(result.ForcedHandoff);
        Assert.Equal(reply, result.Response.SuggestedReply);
    }

    [Fact]
    public void NonTextMessageForcesHandoff()
    {
        var result = _guard.Apply(Response(), string.Empty, true, 0.6m);

        Assert.True(result.ForcedHandoff);
        Assert.Equal("non_text_message", result.ForcedReason);
    }

    [Theory]
    [InlineData("spam", "ask_question")]
    [InlineData("other", "ignore")]
    public void SpamOrIgnoreClearsReplyWithoutForcingHandoff(string intent, string nextAction)
    {
        var result = _guard.Apply(
            Response(intent: intent, nextAction: nextAction),
            string.Empty,
            false,
            0.6m);

        Assert.False(result.ForcedHandoff);
        Assert.Null(result.Response.SuggestedReply);
    }

    [Fact]
    public void ConfidenceAtBoundaryDoesNotForceHandoff()
    {
        var result = Apply(Response(confidence: 0.6m));

        Assert.False(result.ForcedHandoff);
    }

    private GuardedQualification Apply(
        QualificationResponse response,
        string context = "",
        bool nonText = false) =>
        _guard.Apply(response, context, nonText, 0.6m);

    private static QualificationResponse Response(
        string intent = "info",
        int score = 50,
        string nextAction = "ask_question",
        string? suggestedReply = "Como posso ajudar?",
        decimal confidence = 0.9m) =>
        new(intent, score, [], nextAction, suggestedReply, confidence);
}
