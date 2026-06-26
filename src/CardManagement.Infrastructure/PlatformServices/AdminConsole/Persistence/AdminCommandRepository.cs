using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using CardManagement.Domain.PlatformServices.AdminConsole;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole.Persistence;

/// <summary>
/// EF Core implementation of IAdminCommandRepository.
/// Provides persistence for maker-checker pending commands.
/// </summary>
public class AdminCommandRepository : IAdminCommandRepository
{
    private readonly CardManagementDbContext _dbContext;

    public AdminCommandRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<PendingCommand> CreateAsync(PendingCommand command, CancellationToken ct)
    {
        _dbContext.AdminPendingCommands.Add(command);
        await _dbContext.SaveChangesAsync(ct);
        return command;
    }

    public async Task<PendingCommand?> GetByIdAsync(Guid commandId, CancellationToken ct)
    {
        return await _dbContext.AdminPendingCommands
            .FirstOrDefaultAsync(c => c.Id == commandId, ct);
    }

    public async Task UpdateAsync(PendingCommand command, CancellationToken ct)
    {
        _dbContext.AdminPendingCommands.Update(command);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PendingCommand>> GetExpiredAsync(TimeSpan expiryThreshold, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - expiryThreshold;
        return await _dbContext.AdminPendingCommands
            .Where(c => c.Status == CommandStatus.Pending && c.ExpiresAtUtc <= DateTime.UtcNow)
            .OrderBy(c => c.ExpiresAtUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PendingCommand>> GetPendingAsync(int limit, int offset, CancellationToken ct)
    {
        return await _dbContext.AdminPendingCommands
            .AsNoTracking()
            .Where(c => c.Status == CommandStatus.Pending)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }
}
