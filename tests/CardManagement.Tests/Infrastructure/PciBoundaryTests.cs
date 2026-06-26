using System.Security.Authentication;
using System.Security.Cryptography;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class PciBoundaryTests : IDisposable
{
    private readonly PciBoundary _boundary;
    private readonly TokenVault _tokenVault;
    private readonly ServiceAllowlist _allowlist;
    private readonly InMemoryAuditStore _auditStore;
    private readonly byte[] _encryptionKey;

    public PciBoundaryTests()
    {
        _encryptionKey = new byte[32];
        RandomNumberGenerator.Fill(_encryptionKey);

        var options = Options.Create(new PciSecurityOptions
        {
            EncryptionKey = Convert.ToBase64String(_encryptionKey),
            AllowedServices = new List<string> { "InterswitchAdapter", "CardifyAdapter", "InternalProcessor" }
        });

        _tokenVault = new TokenVault();
        _allowlist = new ServiceAllowlist(options);
        _auditStore = new InMemoryAuditStore();

        _boundary = new PciBoundary(_tokenVault, _allowlist, _auditStore, options);
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task TokenizeAsync_ProducesConsistentTokens_ForSamePan()
    {
        // Arrange
        var pan = "4111111111111111";
        var correlationId = Guid.NewGuid().ToString();

        // Act
        var token1 = await _boundary.TokenizeAsync(pan, null, correlationId, CancellationToken.None);
        var token2 = await _boundary.TokenizeAsync(pan, null, correlationId, CancellationToken.None);

        // Assert - same PAN produces same token (deterministic)
        Assert.Equal(token1, token2);
        Assert.True(Guid.TryParse(token1, out _), "Token should be a valid UUID");
    }

    [Fact]
    public async Task TokenizeAsync_ProducesDifferentTokens_ForDifferentPans()
    {
        // Arrange
        var pan1 = "4111111111111111";
        var pan2 = "5500000000000004";
        var correlationId = Guid.NewGuid().ToString();

        // Act
        var token1 = await _boundary.TokenizeAsync(pan1, null, correlationId, CancellationToken.None);
        var token2 = await _boundary.TokenizeAsync(pan2, null, correlationId, CancellationToken.None);

        // Assert
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public async Task DetokenizeAsync_AllowedService_ReturnsOriginalData()
    {
        // Arrange
        var pan = "4111111111111111";
        var cvv = "123";
        var correlationId = Guid.NewGuid().ToString();
        var token = await _boundary.TokenizeAsync(pan, cvv, correlationId, CancellationToken.None);

        // Act
        var result = await _boundary.DetokenizeAsync(token, "InterswitchAdapter", correlationId, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(pan, result.Pan);
        Assert.Equal(cvv, result.Cvv);
    }

    [Fact]
    public async Task DetokenizeAsync_UnauthorizedService_ReturnsDenied()
    {
        // Arrange
        var pan = "4111111111111111";
        var correlationId = Guid.NewGuid().ToString();
        var token = await _boundary.TokenizeAsync(pan, null, correlationId, CancellationToken.None);

        // Act
        var result = await _boundary.DetokenizeAsync(token, "UnauthorizedService", correlationId, CancellationToken.None);

        // Assert
        Assert.True(result.IsUnauthorized);
        Assert.False(result.IsSuccess);
        Assert.Equal("UnauthorizedService", result.DeniedService);
    }

    [Fact]
    public async Task DetokenizeAsync_UnauthorizedAccess_IsLogged()
    {
        // Arrange
        var pan = "4111111111111111";
        var correlationId = Guid.NewGuid().ToString();
        var token = await _boundary.TokenizeAsync(pan, null, correlationId, CancellationToken.None);

        // Act
        await _boundary.DetokenizeAsync(token, "EvilService", correlationId, CancellationToken.None);

        // Assert - access attempt is logged
        var auditEntries = _auditStore.GetEntries();
        var deniedEntry = auditEntries.FirstOrDefault(e =>
            e.ActorIdentity == "EvilService" &&
            e.Action == "Detokenize-Denied" &&
            e.NewState == "AccessDenied");

        Assert.NotNull(deniedEntry);
        // Ensure no cardholder data in the log
        Assert.DoesNotContain(pan, deniedEntry.TransactionReference);
        Assert.DoesNotContain(pan, deniedEntry.Action);
    }

    [Fact]
    public void Encrypt_UsesAes256()
    {
        // Act
        var encrypted = _boundary.Encrypt("4111111111111111");

        // Assert
        // AES-256-CBC: 16 byte IV + at least 16 byte ciphertext (one full block for a 16-char PAN)
        Assert.True(encrypted.Length >= 32, "Encrypted data should include IV (16) + at least one AES block (16)");

        // Verify we can decrypt
        var decrypted = _boundary.Decrypt(encrypted);
        Assert.Equal("4111111111111111", decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertext_EachTime()
    {
        // Act (different random IVs each time)
        var encrypted1 = _boundary.Encrypt("4111111111111111");
        var encrypted2 = _boundary.Encrypt("4111111111111111");

        // Assert - different IVs produce different ciphertext
        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void Decrypt_ReversesEncryption()
    {
        // Arrange
        var original = "5500000000000004";

        // Act
        var encrypted = _boundary.Encrypt(original);
        var decrypted = _boundary.Decrypt(encrypted);

        // Assert
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void MaskPan_ShowsOnlyLast4Digits()
    {
        // Act & Assert
        Assert.Equal("************1111", PciBoundary.MaskPan("4111111111111111"));
        Assert.Equal("************0004", PciBoundary.MaskPan("5500000000000004"));
        Assert.Equal("***1234", PciBoundary.MaskPan("0001234"));
    }

    [Fact]
    public void MaskPan_ShortPan_ReturnsFull()
    {
        // Very short PANs (4 or fewer digits) return as-is
        Assert.Equal("1234", PciBoundary.MaskPan("1234"));
        Assert.Equal("123", PciBoundary.MaskPan("123"));
        Assert.Equal(string.Empty, PciBoundary.MaskPan(""));
    }

    [Fact]
    public async Task GetMaskedPanAsync_ReturnsMaskedRepresentation()
    {
        // Arrange
        var pan = "4111111111111111";
        var correlationId = Guid.NewGuid().ToString();
        var token = await _boundary.TokenizeAsync(pan, null, correlationId, CancellationToken.None);

        // Act - no allowlist check needed for masked data
        var masked = await _boundary.GetMaskedPanAsync(token, "AnyService", correlationId, CancellationToken.None);

        // Assert
        Assert.Equal("************1111", masked);
    }

    [Fact]
    public void ServiceAllowlist_AuthorizedService_ReturnsTrue()
    {
        Assert.True(_allowlist.IsAuthorized("InterswitchAdapter"));
        Assert.True(_allowlist.IsAuthorized("CardifyAdapter"));
        Assert.True(_allowlist.IsAuthorized("InternalProcessor"));
    }

    [Fact]
    public void ServiceAllowlist_UnauthorizedService_ReturnsFalse()
    {
        Assert.False(_allowlist.IsAuthorized("UnknownService"));
        Assert.False(_allowlist.IsAuthorized(""));
        Assert.False(_allowlist.IsAuthorized("  "));
    }

    [Fact]
    public void ServiceAllowlist_IsCaseInsensitive()
    {
        Assert.True(_allowlist.IsAuthorized("interswitchadapter"));
        Assert.True(_allowlist.IsAuthorized("CARDIFYADAPTER"));
    }

    [Fact]
    public void TlsEnforcement_CreateSecureHandler_EnforcesTls12Plus()
    {
        // Act
        var handler = TlsEnforcementHandler.CreateSecureHandler();

        // Assert
        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, handler.SslProtocols);
    }

    [Fact]
    public void TlsEnforcement_IsProtocolCompliant_AcceptsTls12And13()
    {
        Assert.True(TlsEnforcementHandler.IsProtocolCompliant(SslProtocols.Tls12));
        Assert.True(TlsEnforcementHandler.IsProtocolCompliant(SslProtocols.Tls13));
        Assert.True(TlsEnforcementHandler.IsProtocolCompliant(SslProtocols.Tls12 | SslProtocols.Tls13));
    }

    [Fact]
    public void TlsEnforcement_IsProtocolCompliant_RejectsOlderProtocols()
    {
#pragma warning disable SYSLIB0039 // SslProtocols.Tls11 is obsolete
        Assert.False(TlsEnforcementHandler.IsProtocolCompliant(SslProtocols.Tls11));
#pragma warning restore SYSLIB0039
        Assert.False(TlsEnforcementHandler.IsProtocolCompliant(SslProtocols.None));
    }

    [Fact]
    public void TlsEnforcement_GetSecureSslOptions_EnforcesTls12Plus()
    {
        // Act
        var options = TlsEnforcementHandler.GetSecureSslOptions("example.com");

        // Assert
        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, options.EnabledSslProtocols);
        Assert.Equal("example.com", options.TargetHost);
    }

    [Fact]
    public async Task AllAccessAttempts_AreLoggedWithoutCardholderData()
    {
        // Arrange
        var pan = "4111111111111111";
        var correlationId = Guid.NewGuid().ToString();

        // Act - perform various operations
        var token = await _boundary.TokenizeAsync(pan, "123", correlationId, CancellationToken.None);
        await _boundary.DetokenizeAsync(token, "InterswitchAdapter", correlationId, CancellationToken.None);
        await _boundary.DetokenizeAsync(token, "UnauthorizedService", correlationId, CancellationToken.None);
        await _boundary.GetMaskedPanAsync(token, "SomeService", correlationId, CancellationToken.None);

        // Assert - all operations logged
        var entries = _auditStore.GetEntries();
        Assert.True(entries.Count >= 4); // at least 4 access logs

        // No cardholder data in any log entry
        foreach (var entry in entries)
        {
            Assert.DoesNotContain(pan, entry.TransactionReference);
            Assert.DoesNotContain(pan, entry.Action);
            Assert.DoesNotContain(pan, entry.ActorIdentity);
            Assert.DoesNotContain(pan, entry.PreviousState ?? string.Empty);
            Assert.DoesNotContain(pan, entry.NewState ?? string.Empty);
            Assert.DoesNotContain(pan, entry.CorrelationId);
        }
    }

    [Fact]
    public void TokenVault_StoreAndRetrieve_Works()
    {
        // Arrange
        var pan = "4111111111111111";
        var encrypted = _boundary.Encrypt(pan);

        // Act
        var token = _tokenVault.StoreToken(pan, encrypted, null);
        var entry = _tokenVault.GetByToken(token);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(encrypted, entry.EncryptedPan);
        Assert.True(_tokenVault.TokenExists(token));
    }

    [Fact]
    public void TokenVault_ConsistentTokenForSamePan()
    {
        // Arrange
        var pan = "4111111111111111";
        var encrypted = _boundary.Encrypt(pan);

        // Act
        var token1 = _tokenVault.StoreToken(pan, encrypted);
        var token2 = _tokenVault.StoreToken(pan, encrypted);

        // Assert
        Assert.Equal(token1, token2);
    }

    [Fact]
    public void Constructor_ThrowsOnInvalidKeyLength()
    {
        var shortKey = new byte[16]; // Only 128 bits, not 256
        var options = Options.Create(new PciSecurityOptions
        {
            EncryptionKey = Convert.ToBase64String(shortKey),
            AllowedServices = new List<string> { "test" }
        });

        Assert.Throws<InvalidOperationException>(() =>
            new PciBoundary(new TokenVault(), new ServiceAllowlist(options), _auditStore, options));
    }

    [Fact]
    public void Constructor_ThrowsOnMissingKey()
    {
        var options = Options.Create(new PciSecurityOptions
        {
            EncryptionKey = "",
            AllowedServices = new List<string> { "test" }
        });

        Assert.Throws<InvalidOperationException>(() =>
            new PciBoundary(new TokenVault(), new ServiceAllowlist(options), _auditStore, options));
    }

    /// <summary>
    /// In-memory audit store for testing purposes.
    /// </summary>
    private class InMemoryAuditStore : IAuditStore
    {
        private readonly List<AuditEntry> _entries = new();

        public Task AppendAsync(AuditEntry entry, CancellationToken ct)
        {
            _entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
        {
            var result = _entries.Where(e => e.TransactionReference == transactionReference).ToList();
            return Task.FromResult<IReadOnlyList<AuditEntry>>(result);
        }

        public List<AuditEntry> GetEntries() => _entries;
    }
}
