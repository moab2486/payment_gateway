using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.PlatformServices.DeveloperPortal.Persistence;

/// <summary>
/// EF Core implementation of IApiKeyRepository.
/// Provides persistence operations for API key lifecycle management.
/// Only key hashes are stored — raw keys are never persisted.
/// </summary>
public class ApiKeyRepository : IApiKeyRepository
{
    private readonly CardManagementDbContext _dbContext;

    public ApiKeyRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<ApiKey> CreateAsync(ApiKey key, CancellationToken ct)
    {
        _dbContext.DeveloperApiKeys.Add(key);
        await _dbContext.SaveChangesAsync(ct);
        return key;
    }

    public async Task<ApiKey?> GetByIdAsync(Guid keyId, CancellationToken ct)
    {
        return await _dbContext.DeveloperApiKeys.FindAsync(new object[] { keyId }, ct);
    }

    public async Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken ct)
    {
        return await _dbContext.DeveloperApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash, ct);
    }

    public async Task<int> CountActiveByDeveloperAsync(Guid developerId, CancellationToken ct)
    {
        return await _dbContext.DeveloperApiKeys
            .CountAsync(k => k.DeveloperId == developerId && k.Status == KeyStatus.Active, ct);
    }

    public async Task UpdateAsync(ApiKey key, CancellationToken ct)
    {
        _dbContext.DeveloperApiKeys.Update(key);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ApiKey>> GetByDeveloperAsync(Guid developerId, CancellationToken ct)
    {
        return await _dbContext.DeveloperApiKeys
            .AsNoTracking()
            .Where(k => k.DeveloperId == developerId)
            .OrderByDescending(k => k.CreatedAtUtc)
            .ToListAsync(ct);
    }
}
