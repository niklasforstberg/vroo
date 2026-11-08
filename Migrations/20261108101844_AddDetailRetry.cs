using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vroo.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DetailAttempts",
                table: "Cars",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DetailsFetchedAt",
                table: "Cars",
                type: "timestamp with time zone",
                nullable: true);

            // Existing cars: details present means the fetch worked; none means it failed once.
            migrationBuilder.Sql("""
                UPDATE "Cars" SET "DetailsFetchedAt" = "FirstSeenAt"
                WHERE "RegistrationNumber" IS NOT NULL OR "Color" IS NOT NULL OR "DriveType" IS NOT NULL
                   OR "Gearbox" IS NOT NULL OR "BodyType" IS NOT NULL;
                UPDATE "Cars" SET "DetailAttempts" = 1 WHERE "DetailsFetchedAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailAttempts",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "DetailsFetchedAt",
                table: "Cars");
        }
    }
}
