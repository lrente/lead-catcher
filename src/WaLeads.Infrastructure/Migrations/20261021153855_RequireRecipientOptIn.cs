using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaLeads.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RequireRecipientOptIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "opt_in_confirmed_at",
                table: "qualifications",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "opt_in_confirmed_at",
                table: "qualifications");
        }
    }
}
