using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReconciliationBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Processor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FileHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    ParsedRows = table.Column<int>(type: "integer", nullable: false),
                    ErrorRows = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SettlementLineItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProcessorReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MatchStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatchedPaymentRequestId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementLineItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SettlementLineItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalAmount = table.Column<long>(type: "bigint", nullable: true),
                    ExternalCurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    InternalAmount = table.Column<long>(type: "bigint", nullable: true),
                    InternalCurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ExternalStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InternalStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResolutionStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationExceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Adjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExceptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RuleName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OperatorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adjustments", x => x.Id);
                });

            // ReconciliationBatches indexes
            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_FileHash",
                table: "ReconciliationBatches",
                column: "FileHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_Status",
                table: "ReconciliationBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_SettlementDate",
                table: "ReconciliationBatches",
                column: "SettlementDate");

            // SettlementLineItems indexes
            migrationBuilder.CreateIndex(
                name: "IX_SettlementLineItems_BatchId",
                table: "SettlementLineItems",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLineItems_TransactionReference",
                table: "SettlementLineItems",
                column: "TransactionReference");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLineItems_MatchStatus",
                table: "SettlementLineItems",
                column: "MatchStatus");

            // ReconciliationExceptions indexes
            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationExceptions_BatchId",
                table: "ReconciliationExceptions",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationExceptions_ResolutionStatus",
                table: "ReconciliationExceptions",
                column: "ResolutionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationExceptions_Type",
                table: "ReconciliationExceptions",
                column: "Type");

            // Adjustments indexes
            migrationBuilder.CreateIndex(
                name: "IX_Adjustments_ExceptionId",
                table: "Adjustments",
                column: "ExceptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Adjustments_Type",
                table: "Adjustments",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Adjustments");
            migrationBuilder.DropTable(name: "ReconciliationExceptions");
            migrationBuilder.DropTable(name: "SettlementLineItems");
            migrationBuilder.DropTable(name: "ReconciliationBatches");
        }
    }
}
