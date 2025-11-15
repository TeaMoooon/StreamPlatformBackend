using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StreamPlatformBackend.Migrations
{
    /// <inheritdoc />
    public partial class Refactored : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
                name: "LastCategoryId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Streams");

            migrationBuilder.RenameColumn(
                name: "LastPreviewlUrl",
                table: "Users",
                newName: "LastPreviewUrl");

            migrationBuilder.RenameColumn(
                name: "PreviewlUrl",
                table: "Streams",
                newName: "PreviewUrl");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LastPreviewUrl",
                table: "Users",
                newName: "LastPreviewlUrl");

            migrationBuilder.RenameColumn(
                name: "PreviewUrl",
                table: "Streams",
                newName: "PreviewlUrl");

            migrationBuilder.AddColumn<int>(
                name: "LastCategoryId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "Streams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "StreamCategory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BannerImageUrl = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
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
    }
}
