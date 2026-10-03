using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeskKit.Persistence.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDesktopDoubleClickGesture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DesktopDoubleClickTogglesWidgets",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesktopDoubleClickTogglesWidgets",
                table: "Settings");
        }
    }
}
