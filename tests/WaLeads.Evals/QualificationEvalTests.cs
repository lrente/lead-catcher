using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WaLeads.Core;
using WaLeads.Infrastructure;
using Xunit.Abstractions;

namespace WaLeads.Evals;

[Trait("Category", "Eval")]
public sealed class QualificationEvalTests(ITestOutputHelper output)
{
    [EvalFact]
    public async Task PortugueseLeadFixturesMatchExpectedIntentAndAcceptableAction()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<QualificationEvalTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var options = configuration.GetSection(AnthropicOptions.SectionName).Get<AnthropicOptions>()
            ?? throw new InvalidOperationException("Configure Anthropic settings with user-secrets or environment variables.");
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.Model))
        {
            throw new InvalidOperationException("Anthropic:ApiKey and Anthropic:Model are required to run evals.");
        }

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
        httpClient.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var qualifier = new AnthropicLeadQualifier(httpClient, Options.Create(options));
        var parser = new QualificationResponseParser(new QualificationResponseValidator());
        var guard = new QualificationGuard();
        var fixtures = Directory.GetFiles(
            Path.Combine(AppContext.BaseDirectory, "fixtures"),
            "*.json").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var results = new List<(string Fixture, string Got, string Expected, bool Passed)>();

        foreach (var path in fixtures)
        {
            var fixture = JsonSerializer.Deserialize<EvalFixture>(
                await File.ReadAllTextAsync(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException($"Could not read eval fixture {Path.GetFileName(path)}.");
            var llmResponse = await qualifier.QualifyAsync(
                new QualificationRequest(
                    fixture.BusinessName,
                    fixture.BusinessContext,
                    fixture.Messages.Select(message =>
                        new QualificationMessage(message.Direction, message.Type, message.Body)).ToArray(),
                    []),
                CancellationToken.None);
            var parsed = await parser.ParseAsync(llmResponse.Content, CancellationToken.None);
            Assert.True(parsed.IsValid, $"{Path.GetFileName(path)} returned invalid JSON: {string.Join("; ", parsed.Errors)}");
            var guarded = guard.Apply(
                parsed.Response!,
                fixture.BusinessContext,
                fixture.Messages.Any(message => message.Type != "text"),
                0.6m);
            var passed = parsed.Response!.Intent == fixture.ExpectedIntent
                && fixture.AcceptableNextActions.Contains(guarded.Response.NextAction, StringComparer.Ordinal);
            results.Add((
                fixture.Name,
                $"{parsed.Response.Intent}/{guarded.Response.NextAction}",
                $"{fixture.ExpectedIntent}/[{string.Join(",", fixture.AcceptableNextActions)}]",
                passed));
        }

        output.WriteLine("| Fixture | Got | Expected | Result |");
        output.WriteLine("|---|---|---|---|");
        foreach (var result in results)
        {
            output.WriteLine(
                $"| {result.Fixture} | {result.Got} | {result.Expected} | {(result.Passed ? "PASS" : "FAIL")} |");
        }

        var passRate = results.Count == 0 ? 0 : results.Count(result => result.Passed) * 100m / results.Count;
        output.WriteLine($"Pass rate: {passRate:F1}% ({results.Count(result => result.Passed)}/{results.Count})");
        Assert.Equal(20, results.Count);
    }

    private sealed record EvalFixture(
        string Name,
        string BusinessName,
        string BusinessContext,
        IReadOnlyList<EvalMessage> Messages,
        string ExpectedIntent,
        IReadOnlyList<string> AcceptableNextActions);

    private sealed record EvalMessage(string Direction, string Type, string Body);
}
