using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;

namespace SupportDesk.Domain.Aggregates.Tickets;

public sealed class SlaSettings
{
    public Dictionary<TicketPriority, double> BaseWindowHours { get; set; } = new()
    {
        [TicketPriority.Low] = 72,
        [TicketPriority.Medium] = 24,
        [TicketPriority.High] = 8,
        [TicketPriority.Critical] = 4
    };
    public double PremiumCustomerMultiplier { get; set; } = 0.5;
    public double MinimumWindowHours { get; set; } = 1;
    public double AtRiskThresholdPercent { get; set; } = 25;

    public void Validate()
    {
        if (Enum.GetValues<TicketPriority>().Any(p => !BaseWindowHours.TryGetValue(p, out var hours)
                || !double.IsFinite(hours) || hours <= 0)
            || !double.IsFinite(PremiumCustomerMultiplier) || PremiumCustomerMultiplier <= 0 || PremiumCustomerMultiplier > 1
            || !double.IsFinite(MinimumWindowHours) || MinimumWindowHours <= 0
            || !double.IsFinite(AtRiskThresholdPercent) || AtRiskThresholdPercent < 0 || AtRiskThresholdPercent > 100)
        {
            throw new InvalidOperationException("Invalid Sla configuration: positive windows/floor, multiplier (0,1], threshold [0,100] required.");
        }
    }
}

public sealed record AssignmentCandidate(int Id, string FullName, bool IsActive, int OpenTickets, int MaxOpenTickets, bool IsSpecialist);
public sealed record AssignmentDecision(int? AgentId, string Reason);

/// <summary>Pure rules shared by create and escalate; database access stays in the application.</summary>
public sealed class TriagePolicy(SlaSettings settings)
{
    public bool IsEligible(AssignmentCandidate agent, bool requiresSpecialist) =>
        agent.IsActive && agent.OpenTickets < agent.MaxOpenTickets && (!requiresSpecialist || agent.IsSpecialist);

    public TicketPriority PriorityFor(TicketPriority? requested, Category category) =>
        category.ForcesCriticalPriority ? TicketPriority.Critical : requested ?? TicketPriority.Medium;

    public TimeSpan WindowFor(TicketPriority priority, CustomerTier tier)
    {
        var hours = settings.BaseWindowHours[priority];
        if (tier == CustomerTier.Premium)
        {
            hours = Math.Max(settings.MinimumWindowHours, hours * settings.PremiumCustomerMultiplier);
        }

        return TimeSpan.FromHours(hours);
    }

    public AssignmentDecision Assign(IEnumerable<AssignmentCandidate> candidates, bool requiresSpecialist, int? currentAgentId = null)
    {
        var eligible = candidates.Where(a => IsEligible(a, requiresSpecialist)).ToList();
        var current = eligible.Find(a => a.Id == currentAgentId);
        if (current is not null)
        {
            return new(current.Id, $"Kept {current.FullName}: still eligible.");
        }

        var selected = eligible.OrderBy(a => a.OpenTickets).ThenBy(a => a.Id).FirstOrDefault();
        return selected is null
            ? new(null, "Unassigned because no active agent with spare capacity and the required specialization is available.")
            : new(selected.Id, $"Assigned {selected.FullName}: fewest open tickets ({selected.OpenTickets}) among eligible agents; ties use lowest agent ID.");
    }
}
