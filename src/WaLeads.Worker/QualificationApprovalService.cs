using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WaLeads.Core;
using WaLeads.Infrastructure;

namespace WaLeads.Worker;

public sealed class QualificationApprovalService(
    IServiceScopeFactory scopeFactory,
    IWhatsAppMessageSender messageSender,
    ILogger<QualificationApprovalService> logger)
{
    public async Task<IResult> ApproveAsync(
        Guid qualificationId,
        bool recipientOptInConfirmed,
        CancellationToken cancellationToken)
    {
        if (!recipientOptInConfirmed)
        {
            return Results.BadRequest(new { error = "Recipient opt-in confirmation is required." });
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadDbContext>();
        var qualification = await db.Qualifications
            .Include(item => item.Lead)
            .ThenInclude(lead => lead.Tenant)
            .SingleOrDefaultAsync(item => item.Id == qualificationId, cancellationToken);
        if (qualification is null)
        {
            return Results.NotFound();
        }

        if (qualification.ActionStatus != QualificationActionStatus.PendingApproval
            || qualification.ActionedAt is not null
            || qualification.NextAction is not ("ask_question" or "propose_booking")
            || string.IsNullOrWhiteSpace(qualification.SuggestedReply))
        {
            return Results.Conflict(new { status = qualification.ActionStatus });
        }

        var approvedAt = DateTimeOffset.UtcNow;
        var claimed = await db.Qualifications
            .Where(item => item.Id == qualificationId
                && item.ActionStatus == QualificationActionStatus.PendingApproval
                && item.ActionedAt == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.ActionStatus, QualificationActionStatus.Sending)
                .SetProperty(item => item.ApprovedAt, approvedAt)
                .SetProperty(item => item.OptInConfirmedAt, approvedAt), cancellationToken);
        if (claimed != 1)
        {
            return Results.Conflict(new { status = "already_actioned" });
        }

        var latestInboundAt = await db.Messages
            .Where(message => message.LeadId == qualification.LeadId && message.Direction == "inbound")
            .MaxAsync(message => (DateTimeOffset?)message.ReceivedAt, CancellationToken.None);
        if (!WhatsAppSendEligibility.IsCustomerServiceWindowOpen(latestInboundAt, DateTimeOffset.UtcNow))
        {
            await UpdateStatusAsync(
                db,
                qualificationId,
                QualificationActionStatus.HumanReview,
                "outside_customer_service_window",
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            return Results.Conflict(new { status = QualificationActionStatus.HumanReview, reason = "outside_customer_service_window" });
        }

        string whatsappMessageId;
        try
        {
            whatsappMessageId = await messageSender.SendTextAsync(
                qualification.Lead.Tenant.PhoneNumberId,
                qualification.Lead.WaId,
                qualification.SuggestedReply,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            var errorId = Guid.NewGuid();
            await UpdateStatusAsync(
                db,
                qualificationId,
                QualificationActionStatus.SendUncertain,
                "whatsapp_send_uncertain",
                DateTimeOffset.UtcNow,
                CancellationToken.None,
                errorId);
            logger.LogError(
                "WhatsApp send outcome is uncertain for qualification {QualificationId}. ErrorId: {ErrorId}",
                qualificationId,
                errorId);
            return Results.Json(
                new { status = QualificationActionStatus.SendUncertain, error_id = errorId },
                statusCode: StatusCodes.Status502BadGateway);
        }

        var sentAt = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        db.Messages.Add(new Message
        {
            LeadId = qualification.LeadId,
            WaMessageId = whatsappMessageId,
            Direction = "outbound",
            Type = "text",
            Body = qualification.SuggestedReply,
            ReceivedAt = sentAt
        });
        qualification.ActionStatus = QualificationActionStatus.Sent;
        qualification.ActionReason = null;
        qualification.WhatsAppMessageId = whatsappMessageId;
        qualification.ActionedAt = sentAt;
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        logger.LogInformation(
            "Sent approved qualification {QualificationId} for lead {LeadId} as WhatsApp message {WhatsAppMessageId}.",
            qualificationId,
            qualification.LeadId,
            whatsappMessageId);
        return Results.Ok(new { status = QualificationActionStatus.Sent, message_id = whatsappMessageId });
    }

    public async Task<IResult> RejectAsync(Guid qualificationId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadDbContext>();
        var now = DateTimeOffset.UtcNow;
        var changed = await db.Qualifications
            .Where(item => item.Id == qualificationId
                && item.ActionStatus == QualificationActionStatus.PendingApproval
                && item.ActionedAt == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.ActionStatus, QualificationActionStatus.Rejected)
                .SetProperty(item => item.ActionedAt, now), cancellationToken);
        return changed == 1 ? Results.NoContent() : Results.Conflict();
    }

    public async Task<IResult> ResolveAsync(Guid qualificationId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadDbContext>();
        var changed = await db.Qualifications
            .Where(item => item.Id == qualificationId
                && item.ActionStatus == QualificationActionStatus.HumanReview)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.ActionStatus, QualificationActionStatus.Resolved)
                .SetProperty(item => item.ActionReason, (string?)null), cancellationToken);
        return changed == 1 ? Results.NoContent() : Results.Conflict();
    }

    private static Task<int> UpdateStatusAsync(
        LeadDbContext db,
        Guid qualificationId,
        string status,
        string reason,
        DateTimeOffset actionedAt,
        CancellationToken cancellationToken,
        Guid? errorId = null) =>
        db.Qualifications
            .Where(item => item.Id == qualificationId && item.ActionStatus == QualificationActionStatus.Sending)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.ActionStatus, status)
                .SetProperty(item => item.ActionReason, reason)
                .SetProperty(item => item.ActionedAt, actionedAt)
                .SetProperty(item => item.ActionErrorId, errorId), cancellationToken);
}
