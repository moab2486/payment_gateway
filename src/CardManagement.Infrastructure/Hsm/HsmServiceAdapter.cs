using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace CardManagement.Infrastructure.Hsm;

/// <summary>
/// HSM service adapter implementing IHsmService using Pkcs11Interop for PKCS#11 communication.
/// All cryptographic operations are performed within the HSM boundary.
/// Key material never leaves the HSM — only opaque key handles are referenced.
/// </summary>
public sealed class HsmServiceAdapter : IHsmService, IDisposable
{
    private readonly HsmOptions _options;
    private readonly ILogger<HsmServiceAdapter> _logger;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private IPkcs11Library? _pkcs11Library;
    private ISlot? _slot;
    private ISession? _session;
    private bool _isAuthenticated;
    private bool _disposed;

    public HsmServiceAdapter(IOptions<HsmOptions> options, ILogger<HsmServiceAdapter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Indicates whether the adapter is currently authenticated to the HSM.
    /// When false, all crypto operations are denied.
    /// </summary>
    public bool IsAuthenticated => _isAuthenticated;

    /// <inheritdoc />
    public async Task<Result<string>> GeneratePanAsync(BinRange binRange, CancellationToken ct)
    {
        if (!_isAuthenticated)
        {
            _logger.LogWarning("HSM operation denied: not authenticated");
            return Result<string>.Failure("HSM not authenticated. All crypto operations denied.", "HSM_AUTH_DENIED");
        }

        try
        {
            using var timeoutCts = CreateOperationTimeoutCts(ct);

            // Number of random digits needed: PAN length - prefix length - 1 (Luhn check digit)
            int randomDigitsNeeded = binRange.PanLength - binRange.Prefix.Length - 1;

            byte[] randomBytes = await Task.Run(() =>
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                return GenerateRandomBytes(randomDigitsNeeded);
            }, timeoutCts.Token);

            // Convert random bytes to digits (0-9)
            var digits = new char[randomDigitsNeeded];
            for (int i = 0; i < randomDigitsNeeded; i++)
            {
                digits[i] = (char)('0' + (randomBytes[i] % 10));
            }

            // Build the partial PAN: BIN prefix + random digits (without check digit)
            string partialPan = binRange.Prefix + new string(digits);

            return Result<string>.Success(partialPan);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("HSM operation timed out during PAN generation after {Timeout}s", _options.OperationTimeoutSeconds);
            return Result<string>.Failure("HSM operation timed out.", "HSM_TIMEOUT");
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Failure("Operation was cancelled.", "CANCELLED");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HSM PAN generation failed");
            return Result<string>.Failure($"HSM PAN generation failed: {ex.Message}", "HSM_ERROR");
        }
    }

