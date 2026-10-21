using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class TriagePolicyTests
{
    private readonly TriagePolicy _policy = new(new SlaSettings());

    [Theory]
    [InlineData(TicketPriority.Low, 72)]
    [InlineData(TicketPriority.Medium, 24)]
    [InlineData(TicketPriority.High, 8)]
    [InlineData(TicketPriority.Critical, 4)]
    public void Windows_apply_to_both_customer_tiers(TicketPriority priority, double hours)
    {
        Assert.Equal(TimeSpan.FromHours(hours), _policy.WindowFor(priority, CustomerTier.Standard));
        Assert.Equal(TimeSpan.FromHours(hours / 2), _policy.WindowFor(priority, CustomerTier.Premium));
    }

    [Fact]
    public void Premium_window_respects_configured_floor()
    {
        var settings = new SlaSettings { PremiumCustomerMultiplier = 0.01, MinimumWindowHours = 1 };
        Assert.Equal(TimeSpan.FromHours(1), new TriagePolicy(settings).WindowFor(TicketPriority.Critical, CustomerTier.Premium));
    }

    [Fact]
    public void Category_flag_overrides_requested_priority_without_name_checks()
    {
        Assert.Equal(TicketPriority.Critical, _policy.PriorityFor(TicketPriority.Low, new Category("Any name", false, true)));
        Assert.Equal(TicketPriority.Medium, _policy.PriorityFor(null, new Category("Security", false, false)));
        Assert.Equal(TicketPriority.High, _policy.PriorityFor(TicketPriority.High, new Category("General", false, false)));
    }

    private static AssignmentCandidate Agent(int id, int open = 0, bool active = true, bool specialist = true, int limit = 5) =>
        new(id, $"Agent {id}", active, open, limit, specialist);

    [Fact]
    public void Chooses_fewest_open_then_lowest_id_independent_of_input_order()
    {
        Assert.Equal(2, _policy.Assign([Agent(1, 3), Agent(3, 1), Agent(2, 1)], true).AgentId);
    }

    [Fact]
    public void Rejects_inactive_full_and_non_specialists()
    {
        Assert.Equal(4, _policy.Assign([Agent(1, active: false), Agent(2, 5), Agent(3, specialist: false), Agent(4, 2)], true).AgentId);
    }

    [Fact]
    public void Non_specialist_is_eligible_when_category_does_not_require_one() =>
        Assert.Equal(1, _policy.Assign([Agent(1, specialist: false)], false).AgentId);

    [Fact]
    public void Keeps_current_eligible_owner_even_when_another_agent_has_less_work() =>
        Assert.Equal(2, _policy.Assign([Agent(1), Agent(2, 4)], true, 2).AgentId);

    [Fact]
    public void Replaces_current_owner_at_capacity() =>
        Assert.Equal(1, _policy.Assign([Agent(1), Agent(2, 5)], true, 2).AgentId);

    [Fact]
    public void No_eligible_agents_is_an_explained_normal_outcome()
    {
        var decision = _policy.Assign([Agent(1, 5)], true);
        Assert.Null(decision.AgentId);
        Assert.Contains("Unassigned because", decision.Reason);
    }

    [Fact]
    public void Invalid_configuration_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => new SlaSettings { AtRiskThresholdPercent = 101 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new SlaSettings { PremiumCustomerMultiplier = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new SlaSettings { BaseWindowHours = [] }.Validate());
    }
}
