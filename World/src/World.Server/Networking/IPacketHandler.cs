using System.Threading;
using System.Threading.Tasks;
using World.Server.Models;

namespace World.Server.Networking;

public interface IPacketHandler
{
    PacketType Type { get; }
    Task HandleAsync(ClientSession session, byte[] payload, CancellationToken ct);
}
