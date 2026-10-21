using System.IdentityModel.Tokens.Jwt;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Services;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Services;

public class AuthenticationTests
{
    [Fact]
    public void Password_hashes_are_salted_and_verify_both_success_and_failure()
    {
        var passwords = new AgentPasswordService();
        var first = passwords.Hash("correct password");
        var second = passwords.Hash("correct password");
        Assert.NotEqual(first, second);
        Assert.NotEqual("correct password", first);
        Assert.True(passwords.Verify(first, "correct password"));
        Assert.False(passwords.Verify(first, "incorrect password"));
        Assert.False(passwords.Verify("", "correct password"));
    }

    [Fact]
    public void Token_and_response_share_expiry_from_injected_clock()
    {
        var clock = new FixedClock(FixedClock.DefaultNow.AddMilliseconds(123));
        var settings = new JwtSettings { Key = new string('k', 32), ExpiryMinutes = 120 };
        var agent = new Agent("Alex", "alex@example.test", 8, clock.UtcNow);
        var result = new JwtTokenService(settings, clock).CreateToken(agent);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Equal(FixedClock.DefaultNow.AddHours(2), result.ExpiresAtUtc);
        Assert.Equal(result.ExpiresAtUtc, token.ValidTo);
        Assert.Equal(settings.Issuer, token.Issuer);
        Assert.Equal(settings.Audience, Assert.Single(token.Audiences));
        Assert.Equal("HS256", token.Header.Alg);
        Assert.Contains(token.Claims, c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == agent.Id.ToString());
    }

    [Fact]
    public void Missing_or_invalid_jwt_configuration_fails_fast()
    {
        Assert.Throws<InvalidOperationException>(() => new JwtSettings().Validate());
        Assert.Throws<InvalidOperationException>(() => new JwtSettings { Key = "short" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new JwtSettings { Key = new string('k', 32), ExpiryMinutes = 0 }.Validate());
        new JwtSettings { Key = new string('k', 32) }.Validate();
    }

    [Fact]
    public async Task Development_seed_backfills_existing_agents_without_resetting_passwords_or_tickets()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var passwords = new AgentPasswordService();
        var seeder = new SupportDbSeeder(db, new FixedClock(), NullLogger<SupportDbSeeder>.Instance, passwords);
        await seeder.SeedAsync();
        var agents = await db.Set<Agent>().OrderBy(a => a.Id).ToListAsync();
        var existingHash = passwords.Hash("Already changed password");
        agents[0].SetPasswordHash(existingHash);
        db.Entry(agents[1]).Property(a => a.PasswordHash).CurrentValue = ""; // Simulate upgraded Task 2 row.
        var other = new Agent("Other user", "other@example.test", 8, FixedClock.DefaultNow);
        db.Add(other);
        await db.SaveChangesAsync();
        var ticketCount = await db.Set<SupportDesk.Domain.Aggregates.Tickets.Ticket>().CountAsync();
        await seeder.SeedAsync();
        Assert.Equal(existingHash, agents[0].PasswordHash);
        Assert.True(passwords.Verify(agents[1].PasswordHash, SupportDbSeeder.DevelopmentPassword));
        Assert.Empty(other.PasswordHash); // Only known local fixtures are backfilled.
        Assert.False(agents.Single(a => a.Email.StartsWith("yuki.")).IsActive);
        var backfilledHash = agents[1].PasswordHash;
        await seeder.SeedAsync();
        Assert.Equal(backfilledHash, agents[1].PasswordHash);
        Assert.Equal(ticketCount, await db.Set<SupportDesk.Domain.Aggregates.Tickets.Ticket>().CountAsync());
    }
}
