namespace SupportDesk.Application.Contracts.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthenticatedAgentDto(int Id, string FullName, string Email);

public sealed record LoginResponse(string Token, DateTime ExpiresAtUtc, AuthenticatedAgentDto Agent);
public sealed record IssuedToken(string Token, DateTime ExpiresAtUtc);

public sealed class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "NorthwindSupport";
    public string Audience { get; set; } = "NorthwindSupportApp";
    public int ExpiryMinutes { get; set; } = 120;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key) || System.Text.Encoding.UTF8.GetByteCount(Key) < 32)
            throw new InvalidOperationException("Authentication:Jwt:Key must be configured with at least 32 UTF-8 bytes.");
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience) || ExpiryMinutes is < 1 or > 1440)
            throw new InvalidOperationException("JWT issuer and audience are required; expiry must be between 1 and 1440 minutes.");
    }
}
