using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ICardRepository.
/// Provides persistence operations for the Card aggregate root.
/// </summary>
public class CardRepository : ICardRepository
{
    private readonly CardManagementDbContext _dbContext;

    public CardRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<Card?> GetByPanHashAsync(string panHash, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Cards
            .FirstOrDefaultAsync(c => c.PanHash == panHash, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByPanHashAsync(string panHash, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Cards
            .AnyAsync(c => c.PanHash == panHash, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(Card card, CancellationToken cancellationToken = default)
    {
        await _dbContext.Cards.AddAsync(card, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
