using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using PlayerFeedback.Api.Hubs;

namespace PlayerFeedback.Api.Activity;

public record ActivityEvent(DateTime Timestamp, string Level, string? GameId, string Message);

/// <summary>In-memory ring buffer of recent worker activity, broadcast live to managers.</summary>
public interface IActivityLog
{
    void Emit(string level, string message, Guid? gameId = null);
    IReadOnlyList<ActivityEvent> Snapshot(int limit = 200);
}

public class ActivityLog : IActivityLog
{
    private const int Max = 500;
    private readonly ConcurrentQueue<ActivityEvent> _events = new();
    private readonly IHubContext<FeedbackHub> _hub;
    private readonly ILogger<ActivityLog> _logger;

    public ActivityLog(IHubContext<FeedbackHub> hub, ILogger<ActivityLog> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public void Emit(string level, string message, Guid? gameId = null)
    {
        var e = new ActivityEvent(DateTime.UtcNow, level, gameId?.ToString(), message);
        _events.Enqueue(e);
        while (_events.Count > Max && _events.TryDequeue(out _)) { }

        // Fire-and-forget broadcast; never let a SignalR hiccup affect worker progress.
        _hub.Clients.Group(FeedbackHub.OpsGroup).SendAsync("activity", e)
            .ContinueWith(t => { if (t.Exception != null) _logger.LogDebug(t.Exception, "activity broadcast failed"); },
                TaskScheduler.Default);
    }

    public IReadOnlyList<ActivityEvent> Snapshot(int limit = 200)
    {
        var arr = _events.ToArray();
        return arr.Length <= limit ? arr : arr[^limit..];
    }
}

public static class ShortId
{
    public static string Of(Guid id) => id.ToString()[..8];
}
