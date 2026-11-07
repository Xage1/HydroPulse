using Microsoft.AspNetCore.SignalR;

namespace HydroPulse.Api.Hubs;

public class TelemetryHub : Hub
{
    public async Task JoinPlantGroup(string plantId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, plantId);
        await Clients.Caller.SendAsync("JoinedGroup", $"Connected to telemetry stream for plant {plantId}");
    }

    public async Task LeavePlantGroup(string plantId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, plantId);
    }
}