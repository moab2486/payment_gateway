using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IAccountRepository.
/// Uses raw SQL for SELECT FOR UPDATE row-level locking with configurable lock_timeout.
/// </summary>
public class AccountRepository : IAccountRepository
{
    private readonly CardManagementDbContext _dbContext;

    public AccountRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Accounts
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses raw SQL with PostgreSQL SELECT FOR UPDATE to acquire a row-level lock.
    /// This must be called within an active database transaction.
    /// The lock_timeout is set via SET LOCAL lock_timeout = '5000' by the UnitOfWork
    /// before this call, ensuring deadlock prevention.
    /// </remarks>
    public async Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Accounts
            .FromSqlInterpolated(
                $@"SELECT ""Id"", ""AccountNumber"", ""AccountType"", ""Currency"", ""Balance"", ""Status"", ""CreatedAtUtc"", ""UpdatedAtUtc"" FROM ""Accounts"" WHERE ""Id"" = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return account;
    }

    /// <inheritdoc />
    public async Task UpdateBalanceAsync(Account account, CancellationToken cancellationToken = default)
    {
        _dbContext.Accounts.Update(account);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
