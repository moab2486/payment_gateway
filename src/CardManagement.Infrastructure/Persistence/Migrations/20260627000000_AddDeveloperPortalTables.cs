using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeveloperPortalTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeveloperApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Scopes = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsSandbox = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    GracePeriodEndsAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperRequestLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RequestHeaders = table.Column<string>(type: "text", nullable: true),
                    RequestBody = table.Column<string>(type: "text", nullable: true),
                    ResponseStatus = table.Column<int>(type: "integer", nullable: false),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    Latency = table.Column<TimeSpan>(type: "interval", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperRequestLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperApiKeys_DeveloperId",
                table: "DeveloperApiKeys",
                column: "DeveloperId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperApiKeys_KeyHash",
                table: "DeveloperApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperApiKeys_Status",
                table: "DeveloperApiKeys",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperApiKeys_DeveloperId_Status",
                table: "DeveloperApiKeys",
                columns: new[] { "DeveloperId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperRequestLogs_ApiKeyId",
                table: "DeveloperRequestLogs",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperRequestLogs_DeveloperId",
                table: "DeveloperRequestLogs",
                column: "DeveloperId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperRequestLogs_ResponseStatus",
                table: "DeveloperRequestLogs",
                column: "ResponseStatus");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperRequestLogs_TimestampUtc",
                table: "DeveloperRequestLogs",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperRequestLogs_DeveloperId_TimestampUtc",
                table: "DeveloperRequestLogs",
                columns: new[] { "DeveloperId", "TimestampUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeveloperApiKeys");

            migrationBuilder.DropTable(
                name: "DeveloperRequestLogs");
        }
    }
}
