using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Repositories;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Features.Tickets.Commands.AssignTicket;

/// <summary>
/// Hands a ticket to an agent, or takes it back off them when no agent is given.
/// </summary>
/// <remarks>
/// Manual selection shares the eligibility rules used by automatic triage and escalation.
/// </remarks>
public sealed class AssignTicketCommandHandler(
    ITicketRepository tickets,
    IAgentRepository agents,
    ICategoryRepository categories,
    ITriageQueries candidates,
    TriagePolicy policy,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<AssignTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The ticket or the agent does not exist.</exception>
    /// <exception cref="ConflictException">The agent is inactive.</exception>
    public async Task<TicketDetailDto> HandleAsync(int id, AssignTicketRequest request, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ticket", id);

        if (request.AgentId is { } agentId)
        {
            var agent = await agents.GetByIdAsync(agentId, ct) ?? throw new NotFoundException("Agent", agentId);

            if (!agent.IsActive)
            {
                throw new ConflictException(
                    $"Agent {agent.FullName} is not active and cannot take new tickets.");
            }

            var category = await categories.GetByIdAsync(ticket.CategoryId, ct)
                ?? throw new NotFoundException("Category", ticket.CategoryId);
            var candidate = (await candidates.GetCandidatesAsync(ticket.CategoryId, ct)).SingleOrDefault(a => a.Id == agentId);
            if (candidate is null || !policy.IsEligible(candidate, category.RequiresSpecialist))
            {
                throw new ConflictException($"Agent {agent.FullName} must have spare capacity and the required specialization.");
            }
        }

        ticket.AssignTo(request.AgentId, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} assigned to agent {AgentId}.", ticket.Reference, request.AgentId);

        return await getTicket.HandleAsync(id, ct);
    }
}
