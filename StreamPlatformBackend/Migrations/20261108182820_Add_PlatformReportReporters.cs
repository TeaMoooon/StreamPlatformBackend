using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StreamPlatformBackend.Migrations
{
    /// <inheritdoc />
    public partial class Add_PlatformReportReporters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformReportReporters",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportId = table.Column<long>(type: "bigint", nullable: false),
                    ReporterUserId = table.Column<int>(type: "integer", nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformReportReporters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformReportReporters_PlatformReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "PlatformReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlatformReportReporters_Users_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformReports_Status_TargetType_Reason_TargetUserId_Strea~",
                table: "PlatformReports",
                columns: new[] { "Status", "TargetType", "Reason", "TargetUserId", "StreamId", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformReportReporters_ReporterUserId_CreatedAt",
                table: "PlatformReportReporters",
                columns: new[] { "ReporterUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformReportReporters_ReportId_ReporterUserId",
                table: "PlatformReportReporters",
                columns: new[] { "ReportId", "ReporterUserId" },
                unique: true);

            // Backfill: each existing case gets its opener as the first reporter vote.
            migrationBuilder.Sql("""
                INSERT INTO "PlatformReportReporters" ("ReportId", "ReporterUserId", "Details", "CreatedAt")
                SELECT r."Id", r."ReporterUserId", r."Details", r."CreatedAt"
                FROM "PlatformReports" r
                WHERE NOT EXISTS (
                    SELECT 1 FROM "PlatformReportReporters" x
                    WHERE x."ReportId" = r."Id" AND x."ReporterUserId" = r."ReporterUserId"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformReportReporters");

            migrationBuilder.DropIndex(
                name: "IX_PlatformReports_Status_TargetType_Reason_TargetUserId_Strea~",
                table: "PlatformReports");
        }
    }
}
