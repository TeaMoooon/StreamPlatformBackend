using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StreamPlatformBackend.Migrations
{
    /// <inheritdoc />
    public partial class Add_StreamChatBans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StreamChatBans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<int>(type: "integer", nullable: false),
                    BannedUserId = table.Column<int>(type: "integer", nullable: false),
                    BannedByUserId = table.Column<int>(type: "integer", nullable: false),
                    BannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamChatBans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StreamChatBans_Users_BannedByUserId",
                        column: x => x.BannedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StreamChatBans_Users_BannedUserId",
                        column: x => x.BannedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StreamChatBans_Users_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StreamChatBans_BannedByUserId",
                table: "StreamChatBans",
                column: "BannedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StreamChatBans_BannedUserId",
                table: "StreamChatBans",
                column: "BannedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StreamChatBans_StreamerId_BannedUserId",
                table: "StreamChatBans",
                columns: new[] { "StreamerId", "BannedUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StreamChatBans");
        }
    }
}
