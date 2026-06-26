using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace CardManagement.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Accounts table
        migrationBuilder.CreateTable(
            name: "Accounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AccountNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                AccountType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Balance = table.Column<long>(type: "bigint", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Accounts", x => x.Id);
            });

        // ProcessorSessions table
        migrationBuilder.CreateTable(
            name: "ProcessorSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProcessorType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Endpoint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                IsSignedOn = table.Column<bool>(type: "boolean", nullable: false),
                LastSignOnUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                LastHeartbeatUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProcessorSessions", x => x.Id);
            });

        // Cards table (depends on Accounts)
        migrationBuilder.CreateTable(
            name: "Cards",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PanEncrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                PanHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Cvv2Encrypted = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ExpiryDate = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                CardScheme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                BinRange = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Cards", x => x.Id);
                table.ForeignKey(
                    name: "FK_Cards_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // TransactionRecords table (depends on Cards)
        migrationBuilder.CreateTable(
            name: "TransactionRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SystemTraceAuditNumber = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                MessageType = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                ResponseCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                CardId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ProcessorType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                PipelineStepReached = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransactionRecords", x => x.Id);
                table.ForeignKey(
                    name: "FK_TransactionRecords_Cards_CardId",
                    column: x => x.CardId,
                    principalTable: "Cards",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // LedgerEntries table (depends on Accounts)
        migrationBuilder.CreateTable(
            name: "LedgerEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                EntryType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Amount = table.Column<long>(type: "bigint", nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                OperationIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_LedgerEntries_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // Indexes
        migrationBuilder.CreateIndex(
            name: "IX_Accounts_AccountNumber",
            table: "Accounts",
            column: "AccountNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Cards_AccountId",
            table: "Cards",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_Cards_PanHash",
            table: "Cards",
            column: "PanHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_LedgerEntries_AccountId_CreatedAtUtc",
            table: "LedgerEntries",
            columns: new[] { "AccountId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_LedgerEntries_TransactionId",
            table: "LedgerEntries",
            column: "TransactionId");

        migrationBuilder.CreateIndex(
            name: "IX_TransactionRecords_CardId",
            table: "TransactionRecords",
            column: "CardId");

        migrationBuilder.CreateIndex(
            name: "IX_TransactionRecords_SystemTraceAuditNumber",
            table: "TransactionRecords",
            column: "SystemTraceAuditNumber",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LedgerEntries");
        migrationBuilder.DropTable(name: "TransactionRecords");
        migrationBuilder.DropTable(name: "Cards");
        migrationBuilder.DropTable(name: "ProcessorSessions");
        migrationBuilder.DropTable(name: "Accounts");
    }
}
