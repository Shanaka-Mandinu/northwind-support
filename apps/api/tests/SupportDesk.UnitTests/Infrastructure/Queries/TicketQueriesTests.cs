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
        _queries = new TicketQueries(_db, new FixedClock(), new SlaSettings());
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

    [Fact]
    public async Task Sla_states_filter_count_sort_and_project_in_sql_at_exact_boundaries()
    {
        var now = FixedClock.DefaultNow;
        var samples = new[]
        {
            ("SLA-WITHIN", now.AddHours(1).AddMilliseconds(1), (DateTime?)null),
            ("SLA-RISK", now.AddHours(1), (DateTime?)null),
            ("SLA-DUE", now, (DateTime?)null),
            ("SLA-BREACH", now.AddMilliseconds(-1), (DateTime?)null),
            ("SLA-MET", now, (DateTime?)now),
            ("SLA-LATE", now, (DateTime?)now.AddMilliseconds(1))
        };
        foreach (var (reference, due, resolved) in samples)
        {
            var ticket = Ticket.Raise(reference, "SLA test", "Description", _contosoId, _billingId,
                TicketPriority.Medium, now.AddHours(-3));
            ticket.ApplyTriage(due, null);
            if (resolved is not null) ticket.ChangeStatus(TicketStatus.Resolved, resolved.Value);
            _db.Add(ticket);
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var all = await _db.Set<Ticket>().AsNoTracking().ToListAsync();
        foreach (var state in Enum.GetValues<SlaStatus>())
        {
            var expected = all.Where(t => SlaEvaluator.Evaluate(t.DueAtUtc, t.ResolvedAtUtc, now,
                t.SlaStartedAtUtc ?? t.CreatedAtUtc) == state).Select(t => t.Id).ToList();
            _sql.Commands.Clear();
            var result = await _queries.GetPagedAsync(new TicketQuery { SlaStatus = state, PageSize = 1, SortBy = "slaStatus" }, default);
            Assert.Equal(expected.Count, result.TotalCount);
            Assert.Equal(expected.Count, result.TotalPages);
            Assert.All(result.Items, item => { Assert.Equal(state, item.SlaStatus); Assert.Contains(item.Id, expected); });
            Assert.Contains("CASE", _sql.Commands[0]);
            Assert.Contains("WHERE", _sql.Commands[0]);
            Assert.Contains("CASE", _sql.Commands[1]);
        }
        var detail = await _queries.GetDetailAsync(all.Single(t => t.Reference == "SLA-RISK").Id, default);
        Assert.Equal(SlaStatus.AtRisk, detail!.SlaStatus);
        var customer = await _queries.GetForCustomerAsync(_contosoId, default);
        Assert.Equal(SlaStatus.AtRisk, customer.Single(t => t.Reference == "SLA-RISK").SlaStatus);
        var combined = await _queries.GetPagedAsync(new TicketQuery { SlaStatus = SlaStatus.AtRisk, Search = "SLA-RISK" }, default);
        Assert.Equal(1, combined.TotalCount);
    }

    [Fact]
    public async Task Escalation_history_is_persisted_newest_first_and_cannot_be_changed()
    {
        var ticket = await _db.Set<Ticket>().SingleAsync(t => t.Reference == "TCK-0002");
        var store = new SupportDesk.Infrastructure.Repositories.EscalationStore(_db);
        var now = FixedClock.DefaultNow;
        var first = ticket.Escalate(now.AddHours(12), _agentId, "First escalation", "Alex", now);
        store.Add(first);
        await _db.SaveChangesAsync();
        var second = ticket.Escalate(now.AddHours(5), null, "Second escalation", "Sara", now.AddHours(1));
        store.Add(second);
        await _db.SaveChangesAsync();
        var history = await store.GetHistoryAsync(ticket.Id, default);
        Assert.Equal(new[] { second.Id, first.Id }, history.Select(e => e.Id));
        Assert.Equal(_agentId, history[0].FromAgent!.Id);
        Assert.Null(history[0].ToAgent);
        Assert.Equal(TicketPriority.Low, history[1].FromPriority);
        _db.Entry(first).Property(e => e.Reason).CurrentValue = "Tampered history";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task Assignment_query_counts_only_open_work_and_specializations()
    {
        var agent = await _db.Set<Agent>().SingleAsync(a => a.Id == _agentId);
        agent.AddSpecialization(_billingId);
        await _db.SaveChangesAsync();
        var candidates = await new TriageQueries(_db).GetCandidatesAsync(_billingId, default);
        var candidate = Assert.Single(candidates);
        Assert.Equal(1, candidate.OpenTickets); // The other assigned ticket is resolved.
        Assert.True(candidate.IsSpecialist);
        Assert.True(candidate.IsActive);
    }

    [Theory]
    [InlineData(25, SlaStatus.WithinSla)]
    [InlineData(75, SlaStatus.AtRisk)]
    public async Task Escalated_sla_uses_new_window_and_configured_threshold(double threshold, SlaStatus expected)
    {
        var now = FixedClock.DefaultNow;
        var ticket = Ticket.Raise("NEW-WINDOW", "Window test", "Description", _contosoId, _billingId,
            TicketPriority.Low, now.AddDays(-4));
        ticket.ApplyTriage(now.AddDays(-1), null);
        _db.Add(ticket);
        await _db.SaveChangesAsync();
        var entry = ticket.Escalate(now.AddHours(3), null, "Start a new window", "Alex", now.AddHours(-1));
        _db.Add(entry);
        await _db.SaveChangesAsync();
        var queries = new TicketQueries(_db, new FixedClock(), new SlaSettings { AtRiskThresholdPercent = threshold });
        var detail = await queries.GetDetailAsync(ticket.Id, default);
        Assert.Equal(expected, detail!.SlaStatus);
        var list = await queries.GetPagedAsync(new TicketQuery { Search = "NEW-WINDOW", SlaStatus = expected }, default);
        Assert.Equal(ticket.Id, Assert.Single(list.Items).Id);
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
