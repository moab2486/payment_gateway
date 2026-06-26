namespace CardManagement.Domain.PlatformServices.Reconciliation;

/// <summary>
/// Identifies the external payment processor from which settlement files are received.
/// </summary>
public enum ProcessorType
{
    /// <summary>
    /// Nigeria Inter-Bank Settlement System.
    /// </summary>
    NIBSS,

    /// <summary>
    /// Interswitch payment processor.
    /// </summary>
    Interswitch,

    /// <summary>
    /// Cardify payment processor.
    /// </summary>
    Cardify
}