    /// <inheritdoc />
    public async Task<Result<string>> ComputeCvv2Async(string pan, string expiryDate, CancellationToken ct)
    {
        if (!_isAuthenticated)
        {
            _logger.LogWarning("HSM operation denied: not authenticated");
            return Result<string>.Failure("HSM not authenticated. All crypto operations denied.", "HSM_AUTH_DENIED");
        }

        try
        {
            using var timeoutCts = CreateOperationTimeoutCts(ct);

            string cvv2 = await Task.Run(() =>
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                return ComputeCvv2WithHsm(pan, expiryDate);
            }, timeoutCts.Token);

            return Result<string>.Success(cvv2);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("HSM operation timed out during CVV2 computation after {Timeout}s", _options.OperationTimeoutSeconds);
            return Result<string>.Failure("HSM operation timed out.", "HSM_TIMEOUT");
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Failure("Operation was cancelled.", "CANCELLED");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HSM CVV2 computation failed");
            return Result<string>.Failure($"HSM CVV2 computation failed: {ex.Message}", "HSM_ERROR");
        }
    }

    /// <inheritdoc />
    public async Task<Result<byte[]>> EncryptAsync(byte[] plaintext, string keyHandle, CancellationToken ct)
    {
        if (!_isAuthenticated)
        {
            _logger.LogWarning("HSM operation denied: not authenticated");
            return Result<byte[]>.Failure("HSM not authenticated. All crypto operations denied.", "HSM_AUTH_DENIED");
        }

        try
        {
            using var timeoutCts = CreateOperationTimeoutCts(ct);

            byte[] ciphertext = await Task.Run(() =>
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                return EncryptWithHsm(plaintext, keyHandle);
            }, timeoutCts.Token);

            return Result<byte[]>.Success(ciphertext);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("HSM operation timed out during encryption after {Timeout}s", _options.OperationTimeoutSeconds);
            return Result<byte[]>.Failure("HSM operation timed out.", "HSM_TIMEOUT");
        }
        catch (OperationCanceledException)
        {
            return Result<byte[]>.Failure("Operation was cancelled.", "CANCELLED");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HSM encryption failed");
            return Result<byte[]>.Failure($"HSM encryption failed: {ex.Message}", "HSM_ERROR");
        }
    }

    /// <summary>
    /// Decrypts ciphertext using an HSM-managed AES-256 key identified by handle.
    /// </summary>
    /// <param name="ciphertext">The encrypted data to decrypt.</param>
    /// <param name="keyHandle">The opaque handle identifying the HSM key to use.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A result containing the plaintext bytes or an error.</returns>
    public async Task<Result<byte[]>> DecryptAsync(byte[] ciphertext, string keyHandle, CancellationToken ct)
    {
        if (!_isAuthenticated)
        {
            _logger.LogWarning("HSM operation denied: not authenticated");
            return Result<byte[]>.Failure("HSM not authenticated. All crypto operations denied.", "HSM_AUTH_DENIED");
        }

        try
        {
            using var timeoutCts = CreateOperationTimeoutCts(ct);

            byte[] plaintext = await Task.Run(() =>
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                return DecryptWithHsm(ciphertext, keyHandle);
            }, timeoutCts.Token);

            return Result<byte[]>.Success(plaintext);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("HSM operation timed out during decryption after {Timeout}s", _options.OperationTimeoutSeconds);
            return Result<byte[]>.Failure("HSM operation timed out.", "HSM_TIMEOUT");
        }
        catch (OperationCanceledException)
        {
            return Result<byte[]>.Failure("Operation was cancelled.", "CANCELLED");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HSM decryption failed");
            return Result<byte[]>.Failure($"HSM decryption failed: {ex.Message}", "HSM_ERROR");
        }
    }

    /// <summary>
    /// Authenticates to the HSM using configured credentials.
    /// Must complete within the configured auth timeout (default 5 seconds).
    /// If authentication fails, all crypto operations are denied and an alert event is emitted.
    /// </summary>
    /// <returns>True if authentication succeeded; false otherwise.</returns>
    public async Task<bool> AuthenticateAsync(CancellationToken ct = default)
    {
        using var authTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        authTimeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.AuthTimeoutSeconds));

        try
        {
            await Task.Run(() =>
            {
                authTimeoutCts.Token.ThrowIfCancellationRequested();

                var factories = new Pkcs11InteropFactories();
                _pkcs11Library = factories.Pkcs11LibraryFactory.LoadPkcs11Library(
                    factories, _options.Endpoint, AppType.MultiThreaded);

                var slots = _pkcs11Library.GetSlotList(SlotsType.WithTokenPresent);
                _slot = slots.FirstOrDefault(s => s.SlotId == _options.SlotId);

                if (_slot == null)
                {
                    throw new InvalidOperationException(
                        $"HSM slot {_options.SlotId} not found.");
                }

                _session = _slot.OpenSession(SessionType.ReadWrite);
                _session.Login(CKU.CKU_USER, _options.Pin);
            }, authTimeoutCts.Token);

            _isAuthenticated = true;
            _logger.LogInformation("Successfully authenticated to HSM slot {SlotId}", _options.SlotId);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _isAuthenticated = false;
            _logger.LogCritical(
                "HSM authentication timed out after {Timeout}s. All crypto operations denied. ALERT.",
                _options.AuthTimeoutSeconds);
            return false;
        }
        catch (Exception ex)
        {
            _isAuthenticated = false;
            _logger.LogCritical(ex,
                "HSM authentication failed. All crypto operations denied. ALERT.");
            return false;
        }
    }

    private byte[] GenerateRandomBytes(int count)
    {
        if (_session == null)
            throw new InvalidOperationException("HSM session is not initialized.");

        // Use PKCS#11 C_GenerateRandom to get true random bytes from the HSM
        return _session.GenerateRandom(count);
    }

    private string ComputeCvv2WithHsm(string pan, string expiryDate)
    {
        if (_session == null)
            throw new InvalidOperationException("HSM session is not initialized.");

        // Find the CVV key in the HSM by label
        var keyHandle = FindKey(_options.CvvKeyLabel, CKO.CKO_SECRET_KEY);

        // Compute HMAC-SHA256 over PAN + expiry data using the HSM key
        // Then extract 3 decimal digits from the result
        var mechanism = _session.Factories.MechanismFactory.Create(CKM.CKM_SHA256_HMAC);
        byte[] dataToSign = System.Text.Encoding.UTF8.GetBytes(pan + expiryDate);
        byte[] hmacResult = _session.Sign(mechanism, keyHandle, dataToSign);

        // Extract 3 digits from the HMAC result using decimalization
        return ExtractCvv2Digits(hmacResult);
    }

    private byte[] EncryptWithHsm(byte[] plaintext, string keyHandleLabel)
    {
        if (_session == null)
            throw new InvalidOperationException("HSM session is not initialized.");

        var keyHandle = FindKey(keyHandleLabel, CKO.CKO_SECRET_KEY);

        // Generate a random IV (16 bytes for AES-CBC)
        byte[] iv = _session.GenerateRandom(16);

        var mechanism = _session.Factories.MechanismFactory.Create(CKM.CKM_AES_CBC_PAD, iv);
        byte[] ciphertext = _session.Encrypt(mechanism, keyHandle, plaintext);

        // Prepend IV to ciphertext for later decryption
        byte[] result = new byte[iv.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, result, iv.Length, ciphertext.Length);

        return result;
    }

    private byte[] DecryptWithHsm(byte[] ciphertext, string keyHandleLabel)
    {
        if (_session == null)
            throw new InvalidOperationException("HSM session is not initialized.");

        if (ciphertext.Length <= 16)
            throw new ArgumentException("Ciphertext too short — must include IV prefix.", nameof(ciphertext));

        var keyHandle = FindKey(keyHandleLabel, CKO.CKO_SECRET_KEY);

        // Extract IV (first 16 bytes) and actual ciphertext
        byte[] iv = new byte[16];
        byte[] encryptedData = new byte[ciphertext.Length - 16];
        Buffer.BlockCopy(ciphertext, 0, iv, 0, 16);
        Buffer.BlockCopy(ciphertext, 16, encryptedData, 0, encryptedData.Length);

        var mechanism = _session.Factories.MechanismFactory.Create(CKM.CKM_AES_CBC_PAD, iv);
        return _session.Decrypt(mechanism, keyHandle, encryptedData);
    }

    private IObjectHandle FindKey(string label, CKO objectClass)
    {
        if (_session == null)
            throw new InvalidOperationException("HSM session is not initialized.");

        var searchAttributes = new List<IObjectAttribute>
        {
            _session.Factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, (ulong)objectClass),
            _session.Factories.ObjectAttributeFactory.Create(CKA.CKA_LABEL, label)
        };

        var foundObjects = _session.FindAllObjects(searchAttributes);

        if (foundObjects.Count == 0)
            throw new InvalidOperationException($"HSM key with label '{label}' not found.");

        return foundObjects[0];
    }

    /// <summary>
    /// Extracts exactly 3 decimal digits from an HMAC result using decimalization.
    /// Takes the first 4 bytes of the HMAC, converts to a 32-bit unsigned integer,
    /// then extracts 3 digits via modulo operations.
    /// </summary>
    private static string ExtractCvv2Digits(byte[] hmacResult)
    {
        // Use the first 4 bytes to form an unsigned integer
        uint value = ((uint)hmacResult[0] << 24)
                   | ((uint)hmacResult[1] << 16)
                   | ((uint)hmacResult[2] << 8)
                   | hmacResult[3];

        // Extract 3 digits
        int digit1 = (int)(value % 10);
        value /= 10;
        int digit2 = (int)(value % 10);
        value /= 10;
        int digit3 = (int)(value % 10);

        return $"{digit3}{digit2}{digit1}";
    }

    private CancellationTokenSource CreateOperationTimeoutCts(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.OperationTimeoutSeconds));
        return cts;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_session != null)
            {
                try { _session.Logout(); } catch { /* Best effort */ }
                _session.Dispose();
            }

            _pkcs11Library?.Dispose();
        }
        catch
        {
            // Best-effort cleanup
        }

        _sessionLock.Dispose();
    }
}
