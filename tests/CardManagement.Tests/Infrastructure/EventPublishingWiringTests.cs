using System.Text;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Cards;
using CardManagement.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Integration tests verifying that card issuance, transaction authorization,
/// and transaction reversal trigger the correct IEventPublisher method calls
/// with appropriate event data.
/// </summary>
[Trait("Feature", "docker-kafka-integration")]
public class EventPublishingWiringTests
{
    private const string ValidPan = "4111111111111111";
    private const string ValidExpiryYymm = "2912"; // Dec 2029
    private const string ValidExpiryMmyy = "12/29"; // Dec 2029 stored on card

    #region RecordingEventPublisher

    /// <summary>
    /// A recording implementation of IEventPublisher that captures all published events
    /// for test assertion purposes.
    /// </summary>
    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<CardIssuedEvent> CardIssuedEvents { get; } = new();
        public List<TransactionAuthorizedEvent> TransactionAuthorizedEvents { get; } = new();
        public List<TransactionReversedEvent> TransactionReversedEvents { get; } = new();

        public Task PublishCardIssuedAsync(CardIssuedEvent cardEvent, CancellationToken ct)
        {
            CardIssuedEvents.Add(cardEvent);
            return Task.CompletedTask;
        }

        public Task PublishTransactionAuthorizedAsync(TransactionAuthorizedEvent txEvent, CancellationToken ct)
        {
            TransactionAuthorizedEvents.Add(txEvent);
            return Task.CompletedTask;
        }

