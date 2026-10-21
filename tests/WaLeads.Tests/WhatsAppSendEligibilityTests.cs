using WaLeads.Core;

namespace WaLeads.Tests;

public sealed class WhatsAppSendEligibilityTests
{
    [Fact]
    public void RequiresAnInboundMessageWithinTheOpenWindow()
    {
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        Assert.False(WhatsAppSendEligibility.IsCustomerServiceWindowOpen(null, now));
        Assert.False(WhatsAppSendEligibility.IsCustomerServiceWindowOpen(now.AddHours(-24), now));
        Assert.True(WhatsAppSendEligibility.IsCustomerServiceWindowOpen(now.AddHours(-24).AddTicks(1), now));
        Assert.False(WhatsAppSendEligibility.IsCustomerServiceWindowOpen(now.AddTicks(1), now));
    }
}
