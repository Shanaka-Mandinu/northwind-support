using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Abstractions;

public interface IEscalationStore
{
    void Add(TicketEscalation escalation);
    Task<IReadOnlyList<EscalationDto>> GetHistoryAsync(int ticketId, CancellationToken ct);
}
