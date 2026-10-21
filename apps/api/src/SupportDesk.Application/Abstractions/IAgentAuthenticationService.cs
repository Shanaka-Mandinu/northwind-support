using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Application.Abstractions;

public interface IAgentAuthenticationService
{
    Task<Agent?> AuthenticateAsync(string email, string password, CancellationToken ct);
}
