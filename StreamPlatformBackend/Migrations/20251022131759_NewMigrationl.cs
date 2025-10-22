using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StreamPlatformBackend.Migrations
{
    /// <inheritdoc />
    public partial class NewMigrationl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserVideoLinks");

            migrationBuilder.DropColumn(
                name: "HlsUrl",
                table: "Streams");

            migrationBuilder.RenameColumn(
                name: "ThumbnailUrl",
                table: "Users",
                newName: "LastStreamName");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "Streams",
                newName: "StreamName");

            migrationBuilder.AddColumn<int>(
                name: "LastCategoryId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastPreviewlUrl",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "LastTags",
                table: "Users",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "PreviewlUrl",
                table: "Streams",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastCategoryId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastPreviewlUrl",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastTags",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreviewlUrl",
                table: "Streams");

            migrationBuilder.RenameColumn(
                name: "LastStreamName",
                table: "Users",
                newName: "ThumbnailUrl");

            migrationBuilder.RenameColumn(
                name: "StreamName",
                table: "Streams",
                newName: "Title");

            migrationBuilder.AddColumn<string>(
                name: "HlsUrl",
                table: "Streams",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "UserVideoLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AddedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VideoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserVideoLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserVideoLinks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserVideoLinks_UserId",
                table: "UserVideoLinks",
                column: "UserId");
        }
    }
}
