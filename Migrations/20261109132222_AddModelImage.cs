using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vroo.Migrations
{
    /// <inheritdoc />
    public partial class AddModelImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelImages",
                columns: table => new
                {
                    WatchedModelId = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelImages", x => x.WatchedModelId);
                    table.ForeignKey(
                        name: "FK_ModelImages_WatchedModels_WatchedModelId",
                        column: x => x.WatchedModelId,
                        principalTable: "WatchedModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelImages");
        }
    }
}
