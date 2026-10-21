using System.Text;
using Microsoft.EntityFrameworkCore;
using WaLeads.Core;
using WaLeads.Infrastructure;
using WaLeads.Worker;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLeadInfrastructure(builder.Configuration);
builder.Services.AddSingleton<QualificationGuard>();
builder.Services.AddScoped<QualificationApprovalService>();
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
builder.Services.AddOptions<ApprovalApiOptions>()
    .Bind(builder.Configuration.GetSection(ApprovalApiOptions.SectionName))
    .Validate(options => Encoding.UTF8.GetByteCount(options.BearerToken) >= 32, "ApprovalApi:BearerToken must contain at least 32 bytes.")
    .ValidateOnStart();
builder.Services.AddHostedService<QualificationWorker>();

var app = builder.Build();
app.MapQualificationApprovalApi();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LeadDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();
