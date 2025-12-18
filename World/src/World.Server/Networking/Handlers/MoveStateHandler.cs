using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using World.Server.Models;
using World.Server.Services;

namespace World.Server.Networking.Handlers;

public class MoveStateHandler : IPacketHandler
{
    public PacketType Type => PacketType.MoveState;
    private readonly WorldManager _world;
    private readonly ILogger<MoveStateHandler> _logger;

    public MoveStateHandler(WorldManager world, ILogger<MoveStateHandler> logger)
    {
        _world = world;
        _logger = logger;
    }

    public Task HandleAsync(ClientSession session, byte[] payload, CancellationToken ct)
    {
        // Payload: [IsMoving (1 byte)]
        if (payload.Length >= 1)
        {
            var isMoving = payload[0] != 0;
            if (session.IsMoving != isMoving)
            {
                session.IsMoving = isMoving;
                // Broadcast immediately so other clients see the change
                _world.BroadcastPlayersSnapshot();
            }
        }
        return Task.CompletedTask;
    }
}
