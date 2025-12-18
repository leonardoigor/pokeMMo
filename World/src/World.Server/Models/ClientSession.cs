using System.Net.Sockets;
using System.Collections.Generic;

namespace World.Server.Models;

public class ClientSession
{
    public TcpClient Client { get; }
    public int ClientId { get; set; }
    public string Username { get; set; } = "";
    public int GhostZoneWidth { get; set; } = 1;
    public HashSet<string> ActiveGhosts { get; set; } = new();
    public long LastMoveAtMs { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public (int cx, int cy) CurrentChunk { get; set; }
    public bool IsProvisional { get; set; }
    public int? SafeX { get; set; }
    public int? SafeY { get; set; }
    public long LastArrivalAtMs { get; set; }
    public bool IsMoving { get; set; }

    public ClientSession(TcpClient client)
    {
        Client = client;
    }
}
