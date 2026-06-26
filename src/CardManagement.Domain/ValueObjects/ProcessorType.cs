namespace CardManagement.Domain.ValueObjects;

/// <summary>
/// Identifies the card processor switch that handles transactions for a given card scheme.
/// </summary>
public enum ProcessorType
{
    /// <summary>
    /// Interswitch processor — handles Verve card transactions.
    /// </summary>
    Interswitch,

    /// <summary>
    /// CardFi/Universal Processing — handles Visa and Mastercard transactions.
    /// </summary>
    CardFi,

    /// <summary>
    /// Cardify processor — handles Visa and Mastercard card transactions.
    /// </summary>
    Cardify
}
