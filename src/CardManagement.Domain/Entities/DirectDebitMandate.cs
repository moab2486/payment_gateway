using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;

namespace CardManagement.Domain.Entities;

/// <summary>
/// Represents a direct debit mandate authorizing recurring debits from a debtor account.
/// Tracks mandate lifecycle including activation, execution, and cancellation.
/// </summary>
public class DirectDebitMandate
{
    public Guid Id { get; private set; }
    public string MandateReference { get; private set; } = string.Empty;
    public string DebtorAccount { get; private set; } = string.Empty;
    public string CreditorAccount { get; private set; } = string.Empty;
    public Money Amount { get; private set; } = null!;
    public MandateFrequency Frequency { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public MandateStatus Status { get; private set; }
    public string? NibssReference { get; private set; }

    private DirectDebitMandate() { }

    public static DirectDebitMandate Create(
        string mandateReference,
        string debtorAccount,
        string creditorAccount,
        Money amount,
        MandateFrequency frequency,
        DateTime startDate,
        DateTime endDate)
    {
        if (string.IsNullOrWhiteSpace(mandateReference))
            throw new ArgumentException("Mandate reference is required.", nameof(mandateReference));

        if (string.IsNullOrWhiteSpace(debtorAccount))
            throw new ArgumentException("Debtor account is required.", nameof(debtorAccount));

        if (string.IsNullOrWhiteSpace(creditorAccount))
            throw new ArgumentException("Creditor account is required.", nameof(creditorAccount));

        if (amount is null)
            throw new ArgumentNullException(nameof(amount));

        if (endDate <= startDate)
            throw new ArgumentException("End date must be after start date.", nameof(endDate));

        return new DirectDebitMandate
        {
            Id = Guid.NewGuid(),
            MandateReference = mandateReference,
            DebtorAccount = debtorAccount,
            CreditorAccount = creditorAccount,
            Amount = amount,
            Frequency = frequency,
            StartDate = startDate,
            EndDate = endDate,
            Status = MandateStatus.Active
        };
    }

    public void SetNibssReference(string nibssReference)
    {
        if (string.IsNullOrWhiteSpace(nibssReference))
            throw new ArgumentException("NIBSS reference is required.", nameof(nibssReference));

        NibssReference = nibssReference;
    }

    public void Cancel()
    {
        if (Status != MandateStatus.Active)
            throw new InvalidOperationException("Can only cancel an active mandate.");

        Status = MandateStatus.Cancelled;
    }

    public void Expire()
    {
        if (Status != MandateStatus.Active)
            throw new InvalidOperationException("Can only expire an active mandate.");

        Status = MandateStatus.Expired;
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
