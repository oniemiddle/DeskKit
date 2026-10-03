using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeskKit.Persistence.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgetAnimationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The defaults are the keys this build ships as, not the empty string EF
            // would otherwise choose: a layout that already exists has never stored an
            // animation preference, and the honest answer for it is the shipped one
            // rather than a value nothing recognises.
            migrationBuilder.AddColumn<string>(
                name: "WidgetAnimationDirection",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "Right");

            migrationBuilder.AddColumn<string>(
                name: "WidgetAnimationEasing",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "WidgetAnimationSpeed",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "WidgetsAnimation",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "Slide");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WidgetAnimationDirection",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "WidgetAnimationEasing",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "WidgetAnimationSpeed",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "WidgetsAnimation",
                table: "Settings");
        }
    }
}
