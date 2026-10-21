namespace WaLeads.Infrastructure;

public sealed class Tenant
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string BusinessContext { get; set; } = string.Empty;
    public bool Active { get; set; }
    public int HotThreshold { get; set; } = 70;
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<Lead> Leads { get; set; } = [];
}

public sealed class Lead
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public string WaId { get; set; } = string.Empty;
    public string? ProfileName { get; set; }
    public int? Score { get; set; }
    public string? Intent { get; set; }
    public string Stage { get; set; } = "new";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? QualifyClaimedUntil { get; set; }
    public int QualifyAttempts { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = [];
    public ICollection<Qualification> Qualifications { get; set; } = [];
}

public sealed class Message
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    public string WaMessageId { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public Lead Lead { get; set; } = null!;
}

public sealed class Qualification
{
    public Guid Id { get; set; }
    public long LeadId { get; set; }
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public int Score { get; set; }
    public string NextAction { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string? SuggestedReply { get; set; }
    public string MissingInfo { get; set; } = "[]";
    public bool ForcedHandoff { get; set; }
    public string? ForcedReason { get; set; }
    public string RawResponse { get; set; } = string.Empty;
    public int LatencyMs { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ActionedAt { get; set; }
    public string ActionStatus { get; set; } = QualificationActionStatus.PendingApproval;
    public string? ActionReason { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? OptInConfirmedAt { get; set; }
    public string? WhatsAppMessageId { get; set; }
    public Guid? ActionErrorId { get; set; }
    public Lead Lead { get; set; } = null!;
}

public static class QualificationActionStatus
{
    public const string PendingApproval = "pending_approval";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string HumanReview = "human_review";
    public const string Rejected = "rejected";
    public const string Ignored = "ignored";
    public const string Resolved = "resolved";
    public const string SendUncertain = "send_uncertain";
}
