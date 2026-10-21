using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class AssignTicketCommandHandlerTests
{
    [Theory]
    [InlineData(10, true)]
    [InlineData(0, false)]
    public async Task Manual_assignment_rejects_capacity_and_specialization_violations(int open, bool specialist)
    {
        var context = new TicketCommandTestContext();
        var ticket = context.ExistingTicket(new TicketBuilder().Build());
        context.ExistingAgent(4);
        context.Categories.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportDesk.Domain.Aggregates.Categories.Category("Specialist category", true, false));
        context.Candidates.Setup(c => c.GetCandidatesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new SupportDesk.Domain.Aggregates.Tickets.AssignmentCandidate(4, "Alex", true, open, 10, specialist) });
        await Assert.ThrowsAsync<ConflictException>(() => context.AssignTicket.HandleAsync(ticket.Id, new(4), default));
        Assert.Null(ticket.AssignedAgentId);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_GivesTheTicketToAnActiveAgent()
    {
        var context = new TicketCommandTestContext();
        var ticket = context.ExistingTicket(new TicketBuilder().Build());
        context.ExistingAgent(id: 4, fullName: "Sara Lindqvist");

        context.Clock.Advance(TimeSpan.FromMinutes(15));

        await context.AssignTicket.HandleAsync(ticket.Id, new AssignTicketRequest(4), CancellationToken.None);

        Assert.Equal(4, ticket.AssignedAgentId);
        Assert.Equal(context.Clock.UtcNow, ticket.UpdatedAtUtc);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WithoutAnAgent_TakesTheTicketBack()
    {
        var context = new TicketCommandTestContext();
        var ticket = context.ExistingTicket(new TicketBuilder().AssignedTo(2).Build());

        await context.AssignTicket.HandleAsync(ticket.Id, new AssignTicketRequest(null), CancellationToken.None);

        Assert.Null(ticket.AssignedAgentId);
        context.Agents.Verify(
            a => a.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenTheAgentIsInactive_Throws()
    {
        var context = new TicketCommandTestContext();
        var ticket = context.ExistingTicket(new TicketBuilder().Build());
        context.ExistingAgent(id: 6, fullName: "Yuki Tanaka", isActive: false);

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => context.AssignTicket.HandleAsync(ticket.Id, new AssignTicketRequest(6), CancellationToken.None));

        Assert.Contains("Yuki Tanaka", exception.Message);
        Assert.Null(ticket.AssignedAgentId);
        context.VerifySaved(Times.Never());
    }
}
