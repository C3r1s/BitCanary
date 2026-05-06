// Миграция схемы БД BitCanary: применение изменений к PostgreSQL.
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Persistence.Migrations
{
    public partial class AddNotificationSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "show_notifications",
                table: "user_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_sender_name",
                table: "user_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "show_notifications",
                table: "user_settings");

            migrationBuilder.DropColumn(
                name: "show_sender_name",
                table: "user_settings");
        }
    }
}
