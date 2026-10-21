namespace SupportDesk.Application.Abstractions;

public interface IAgentPasswordService
{
    string Hash(string password);
    bool Verify(string passwordHash, string password);
}
