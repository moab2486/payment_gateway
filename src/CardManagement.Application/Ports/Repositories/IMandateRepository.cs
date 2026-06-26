using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;

namespace CardManagement.Application.Ports.Repositories;

/// <summary>
/// Repository for persisting and retrieving DirectDebitMandate entities.
/// </summary>
public interface IMandateRepository
{
    Task SaveAsync(DirectDebitMandate mandate, CancellationToken ct);
    Task UpdateAsync(DirectDebitMandate mandate, CancellationToken ct);
    Task<DirectDebitMandate?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<DirectDebitMandate?> GetByMandateReferenceAsync(string mandateReference, CancellationToken ct);
    Task<IReadOnlyList<DirectDebitMandate>> GetByStatusAsync(MandateStatus status, CancellationToken ct);
}
