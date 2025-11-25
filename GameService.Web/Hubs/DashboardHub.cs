using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GameService.Web.Hubs;

[Authorize]
public class DashboardHub : Hub
{
    // Clients connect here. We can add specific groups if needed later.
}