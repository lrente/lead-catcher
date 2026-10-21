using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using WaLeads.Core;
using WaLeads.Infrastructure;

namespace WaLeads.Worker;

public sealed class QualificationWorker(
    IServiceScopeFactory scopeFactory,
    ILeadQualifier qualifier,
    QualificationGuard guard,
    IOptions<QualificationWorkerOptions> options,
    ILogger<QualificationWorker> logger) : BackgroundService
{
    private readonly QualificationWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idleSeconds = _options.PollIntervalSeconds;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var didWork = await RunOnceAsync(stoppingToken);
                if (didWork)
                {
                    idleSeconds = _options.PollIntervalSeconds;
                    continue;
                }

                await Task.Delay(TimeSpan.FromSeconds(idleSeconds), stoppingToken);
                idleSeconds = Math.Min(idleSeconds * 2, _options.MaxPollIntervalSeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                var errorId = Guid.NewGuid();
                logger.LogError(exception, "Qualification worker loop failed. ErrorId: {ErrorId}", errorId);
                await Task.Delay(TimeSpan.FromSeconds(_options.MaxPollIntervalSeconds), stoppingToken);
            }
        }
    }

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        ClaimedLead? claim;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LeadDbContext>();
            claim = await ClaimLeadAsync(db, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var errorId = Guid.NewGuid();
            logger.LogError(exception, "Qualification claim failed. ErrorId: {ErrorId}", errorId);
            return false;
        }

        if (claim is null)
        {
            return false;
        }

        QualificationBatch? batch = null;
        try
        {
            batch = await LoadBatchAsync(claim, cancellationToken);
            var result = await QualifyWithRetryAsync(batch, cancellationToken);
            var guarded = guard.Apply(
                result.Response,
                batch.Lead.Tenant.BusinessContext,
                batch.Messages.Any(message => message.Type != "text"),
                _options.MinimumConfidence);
            await PersistSuccessAsync(batch, result, guarded, cancellationToken);
            logger.LogInformation(
                "Qualified lead {LeadId} with qualification {QualificationId}; latency {LatencyMs} ms, tokens {InputTokens}/{OutputTokens}, forced reason {ForcedReason}.",
                claim.LeadId,
                result.QualificationId,
                result.LatencyMs,
                result.InputTokens,
                result.OutputTokens,
                guarded.ForcedReason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseLeaseAsync(claim.LeadId, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            var errorId = Guid.NewGuid();
            logger.LogError(
                exception,
                "Qualification failed for lead {LeadId}. ErrorId: {ErrorId}",
                claim.LeadId,
                errorId);
            await RecordFailureAsync(
                claim,
                batch?.UnprocessedInboundIds ?? [],
                exception is QualificationValidationException validationFailure
                    ? validationFailure.RawResponse
                    : null,
                cancellationToken);
        }

        return true;
    }

    private async Task<ClaimedLead?> ClaimLeadAsync(LeadDbContext db, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            WITH candidate AS (
                SELECT
                    l.id,
                    COALESCE((SELECT MAX(m.id) FROM messages m WHERE m.lead_id = l.id), 0) AS message_cutoff_id
                FROM leads l
                WHERE (l.qualify_claimed_until IS NULL OR l.qualify_claimed_until < now())
                  AND EXISTS (
                      SELECT 1
                      FROM messages m
                      WHERE m.lead_id = l.id
                        AND m.direction = 'inbound'
                        AND m.processed_at IS NULL
                  )
                  AND (
                      SELECT MAX(m.received_at)
                      FROM messages m
                      WHERE m.lead_id = l.id
                        AND m.direction = 'inbound'
                        AND m.processed_at IS NULL
                  ) < now() - ($1 * interval '1 second')
                ORDER BY (
                    SELECT MAX(m.received_at)
                    FROM messages m
                    WHERE m.lead_id = l.id
                      AND m.direction = 'inbound'
                      AND m.processed_at IS NULL
                )
                LIMIT 1
                FOR UPDATE OF l SKIP LOCKED
            )
            UPDATE leads l
            SET qualify_claimed_until = now() + ($2 * interval '1 second')
            FROM candidate c
            WHERE l.id = c.id
            RETURNING l.id, c.message_cutoff_id;
            """;
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = _options.DebounceSeconds });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = _options.LeaseSeconds });

        ClaimedLead? claim = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                claim = new ClaimedLead(reader.GetInt64(0), reader.GetInt64(1));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return claim;
    }

    private async Task<QualificationBatch> LoadBatchAsync(
        ClaimedLead claim,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadDbContext>();
        var lead = await db.Leads.Include(item => item.Tenant)
            .SingleAsync(item => item.Id == claim.LeadId, cancellationToken);
        var messages = await db.Messages
            .Where(message => message.LeadId == claim.LeadId && message.Id <= claim.MessageCutoffId)
            .OrderByDescending(message => message.ReceivedAt)
            .ThenByDescending(message => message.Id)
            .Take(_options.MessageWindow)
            .ToListAsync(cancellationToken);
        messages.Reverse();

        var conversation = messages.Select(message =>
            new QualificationMessage(message.Direction, message.Type, message.Body)).ToArray();
        var unprocessedIds = messages
            .Where(message => message.Direction == "inbound" && message.ProcessedAt is null)
            .Select(message => message.Id)
            .ToArray();

        return new QualificationBatch(claim.LeadId, lead, conversation, unprocessedIds);
    }

    private async Task<QualificationAttemptResult> QualifyWithRetryAsync(
        QualificationBatch batch,
        CancellationToken cancellationToken)
    {
        var feedback = (IReadOnlyList<string>)Array.Empty<string>();
        var totalLatency = 0;
        int? totalInputTokens = null;
        int? totalOutputTokens = null;
        string rawResponse = string.Empty;
        string model = string.Empty;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await qualifier.QualifyAsync(
                new QualificationRequest(
                    batch.Lead.Tenant.Name,
                    batch.Lead.Tenant.BusinessContext,
                    batch.Messages,
                    feedback),
                cancellationToken);
            totalLatency = (int)Math.Min((long)totalLatency + response.LatencyMs, int.MaxValue);
            totalInputTokens = SaturatingAdd(totalInputTokens, response.InputTokens);
            totalOutputTokens = SaturatingAdd(totalOutputTokens, response.OutputTokens);
            rawResponse = response.Content;
            model = response.Model;

            await using var scope = scopeFactory.CreateAsyncScope();
            var parser = scope.ServiceProvider.GetRequiredService<QualificationResponseParser>();
            var parsed = await parser.ParseAsync(response.Content, cancellationToken);
            if (parsed.IsValid)
            {
                return new QualificationAttemptResult(
                    parsed.Response!,
                    rawResponse,
                    model,
                    totalLatency,
                    totalInputTokens,
                    totalOutputTokens,
                    Guid.NewGuid());
            }

            feedback = parsed.Errors;
        }

        throw new QualificationValidationException(
            $"LLM response validation failed after retry: {string.Join("; ", feedback)}",
            rawResponse);
    }

    private async Task PersistSuccessAsync(
        QualificationBatch batch,
        QualificationAttemptResult attempt,
        GuardedQualification guarded,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var lead = await db.Leads.Include(item => item.Tenant)
            .SingleAsync(item => item.Id == batch.LeadId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var qualification = new Qualification
        {
            Id = attempt.QualificationId,
            LeadId = batch.LeadId,
            Model = attempt.Model,
            PromptVersion = QualificationConstants.PromptVersion,
            Intent = attempt.Response.Intent,
            Score = attempt.Response.Score,
            NextAction = guarded.Response.NextAction,
            Confidence = attempt.Response.Confidence,
            SuggestedReply = guarded.Response.SuggestedReply,
            MissingInfo = JsonSerializer.Serialize(attempt.Response.MissingInfo),
            ForcedHandoff = guarded.ForcedHandoff,
            ForcedReason = guarded.ForcedReason,
            RawResponse = attempt.RawResponse,
            LatencyMs = attempt.LatencyMs,
            InputTokens = attempt.InputTokens,
            OutputTokens = attempt.OutputTokens,
            CreatedAt = now
        };
        lead.Score = attempt.Response.Score;
        lead.Intent = attempt.Response.Intent;
        lead.Stage = LeadStageMapper.Map(lead.Stage, attempt.Response.Intent, attempt.Response.Score, lead.Tenant.HotThreshold);
        lead.QualifyAttempts = 0;
        lead.QualifyClaimedUntil = null;
        lead.UpdatedAt = now;

        db.Qualifications.Add(qualification);
        if (batch.UnprocessedInboundIds.Count > 0)
        {
            await db.Messages
                .Where(message => batch.UnprocessedInboundIds.Contains(message.Id) && message.ProcessedAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(message => message.ProcessedAt, now), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReleaseLeaseAsync(long leadId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadDbContext>();
        await db.Leads
            .Where(lead => lead.Id == leadId)
            .ExecuteUpdateAsync(update => update.SetProperty(lead => lead.QualifyClaimedUntil, (DateTimeOffset?)null), cancellationToken);
    }

    private async Task RecordFailureAsync(
        ClaimedLead claim,
        IReadOnlyList<long> includedMessageIds,
        string? rawResponse,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lead = await db.Leads.SingleAsync(item => item.Id == claim.LeadId, cancellationToken);
        lead.QualifyAttempts++;
        lead.QualifyClaimedUntil = null;

        if (lead.QualifyAttempts >= _options.MaxAttempts)
        {
            var qualificationId = Guid.NewGuid();
            db.Qualifications.Add(new Qualification
            {
                Id = qualificationId,
                LeadId = lead.Id,
                Model = "unavailable",
                PromptVersion = QualificationConstants.PromptVersion,
                Intent = "other",
                Score = lead.Score ?? 0,
                NextAction = "handoff_human",
                Confidence = 0m,
                SuggestedReply = null,
                MissingInfo = "[]",
                ForcedHandoff = true,
                ForcedReason = "llm_failure",
                RawResponse = string.IsNullOrWhiteSpace(rawResponse)
                    ? "Qualification failed after the configured retry limit."
                    : rawResponse,
                LatencyMs = 0,
                CreatedAt = DateTimeOffset.UtcNow
            });

            if (includedMessageIds.Count > 0)
            {
                var messageIds = includedMessageIds.ToArray();
                await db.Messages
                    .Where(message => messageIds.Contains(message.Id) && message.ProcessedAt == null)
                    .ExecuteUpdateAsync(
                        update => update.SetProperty(message => message.ProcessedAt, DateTimeOffset.UtcNow),
                        cancellationToken);
            }

            logger.LogWarning(
                "Lead {LeadId} exhausted qualification attempts; stored qualification {QualificationId} with forced reason {ForcedReason}.",
                lead.Id,
                qualificationId,
                "llm_failure");
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static int? SaturatingAdd(int? value, int? addend)
    {
        if (value is null && addend is null)
        {
            return null;
        }

        return (int)Math.Min((long)(value ?? 0) + (addend ?? 0), int.MaxValue);
    }

    private sealed record ClaimedLead(long LeadId, long MessageCutoffId);

    private sealed record QualificationBatch(
        long LeadId,
        Lead Lead,
        IReadOnlyList<QualificationMessage> Messages,
        IReadOnlyList<long> UnprocessedInboundIds);

    private sealed record QualificationAttemptResult(
        QualificationResponse Response,
        string RawResponse,
        string Model,
        int LatencyMs,
        int? InputTokens,
        int? OutputTokens,
        Guid QualificationId);

    private sealed class QualificationValidationException(string message, string rawResponse)
        : InvalidOperationException(message)
    {
        public string RawResponse { get; } = rawResponse;
    }
}
