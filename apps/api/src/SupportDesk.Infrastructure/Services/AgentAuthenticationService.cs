using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Infrastructure.Services;

public sealed class AgentAuthenticationService(
    IAgentRepository agents,
    IAgentPasswordService passwordService) : IAgentAuthenticationService
{
    public async Task<Agent?> AuthenticateAsync(string email, string password, CancellationToken ct)
    {
        var trimmedEmail = email?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedEmail) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var agent = await agents.GetByEmailAsync(trimmedEmail, ct);
        if (agent is null || !agent.IsActive)
        {
            return null;
        }

        return passwordService.Verify(agent.PasswordHash, password)
            ? agent
            : null;
    }
}
