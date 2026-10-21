using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WaLeads.Core;
using WaLeads.Infrastructure;
using WaLeads.Worker;

namespace WaLeads.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class QualificationWorkerIntegrationTests(PostgresFixture fixture)
{
    [IntegrationFact]
    public async Task DebouncedBurstProducesOneQualificationAndProcessesIncludedMessages()
    {
        var leadId = await CreateLeadWithMessagesAsync(3, DateTimeOffset.UtcNow.AddSeconds(-11));
        var fake = new FakeLeadQualifier();
        fake.EnqueueResponse(FakeLeadQualifier.ValidResponse(score: 85));
        await using var provider = BuildProvider(fake, debounceSeconds: 8);
        var worker = CreateWorker(provider);

        Assert.True(await worker.RunOnceAsync(CancellationToken.None));
        await using var db = fixture.CreateDbContext();
        var qualifications = await db.Qualifications.Where(item => item.LeadId == leadId).ToListAsync();
        Assert.Single(qualifications);
        Assert.Equal(QualificationActionStatus.PendingApproval, qualifications[0].ActionStatus);
        Assert.Null(qualifications[0].ActionedAt);
        Assert.Equal(3, await db.Messages.CountAsync(message =>
            message.LeadId == leadId && message.Direction == "inbound" && message.ProcessedAt != null));
        Assert.Equal(85, await db.Leads.Where(lead => lead.Id == leadId).Select(lead => lead.Score).SingleAsync());
    }

