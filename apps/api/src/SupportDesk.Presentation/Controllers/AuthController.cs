using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IAgentAuthenticationService authentication,
    IJwtTokenService jwt,
    IAgentRepository agents) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var agent = await authentication.AuthenticateAsync(request.Email, request.Password, ct);
        if (agent is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Authentication failed",
                Detail = "Invalid email or password.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        var token = jwt.CreateToken(agent);

        return Ok(new LoginResponse(
            token.Token,
            token.ExpiresAtUtc,
            new AuthenticatedAgentDto(agent.Id, agent.FullName, agent.Email)));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthenticatedAgentDto>> Me(CancellationToken ct)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!int.TryParse(userIdText, out var userId))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Authentication required.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        var agent = await agents.GetByIdAsync(userId, ct);
        if (agent is null || !agent.IsActive)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Authentication required.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        return Ok(new AuthenticatedAgentDto(agent.Id, agent.FullName, agent.Email));
    }
}
