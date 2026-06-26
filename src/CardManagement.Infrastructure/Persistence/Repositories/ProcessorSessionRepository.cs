using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IProcessorSessionRepository.
/// Provides persistence operations for ProcessorSession entities tracking
/// external card processor sign-on state and connection health.
/// </summary>
public class ProcessorSessionRepository : IProcessorSessionRepository
{
    private readonly CardManagementDbContext _dbContext;

    public ProcessorSessionRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<ProcessorSession?> GetByTypeAsync(ProcessorType processorType, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProcessorSessions
            .FirstOrDefaultAsync(p => p.ProcessorType == processorType, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ProcessorSession session, CancellationToken cancellationToken = default)
    {
        _dbContext.ProcessorSessions.Update(session);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
