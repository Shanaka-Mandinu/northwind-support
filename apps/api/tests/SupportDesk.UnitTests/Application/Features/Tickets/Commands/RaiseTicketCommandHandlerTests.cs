using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class RaiseTicketCommandHandlerTests
{
    private static CreateTicketRequest Request(TicketPriority? priority = null) =>
        new("Cannot log in to the portal", "Happens for every user since this morning.", 1, 3, priority);

    [Fact]
    public async Task Forced_priority_and_selected_owner_are_applied_before_saving()
    {
        var context = new TicketCommandTestContext();
        context.Categories.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportDesk.Domain.Aggregates.Categories.Category("Custom forced category", true, true));
        context.Candidates.Setup(q => q.GetCandidatesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AssignmentCandidate(2, "Ben", true, 1, 8, true) });
        var result = await context.RaiseTicket.HandleAsync(Request(TicketPriority.Low), default);
        Assert.Equal(TicketPriority.Critical, context.AddedTicket!.Priority);
        Assert.Equal(2, context.AddedTicket.AssignedAgentId);
        Assert.Equal(context.Clock.UtcNow.AddHours(2), context.AddedTicket.DueAtUtc);
        Assert.Contains("forces Critical", result.Triage!.PriorityReason);
    }

    [Fact]
    public async Task No_eligible_agent_still_creates_with_due_date_and_explanation()
    {
        var context = new TicketCommandTestContext();
        var result = await context.RaiseTicket.HandleAsync(Request(), default);
        Assert.Null(context.AddedTicket!.AssignedAgentId);
        Assert.Equal(context.Clock.UtcNow.AddHours(12), context.AddedTicket.DueAtUtc);
        Assert.Contains("Unassigned because", result.Triage!.AssignmentReason);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_StampsReferenceStatusAndTimestamps()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        var created = context.AddedTicket;
        Assert.NotNull(created);
        Assert.Equal("TCK-0041", created.Reference);
        Assert.Equal(TicketStatus.New, created.Status);
        Assert.Equal(TicketPriority.High, created.Priority);
        Assert.Equal(FixedClock.DefaultNow, created.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow, created.UpdatedAtUtc);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WithoutRequestedPriority_FallsBackToMedium()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Medium, context.AddedTicket.Priority);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.Customers
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupportDesk.Domain.Aggregates.Customers.Customer?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Customer", exception.Message);
        context.VerifySaved(Times.Never());
    }
}
