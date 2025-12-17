using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using World.Server.Models;

namespace World.Server.Networking.Handlers;

public class ProvisionalConnectionHandler : IPacketHandler
{
    public PacketType Type => PacketType.ProvisionalConnection;
    private readonly ILogger<ProvisionalConnectionHandler> _logger;

    public ProvisionalConnectionHandler(ILogger<ProvisionalConnectionHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(ClientSession session, byte[] payload, CancellationToken ct)
    {
        session.IsProvisional = true;
        _logger.LogInformation("session_provisional client_id={id} set to provisional", session.ClientId);
        return Task.CompletedTask;
    }
}
