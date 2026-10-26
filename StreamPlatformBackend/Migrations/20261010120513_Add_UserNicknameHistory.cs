using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StreamPlatformBackend.Migrations
{
    /// <inheritdoc />
    public partial class Add_UserNicknameHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserNicknameHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Nickname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SetAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNicknameHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNicknameHistories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserNicknameHistories_Nickname_SetAt",
                table: "UserNicknameHistories",
                columns: new[] { "Nickname", "SetAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNicknameHistories_UserId_SetAt",
                table: "UserNicknameHistories",
                columns: new[] { "UserId", "SetAt" });

            // Backfill: текущий ник каждого пользователя как первая запись истории.
            migrationBuilder.Sql(
                """
                INSERT INTO "UserNicknameHistories" ("UserId", "Nickname", "SetAt", "EndedAt")
                SELECT u."Id", u."Nickname", COALESCE(u."RegistrationDate", NOW() AT TIME ZONE 'utc'), NULL
                FROM "Users" u
                WHERE NOT EXISTS (
                    SELECT 1 FROM "UserNicknameHistories" h WHERE h."UserId" = u."Id"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserNicknameHistories");
        }
    }
}
