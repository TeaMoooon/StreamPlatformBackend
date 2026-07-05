using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StreamPlatformBackend.Migrations
{
    /// <inheritdoc />
    public partial class UserSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Streams_UserId",
                table: "Streams");

            migrationBuilder.AddColumn<int>(
                name: "CurrentStreamId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserSocialLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Url = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSocialLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSocialLinks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_CurrentStreamId",
                table: "Users",
                column: "CurrentStreamId");

            migrationBuilder.CreateIndex(
                name: "IX_Streams_UserId",
                table: "Streams",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSocialLinks_UserId",
                table: "UserSocialLinks",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Streams_CurrentStreamId",
                table: "Users",
                column: "CurrentStreamId",
                principalTable: "Streams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Streams_CurrentStreamId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "UserSocialLinks");

            migrationBuilder.DropIndex(
                name: "IX_Users_CurrentStreamId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Streams_UserId",
                table: "Streams");

            migrationBuilder.DropColumn(
                name: "CurrentStreamId",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Streams_UserId",
                table: "Streams",
                column: "UserId",
                unique: true);
        }
    }
}
