using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Infrastructure.Queries;

internal sealed class TicketSlaRow
{
    public Ticket Ticket { get; init; } = null!;
    public SlaStatus Status { get; init; }
}

/// <summary>SQL projection shared by list, detail, customer tickets, filtering and sorting.</summary>
internal static class TicketSlaQuery
{
    public static IQueryable<TicketSlaRow> Project(IQueryable<Ticket> source, DateTime now, double threshold, bool sqlServer)
    {
        // DATEDIFF(day) + time-of-day avoids DATEDIFF(millisecond)'s 24-day overflow.
        var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var nowMs = (now - epoch).TotalMilliseconds;
        var timings = sqlServer
            ? source.Select(t => new Timing
            {
                Ticket = t,
                Due = EF.Functions.DateDiffDay(epoch, t.DueAtUtc!.Value) * 86400000d
                    + t.DueAtUtc.Value.Hour * 3600000d + t.DueAtUtc.Value.Minute * 60000d
                    + t.DueAtUtc.Value.Second * 1000d + t.DueAtUtc.Value.Millisecond,
                Start = EF.Functions.DateDiffDay(epoch, t.SlaStartedAtUtc ?? t.CreatedAtUtc) * 86400000d
                    + (t.SlaStartedAtUtc ?? t.CreatedAtUtc).Hour * 3600000d
                    + (t.SlaStartedAtUtc ?? t.CreatedAtUtc).Minute * 60000d
                    + (t.SlaStartedAtUtc ?? t.CreatedAtUtc).Second * 1000d
                    + (t.SlaStartedAtUtc ?? t.CreatedAtUtc).Millisecond
            })
            : source.Select(t => new Timing
            {
                Ticket = t,
                Due = Math.Round((t.DueAtUtc!.Value.Ticks - epoch.Ticks) / (double)TimeSpan.TicksPerMillisecond),
                Start = Math.Round(((t.SlaStartedAtUtc ?? t.CreatedAtUtc).Ticks - epoch.Ticks) / (double)TimeSpan.TicksPerMillisecond)
            });

        return timings.Select(x => new TicketSlaRow
        {
            Ticket = x.Ticket,
            Status = x.Ticket.DueAtUtc == null ? SlaStatus.NotApplicable
                : x.Ticket.ResolvedAtUtc != null
                    ? x.Ticket.ResolvedAtUtc <= x.Ticket.DueAtUtc ? SlaStatus.Met : SlaStatus.Breached
                : now > x.Ticket.DueAtUtc ? SlaStatus.Breached
                : x.Due - nowMs <= (x.Due - x.Start) * threshold / 100 ? SlaStatus.AtRisk
                : SlaStatus.WithinSla
        });
    }

    private sealed class Timing
    {
        public Ticket Ticket { get; init; } = null!;
        public double Due { get; init; }
        public double Start { get; init; }
    }
}
