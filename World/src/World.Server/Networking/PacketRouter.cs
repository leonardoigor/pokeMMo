using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using World.Server.Models;

namespace World.Server.Networking;

public class PacketRouter
{
    private readonly Dictionary<PacketType, IPacketHandler> _handlers;

    public PacketRouter(IEnumerable<IPacketHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Type);
    }

    public async Task RouteAsync(ClientSession session, PacketType type, byte[] payload, CancellationToken ct)
    {
        if (_handlers.TryGetValue(type, out var handler))
        {
            await handler.HandleAsync(session, payload, ct);
        }
    }
}
