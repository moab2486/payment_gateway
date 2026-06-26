namespace CardManagement.Domain.ValueObjects;

/// <summary>
/// System Trace Audit Number (STAN) — a 6-digit unique trace number
/// assigned to each transaction for end-to-end traceability.
/// </summary>
public record SystemTraceAuditNumber
{
    public string Value { get; }

    public SystemTraceAuditNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                "System Trace Audit Number cannot be null or empty.", nameof(value));

        if (value.Length != 6)
            throw new ArgumentException(
                $"System Trace Audit Number must be exactly 6 digits. Got {value.Length} characters.",
                nameof(value));

        if (!value.All(char.IsDigit))
            throw new ArgumentException(
                "System Trace Audit Number must contain only digits.", nameof(value));

        Value = value;
    }

    public override string ToString() => Value;
}
