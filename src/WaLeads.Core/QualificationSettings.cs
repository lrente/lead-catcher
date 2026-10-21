namespace WaLeads.Core;

public sealed class QualificationWorkerOptions
{
    public int PollIntervalSeconds { get; set; } = 2;
    public int MaxPollIntervalSeconds { get; set; } = 10;
    public int DebounceSeconds { get; set; } = 8;
    public int LeaseSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 3;
    public int MessageWindow { get; set; } = 10;
    public decimal MinimumConfidence { get; set; } = 0.6m;
}

public static class QualificationConstants
{
    public const string PromptVersion = "v1";
}
