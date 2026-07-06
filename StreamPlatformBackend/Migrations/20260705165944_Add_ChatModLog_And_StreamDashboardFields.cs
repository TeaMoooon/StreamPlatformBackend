using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StreamPlatformBackend.Migrations
{
    /// <inheritdoc />
    public partial class Add_ChatModLog_And_StreamDashboardFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StreamAnnouncement",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StreamLanguage",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "StreamChatModerationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetUserId = table.Column<int>(type: "integer", nullable: true),
                    TargetUsername = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MessageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamChatModerationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StreamChatModerationLogs_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StreamChatModerationLogs_Users_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StreamChatModerationLogs_ActorUserId",
                table: "StreamChatModerationLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StreamChatModerationLogs_StreamerId_CreatedAt",
                table: "StreamChatModerationLogs",
                columns: new[] { "StreamerId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StreamChatModerationLogs");

            migrationBuilder.DropColumn(
                name: "StreamAnnouncement",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StreamLanguage",
                table: "Users");
        }
    }
}
