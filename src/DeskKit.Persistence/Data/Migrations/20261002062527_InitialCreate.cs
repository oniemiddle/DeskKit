using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeskKit.Persistence.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Meta",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meta", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Theme = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StartWithWindows = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowTrayIcon = table.Column<bool>(type: "INTEGER", nullable: false),
                    WidgetsVisible = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Widgets",
                columns: table => new
                {
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    WidgetId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    X = table.Column<int>(type: "INTEGER", nullable: false),
                    Y = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false),
                    SettingsVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Widgets", x => x.InstanceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Widgets_Order",
                table: "Widgets",
                column: "Order");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Meta");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Widgets");
        }
    }
}
