using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    Attempts = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDlqItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalPayload = table.Column<string>(type: "text", nullable: false),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    MovedToDlqAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    Replayed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDlqItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EventTypes = table.Column<string[]>(type: "jsonb", nullable: false),
                    SigningSecret = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    LastDeliveryAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Adjustments_ExceptionId",
                table: "Adjustments",
                column: "ExceptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Adjustments_Type",
                table: "Adjustments",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_FileHash",
                table: "ReconciliationBatches",
                column: "FileHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_SettlementDate",
                table: "ReconciliationBatches",
                column: "SettlementDate");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_Status",
                table: "ReconciliationBatches",
                column: "Status");

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

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLineItems_BatchId",
                table: "SettlementLineItems",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLineItems_MatchStatus",
                table: "SettlementLineItems",
                column: "MatchStatus");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLineItems_TransactionReference",
                table: "SettlementLineItems",
                column: "TransactionReference");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_Status",
                table: "WebhookDeliveries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_SubscriptionId",
                table: "WebhookDeliveries",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDlqItems_DeliveryId",
                table: "WebhookDlqItems",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDlqItems_SubscriptionId",
                table: "WebhookDlqItems",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_MerchantId",
                table: "WebhookSubscriptions",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_Status",
                table: "WebhookSubscriptions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Adjustments");

            migrationBuilder.DropTable(
                name: "ReconciliationBatches");

            migrationBuilder.DropTable(
                name: "ReconciliationExceptions");

            migrationBuilder.DropTable(
                name: "SettlementLineItems");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "WebhookDlqItems");

            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");
        }
    }
}