    [IntegrationFact]
    public async Task MessageArrivingDuringLlmCallRemainsUnprocessedForNextRound()
    {
        var leadId = await CreateLeadWithMessagesAsync(1, DateTimeOffset.UtcNow.AddSeconds(-2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeLeadQualifier();
        fake.EnqueueHandler(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return FakeLeadQualifier.ValidResponse();
        });
        fake.EnqueueResponse(FakeLeadQualifier.ValidResponse(action: "handoff_human", reply: null));
        await using var provider = BuildProvider(fake);
        var worker = CreateWorker(provider);
        var firstRound = worker.RunOnceAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await AddMessageAsync(leadId, "arrived-during-llm", DateTimeOffset.UtcNow, "Arriving while processing");
        release.TrySetResult();
        Assert.True(await firstRound);

        await using (var db = fixture.CreateDbContext())
        {
            Assert.Null(await db.Messages.Where(message => message.WaMessageId == "arrived-during-llm")
                .Select(message => message.ProcessedAt).SingleAsync());
            Assert.True(await worker.RunOnceAsync(CancellationToken.None));
        }

        await using var verify = fixture.CreateDbContext();
        Assert.NotNull(await verify.Messages.Where(message => message.WaMessageId == "arrived-during-llm")
            .Select(message => message.ProcessedAt).SingleAsync());
        Assert.Equal(2, await verify.Qualifications.CountAsync(qualification => qualification.LeadId == leadId));
    }

    [IntegrationFact]
    public async Task ConcurrentWorkersDoNotDoubleProcessALead()
    {
        var leadId = await CreateLeadWithMessagesAsync(1, DateTimeOffset.UtcNow.AddSeconds(-2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeLeadQualifier();
        fake.EnqueueHandler(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return FakeLeadQualifier.ValidResponse();
        });
        await using var provider = BuildProvider(fake);
        var workerA = CreateWorker(provider);
        var workerB = CreateWorker(provider);
        var runA = workerA.RunOnceAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(await workerB.RunOnceAsync(CancellationToken.None));
        release.TrySetResult();
        Assert.True(await runA);

        await using var db = fixture.CreateDbContext();
        Assert.Single(await db.Qualifications.Where(item => item.LeadId == leadId).ToListAsync());
    }

    [IntegrationFact]
    public async Task ThreeFailuresCreateForcedHandoffAndStopRetryingPoisonMessages()
    {
        var leadId = await CreateLeadWithMessagesAsync(1, DateTimeOffset.UtcNow.AddSeconds(-2));
        var fake = new FakeLeadQualifier();
        fake.EnqueueException(new InvalidOperationException("fake failure 1"));
        fake.EnqueueException(new InvalidOperationException("fake failure 2"));
        fake.EnqueueException(new InvalidOperationException("fake failure 3"));
        await using var provider = BuildProvider(fake);
        var worker = CreateWorker(provider);

        Assert.True(await worker.RunOnceAsync(CancellationToken.None));
        Assert.True(await worker.RunOnceAsync(CancellationToken.None));
        Assert.True(await worker.RunOnceAsync(CancellationToken.None));
        Assert.False(await worker.RunOnceAsync(CancellationToken.None));

        await using var db = fixture.CreateDbContext();
        var qualification = await db.Qualifications.SingleAsync(item => item.LeadId == leadId);
        Assert.Equal("handoff_human", qualification.NextAction);
        Assert.True(qualification.ForcedHandoff);
        Assert.Equal("llm_failure", qualification.ForcedReason);
        Assert.Equal(QualificationActionStatus.HumanReview, qualification.ActionStatus);
        Assert.NotNull(await db.Messages.Where(message => message.LeadId == leadId)
            .Select(message => message.ProcessedAt).SingleAsync());
    }

    [IntegrationFact]
    public async Task ExpiredLeaseCanBeClaimedAndLiveLeaseCannot()
    {
        var liveLead = await CreateLeadWithMessagesAsync(1, DateTimeOffset.UtcNow.AddSeconds(-2));
        var fake = new FakeLeadQualifier();
        fake.EnqueueResponse(FakeLeadQualifier.ValidResponse());
        await using var provider = BuildProvider(fake);
        var worker = CreateWorker(provider);
        await using (var db = fixture.CreateDbContext())
        {
            var lead = await db.Leads.SingleAsync(item => item.Id == liveLead);
            lead.QualifyClaimedUntil = DateTimeOffset.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }
        Assert.False(await worker.RunOnceAsync(CancellationToken.None));

        await using (var db = fixture.CreateDbContext())
        {
            var lead = await db.Leads.SingleAsync(item => item.Id == liveLead);
            lead.QualifyClaimedUntil = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }
        Assert.True(await worker.RunOnceAsync(CancellationToken.None));
        Assert.Equal(1, fake.Calls);
    }

    [IntegrationFact]
    public async Task TenantHotThresholdControlsLeadStage()
    {
        var leadId = await CreateLeadWithMessagesAsync(1, DateTimeOffset.UtcNow.AddSeconds(-2), hotThreshold: 70);
        var fake = new FakeLeadQualifier();
        fake.EnqueueResponse(FakeLeadQualifier.ValidResponse(score: 85));
        await using var provider = BuildProvider(fake);

        Assert.True(await CreateWorker(provider).RunOnceAsync(CancellationToken.None));
        await using var db = fixture.CreateDbContext();
        Assert.Equal("hot", await db.Leads.Where(lead => lead.Id == leadId).Select(lead => lead.Stage).SingleAsync());
    }

    [IntegrationFact]
    public async Task ApprovedReplyRequiresOptInAndPersistsOutboundMessage()
    {
        var leadId = await CreateLeadWithMessagesAsync(1, DateTimeOffset.UtcNow.AddMinutes(-2));
        var fakeQualifier = new FakeLeadQualifier();
        fakeQualifier.EnqueueResponse(FakeLeadQualifier.ValidResponse());
        await using var provider = BuildProvider(fakeQualifier);
        Assert.True(await CreateWorker(provider).RunOnceAsync(CancellationToken.None));

        await using var db = fixture.CreateDbContext();
        var qualification = await db.Qualifications.SingleAsync(item => item.LeadId == leadId);
        var sender = provider.GetRequiredService<FakeWhatsAppMessageSender>();
        var service = new QualificationApprovalService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            sender,
            provider.GetRequiredService<ILogger<QualificationApprovalService>>());

        await service.ApproveAsync(qualification.Id, recipientOptInConfirmed: false, CancellationToken.None);
        Assert.Equal(0, sender.Calls);
        Assert.Equal(QualificationActionStatus.PendingApproval,
            await db.Qualifications.Where(item => item.Id == qualification.Id).Select(item => item.ActionStatus).SingleAsync());

        await service.ApproveAsync(qualification.Id, recipientOptInConfirmed: true, CancellationToken.None);

        Assert.Equal(1, sender.Calls);
        Assert.Equal(QualificationActionStatus.Sent,
            await db.Qualifications.Where(item => item.Id == qualification.Id).Select(item => item.ActionStatus).SingleAsync());
        Assert.NotNull(await db.Qualifications.Where(item => item.Id == qualification.Id)
            .Select(item => item.OptInConfirmedAt).SingleAsync());
        Assert.Equal(1, await db.Messages.CountAsync(message =>
            message.LeadId == leadId && message.Direction == "outbound" && message.WaMessageId == "wamid-test-outbound"));
    }

    [IntegrationFact]
    public async Task ApprovalOutsideCustomerServiceWindowCreatesHumanReviewWithoutSending()
    {
        var leadId = await CreateLeadWithMessagesAsync(1, DateTimeOffset.UtcNow.AddHours(-25));
        var fakeQualifier = new FakeLeadQualifier();
        fakeQualifier.EnqueueResponse(FakeLeadQualifier.ValidResponse());
        await using var provider = BuildProvider(fakeQualifier);
        Assert.True(await CreateWorker(provider).RunOnceAsync(CancellationToken.None));

        await using var db = fixture.CreateDbContext();
        var qualificationId = await db.Qualifications.Where(item => item.LeadId == leadId)
            .Select(item => item.Id).SingleAsync();
        var sender = provider.GetRequiredService<FakeWhatsAppMessageSender>();
        var service = new QualificationApprovalService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            sender,
            provider.GetRequiredService<ILogger<QualificationApprovalService>>());

        await service.ApproveAsync(qualificationId, recipientOptInConfirmed: true, CancellationToken.None);

        var qualification = await db.Qualifications.SingleAsync(item => item.Id == qualificationId);
        Assert.Equal(QualificationActionStatus.HumanReview, qualification.ActionStatus);
        Assert.Equal("outside_customer_service_window", qualification.ActionReason);
        Assert.NotNull(qualification.ActionedAt);
        Assert.Equal(0, sender.Calls);
    }

