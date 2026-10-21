namespace WaLeads.Worker;

public sealed class ApprovalApiOptions
{
    public const string SectionName = "ApprovalApi";

    public string BearerToken { get; set; } = string.Empty;
}
