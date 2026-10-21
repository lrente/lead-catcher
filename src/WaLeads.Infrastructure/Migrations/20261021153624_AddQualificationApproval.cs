using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaLeads.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualificationApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "action_error_id",
                table: "qualifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "action_reason",
                table: "qualifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "action_status",
                table: "qualifications",
                type: "text",
                nullable: false,
                defaultValue: "pending_approval");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "qualifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whatsapp_message_id",
                table: "qualifications",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "qualifications_action_status_created_idx",
                table: "qualifications",
                columns: new[] { "action_status", "created_at" });

            migrationBuilder.Sql("""
                UPDATE qualifications
                SET action_status = CASE next_action
                    WHEN 'ignore' THEN 'ignored'
                    ELSE 'human_review'
                END,
                action_reason = COALESCE(forced_reason, CASE next_action
                    WHEN 'ignore' THEN 'ignored'
                    ELSE 'handoff_requested'
                END),
                actioned_at = COALESCE(actioned_at, now())
                WHERE next_action IN ('ignore', 'handoff_human')
                  AND actioned_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "qualifications_action_status_created_idx",
                table: "qualifications");

            migrationBuilder.DropColumn(
                name: "action_error_id",
                table: "qualifications");

            migrationBuilder.DropColumn(
                name: "action_reason",
                table: "qualifications");

            migrationBuilder.DropColumn(
                name: "action_status",
                table: "qualifications");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "qualifications");

            migrationBuilder.DropColumn(
                name: "whatsapp_message_id",
                table: "qualifications");
        }
    }
}
