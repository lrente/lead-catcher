using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WaLeads.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "qualify_claimed_until",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "qualify_attempts",
                table: "leads",
                type: "integer",
                nullable: false,
                defaultValue: 0);
            migrationBuilder.AddColumn<int>(
                name: "hot_threshold",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 70);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processed_at",
                table: "messages",
                type: "timestamp with time zone",
                nullable: true);
            migrationBuilder.CreateTable(
                name: "qualifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    lead_id = table.Column<long>(type: "bigint", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    prompt_version = table.Column<string>(type: "text", nullable: false),
                    intent = table.Column<string>(type: "text", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    next_action = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    suggested_reply = table.Column<string>(type: "text", nullable: true),
                    missing_info = table.Column<string>(type: "jsonb", nullable: false),
                    forced_handoff = table.Column<bool>(type: "boolean", nullable: false),
                    forced_reason = table.Column<string>(type: "text", nullable: true),
                    raw_response = table.Column<string>(type: "text", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actioned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qualifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_qualifications_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "qualifications_lead_created_idx",
                table: "qualifications",
                columns: new[] { "lead_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "qualifications");
            migrationBuilder.DropColumn(name: "qualify_claimed_until", table: "leads");
            migrationBuilder.DropColumn(name: "qualify_attempts", table: "leads");
            migrationBuilder.DropColumn(name: "hot_threshold", table: "tenants");
            migrationBuilder.DropColumn(name: "processed_at", table: "messages");
        }
    }
}
