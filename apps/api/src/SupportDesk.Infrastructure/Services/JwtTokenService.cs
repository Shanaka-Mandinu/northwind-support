using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Infrastructure.Services;

public sealed class JwtTokenService(JwtSettings settings, IClock clock) : IJwtTokenService
{
    public IssuedToken CreateToken(Agent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        // JWT timestamps have whole-second precision. Return that same expiry to the client.
        var now = DateTimeOffset.FromUnixTimeSeconds(new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds()).UtcDateTime;
        var expiresAtUtc = now.AddMinutes(settings.ExpiryMinutes);

        var key = Encoding.UTF8.GetBytes(settings.Key);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, agent.Id.ToString()),
            new Claim(ClaimTypes.Name, agent.FullName),
            new Claim(ClaimTypes.Email, agent.Email),
        };

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
