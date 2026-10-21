using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WaLeads.Core;

namespace WaLeads.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLeadInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LeadDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var database = configuration.GetSection("Database");
            var builder = new Npgsql.NpgsqlConnectionStringBuilder
            {
                Host = database["Host"] ?? "localhost",
                Port = int.TryParse(database["Port"], out var port) ? port : 5432,
                Database = database["Name"] ?? throw new InvalidOperationException("Database:Name is required."),
                Username = database["User"] ?? throw new InvalidOperationException("Database:User is required."),
                Password = database["Password"] ?? throw new InvalidOperationException("Database:Password is required.")
            };
            connectionString = builder.ConnectionString;
        }

        services.AddDbContext<LeadDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IValidator<QualificationResponse>, QualificationResponseValidator>();
        services.AddScoped<QualificationResponseParser>();

        services.AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Anthropic:BaseUrl must be an absolute URI.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Anthropic:ApiKey is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model), "Anthropic:Model is required.")
            .Validate(options => options.TimeoutSeconds > 0, "Anthropic:TimeoutSeconds must be positive.")
            .Validate(options => options.MaxTokens > 0, "Anthropic:MaxTokens must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<ILeadQualifier, AnthropicLeadQualifier>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }).AddStandardResilienceHandler();

        return services;
    }
}
