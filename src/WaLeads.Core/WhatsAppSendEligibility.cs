namespace WaLeads.Core;

public static class WhatsAppSendEligibility
{
    public static bool IsCustomerServiceWindowOpen(DateTimeOffset? latestInboundAt, DateTimeOffset now) =>
        latestInboundAt is not null
        && latestInboundAt <= now
        && latestInboundAt > now.AddHours(-24);
}
