using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StreamPlatformBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddThumbnailUrlToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BannedChatUsers");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatModerators");

            migrationBuilder.DropColumn(
                name: "IsStreamer",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotalPremiumSubscribers",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotalSubscribers",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmoteOnlyMode",
                table: "Streams");

            migrationBuilder.DropColumn(
                name: "IsChatEnabled",
                table: "Streams");

            migrationBuilder.DropColumn(
                name: "IsLive",
                table: "Streams");

            migrationBuilder.DropColumn(
                name: "IsSubOnlyChat",
                table: "Streams");

            migrationBuilder.DropColumn(
                name: "StreamName",
                table: "Streams");

            migrationBuilder.RenameColumn(
                name: "StreamKey",
                table: "Streams",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "SlowModeInterval",
                table: "Streams",
                newName: "TotalViews");

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "Streams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string[]>(
                name: "Tags",
                table: "Streams",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "StreamCategory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BannerImageUrl = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamCategory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Streams_CategoryId",
                table: "Streams",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Streams_StreamCategory_CategoryId",
                table: "Streams",
                column: "CategoryId",
                principalTable: "StreamCategory",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Streams_StreamCategory_CategoryId",
                table: "Streams");

            migrationBuilder.DropTable(
                name: "StreamCategory");

            migrationBuilder.DropIndex(
                name: "IX_Streams_CategoryId",
                table: "Streams");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Streams");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Streams");

            migrationBuilder.RenameColumn(
                name: "TotalViews",
                table: "Streams",
                newName: "SlowModeInterval");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "Streams",
                newName: "StreamKey");

            migrationBuilder.AddColumn<bool>(
                name: "IsStreamer",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TotalPremiumSubscribers",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalSubscribers",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "EmoteOnlyMode",
                table: "Streams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsChatEnabled",
                table: "Streams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsLive",
                table: "Streams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSubOnlyChat",
                table: "Streams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StreamName",
                table: "Streams",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BannedChatUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BannedUserId = table.Column<int>(type: "integer", nullable: false),
                    StreamId = table.Column<int>(type: "integer", nullable: false),
                    BannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BannedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BannedChatUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BannedChatUsers_Streams_StreamId",
                        column: x => x.StreamId,
                        principalTable: "Streams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BannedChatUsers_Users_BannedUserId",
                        column: x => x.BannedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    DonationAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    MessageColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MessageType = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Streams_StreamId",
                        column: x => x.StreamId,
                        principalTable: "Streams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatModerators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModeratorId = table.Column<int>(type: "integer", nullable: false),
                    StreamId = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatModerators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatModerators_Streams_StreamId",
                        column: x => x.StreamId,
                        principalTable: "Streams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatModerators_Users_ModeratorId",
                        column: x => x.ModeratorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BannedChatUsers_BannedUserId",
                table: "BannedChatUsers",
                column: "BannedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BannedChatUsers_StreamId",
                table: "BannedChatUsers",
                column: "StreamId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_StreamId",
                table: "ChatMessages",
                column: "StreamId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_UserId",
                table: "ChatMessages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatModerators_ModeratorId",
                table: "ChatModerators",
                column: "ModeratorId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatModerators_StreamId",
                table: "ChatModerators",
                column: "StreamId");
        }
    }
}
