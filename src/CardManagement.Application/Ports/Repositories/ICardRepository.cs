using CardManagement.Domain.Entities;

namespace CardManagement.Application.Ports.Repositories;

/// <summary>
/// Repository interface for Card aggregate persistence operations.
/// </summary>
public interface ICardRepository
{
    /// <summary>
    /// Retrieves a card by its PAN hash (SHA-256, non-reversible lookup).
    /// </summary>
    Task<Card?> GetByPanHashAsync(string panHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a card with the given PAN hash already exists in the database.
    /// Used for PAN uniqueness verification during card issuance.
    /// </summary>
    Task<bool> ExistsByPanHashAsync(string panHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new card entity to the database.
    /// </summary>
    Task AddAsync(Card card, CancellationToken cancellationToken = default);
}
