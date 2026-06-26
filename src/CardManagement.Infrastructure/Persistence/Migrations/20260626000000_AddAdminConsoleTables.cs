using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminConsoleTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminPendingCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SerializedParameters = table.Column<string>(type: "jsonb", nullable: false),
                    MakerId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CheckerId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminPendingCommands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminTransactionSummaries",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalCount = table.Column<long>(type: "bigint", nullable: false),
                    SuccessCount = table.Column<long>(type: "bigint", nullable: false),
                    FailedCount = table.Column<long>(type: "bigint", nullable: false),
                    TotalAmountKobo = table.Column<long>(type: "bigint", nullable: false),
                    ProjectedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminTransactionSummaries", x => new { x.Date, x.Channel });
                });

            migrationBuilder.CreateTable(
                name: "AdminReconciliationStatuses",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Processor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalRecords = table.Column<int>(type: "integer", nullable: false),
                    MatchedRecords = table.Column<int>(type: "integer", nullable: false),
                    ExceptionCount = table.Column<int>(type: "integer", nullable: false),
                    ResolvedCount = table.Column<int>(type: "integer", nullable: false),
                    ProjectedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminReconciliationStatuses", x => x.BatchId);
                });

            migrationBuilder.CreateTable(
                name: "AdminDisputeMetrics",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DisputeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpenedCount = table.Column<int>(type: "integer", nullable: false),
                    ResolvedCount = table.Column<int>(type: "integer", nullable: false),
                    EscalatedCount = table.Column<int>(type: "integer", nullable: false),
                    TotalDisputedAmountKobo = table.Column<long>(type: "bigint", nullable: false),
                    AverageResolutionHours = table.Column<double>(type: "double precision", nullable: false),
                    ProjectedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminDisputeMetrics", x => new { x.Date, x.DisputeType });
                });

            migrationBuilder.CreateTable(
                name: "AdminChannelHealth",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ChannelName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalDeliveries = table.Column<long>(type: "bigint", nullable: false),
                    SuccessfulDeliveries = table.Column<long>(type: "bigint", nullable: false),
                    FailedDeliveries = table.Column<long>(type: "bigint", nullable: false),
                    AverageLatencyMs = table.Column<double>(type: "double precision", nullable: false),
                    UptimePercentage = table.Column<double>(type: "double precision", nullable: false),
                    ProjectedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminChannelHealth", x => new { x.Date, x.ChannelName });
                });

            // AdminPendingCommands indexes
            migrationBuilder.CreateIndex(
                name: "IX_AdminPendingCommands_Status",
                table: "AdminPendingCommands",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AdminPendingCommands_MakerId",
                table: "AdminPendingCommands",
                column: "MakerId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminPendingCommands_ExpiresAtUtc",
                table: "AdminPendingCommands",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdminPendingCommands_CommandType",
                table: "AdminPendingCommands",
                column: "CommandType");

            // AdminRoles indexes
            migrationBuilder.CreateIndex(
                name: "IX_AdminRoles_UserId",
                table: "AdminRoles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminRoles_UserId_Role",
                table: "AdminRoles",
                columns: new[] { "UserId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminRoles_IsActive",
                table: "AdminRoles",
                column: "IsActive");

            // AdminTransactionSummaries indexes
            migrationBuilder.CreateIndex(
                name: "IX_AdminTransactionSummaries_Date",
                table: "AdminTransactionSummaries",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_AdminTransactionSummaries_Channel",
                table: "AdminTransactionSummaries",
                column: "Channel");

            // AdminReconciliationStatuses indexes
            migrationBuilder.CreateIndex(
                name: "IX_AdminReconciliationStatuses_Processor",
                table: "AdminReconciliationStatuses",
                column: "Processor");

            migrationBuilder.CreateIndex(
                name: "IX_AdminReconciliationStatuses_SettlementDate",
                table: "AdminReconciliationStatuses",
                column: "SettlementDate");

            // AdminDisputeMetrics indexes
            migrationBuilder.CreateIndex(
                name: "IX_AdminDisputeMetrics_Date",
                table: "AdminDisputeMetrics",
                column: "Date");

            // AdminChannelHealth indexes
            migrationBuilder.CreateIndex(
                name: "IX_AdminChannelHealth_Date",
                table: "AdminChannelHealth",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_AdminChannelHealth_ChannelName",
                table: "AdminChannelHealth",
                column: "ChannelName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AdminChannelHealth");
            migrationBuilder.DropTable(name: "AdminDisputeMetrics");
            migrationBuilder.DropTable(name: "AdminReconciliationStatuses");
            migrationBuilder.DropTable(name: "AdminTransactionSummaries");
            migrationBuilder.DropTable(name: "AdminRoles");
            migrationBuilder.DropTable(name: "AdminPendingCommands");
        }
    }
}
