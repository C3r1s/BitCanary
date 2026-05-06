// Миграция схемы БД BitCanary: применение изменений к PostgreSQL.
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Persistence.Migrations
{
    public partial class AddKeyBundles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "protocol_version",
                table: "messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "one_time_pre_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_one_time_pre_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_key_bundles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ik_public = table.Column<byte[]>(type: "bytea", nullable: false),
                    spk_public = table.Column<byte[]>(type: "bytea", nullable: false),
                    spk_signature = table.Column<byte[]>(type: "bytea", nullable: false),
                    spk_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_key_bundles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_one_time_pre_keys_user_id_claimed_at",
                table: "one_time_pre_keys",
                columns: new[] { "user_id", "claimed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_user_key_bundles_user_id_device_id",
                table: "user_key_bundles",
                columns: new[] { "user_id", "device_id" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "one_time_pre_keys");

            migrationBuilder.DropTable(
                name: "user_key_bundles");

            migrationBuilder.DropColumn(
                name: "protocol_version",
                table: "messages");
        }
    }
}
