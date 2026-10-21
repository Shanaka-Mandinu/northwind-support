using FluentValidation;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketRequestValidator : AbstractValidator<EscalateTicketRequest>
{
    public EscalateTicketRequestValidator()
    {
        RuleFor(x => x.Reason).Must(x => x is not null && x.Trim().Length is >= 5 and <= 500)
            .WithMessage("Reason must be 5–500 characters after trimming.");
    }
}

public sealed class EscalateTicketCommandHandler(ITicketRepository tickets, ICustomerRepository customers,
    ICategoryRepository categories, ITriageQueries candidates, TriagePolicy policy, IEscalationStore history,
    IUnitOfWork unitOfWork, IClock clock, GetTicketQueryHandler getTicket)
{
    public async Task<EscalationResultDto> HandleAsync(int id, EscalateTicketRequest request, string authenticatedActor, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ticket", id);
        var priority = ticket.EscalationPriority();
        var customer = await customers.GetByIdAsync(ticket.CustomerId, ct) ?? throw new NotFoundException("Customer", ticket.CustomerId);
        var category = await categories.GetByIdAsync(ticket.CategoryId, ct) ?? throw new NotFoundException("Category", ticket.CategoryId);
        var now = clock.UtcNow;
        var owner = policy.Assign(await candidates.GetCandidatesAsync(ticket.CategoryId, ct), category.RequiresSpecialist, ticket.AssignedAgentId);
        var escalation = ticket.Escalate(now.Add(policy.WindowFor(priority, customer.Tier)), owner.AgentId,
            request.Reason, authenticatedActor, now);
        history.Add(escalation);
        // Ticket and history use the same DbContext and commit atomically.
        await unitOfWork.SaveChangesAsync(ct);
        var updated = await getTicket.HandleAsync(id, ct);
        var rows = await history.GetHistoryAsync(id, ct);
        return new(updated, rows.Single(row => row.Id == escalation.Id));
    }
}