    private async Task<long> CreateLeadWithMessagesAsync(
        int messageCount,
        DateTimeOffset receivedAt,
        int hotThreshold = 70)
    {
        await using var db = fixture.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N");
        var tenant = new Tenant
        {
            Name = $"Integration tenant {suffix}",
            PhoneNumberId = $"test-{suffix}",
            BusinessContext = "Services are by appointment. No price information is verified.",
            HotThreshold = hotThreshold,
            Active = true
        };
        var lead = new Lead { WaId = $"wa-{suffix}", Stage = "new", Tenant = tenant };
        for (var index = 0; index < messageCount; index++)
        {
            lead.Messages.Add(new Message
            {
                WaMessageId = $"msg-{suffix}-{index}",
                Direction = "inbound",
                Type = "text",
                Body = $"Question {index + 1}",
                ReceivedAt = receivedAt.AddMilliseconds(index)
            });
        }

        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id;
    }

    private async Task AddMessageAsync(long leadId, string messageId, DateTimeOffset receivedAt, string body)
    {
        await using var db = fixture.CreateDbContext();
        db.Messages.Add(new Message
        {
            LeadId = leadId,
            WaMessageId = messageId,
            Direction = "inbound",
            Type = "text",
            Body = body,
            ReceivedAt = receivedAt
        });
        await db.SaveChangesAsync();
    }

    private ServiceProvider BuildProvider(FakeLeadQualifier fake, int debounceSeconds = 0)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<LeadDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddScoped<FluentValidation.IValidator<QualificationResponse>, QualificationResponseValidator>();
        services.AddScoped<QualificationResponseParser>();
        services.AddSingleton<ILeadQualifier>(fake);
        services.AddSingleton<FakeWhatsAppMessageSender>();
        services.AddSingleton<IWhatsAppMessageSender>(provider =>
            provider.GetRequiredService<FakeWhatsAppMessageSender>());
        services.AddSingleton<QualificationGuard>();
        services.Configure<QualificationWorkerOptions>(options =>
        {
            options.DebounceSeconds = debounceSeconds;
            options.LeaseSeconds = 120;
            options.MaxAttempts = 3;
            options.MessageWindow = 10;
        });
        return services.BuildServiceProvider();
    }

    private static QualificationWorker CreateWorker(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILeadQualifier>(),
            provider.GetRequiredService<QualificationGuard>(),
            provider.GetRequiredService<IOptions<QualificationWorkerOptions>>(),
            provider.GetRequiredService<ILogger<QualificationWorker>>());

    private sealed class FakeWhatsAppMessageSender : IWhatsAppMessageSender
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<string> SendTextAsync(
            string phoneNumberId,
            string recipient,
            string body,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult("wamid-test-outbound");
        }
    }
}
