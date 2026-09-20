using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using Serilog;

internal static class ForensicIntegrity
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ForensicIntegrity));

    public sealed class EventClear
    {
        public string Label { get; init; } = "";
        public string Channel { get; init; } = "";
        public int EventId { get; init; }
        public string When { get; init; } = "";
    }

    public static List<EventClear> DetectEventLogClears(DateTime bootLocal)
    {
        var hits = new List<EventClear>(4);
        TryAdd(hits, "Security log (1102)", "Security", 1102, bootLocal);
        TryAdd(hits, "System log (104)", "System", 104, bootLocal);
        TryAdd(hits, "Application log (104)", "Application", 104, bootLocal);
        TryAdd(hits, "Setup log (104)", "Setup", 104, bootLocal);
        return hits;
    }

    private static void TryAdd(
        List<EventClear> hits, string label, string logName, int eventId, DateTime bootLocal)
    {
        DateTime? when = GetLatestEventTime(logName, eventId, "Microsoft-Windows-Eventlog", bootLocal)
                         ?? GetLatestEventTime(logName, eventId, "EventLog", bootLocal);
        if (!when.HasValue)
            return;
        hits.Add(new EventClear
        {
            Label = label,
            Channel = logName,
            EventId = eventId,
            When = when.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        });
    }

    public static DateTime? GetLatestEventTime(
        string logName, int eventId, string? provider, DateTime? after)
    {
        try
        {
            string filter = BuildEventFilter(eventId, after);
            var q = new EventLogQuery(logName, PathType.LogName, filter) { ReverseDirection = true };
            using var reader = new EventLogReader(q);
            return ReadMatchingEvent(reader, provider, after, maxRead: 40);
        }
        catch (EventLogNotFoundException)
        {
            return null;
        }
        catch (EventLogException)
        {
            return TryEventFallback(logName, eventId, provider, after);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Event query failed {Log} {Id}", logName, eventId);
            return TryEventFallback(logName, eventId, provider, after);
        }
    }

    private static DateTime? TryEventFallback(
        string logName, int eventId, string? provider, DateTime? after)
    {
        try
        {
            string filter = "*[System[(EventID=" + eventId + ")]]";
            var q = new EventLogQuery(logName, PathType.LogName, filter) { ReverseDirection = true };
            using var reader = new EventLogReader(q);
            return ReadMatchingEvent(reader, provider, after, maxRead: 40);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? ReadMatchingEvent(
        EventLogReader reader, string? provider, DateTime? after, int maxRead)
    {
        for (int i = 0; i < maxRead; i++)
        {
            using EventRecord? evt = reader.ReadEvent();
            if (evt is null)
                return null;

            if (after.HasValue && evt.TimeCreated.HasValue && evt.TimeCreated.Value < after.Value)
                return null;

            if (!string.IsNullOrEmpty(provider) &&
                !string.Equals(evt.ProviderName, provider, StringComparison.OrdinalIgnoreCase))
                continue;

            return evt.TimeCreated;
        }

        return null;
    }

    private static string BuildEventFilter(int eventId, DateTime? after)
    {
        if (!after.HasValue)
            return "*[System[(EventID=" + eventId + ")]]";

        string iso = after.Value.ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        return "*[System[(EventID=" + eventId + ") and TimeCreated[@SystemTime>='" + iso + "']]]";
    }
}