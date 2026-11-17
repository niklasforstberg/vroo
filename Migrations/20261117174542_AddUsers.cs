using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Vroo.Migrations
{
    /// <inheritdoc />
    public partial class AddUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmailVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerificationTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VerificationTokenExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResetTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResetTokenExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    WatchedModelId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => new { x.UserId, x.WatchedModelId });
                    table.ForeignKey(
                        name: "FK_Subscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Subscriptions_WatchedModels_WatchedModelId",
                        column: x => x.WatchedModelId,
                        principalTable: "WatchedModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_WatchedModelId",
                table: "Subscriptions",
                column: "WatchedModelId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            // The models from the single-user era go to a placeholder admin (empty email, no password)
            // that AdminBootstrap claims at startup with Auth:AdminEmail. The pause state moves to the subscription.
            migrationBuilder.Sql("""
                INSERT INTO "Users" ("Email", "PasswordHash", "IsAdmin", "CreatedAt", "PasswordChangedAt")
                SELECT '', '', true, now(), now() WHERE EXISTS (SELECT 1 FROM "WatchedModels");

                INSERT INTO "Subscriptions" ("UserId", "WatchedModelId", "IsActive", "CreatedAt")
                SELECT u."Id", w."Id", w."IsActive", w."CreatedAt"
                FROM "WatchedModels" w CROSS JOIN "Users" u;
                """);

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "WatchedModels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "WatchedModels",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
