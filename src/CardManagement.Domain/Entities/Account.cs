using CardManagement.Domain.Enums;

namespace CardManagement.Domain.Entities;

/// <summary>
/// Account aggregate root representing a financial account in the ledger system.
/// Supports asset, liability, revenue, and expense account types.
/// Balance is stored as the smallest currency unit (e.g., kobo, cents).
/// </summary>
public class Account
{
    public Guid Id { get; private set; }
    public string AccountNumber { get; private set; } = string.Empty;
    public AccountType AccountType { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public long Balance { get; private set; }
    public AccountStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private Account() { }

    public static Account Create(
        string accountNumber,
        AccountType accountType,
        string currency,
        long initialBalance = 0)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("Account number is required.", nameof(accountNumber));

        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));

        var now = TruncateToMilliseconds(DateTime.UtcNow);

        return new Account
        {
            Id = Guid.NewGuid(),
            AccountNumber = accountNumber,
            AccountType = accountType,
            Currency = currency,
            Balance = initialBalance,
            Status = AccountStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    /// <summary>
    /// Determines if this is a debit-normal account (Asset or Expense).
    /// Debit-normal accounts cannot have negative balances.
    /// </summary>
    public bool IsDebitNormal => AccountType == AccountType.Asset || AccountType == AccountType.Expense;

    public void Debit(long amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Debit amount must be positive.", nameof(amount));

        if (IsDebitNormal && Balance - amount < 0)
            throw new InvalidOperationException("Insufficient funds: debit would cause negative balance on a debit-normal account.");

        Balance -= amount;
        UpdatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void Credit(long amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Credit amount must be positive.", nameof(amount));

        Balance += amount;
        UpdatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void Freeze()
    {
        if (Status == AccountStatus.Closed)
            throw new InvalidOperationException("Cannot freeze a closed account.");

        Status = AccountStatus.Frozen;
        UpdatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void Close()
    {
        Status = AccountStatus.Closed;
        UpdatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void Activate()
    {
        if (Status == AccountStatus.Closed)
            throw new InvalidOperationException("Cannot reactivate a closed account.");

        Status = AccountStatus.Active;
        UpdatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
