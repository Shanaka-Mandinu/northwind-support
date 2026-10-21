using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Abstractions;

public interface ITriageQueries
{
    Task<IReadOnlyList<AssignmentCandidate>> GetCandidatesAsync(int categoryId, CancellationToken ct);
}
