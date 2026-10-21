using System.Collections.Concurrent;
using WaLeads.Core;

namespace WaLeads.Tests;

public sealed class FakeLeadQualifier : ILeadQualifier
{
    private readonly ConcurrentQueue<Func<QualificationRequest, CancellationToken, Task<LeadQualifierResponse>>> _responses = new();
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public void EnqueueResponse(LeadQualifierResponse response) =>
        _responses.Enqueue((_, _) => Task.FromResult(response));

    public void EnqueueException(Exception exception) =>
        _responses.Enqueue((_, _) => Task.FromException<LeadQualifierResponse>(exception));

    public void EnqueueHandler(Func<QualificationRequest, CancellationToken, Task<LeadQualifierResponse>> handler) =>
        _responses.Enqueue(handler);

    public Task<LeadQualifierResponse> QualifyAsync(
        QualificationRequest request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (!_responses.TryDequeue(out var handler))
        {
            throw new InvalidOperationException("No fake qualifier response was configured.");
        }

        return handler(request, cancellationToken);
    }

    public static LeadQualifierResponse ValidResponse(
        string intent = "info",
        int score = 50,
        string action = "ask_question",
        decimal confidence = 0.9m,
        string? reply = "Como posso ajudar?") =>
        new(
            $$"""{"intent":"{{intent}}","score":{{score}},"missing_info":[],"next_action":"{{action}}","suggested_reply":{{(reply is null ? "null" : System.Text.Json.JsonSerializer.Serialize(reply))}},"confidence":{{confidence}}}""",
            "fake-model",
            12,
            8,
            15);
}
