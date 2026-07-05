using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StreamPlatformBackend.Migrations
{
    /// <inheritdoc />
    public partial class Add_Role_To_StreamModerators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StreamModerators_StreamerId",
                table: "StreamModerators");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "StreamModerators",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_StreamModerators_StreamerId_ModeratorId",
                table: "StreamModerators",
                columns: new[] { "StreamerId", "ModeratorId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StreamModerators_StreamerId_ModeratorId",
                table: "StreamModerators");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "StreamModerators");

            migrationBuilder.CreateIndex(
                name: "IX_StreamModerators_StreamerId",
                table: "StreamModerators",
                column: "StreamerId");
        }
    }
}
