namespace WaLeads.Infrastructure;

public sealed class WhatsAppCloudOptions
{
    public const string SectionName = "WhatsApp";

    public string AccessToken { get; set; } = string.Empty;
    public string GraphApiVersion { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 25;
}
