// Миграция схемы БД BitCanary: применение изменений к PostgreSQL.
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Messenger.Infrastructure.Persistence.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "call_signal_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_call_signal_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    avatar_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    public_key = table.Column<string>(type: "text", nullable: false),
                    bio = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    avatar_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    last_seen_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_memberships",
                columns: table => new
                {
                    chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    joined_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_read_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_muted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_memberships", x => new { x.chat_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_chat_memberships_chats_chat_id",
                        column: x => x.chat_id,
                        principalTable: "chats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chat_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "folders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folders", x => x.id);
                    table.ForeignKey(
                        name: "FK_folders_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blob_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    content_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.id);
                    table.ForeignKey(
                        name: "FK_media_assets_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    theme_preference = table.Column<int>(type: "integer", nullable: false),
                    send_by_enter = table.Column<bool>(type: "boolean", nullable: false),
                    use_compact_mode = table.Column<bool>(type: "boolean", nullable: false),
                    enable_custom_emoji = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_settings", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "folder_chats",
                columns: table => new
                {
                    folder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folder_chats", x => new { x.folder_id, x.chat_id });
                    table.ForeignKey(
                        name: "FK_folder_chats_chats_chat_id",
                        column: x => x.chat_id,
                        principalTable: "chats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_folder_chats_folders_folder_id",
                        column: x => x.folder_id,
                        principalTable: "folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    encrypted_payload = table.Column<string>(type: "text", nullable: false),
                    encryption_algorithm = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    key_envelope = table.Column<string>(type: "text", nullable: false),
                    media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reply_to_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_chats_chat_id",
                        column: x => x.chat_id,
                        principalTable: "chats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_messages_media_assets_media_id",
                        column: x => x.media_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_messages_messages_reply_to_message_id",
                        column: x => x.reply_to_message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_messages_users_sender_id",
                        column: x => x.sender_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_memberships_user_id",
                table: "chat_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_folder_chats_chat_id",
                table: "folder_chats",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_folders_user_id",
                table: "folders",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_uploaded_by_user_id",
                table: "media_assets",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_chat_id_created_at_utc",
                table: "messages",
                columns: new[] { "chat_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_chat_id_sender_id_client_message_id",
                table: "messages",
                columns: new[] { "chat_id", "sender_id", "client_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_media_id",
                table: "messages",
                column: "media_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_reply_to_message_id",
                table: "messages",
                column: "reply_to_message_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_sender_id",
                table: "messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_settings_user_id",
                table: "user_settings",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "call_signal_logs");

            migrationBuilder.DropTable(
                name: "chat_memberships");

            migrationBuilder.DropTable(
                name: "folder_chats");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropTable(
                name: "folders");

            migrationBuilder.DropTable(
                name: "chats");

            migrationBuilder.DropTable(
                name: "media_assets");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
