using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Repositories;

public sealed class EscalationStore(SupportDbContext db) : IEscalationStore
{
    public void Add(TicketEscalation escalation) => db.Add(escalation);

    public async Task<IReadOnlyList<EscalationDto>> GetHistoryAsync(int ticketId, CancellationToken ct) =>
        await (from e in db.Set<TicketEscalation>().AsNoTracking()
               from previous in db.Set<Agent>().Where(a => a.Id == e.FromAgentId).DefaultIfEmpty()
               from next in db.Set<Agent>().Where(a => a.Id == e.ToAgentId).DefaultIfEmpty()
               where e.TicketId == ticketId
               orderby e.EscalatedAtUtc descending, e.Id descending
               select new EscalationDto(e.Id, e.FromPriority, e.ToPriority,
                   previous == null ? null : new AgentSummaryDto(previous.Id, previous.FullName),
                   next == null ? null : new AgentSummaryDto(next.Id, next.FullName),
                   e.FromDueAtUtc, e.ToDueAtUtc, e.Reason, e.EscalatedBy, e.EscalatedAtUtc)).ToListAsync(ct);
}
