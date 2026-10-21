using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class EscalationTests
{
    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Medium)]
    [InlineData(TicketPriority.Medium, TicketPriority.High)]
    [InlineData(TicketPriority.High, TicketPriority.Critical)]
    public void Escalation_updates_ticket_and_snapshots_previous_values(TicketPriority from, TicketPriority to)
    {
        var ticket = new TicketBuilder().WithPriority(from).AssignedTo(1).Build();
        var oldDue = FixedClock.DefaultNow.AddDays(3);
        ticket.ApplyTriage(oldDue, 1);
        var now = FixedClock.DefaultNow.AddHours(2);
        var due = now.AddHours(4);
        var entry = ticket.Escalate(due, 2, "  Needs urgent help  ", "  Alex  ", now);
        Assert.Equal(to, ticket.Priority);
        Assert.Equal(now, ticket.SlaStartedAtUtc);
        Assert.Equal(due, ticket.DueAtUtc);
        Assert.Equal(now, ticket.UpdatedAtUtc);
        Assert.Equal(2, ticket.AssignedAgentId);
        Assert.Equal(from, entry.FromPriority);
        Assert.Equal(to, entry.ToPriority);
        Assert.Equal(1, entry.FromAgentId);
        Assert.Equal(2, entry.ToAgentId);
        Assert.Equal(oldDue, entry.FromDueAtUtc);
        Assert.Equal(due, entry.ToDueAtUtc);
        Assert.Equal("Needs urgent help", entry.Reason);
        Assert.Equal("Alex", entry.EscalatedBy);
        Assert.Equal(now, entry.EscalatedAtUtc);
        ticket.AssignTo(3, now);
        Assert.Equal(2, entry.ToAgentId);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, TicketStatus.Open, "Critical")]
    [InlineData(TicketPriority.Low, TicketStatus.Resolved, "Resolved")]
    [InlineData(TicketPriority.Low, TicketStatus.Closed, "Closed")]
    public void Cannot_escalate_terminal_priority_or_status(TicketPriority priority, TicketStatus status, string message)
    {
        var ticket = new TicketBuilder().WithPriority(priority).WithStatus(status).Build();
        var error = Assert.Throws<BusinessRuleViolationException>(() => ticket.Escalate(
            FixedClock.DefaultNow.AddHours(1), null, "Valid reason", "Alex", FixedClock.DefaultNow));
        Assert.Contains(message, error.Message);
        Assert.Equal(priority, ticket.Priority);
    }

    [Theory]
    [InlineData(-1, SlaStatus.WithinSla)]
    [InlineData(0, SlaStatus.AtRisk)]
    [InlineData(1, SlaStatus.AtRisk)]
    public void At_risk_boundary_is_inclusive(int milliseconds, SlaStatus expected)
    {
        var start = FixedClock.DefaultNow;
        Assert.Equal(expected, SlaEvaluator.Evaluate(start.AddHours(4), null,
            start.AddHours(3).AddMilliseconds(milliseconds), start));
    }

    [Fact]
    public void Due_and_resolution_boundaries_are_exact()
    {
        var start = FixedClock.DefaultNow;
        var due = start.AddHours(4);
        Assert.Equal(SlaStatus.AtRisk, SlaEvaluator.Evaluate(due, null, due, start));
        Assert.Equal(SlaStatus.Breached, SlaEvaluator.Evaluate(due, null, due.AddMilliseconds(1), start));
        Assert.Equal(SlaStatus.Met, SlaEvaluator.Evaluate(due, due, due.AddDays(1), start));
        Assert.Equal(SlaStatus.Breached, SlaEvaluator.Evaluate(due, due.AddMilliseconds(1), due, start));
        Assert.Equal(SlaStatus.AtRisk, SlaEvaluator.Evaluate(due, null, start.AddHours(2), start, 50));
    }
}
