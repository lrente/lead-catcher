using Microsoft.EntityFrameworkCore;
using WaLeads.Core;
using WaLeads.Infrastructure;
using WaLeads.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLeadInfrastructure(builder.Configuration);
builder.Services.AddSingleton<QualificationGuard>();
builder.Services.AddOptions<QualificationWorkerOptions>()
    .Bind(builder.Configuration.GetSection("Qualification"))
    .Validate(options => options.PollIntervalSeconds > 0, "Qualification:PollIntervalSeconds must be positive.")
    .Validate(options => options.MaxPollIntervalSeconds >= options.PollIntervalSeconds, "Qualification:MaxPollIntervalSeconds must be at least PollIntervalSeconds.")
    .Validate(options => options.DebounceSeconds >= 0, "Qualification:DebounceSeconds cannot be negative.")
    .Validate(options => options.LeaseSeconds > 0, "Qualification:LeaseSeconds must be positive.")
    .Validate(options => options.MaxAttempts > 0, "Qualification:MaxAttempts must be positive.")
    .Validate(options => options.MessageWindow > 0, "Qualification:MessageWindow must be positive.")
    .Validate(options => options.MinimumConfidence is >= 0 and <= 1, "Qualification:MinimumConfidence must be between 0 and 1.")
    .ValidateOnStart();
builder.Services.AddHostedService<QualificationWorker>();

var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LeadDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
