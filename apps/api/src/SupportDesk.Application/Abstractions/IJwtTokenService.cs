using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Application.Contracts.Auth;

namespace SupportDesk.Application.Abstractions;

public interface IJwtTokenService
{
    IssuedToken CreateToken(Agent agent);
}
