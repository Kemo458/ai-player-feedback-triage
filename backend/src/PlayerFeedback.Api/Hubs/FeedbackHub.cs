using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PlayerFeedback.Api.Hubs;

[Authorize]
public class FeedbackHub : Hub
{
    public const string OpsGroup = "ops";

    // Every manager connection joins the ops group to receive the live worker activity stream.
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, OpsGroup);
        await base.OnConnectedAsync();
    }

    public Task JoinGame(string gameId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, Group(gameId));

    public Task LeaveGame(string gameId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(gameId));

    public static string Group(string gameId) => $"game:{gameId}";
    public static string Group(Guid gameId) => $"game:{gameId}";
}

public interface IFeedbackNotifier
{
    Task FeedbackCompleted(Guid gameId, Guid feedbackId);
    Task ImportProgressChanged(Guid gameId, Guid importId);
    Task SummaryUpdated(Guid gameId, Guid summaryId);
}

public class SignalRFeedbackNotifier : IFeedbackNotifier
{
    private readonly IHubContext<FeedbackHub> _hub;
    public SignalRFeedbackNotifier(IHubContext<FeedbackHub> hub) => _hub = hub;

    public Task FeedbackCompleted(Guid gameId, Guid feedbackId) =>
        Send(gameId, new { eventType = "FeedbackCompleted", gameId, feedbackId });

    public Task ImportProgressChanged(Guid gameId, Guid importId) =>
        Send(gameId, new { eventType = "ImportProgressChanged", gameId, importId });

    public Task SummaryUpdated(Guid gameId, Guid summaryId) =>
        Send(gameId, new { eventType = "SummaryUpdated", gameId, summaryId });

    private Task Send(Guid gameId, object payload) =>
        _hub.Clients.Group(FeedbackHub.Group(gameId)).SendAsync("notify", payload);
}

/// <summary>Used when SignalR notifications are disabled; REST stays authoritative.</summary>
public class NoopNotifier : IFeedbackNotifier
{
    public Task FeedbackCompleted(Guid gameId, Guid feedbackId) => Task.CompletedTask;
    public Task ImportProgressChanged(Guid gameId, Guid importId) => Task.CompletedTask;
    public Task SummaryUpdated(Guid gameId, Guid summaryId) => Task.CompletedTask;
}
