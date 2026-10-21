namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Works out where a ticket stands against its due date. SLA status is always derived from
/// the ticket's dates and the current time - never stored.
/// </summary>
public static class SlaEvaluator
{
    public static SlaStatus Evaluate(DateTime? dueAtUtc, DateTime? resolvedAtUtc, DateTime nowUtc,
        DateTime? startedAtUtc = null, double atRiskThresholdPercent = 25) =>
        dueAtUtc is null ? SlaStatus.NotApplicable
        : resolvedAtUtc is not null
            ? (resolvedAtUtc <= dueAtUtc ? SlaStatus.Met : SlaStatus.Breached)
        : nowUtc > dueAtUtc ? SlaStatus.Breached
        : startedAtUtc is not null && (dueAtUtc.Value - nowUtc).TotalMilliseconds
            <= (dueAtUtc.Value - startedAtUtc.Value).TotalMilliseconds * atRiskThresholdPercent / 100
            ? SlaStatus.AtRisk
        : SlaStatus.WithinSla;
}
