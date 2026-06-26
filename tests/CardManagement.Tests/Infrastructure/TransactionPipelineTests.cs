using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Kafka;
using CardManagement.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for TransactionPipeline covering the authorization pipeline:
/// message parsing, card validation, balance check, ledger posting, response construction,
/// and response transmission steps with appropriate failure handling and rollback.
/// </summary>
public class TransactionPipelineTests
{
    private const string ValidPan = "4111111111111111";
    private const string ValidExpiryYymm = "2912"; // Dec 2029
    private const string ValidExpiryMmyy = "12/29"; // Dec 2029 stored on card

    private static TransactionPipeline CreatePipeline(
        FakeIso8583Gateway? gateway = null,
        FakeCardRepository? cardRepo = null,
        FakeAccountRepository? accountRepo = null,
        FakeLedgerService? ledgerService = null,
        FakeTransactionRepository? transactionRepo = null)
    {
        return new TransactionPipeline(
            gateway ?? new FakeIso8583Gateway(),
            cardRepo ?? new FakeCardRepository(),
            accountRepo ?? new FakeAccountRepository(),
            ledgerService ?? new FakeLedgerService(),
            transactionRepo ?? new FakeTransactionRepository(),
            new NullEventPublisher(new NullLoggerFactory().CreateLogger<NullEventPublisher>()),
            new NullLoggerFactory().CreateLogger<TransactionPipeline>());
    }

