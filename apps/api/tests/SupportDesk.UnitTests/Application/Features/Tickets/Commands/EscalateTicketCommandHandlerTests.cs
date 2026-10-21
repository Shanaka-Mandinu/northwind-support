using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class EscalateTicketCommandHandlerTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 2)]
    public async Task Handler_uses_current_time_and_premium_window_and_keeps_or_replaces_owner(int open, int expectedOwner)
    {
        var context = new TicketCommandTestContext();
        var ticket = context.ExistingTicket(new TicketBuilder().WithPriority(TicketPriority.Medium).AssignedTo(1).Build());
        context.Clock.Advance(TimeSpan.FromHours(5));
        context.Candidates.Setup(q => q.GetCandidatesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AssignmentCandidate(1, "Alex", true, open, 10, true), new AssignmentCandidate(2, "Ben", true, 0, 8, true) });
        var history = new Mock<IEscalationStore>();
        TicketEscalation? recorded = null;
        history.Setup(h => h.Add(It.IsAny<TicketEscalation>())).Callback<TicketEscalation>(e => recorded = e);
        history.Setup(h => h.GetHistoryAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            new[] { new EscalationDto(recorded!.Id, recorded.FromPriority, recorded.ToPriority, null, null,
                recorded.FromDueAtUtc, recorded.ToDueAtUtc, recorded.Reason, recorded.EscalatedBy, recorded.EscalatedAtUtc) });
        var handler = new EscalateTicketCommandHandler(context.Tickets.Object, context.Customers.Object, context.Categories.Object,
            context.Candidates.Object, new TriagePolicy(new SlaSettings()), history.Object, context.UnitOfWork.Object,
            context.Clock, new GetTicketQueryHandler(context.TicketQueries.Object));

        var result = await handler.HandleAsync(ticket.Id, new("Needs attention"), "Alex", default);

        Assert.Equal(context.Clock.UtcNow.AddHours(4), ticket.DueAtUtc); // High 8h, premium half.
        Assert.Equal(expectedOwner, ticket.AssignedAgentId);
        Assert.Equal(TicketPriority.High, result.Escalation.ToPriority);
        Assert.NotNull(recorded);
        Assert.Equal(context.Clock.UtcNow, recorded.EscalatedAtUtc);
        context.VerifySaved(Times.Once());
    }

    [Theory]
    [InlineData("tiny", false)]
    [InlineData("     ", false)]
    [InlineData(" 12345 ", true)]
    public void Validation_matches_trimmed_input_rules(string reason, bool valid) =>
        Assert.Equal(valid, new EscalateTicketRequestValidator().Validate(new EscalateTicketRequest(reason)).IsValid);

    [Fact]
    public void Validation_checks_maximum_lengths()
    {
        var validator = new EscalateTicketRequestValidator();
        Assert.True(validator.Validate(new EscalateTicketRequest(new string('r', 500))).IsValid);
        Assert.False(validator.Validate(new EscalateTicketRequest(new string('r', 501))).IsValid);
    }

    [Fact]
    public async Task Unknown_ticket_does_not_save_history()
    {
        var context = new TicketCommandTestContext();
        var history = new Mock<IEscalationStore>();
        var handler = new EscalateTicketCommandHandler(context.Tickets.Object, context.Customers.Object, context.Categories.Object,
            context.Candidates.Object, new TriagePolicy(new SlaSettings()), history.Object, context.UnitOfWork.Object,
            context.Clock, new GetTicketQueryHandler(context.TicketQueries.Object));
        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(999, new("Valid reason"), "Alex", default));
        history.Verify(h => h.Add(It.IsAny<TicketEscalation>()), Times.Never);
        context.VerifySaved(Times.Never());
    }
}
