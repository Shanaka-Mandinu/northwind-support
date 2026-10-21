using Moq;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.UnitTests.Infrastructure.Services;

public class AgentAuthenticationServiceTests
{
    [Theory]
    [InlineData(null, "password")]
    [InlineData("", "password")]
    [InlineData("  ", "password")]
    [InlineData("agent@example.com", null)]
    [InlineData("agent@example.com", "")]
    public async Task AuthenticateAsync_RejectsMissingCredentials(string? email, string? password)
    {
        var agents = new Mock<IAgentRepository>();
        var passwordService = new Mock<IAgentPasswordService>();
        var sut = new SupportDesk.Infrastructure.Services.AgentAuthenticationService(agents.Object, passwordService.Object);

        var result = await sut.AuthenticateAsync(email ?? string.Empty, password ?? string.Empty, CancellationToken.None);

        Assert.Null(result);
        agents.Verify(a => a.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        passwordService.Verify(p => p.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsNull_WhenAgentIsInactive()
    {
        var agent = new Agent("Alex Turner", "alex@example.com", 2, DateTime.UtcNow);
        agent.SetPasswordHash("hash");
        agent.Deactivate();

        var agents = new Mock<IAgentRepository>();
        agents.Setup(a => a.GetByEmailAsync("alex@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(agent);

        var passwordService = new Mock<IAgentPasswordService>();
        passwordService.Setup(p => p.Verify("hash", "correct-password")).Returns(true);

        var sut = new SupportDesk.Infrastructure.Services.AgentAuthenticationService(agents.Object, passwordService.Object);

        var result = await sut.AuthenticateAsync("alex@example.com", "correct-password", CancellationToken.None);

        Assert.Null(result);
        passwordService.Verify(p => p.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsAgent_WhenPasswordMatches()
    {
        var agent = new Agent("Alex Turner", "alex@example.com", 2, DateTime.UtcNow);
        agent.SetPasswordHash("hash");

        var agents = new Mock<IAgentRepository>();
        agents.Setup(a => a.GetByEmailAsync("alex@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(agent);

        var passwordService = new Mock<IAgentPasswordService>();
        passwordService.Setup(p => p.Verify("hash", "correct-password")).Returns(true);

        var sut = new SupportDesk.Infrastructure.Services.AgentAuthenticationService(agents.Object, passwordService.Object);

        var result = await sut.AuthenticateAsync("alex@example.com", "correct-password", CancellationToken.None);

        Assert.Same(agent, result);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsNull_WhenPasswordDoesNotMatch()
    {
        var agent = new Agent("Alex Turner", "alex@example.com", 2, DateTime.UtcNow);
        agent.SetPasswordHash("hash");

        var agents = new Mock<IAgentRepository>();
        agents.Setup(a => a.GetByEmailAsync("alex@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(agent);

        var passwordService = new Mock<IAgentPasswordService>();
        passwordService.Setup(p => p.Verify("hash", "wrong-password")).Returns(false);

        var sut = new SupportDesk.Infrastructure.Services.AgentAuthenticationService(agents.Object, passwordService.Object);

        var result = await sut.AuthenticateAsync("alex@example.com", "wrong-password", CancellationToken.None);

        Assert.Null(result);
    }
}