    private static Iso8583Message CreateValidAuthRequest(
        string pan = ValidPan,
        string amount = "5000",
        string expiryDate = ValidExpiryYymm,
        string currency = "566")
    {
        return new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = pan,
                [3] = "000000",
                [4] = amount,
                [11] = "123456",
                [14] = expiryDate,
                [22] = "051",
                [25] = "00",
                [41] = "TERM0001",
                [49] = currency
            }
        };
    }

    private static string ComputePanHash(string pan)
    {
        byte[] hashBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(pan));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    #region Step 1: Message Parsing Tests

    [Fact]
    public async Task ProcessAuthorizationAsync_NullRequest_ReturnsDeclineWith96()
    {
        var pipeline = CreatePipeline();

        // Using a request with wrong MTI to trigger parsing failure
        var request = new Iso8583Message
        {
            Mti = "0420", // wrong MTI
            Fields = new Dictionary<int, string>()
        };

        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Equal("0110", response.Mti);
        Assert.Equal("96", response.Fields[39]);
    }

    [Fact]
    public async Task ProcessAuthorizationAsync_MissingMandatoryFields_ReturnsDeclineWith96()
    {
        var pipeline = CreatePipeline();

        var request = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = ValidPan
                // Missing amount, expiry, currency
            }
        };

        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Equal("0110", response.Mti);
        Assert.Equal("96", response.Fields[39]);
    }

    #endregion

    #region Step 2: Card Validation Tests

    [Fact]
    public async Task ProcessAuthorizationAsync_CardNotFound_ReturnsDeclineWith14()
    {
        var cardRepo = new FakeCardRepository(); // empty — no card found
        var pipeline = CreatePipeline(cardRepo: cardRepo);

        var request = CreateValidAuthRequest();

        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Equal("0110", response.Mti);
        Assert.Equal("14", response.Fields[39]);
    }

    [Fact]
    public async Task ProcessAuthorizationAsync_CardBlocked_ReturnsDeclineWith14()
    {
        var card = CreateActiveCard();
        card.Block();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 10000));
        var pipeline = CreatePipeline(cardRepo: cardRepo, accountRepo: accountRepo);

        var request = CreateValidAuthRequest();

        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Equal("0110", response.Mti);
        Assert.Equal("14", response.Fields[39]);
    }

    [Fact]
    public async Task ProcessAuthorizationAsync_CardExpired_ReturnsDeclineWith54()
    {
        // Create card with past expiry date
        var card = CreateCardWithExpiry("01/20"); // January 2020 — expired
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 10000));
        var pipeline = CreatePipeline(cardRepo: cardRepo, accountRepo: accountRepo);

        var request = CreateValidAuthRequest(expiryDate: "2001"); // YYMM for 01/20
        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Equal("0110", response.Mti);
        Assert.Equal("54", response.Fields[39]);
    }

    #endregion

    #region Step 3: Balance Check Tests

    [Fact]
    public async Task ProcessAuthorizationAsync_InsufficientFunds_ReturnsDeclineWith51()
    {
        var card = CreateActiveCard();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 100)); // Balance = 100, requesting 5000
        var pipeline = CreatePipeline(cardRepo: cardRepo, accountRepo: accountRepo);

        var request = CreateValidAuthRequest(amount: "5000");
        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Equal("0110", response.Mti);
        Assert.Equal("51", response.Fields[39]);
    }

    [Fact]
    public async Task ProcessAuthorizationAsync_AccountNotFound_ReturnsDeclineWith96()
    {
        var card = CreateActiveCard();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository(); // empty — no account found
        var pipeline = CreatePipeline(cardRepo: cardRepo, accountRepo: accountRepo);

        var request = CreateValidAuthRequest();
        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Equal("0110", response.Mti);
        Assert.Equal("96", response.Fields[39]);
    }

    #endregion

    #region Step 4: Ledger Posting Tests

    [Fact]
    public async Task ProcessAuthorizationAsync_LedgerPostingFails_ReturnsDeclineWith96()
    {
        var card = CreateActiveCard();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 10000));
        var ledgerService = new FakeLedgerService { ShouldFail = true, FailureErrorCode = "96" };
        var pipeline = CreatePipeline(cardRepo: cardRepo, accountRepo: accountRepo, ledgerService: ledgerService);

        var request = CreateValidAuthRequest();
        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Equal("0110", response.Mti);
        Assert.Equal("96", response.Fields[39]);
    }

    [Fact]
    public async Task ProcessAuthorizationAsync_LedgerPostingInsufficientFunds_ReturnsDeclineWith51()
    {
        var card = CreateActiveCard();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 10000));
        var ledgerService = new FakeLedgerService { ShouldFail = true, FailureErrorCode = "51" };
        var pipeline = CreatePipeline(cardRepo: cardRepo, accountRepo: accountRepo, ledgerService: ledgerService);

        var request = CreateValidAuthRequest();
        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Equal("0110", response.Mti);
        Assert.Equal("51", response.Fields[39]);
    }

    #endregion

    #region Step 5-6: Successful Authorization

    [Fact]
    public async Task ProcessAuthorizationAsync_AllStepsPass_ReturnsApprovalWith00()
    {
        var card = CreateActiveCard();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 10000));
        var transactionRepo = new FakeTransactionRepository();
        var pipeline = CreatePipeline(
            cardRepo: cardRepo,
            accountRepo: accountRepo,
            transactionRepo: transactionRepo);

        var request = CreateValidAuthRequest(amount: "5000");
        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Equal("0110", response.Mti);
        Assert.Equal("00", response.Fields[39]);
    }

    [Fact]
    public async Task ProcessAuthorizationAsync_Success_PersistsTransactionRecord()
    {
        var card = CreateActiveCard();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 10000));
        var transactionRepo = new FakeTransactionRepository();
        var pipeline = CreatePipeline(
            cardRepo: cardRepo,
            accountRepo: accountRepo,
            transactionRepo: transactionRepo);

        var request = CreateValidAuthRequest(amount: "2000");
        await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Single(transactionRepo.AddedTransactions);
        var txn = transactionRepo.AddedTransactions[0];
        Assert.Equal("0100", txn.MessageType);
        Assert.Equal("00", txn.ResponseCode);
        Assert.Equal(TransactionStatus.Approved, txn.Status);
        Assert.Equal(2000, txn.Amount);
    }

    [Fact]
    public async Task ProcessAuthorizationAsync_Success_AssignsUniqueStan()
    {
        var card = CreateActiveCard();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 10000));
        var transactionRepo = new FakeTransactionRepository();
        var pipeline = CreatePipeline(
            cardRepo: cardRepo,
            accountRepo: accountRepo,
            transactionRepo: transactionRepo);

        var request = CreateValidAuthRequest(amount: "1000");
        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        // STAN should be in the response (field 11)
        Assert.True(response.Fields.ContainsKey(11));
        var stan = response.Fields[11];
        Assert.Equal(6, stan.Length);
        Assert.True(stan.All(char.IsDigit));
    }

    [Fact]
    public async Task ProcessAuthorizationAsync_MultipleTransactions_HaveDistinctStans()
    {
        var card = CreateActiveCard();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 100000));
        var transactionRepo = new FakeTransactionRepository();
        var pipeline = CreatePipeline(
            cardRepo: cardRepo,
            accountRepo: accountRepo,
            transactionRepo: transactionRepo);

        var stans = new HashSet<string>();
        for (int i = 0; i < 10; i++)
        {
            var request = CreateValidAuthRequest(amount: "100");
            var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);
            stans.Add(response.Fields[11]);
        }

        Assert.Equal(10, stans.Count); // All STANs should be distinct
    }

    #endregion

    #region Step Failure Logging and Decline Transaction Persistence

    [Fact]
    public async Task ProcessAuthorizationAsync_CardNotFound_PersistsDeclinedTransaction()
    {
        var cardRepo = new FakeCardRepository(); // empty
        var transactionRepo = new FakeTransactionRepository();
        var pipeline = CreatePipeline(cardRepo: cardRepo, transactionRepo: transactionRepo);

        var request = CreateValidAuthRequest();
        await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Single(transactionRepo.AddedTransactions);
        var txn = transactionRepo.AddedTransactions[0];
        Assert.Equal(TransactionStatus.Declined, txn.Status);
        Assert.Equal("14", txn.ResponseCode);
        Assert.Equal("CardValidation", txn.PipelineStepReached);
    }

    [Fact]
    public async Task ProcessAuthorizationAsync_InsufficientFunds_PersistsDeclinedTransaction()
    {
        var card = CreateActiveCard();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 100));
        var transactionRepo = new FakeTransactionRepository();
        var pipeline = CreatePipeline(
            cardRepo: cardRepo,
            accountRepo: accountRepo,
            transactionRepo: transactionRepo);

        var request = CreateValidAuthRequest(amount: "5000");
        await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Single(transactionRepo.AddedTransactions);
        var txn = transactionRepo.AddedTransactions[0];
        Assert.Equal(TransactionStatus.Declined, txn.Status);
        Assert.Equal("51", txn.ResponseCode);
        Assert.Equal("BalanceCheck", txn.PipelineStepReached);
    }

    #endregion

    #region Reversal Pipeline Tests

    [Fact]
    public async Task ProcessReversalAsync_SuccessfulReversal_ReturnsApprovalWith00()
    {
        var ledgerService = new FakeLedgerService();
        var pipeline = CreatePipeline(ledgerService: ledgerService);

        var request = new Iso8583Message
        {
            Mti = "0420",
            Fields = new Dictionary<int, string>
            {
                [2] = ValidPan,
                [4] = "5000",
                [11] = "123456"
            }
        };

        var response = await pipeline.ProcessReversalAsync(request, CancellationToken.None);

        Assert.Equal("0430", response.Mti);
        Assert.Equal("00", response.Fields[39]);
    }

    [Fact]
    public async Task ProcessReversalAsync_OriginalNotFound_ReturnsDecline()
    {
        var ledgerService = new FakeLedgerService { ReversalShouldFail = true };
        var pipeline = CreatePipeline(ledgerService: ledgerService);

        var request = new Iso8583Message
        {
            Mti = "0420",
            Fields = new Dictionary<int, string>
            {
                [2] = ValidPan,
                [4] = "5000",
                [11] = "999999"
            }
        };

        var response = await pipeline.ProcessReversalAsync(request, CancellationToken.None);

        Assert.Equal("0430", response.Mti);
        Assert.Equal("96", response.Fields[39]);
    }

    #endregion

    #region STAN Generation Tests

    [Fact]
    public void GenerateStan_Returns6DigitString()
    {
        var stan = TransactionPipeline.GenerateStan();

        Assert.Equal(6, stan.Length);
        Assert.True(stan.All(char.IsDigit));
    }

    [Fact]
    public void GenerateStan_ConsecutiveCalls_ReturnDifferentValues()
    {
        var stan1 = TransactionPipeline.GenerateStan();
        var stan2 = TransactionPipeline.GenerateStan();

        Assert.NotEqual(stan1, stan2);
    }

    #endregion

    #region Response Echoes Fields from Request

    [Fact]
    public async Task ProcessAuthorizationAsync_Success_EchoesRequestFieldsInResponse()
    {
        var card = CreateActiveCard();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 10000));
        var pipeline = CreatePipeline(cardRepo: cardRepo, accountRepo: accountRepo);

        var request = CreateValidAuthRequest(amount: "3000", currency: "566");
        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        Assert.Equal(ValidPan, response.Fields[2]);
        Assert.Equal("3000", response.Fields[4]);
        Assert.Equal("566", response.Fields[49]);
    }

    #endregion

    #region Helpers

    private static Card CreateActiveCard()
    {
        return Card.Create(
            panEncrypted: "encrypted_pan",
            panHash: ComputePanHash(ValidPan),
            cvv2Encrypted: "encrypted_cvv2",
            expiryDate: ValidExpiryMmyy,
            accountId: Guid.NewGuid(),
            cardScheme: CardScheme.Visa,
            binRange: "411111");
    }

    private static Card CreateCardWithExpiry(string expiryMmyy)
    {
        return Card.Create(
            panEncrypted: "encrypted_pan",
            panHash: ComputePanHash(ValidPan),
            cvv2Encrypted: "encrypted_cvv2",
            expiryDate: expiryMmyy,
            accountId: Guid.NewGuid(),
            cardScheme: CardScheme.Visa,
            binRange: "411111");
    }

    private static Account CreateAccount(Guid accountId, long balance)
    {
        // Use reflection to set the Id since Account.Create generates a new one
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN", balance);
        var idProp = typeof(Account).GetProperty(nameof(Account.Id));
        idProp!.SetValue(account, accountId);
        return account;
    }

    #endregion

    #region Fake Implementations

    private class FakeIso8583Gateway : IIso8583Gateway
    {
        public Result<Iso8583Message> Parse(ReadOnlySpan<byte> rawMessage)
        {
            return Result<Iso8583Message>.Success(new Iso8583Message
            {
                Mti = "0100",
                Fields = new Dictionary<int, string>()
            });
        }

        public Result<byte[]> Construct(Iso8583Message message)
        {
            return Result<byte[]>.Success(Array.Empty<byte>());
        }

        public bool IsValidFrameSize(int messageLength)
        {
            return messageLength <= 9999;
        }
    }

    private class FakeCardRepository : ICardRepository
    {
        private readonly Dictionary<string, Card> _cards = new();

        public void AddCard(string panHash, Card card)
        {
            _cards[panHash] = card;
        }

        public Task<Card?> GetByPanHashAsync(string panHash, CancellationToken cancellationToken = default)
        {
            _cards.TryGetValue(panHash, out var card);
            return Task.FromResult(card);
        }

        public Task<bool> ExistsByPanHashAsync(string panHash, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_cards.ContainsKey(panHash));
        }

        public Task AddAsync(Card card, CancellationToken cancellationToken = default)
        {
            _cards[card.PanHash] = card;
            return Task.CompletedTask;
        }
    }

    private class FakeAccountRepository : IAccountRepository
    {
        private readonly Dictionary<Guid, Account> _accounts = new();

        public void AddAccount(Account account)
        {
            _accounts[account.Id] = account;
        }

        public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _accounts.TryGetValue(id, out var account);
            return Task.FromResult(account);
        }

        public Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _accounts.TryGetValue(id, out var account);
            return Task.FromResult(account);
        }

        public Task UpdateBalanceAsync(Account account, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class FakeLedgerService : ILedgerService
    {
        public bool ShouldFail { get; set; }
        public string FailureErrorCode { get; set; } = "96";
        public bool ReversalShouldFail { get; set; }
        public List<LedgerTransaction> PostedTransactions { get; } = new();

        public Task<Result<TransactionRecordDto>> PostTransactionAsync(LedgerTransaction transaction, CancellationToken ct)
        {
            if (ShouldFail)
            {
                return Task.FromResult(
                    Result<TransactionRecordDto>.Failure("Ledger posting failed", FailureErrorCode));
            }

            PostedTransactions.Add(transaction);
            var dto = new TransactionRecordDto
            {
                Id = Guid.NewGuid(),
                SystemTraceAuditNumber = transaction.OperationIdentifier,
                MessageType = "0100",
                ResponseCode = "00",
                Amount = transaction.Entries.Where(e => e.EntryType == EntryType.Debit).Sum(e => e.Amount),
                Currency = "NGN",
                ProcessorType = ProcessorType.Interswitch,
                Status = "Approved"
            };

            return Task.FromResult(Result<TransactionRecordDto>.Success(dto));
        }

        public Task<Result<TransactionRecordDto>> ReverseTransactionAsync(string systemTraceAuditNumber, CancellationToken ct)
        {
            if (ReversalShouldFail)
            {
                return Task.FromResult(
                    Result<TransactionRecordDto>.Failure("Original transaction not found", "30"));
            }

            var dto = new TransactionRecordDto
            {
                Id = Guid.NewGuid(),
                SystemTraceAuditNumber = systemTraceAuditNumber,
                MessageType = "0420",
                ResponseCode = "00",
                Amount = 0,
                Currency = "NGN",
                ProcessorType = ProcessorType.Interswitch,
                Status = "Reversed"
            };

            return Task.FromResult(Result<TransactionRecordDto>.Success(dto));
        }

        public Task<Result<AccountBalance>> GetBalanceAsync(Guid accountId, CancellationToken ct)
        {
            return Task.FromResult(Result<AccountBalance>.Success(new AccountBalance
            {
                AccountId = accountId,
                Balance = 10000,
                Currency = "NGN"
            }));
        }
    }

    private class FakeTransactionRepository : ITransactionRepository
    {
        public List<TransactionRecord> AddedTransactions { get; } = new();

        public Task AddAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
        {
            AddedTransactions.Add(transaction);
            return Task.CompletedTask;
        }

        public Task<TransactionRecord?> GetByStanAsync(string systemTraceAuditNumber, CancellationToken cancellationToken = default)
        {
            var record = AddedTransactions.FirstOrDefault(t => t.SystemTraceAuditNumber == systemTraceAuditNumber);
            return Task.FromResult(record);
        }

        public Task UpdateAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    #endregion
}
