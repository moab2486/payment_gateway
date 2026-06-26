using CardManagement.Application.DTOs;
using CardManagement.Domain.Entities;

namespace CardManagement.Application.Ports;

/// <summary>
/// Evaluates payment requests for fraud risk, returning a score and verdict.
/// </summary>
public interface IFraudRiskEngine
{
    Task<RiskDecision> EvaluateAsync(PaymentRequest request, CancellationToken ct);
}
