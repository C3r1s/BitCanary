// Миграция схемы БД BitCanary: применение изменений к PostgreSQL.
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Messenger.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260429182000_AddTerminalColorScheme")]
    public partial class AddTerminalColorScheme : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "terminal_color_scheme",
                table: "user_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "terminal_color_scheme",
                table: "user_settings");
        }
    }
}
