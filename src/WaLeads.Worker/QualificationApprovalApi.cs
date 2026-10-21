using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using WaLeads.Infrastructure;

namespace WaLeads.Worker;

public static class QualificationApprovalApi
{
    public static void MapQualificationApprovalApi(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/approvals"))
            {
                var configuredToken = app.Configuration[$"{ApprovalApiOptions.SectionName}:BearerToken"];
                if (string.IsNullOrWhiteSpace(configuredToken))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    return;
                }

                var authorization = context.Request.Headers.Authorization.ToString();
                const string bearerPrefix = "Bearer ";
                var token = authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                    ? authorization[bearerPrefix.Length..]
                    : string.Empty;
                var configuredBytes = Encoding.UTF8.GetBytes(configuredToken);
                var providedBytes = Encoding.UTF8.GetBytes(token);
                if (configuredBytes.Length != providedBytes.Length
                    || !CryptographicOperations.FixedTimeEquals(configuredBytes, providedBytes))
                {
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
            }

            await next(context);
        });

        app.MapGet("/api/approvals", async (LeadDbContext db, CancellationToken cancellationToken) =>
            await db.Qualifications
                .Where(item => (item.ActionStatus == QualificationActionStatus.PendingApproval
                        && item.ActionedAt == null)
                    || item.ActionStatus == QualificationActionStatus.HumanReview
                    || item.ActionStatus == QualificationActionStatus.SendUncertain
                    || item.ActionStatus == QualificationActionStatus.Sending)
                .OrderBy(item => item.CreatedAt)
                .Select(item => new
                {
                    qualification_id = item.Id,
                    lead_id = item.LeadId,
                    wa_id = item.Lead.WaId,
                    profile_name = item.Lead.ProfileName,
                    intent = item.Intent,
                    score = item.Score,
                    next_action = item.NextAction,
                    confidence = item.Confidence,
                    suggested_reply = item.SuggestedReply,
                    forced_reason = item.ForcedReason,
                    status = item.ActionStatus,
                    reason = item.ActionReason,
                    created_at = item.CreatedAt
                })
                .ToListAsync(cancellationToken));

        app.MapPost(
            "/api/approvals/{qualificationId:guid}/approve",
            (Guid qualificationId,
                [FromBody] ApproveQualificationRequest request,
                QualificationApprovalService service,
                CancellationToken cancellationToken) =>
                service.ApproveAsync(qualificationId, request.RecipientOptInConfirmed, cancellationToken));
        app.MapPost(
            "/api/approvals/{qualificationId:guid}/reject",
            (Guid qualificationId, QualificationApprovalService service, CancellationToken cancellationToken) =>
                service.RejectAsync(qualificationId, cancellationToken));
        app.MapPost(
            "/api/approvals/{qualificationId:guid}/resolve",
            (Guid qualificationId, QualificationApprovalService service, CancellationToken cancellationToken) =>
                service.ResolveAsync(qualificationId, cancellationToken));
    }

    public sealed record ApproveQualificationRequest(bool RecipientOptInConfirmed);
}
