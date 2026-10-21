namespace WaLeads.Infrastructure;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 25;
    public int MaxTokens { get; set; } = 700;
}
