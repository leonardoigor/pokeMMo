using System.Collections.Generic;

namespace World.Server.Models;

public class MapData
{
    public int Width { get; init; }
    public int Height { get; init; }
    public Dictionary<(int x, int y), string> Tiles { get; init; } = new();
    public Dictionary<(int x, int y), string> Objects { get; init; } = new();
    public Dictionary<(int x, int y), TeleportData> Teleports { get; init; } = new();
    public Dictionary<string, TileDefJson> TileDefs { get; init; } = new();
    public Dictionary<string, ObjectDefJson> ObjectDefs { get; init; } = new();
}
