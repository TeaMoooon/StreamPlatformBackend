using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StreamPlatformBackend.Migrations
{
    /// <inheritdoc />
    public partial class StreamSave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RecordEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RecordEnabled",
                table: "Streams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RecordPath",
                table: "Streams",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RecordEnabled",
                table: "Streams");

            migrationBuilder.DropColumn(
                name: "RecordPath",
                table: "Streams");
        }
    }
}
