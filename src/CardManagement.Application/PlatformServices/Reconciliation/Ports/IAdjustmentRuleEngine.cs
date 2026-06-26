using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.Ports;

/// <summary>
/// Evaluates reconciliation exceptions against configured auto-adjustment rules
/// to determine whether an automatic adjustment should be applied.
/// </summary>
public interface IAdjustmentRuleEngine
{
    AdjustmentDecision Evaluate(ReconciliationException exception);
}