        public Task PublishTransactionReversedAsync(TransactionReversedEvent txEvent, CancellationToken ct)
        {
            TransactionReversedEvents.Add(txEvent);
            return Task.CompletedTask;
        }
    }

    #endregion

    #region Test: Card Issuance publishes CardIssuedEvent

    [Fact]
    [Trait("Feature", "docker-kafka-integration")]
    public async Task CardIssuance_PublishesCardIssuedEvent()
    {
        // Arrange
        var recorder = new RecordingEventPublisher();
        var hsm = new FakeHsmService();
        var cardRepo = new FakeCardRepository();
        var logger = new NullLoggerFactory().CreateLogger<VirtualCardService>();
        var service = new VirtualCardService(hsm, cardRepo, recorder, logger);

        var accountId = Guid.NewGuid();
        var request = new CardIssuanceRequest
        {
            CardScheme = CardScheme.Visa,
            BinRange = new BinRange("411111", CardScheme.Visa, 16),
            AccountId = accountId,
            ValidityMonths = 12
        };

        // Act
        var result = await service.IssueCardAsync(request, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(recorder.CardIssuedEvents);

        var cardEvent = recorder.CardIssuedEvents[0];
        Assert.Equal(result.Value!.Id, cardEvent.CardId);
        Assert.Equal("Visa", cardEvent.CardScheme);
        Assert.Equal(accountId, cardEvent.AccountId);
        Assert.NotEqual(default, cardEvent.IssuanceTimestamp);
        Assert.NotEmpty(cardEvent.CorrelationId);
    }

    #endregion

    #region Test: Transaction Authorization publishes TransactionAuthorizedEvent

    [Fact]
    [Trait("Feature", "docker-kafka-integration")]
    public async Task TransactionAuthorization_PublishesTransactionAuthorizedEvent()
    {
        // Arrange
        var recorder = new RecordingEventPublisher();
        var card = CreateActiveCard();
        var cardRepo = new FakeCardRepository();
        cardRepo.AddCard(ComputePanHash(ValidPan), card);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(CreateAccount(card.AccountId, 10000));
        var transactionRepo = new FakeTransactionRepository();

        var pipeline = new TransactionPipeline(
            new FakeIso8583Gateway(),
            cardRepo,
            accountRepo,
            new FakeLedgerService(),
            transactionRepo,
            recorder,
            new NullLoggerFactory().CreateLogger<TransactionPipeline>());

        var request = CreateValidAuthRequest(amount: "5000", currency: "566");

        // Act
        var response = await pipeline.ProcessAuthorizationAsync(request, CancellationToken.None);

        // Assert — transaction should be approved
        Assert.Equal("00", response.Fields[39]);

        // Assert — event was published
        Assert.Single(recorder.TransactionAuthorizedEvents);

        var txEvent = recorder.TransactionAuthorizedEvents[0];
        Assert.Equal(card.Id, txEvent.CardId);
        Assert.Equal(5000, txEvent.Amount);
        Assert.Equal("566", txEvent.Currency);
        Assert.Equal("00", txEvent.ResponseCode);
        Assert.NotEqual(Guid.Empty, txEvent.TransactionId);
        Assert.NotEqual(default, txEvent.AuthorizationTimestamp);
        Assert.NotEmpty(txEvent.CorrelationId);
    }

    #endregion

    #region Test: Transaction Reversal publishes TransactionReversedEvent

    [Fact]
    [Trait("Feature", "docker-kafka-integration")]
    public async Task TransactionReversal_PublishesTransactionReversedEvent()
    {
        // Arrange
        var recorder = new RecordingEventPublisher();
        var ledgerService = new FakeLedgerService();

        var pipeline = new TransactionPipeline(
            new FakeIso8583Gateway(),
            new FakeCardRepository(),
            new FakeAccountRepository(),
            ledgerService,
            new FakeTransactionRepository(),
            recorder,
            new NullLoggerFactory().CreateLogger<TransactionPipeline>());

        var request = new Iso8583Message
        {
            Mti = "0420",
            Fields = new Dictionary<int, string>
            {
                [2] = ValidPan,
                [4] = "5000",
                [11] = "123456",
                [49] = "566"
            }
        };

        // Act
        var response = await pipeline.ProcessReversalAsync(request, CancellationToken.None);

        // Assert — reversal should be approved
        Assert.Equal("0430", response.Mti);
        Assert.Equal("00", response.Fields[39]);

        // Assert — event was published
        Assert.Single(recorder.TransactionReversedEvents);

        var txEvent = recorder.TransactionReversedEvents[0];
        Assert.Equal(5000, txEvent.Amount);
        Assert.Equal("566", txEvent.Currency);
        Assert.NotEqual(Guid.Empty, txEvent.OriginalTransactionId);
        Assert.NotEqual(Guid.Empty, txEvent.ReversalTransactionId);
        Assert.NotEqual(default, txEvent.ReversalTimestamp);
        Assert.NotEmpty(txEvent.CorrelationId);
    }

    #endregion

    #region Helpers

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
            Encoding.UTF8.GetBytes(pan));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

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

    private static Account CreateAccount(Guid accountId, long balance)
    {
        var account = Account.Create("ACC-001", AccountType.Asset, "NGN", balance);
        var idProp = typeof(Account).GetProperty(nameof(Account.Id));
        idProp!.SetValue(account, accountId);
        return account;
    }

    #endregion

    #region Fake Implementations

    private sealed class FakeHsmService : IHsmService
    {
        private int _panGenerationCallCount;

        public Task<Result<string>> GeneratePanAsync(BinRange binRange, CancellationToken ct)
        {
            _panGenerationCallCount++;
            int randomDigitsNeeded = binRange.PanLength - binRange.Prefix.Length - 1;
            var random = new Random(42 + _panGenerationCallCount);
            var digits = new char[randomDigitsNeeded];
            for (int i = 0; i < randomDigitsNeeded; i++)
            {
                digits[i] = (char)('0' + random.Next(10));
            }

            string partialPan = binRange.Prefix + new string(digits);
            return Task.FromResult(Result<string>.Success(partialPan));
        }

        public Task<Result<string>> ComputeCvv2Async(string pan, string expiryDate, CancellationToken ct)
        {
            return Task.FromResult(Result<string>.Success("123"));
        }

        public Task<Result<byte[]>> EncryptAsync(byte[] plaintext, string keyHandle, CancellationToken ct)
        {
            byte[] ciphertext = new byte[plaintext.Length + 1];
            ciphertext[0] = 0xAA;
            for (int i = 0; i < plaintext.Length; i++)
            {
                ciphertext[i + 1] = (byte)(plaintext[i] ^ 0x55);
            }

            return Task.FromResult(Result<byte[]>.Success(ciphertext));
        }

        public Task<Result<byte[]>> DecryptAsync(byte[] ciphertext, string keyHandle, CancellationToken ct)
        {
            byte[] plaintext = new byte[ciphertext.Length - 1];
            for (int i = 0; i < plaintext.Length; i++)
            {
                plaintext[i] = (byte)(ciphertext[i + 1] ^ 0x55);
            }

            return Task.FromResult(Result<byte[]>.Success(plaintext));
        }
    }

    private sealed class FakeCardRepository : ICardRepository
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

    private sealed class FakeAccountRepository : IAccountRepository
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

    private sealed class FakeLedgerService : ILedgerService
    {
        public Task<Result<TransactionRecordDto>> PostTransactionAsync(LedgerTransaction transaction, CancellationToken ct)
        {
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

    private sealed class FakeIso8583Gateway : IIso8583Gateway
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

    private sealed class FakeTransactionRepository : ITransactionRepository
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
