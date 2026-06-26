using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EmailSubjectTemplate = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    EmailBodyTemplate = table.Column<string>(type: "text", nullable: true),
                    SmsBodyTemplate = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: true),
                    WhatsAppBodyTemplate = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    RequiredVariables = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PrimaryChannel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FallbackChannel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CategoryOptIn = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveryLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DispatchedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveryLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_Name",
                table: "NotificationTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_Category",
                table: "NotificationTemplates",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_RecipientId",
                table: "NotificationPreferences",
                column: "RecipientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryLogs_RecipientId",
                table: "NotificationDeliveryLogs",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryLogs_Channel",
                table: "NotificationDeliveryLogs",
                column: "Channel");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryLogs_TemplateId",
                table: "NotificationDeliveryLogs",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryLogs_DispatchedAtUtc",
                table: "NotificationDeliveryLogs",
                column: "DispatchedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationTemplates");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "NotificationDeliveryLogs");
        }
    }
}
