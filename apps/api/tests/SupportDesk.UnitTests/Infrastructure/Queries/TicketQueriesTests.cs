using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;
using Xunit.Abstractions;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

/// <summary>
/// Real relational queries. SQLite is the default; set SUPPORTDESK_TEST_SQLSERVER to run
/// the same cases on SQL Server in a unique temporary database, removed after each test.
/// </summary>
public sealed class TicketQueriesTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly SqlCapture _sql = new();
    private SqliteConnection? _sqlite;
    private SupportDbContext _db = null!;
    private TicketQueries _queries = null!;
    private bool _sqlServer;
    private int _contosoId;
    private int _billingId;
    private int _agentId;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<SupportDbContext>().AddInterceptors(_sql);
        var server = Environment.GetEnvironmentVariable("SUPPORTDESK_TEST_SQLSERVER");
        _sqlServer = !string.IsNullOrWhiteSpace(server);
        if (_sqlServer)
        {
            // Never create or delete the database named in the supplied connection string.
            var connection = new SqlConnectionStringBuilder(server)
            {
                InitialCatalog = $"SupportDeskFilteringTests_{Guid.NewGuid():N}"
            };
            options.UseSqlServer(connection.ConnectionString);
        }
        else
        {
            _sqlite = new SqliteConnection("Data Source=:memory:");
            await _sqlite.OpenAsync();
            options.UseSqlite(_sqlite);
        }

        _db = new SupportDbContext(options.Options);
        await _db.Database.EnsureCreatedAsync();
        var contoso = new Customer("Contoso", "hello@contoso.example", null, CustomerTier.Premium, FixedClock.DefaultNow);
        var fabrikam = new Customer("Fabrikam", "hello@fabrikam.example", null, CustomerTier.Standard, FixedClock.DefaultNow);
        var billing = new Category("Billing", true, false);
        var general = new Category("General", false, false);
        var agent = new Agent("Test agent", "agent@example.test", 10, FixedClock.DefaultNow);
        _db.AddRange(contoso, fabrikam, billing, general, agent);
        await _db.SaveChangesAsync();
        _contosoId = contoso.Id;
        _billingId = billing.Id;
        _agentId = agent.Id;

        AddTicket("TCK-0001", "Invoice problem", contoso.Id, billing.Id, TicketPriority.High, TicketStatus.Open, agent.Id);
        AddTicket("TCK-0002", "Portal problem", contoso.Id, general.Id, TicketPriority.Low, TicketStatus.New, null);
        AddTicket("TCK-0003", "Invoice export", fabrikam.Id, billing.Id, TicketPriority.High, TicketStatus.Open, null);
        AddTicket("TCK-0004", "Literal 100%_ issue", fabrikam.Id, general.Id, TicketPriority.High, TicketStatus.Resolved, agent.Id);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _sql.Commands.Clear();
        _queries = new TicketQueries(_db, new FixedClock());
    }

    private void AddTicket(string reference, string title, int customerId, int categoryId,
        TicketPriority priority, TicketStatus status, int? agentId)
    {
        var ticket = Ticket.Raise(reference, title, "Test description", customerId, categoryId, priority, FixedClock.DefaultNow);
        ticket.ChangeStatus(status, FixedClock.DefaultNow);
        ticket.AssignTo(agentId, FixedClock.DefaultNow);
        _db.Add(ticket);
    }

    [Fact]
    public async Task Priority_filters_before_count_and_paging_with_stable_sort()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery
        {
            Priority = TicketPriority.High,
            PageSize = 2,
            Page = 2
        }, default);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal("TCK-0001", Assert.Single(result.Items).Reference);
        Assert.All(result.Items, item => Assert.Equal(TicketPriority.High, item.Priority));
        Assert.Empty(_db.ChangeTracker.Entries());

        Assert.Equal(2, _sql.Commands.Count);
        var count = _sql.Commands[0];
        var page = _sql.Commands[1];
        output.WriteLine(count);
        output.WriteLine(page);
        Assert.Contains("COUNT", count);
        Assert.Contains("WHERE", count);
        Assert.Contains("Priority", count);
        Assert.DoesNotContain("OFFSET", count);
        Assert.Contains("WHERE", page);
        Assert.Contains("Priority", page);
        Assert.True(page.IndexOf("WHERE", StringComparison.Ordinal) < page.IndexOf("ORDER BY", StringComparison.Ordinal));
        Assert.Contains("OFFSET", page);
    }

    [Fact]
    public async Task All_filters_combine_with_and()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery
        {
            Status = TicketStatus.Open,
            Priority = TicketPriority.High,
            CategoryId = _billingId,
            CustomerId = _contosoId,
            AssignedAgentId = _agentId,
            Search = " Invoice "
        }, default);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0001", Assert.Single(result.Items).Reference);
    }

    [Theory]
    [InlineData("status", "TCK-0002")]
    [InlineData("customer", "TCK-0002,TCK-0001")]
    [InlineData("category", "TCK-0003,TCK-0001")]
    [InlineData("agent", "TCK-0004,TCK-0001")]
    public async Task Exact_match_filters_work_independently(string filter, string expected)
    {
        var query = filter switch
        {
            "status" => new TicketQuery { Status = TicketStatus.New },
            "customer" => new TicketQuery { CustomerId = _contosoId },
            "category" => new TicketQuery { CategoryId = _billingId },
            _ => new TicketQuery { AssignedAgentId = _agentId }
        };
        var result = await _queries.GetPagedAsync(query, default);
        Assert.Equal(expected.Split(','), result.Items.Select(item => item.Reference));
        Assert.Equal(result.Items.Count, result.TotalCount);
    }

    [Theory]
    [InlineData(" Contoso ", "TCK-0002,TCK-0001")]
    [InlineData("Invoice", "TCK-0003,TCK-0001")]
    [InlineData("TCK-0003", "TCK-0003")]
    [InlineData("%_", "TCK-0004")]
    public async Task Search_matches_customer_title_or_reference_as_literal_substring(string search, string expected)
    {
        var result = await _queries.GetPagedAsync(new TicketQuery { Search = search }, default);
        Assert.Equal(expected.Split(','), result.Items.Select(item => item.Reference));
        Assert.Equal(result.Items.Count, result.TotalCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_search_is_ignored(string? search)
    {
        var result = await _queries.GetPagedAsync(new TicketQuery { Search = search }, default);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(4, result.Items.Count);
    }

    [Fact]
    public async Task Unassigned_only_excludes_assigned_tickets()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery { UnassignedOnly = true }, default);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Null(item.AssignedAgent));
    }

    [Fact]
    public async Task Contradictory_assignment_filters_return_empty_set()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery
        {
            UnassignedOnly = true,
            AssignedAgentId = _agentId
        }, default);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task Page_beyond_filtered_results_keeps_filtered_count()
    {
        var result = await _queries.GetPagedAsync(new TicketQuery
        {
            Priority = TicketPriority.Low,
            Page = 2,
            PageSize = 1
        }, default);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            if (_sqlServer)
            {
                await _db.Database.EnsureDeletedAsync();
            }

            await _db.DisposeAsync();
        }

        if (_sqlite is not null)
        {
            await _sqlite.DisposeAsync();
        }
    }

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
