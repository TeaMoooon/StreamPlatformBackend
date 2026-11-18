using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StreamPlatformBackend.Migrations
{
    /// <inheritdoc />
    public partial class Rename_PlatformRoles_Safety_Owner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Platform account roles only (Users.Role). Do NOT touch StreamModerators.Role
            // — that is the channel team role "Moderator" / "Assistant".
            migrationBuilder.Sql("""
                UPDATE "Users" SET "Role" = 'Safety' WHERE "Role" = 'Moderator';
                UPDATE "Users" SET "Role" = 'Owner' WHERE "Role" = 'SuperAdmin';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Users" SET "Role" = 'Moderator' WHERE "Role" = 'Safety';
                UPDATE "Users" SET "Role" = 'SuperAdmin' WHERE "Role" = 'Owner';
                """);
        }
    }
}
