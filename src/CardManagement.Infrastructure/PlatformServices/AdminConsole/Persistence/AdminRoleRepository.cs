using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using CardManagement.Domain.PlatformServices.AdminConsole;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole.Persistence;

/// <summary>
/// EF Core implementation of IAdminRoleRepository.
/// Provides persistence for admin role assignments.
/// </summary>
public class AdminRoleRepository : IAdminRoleRepository
{
    private readonly CardManagementDbContext _dbContext;

    public AdminRoleRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<AdminRole> CreateAsync(AdminRole role, CancellationToken ct)
    {
        _dbContext.AdminRoles.Add(role);
        await _dbContext.SaveChangesAsync(ct);
        return role;
    }

    public async Task<IReadOnlyList<AdminRole>> GetActiveByUserAsync(string userId, CancellationToken ct)
    {
        return await _dbContext.AdminRoles
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.IsActive)
            .OrderBy(r => r.Role)
            .ToListAsync(ct);
    }

    public async Task<AdminRole?> GetByUserAndRoleAsync(string userId, string role, CancellationToken ct)
    {
        return await _dbContext.AdminRoles
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Role == role && r.IsActive, ct);
    }

    public async Task UpdateAsync(AdminRole role, CancellationToken ct)
    {
        _dbContext.AdminRoles.Update(role);
        await _dbContext.SaveChangesAsync(ct);
    }
}
