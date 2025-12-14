using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using Observability.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<MapStore>();
builder.Services.AddSingleton<MapLoader>();
builder.Services.AddSingleton<MovementValidator>();
builder.Services.AddSingleton<ChunkManager>();
builder.Services.AddSingleton<RegionConfig>();
builder.Services.AddHostedService<SocketServer>();
builder.Services.AddObservability(builder.Configuration, "world");
var app = builder.Build();
app.UseRequestResponseLogging();
var store = app.Services.GetRequiredService<MapStore>();
var loader = app.Services.GetRequiredService<MapLoader>();
var chunks = app.Services.GetRequiredService<ChunkManager>();
var regionCfg = app.Services.GetRequiredService<RegionConfig>();
var dataDir = Environment.GetEnvironmentVariable("MAP_DATA_DIR");
if (string.IsNullOrWhiteSpace(dataDir))
{
    dataDir = Path.Combine("d:\\Dev\\pokeMMo\\World\\data");
}
app.MapGet("/healthz", () => Results.Ok("ok"));
if (Directory.Exists(dataDir))
{
    foreach (var dir in Directory.GetDirectories(dataDir))
    {
        var mapPath = Path.Combine(dir, "map.json");
        var tileDefsPath = Path.Combine(dir, "tile_definitions.json");
        var objDefsPath = Path.Combine(dir, "object_definitions.json");
        if (File.Exists(mapPath) && File.Exists(tileDefsPath) && File.Exists(objDefsPath))
        {
            var mapName = new DirectoryInfo(dir).Name;
            var data = loader.Load(mapPath, tileDefsPath, objDefsPath);
            if (data != null) store.AddMap(mapName, data);
        }
    }
}
app.MapGet("/world/regions", () =>
{
    return store.ListRegions();
});
app.MapGet("/world/chunks", (string? region, int? size) =>
{
    var name = region ?? store.Maps.Keys.FirstOrDefault();
    if (string.IsNullOrEmpty(name)) return Results.NotFound();
    if (!store.Maps.TryGetValue(name, out var map)) return Results.NotFound();
    var s = size.HasValue && size.Value > 0 ? size.Value : chunks.ChunkSize;
    var info = chunks.DescribeGrid(map, s);
    return Results.Ok(info);
});
app.MapGet("/world/region/bounds", () =>
{
    return new
    {
        minX = regionCfg.MinX,
        maxX = regionCfg.MaxX,
        minY = regionCfg.MinY,
        maxY = regionCfg.MaxY,
        neighbors = new
        {
            east = regionCfg.NeighborEast,
            west = regionCfg.NeighborWest,
            north = regionCfg.NeighborNorth,
            south = regionCfg.NeighborSouth
        }
    };
});
app.Run();

public enum PacketType : byte
{
    MoveRequest = 0,
    PositionUpdate = 1,
    Handoff = 2,
    ClientConfig = 3,
    GhostZoneEnter = 4,
    GhostZoneLeave = 5
}

public record TileCell(int x, int y, string tileId);
public record ObjectCell(int x, int y, string objectId);
public record MapJson(int width, int height, TileCell[] tiles, ObjectCell[] objects);
public record TileDefJson(string tileId, string nome, bool isWalkable, bool blocksVision);
public record ObjectDefJson(string objectId, string nome, bool blocksMovement, bool interactable);

public class MapData
{
    public int Width { get; init; }
    public int Height { get; init; }
    public Dictionary<(int x, int y), string> Tiles { get; init; } = new();
    public Dictionary<(int x, int y), string> Objects { get; init; } = new();
    public Dictionary<string, TileDefJson> TileDefs { get; init; } = new();
    public Dictionary<string, ObjectDefJson> ObjectDefs { get; init; } = new();
}

public class MapLoader
{
    public MapData? Load(string mapPath, string tileDefsPath, string objDefsPath)
    {
        var map = JsonSerializer.Deserialize<MapJson>(File.ReadAllText(mapPath));
        var tileDefs = JsonSerializer.Deserialize<TileDefJson[]>(File.ReadAllText(tileDefsPath));
        var objDefs = JsonSerializer.Deserialize<ObjectDefJson[]>(File.ReadAllText(objDefsPath));
        if (map == null || tileDefs == null || objDefs == null) return null;
        var data = new MapData { Width = map.width, Height = map.height };
        foreach (var t in map.tiles) data.Tiles[(t.x, t.y)] = t.tileId;
        foreach (var o in map.objects) data.Objects[(o.x, o.y)] = o.objectId;
        foreach (var td in tileDefs) data.TileDefs[td.tileId] = td;
        foreach (var od in objDefs) data.ObjectDefs[od.objectId] = od;
        return data;
    }
}

public class RegionConfig
{
    public int MinX { get; }
    public int MaxX { get; }
    public int MinY { get; }
    public int MaxY { get; }
    public string? NeighborEast { get; }
    public string? NeighborWest { get; }
    public string? NeighborNorth { get; }
    public string? NeighborSouth { get; }
    public RegionConfig()
    {
        MinX = ReadIntEnv("REGION_MIN_X", int.MinValue);
        MaxX = ReadIntEnv("REGION_MAX_X", int.MaxValue);
        MinY = ReadIntEnv("REGION_MIN_Y", int.MinValue);
        MaxY = ReadIntEnv("REGION_MAX_Y", int.MaxValue);
        NeighborEast = Environment.GetEnvironmentVariable("NEIGHBOR_EAST");
        NeighborWest = Environment.GetEnvironmentVariable("NEIGHBOR_WEST");
        NeighborNorth = Environment.GetEnvironmentVariable("NEIGHBOR_NORTH");
        NeighborSouth = Environment.GetEnvironmentVariable("NEIGHBOR_SOUTH");
    }
    public (string host, int port)? ResolveNeighborFor(int x, int y)
    {
        if (x > MaxX && !string.IsNullOrWhiteSpace(NeighborEast))
            return ParseHostPort(NeighborEast);
        if (x < MinX && !string.IsNullOrWhiteSpace(NeighborWest))
            return ParseHostPort(NeighborWest);
        if (y > MaxY && !string.IsNullOrWhiteSpace(NeighborNorth))
            return ParseHostPort(NeighborNorth);
        if (y < MinY && !string.IsNullOrWhiteSpace(NeighborSouth))
            return ParseHostPort(NeighborSouth);
        return null;
    }
    static (string host, int port)? ParseHostPort(string s)
    {
        var parts = s.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[1], out var p))
            return (parts[0], p);
        return null;
    }
    static int ReadIntEnv(string name, int def)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(v, out var n)) return n;
        return def;
    }
}

public class MapStore
{
    readonly Dictionary<string, MapData> maps = new();
    public void AddMap(string name, MapData data) => maps[name] = data;
    public IReadOnlyDictionary<string, MapData> Maps => maps;
    public object ListRegions() => maps.Keys.Select(k => new { name = k });
}

public class MovementValidator
{
    public bool IsWalkable(MapData map, int x, int y)
    {
        var hasTile = map.Tiles.TryGetValue((x, y), out var tileId);
        if (!hasTile) return false;
        if (map.TileDefs.TryGetValue(tileId, out var def))
        {
            if (!def.isWalkable) return false;
        }
        if (map.Objects.TryGetValue((x, y), out var objId))
        {
            if (map.ObjectDefs.TryGetValue(objId, out var od))
            {
                if (od.blocksMovement) return false;
            }
        }
        return true;
    }
}

public class SocketServer : BackgroundService
{
    readonly MapStore store;
    readonly MovementValidator validator;
    readonly ChunkManager chunks;
    readonly RegionConfig regionCfg;
    readonly ILogger<SocketServer> logger;
    readonly Dictionary<System.Net.Sockets.TcpClient, (int x, int y, (int cx, int cy) chunk)> players = new();
    readonly Dictionary<System.Net.Sockets.TcpClient, int> ghostZones = new();
    readonly Dictionary<System.Net.Sockets.TcpClient, bool> ghostActive = new();
    readonly Dictionary<System.Net.Sockets.TcpClient, (string host, int port)?> lastNeighbor = new();
    public SocketServer(MapStore store, MovementValidator validator, ChunkManager chunks, RegionConfig regionCfg, ILogger<SocketServer> logger)
    {
        this.store = store;
        this.validator = validator;
        this.chunks = chunks;
        this.regionCfg = regionCfg;
        this.logger = logger;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, 9090);
        listener.Start();
        logger.LogInformation("socket_listen port={port}", 9090);
        while (!stoppingToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(stoppingToken);
            try
            {
                var ep = client.Client?.RemoteEndPoint?.ToString() ?? "unknown";
                logger.LogInformation("socket_accept remote={remote}", ep);
            }
            catch { }
            _ = HandleClient(client, stoppingToken);
        }
    }
    async Task HandleClient(System.Net.Sockets.TcpClient client, CancellationToken ct)
    {
        using var c = client;
        var stream = c.GetStream();
        var region = store.Maps.Keys.FirstOrDefault();
        var pos = new System.Numerics.Vector2(0, 0);
        var currentChunk = (0, 0);
        var header = new byte[3];
        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(header, 0, 3, ct);
            if (read != 3) break;
            var type = (PacketType)header[1];
            var len = header[2];
            var payload = new byte[len];
            var off = 0;
            while (off < len)
            {
                var r = await stream.ReadAsync(payload, off, len - off, ct);
                if (r <= 0) break;
                off += r;
            }
            if (type == PacketType.ClientConfig)
            {
                var gz = ReadInt(payload, 0);
                ghostZones[c] = gz < 0 ? 0 : gz;
                logger.LogInformation("client_config ghost_zone_width={width}", ghostZones[c]);
            }
            else if (type == PacketType.MoveRequest)
            {
                var x = ReadInt(payload, 0);
                var y = ReadInt(payload, 4);
                var map = region != null ? store.Maps[region] : null;
                var gz = ghostZones.TryGetValue(c, out var g) ? g : 0;
                var neighborNear = ResolveNeighborNear(x, y, gz);
                var wasActive = ghostActive.TryGetValue(c, out var act) && act;
                var prevNeighbor = lastNeighbor.TryGetValue(c, out var ln) ? ln : null;
                if (neighborNear != null)
                {
                    if (!wasActive || prevNeighbor == null || prevNeighbor.Value.host != neighborNear.Value.host || prevNeighbor.Value.port != neighborNear.Value.port)
                    {
                        var hint = BuildGhostHintMessage(PacketType.GhostZoneEnter, neighborNear.Value.host, neighborNear.Value.port);
                        await stream.WriteAsync(hint, 0, hint.Length, ct);
                        ghostActive[c] = true;
                        lastNeighbor[c] = neighborNear;
                        logger.LogInformation("ghost_enter host={host} port={port} x={x} y={y}", neighborNear.Value.host, neighborNear.Value.port, x, y);
                    }
                }
                else
                {
                    if (wasActive && prevNeighbor != null)
                    {
                        var hint = BuildGhostHintMessage(PacketType.GhostZoneLeave, prevNeighbor.Value.host, prevNeighbor.Value.port);
                        await stream.WriteAsync(hint, 0, hint.Length, ct);
                        ghostActive[c] = false;
                        lastNeighbor[c] = null;
                        logger.LogInformation("ghost_leave host={host} port={port} x={x} y={y}", prevNeighbor.Value.host, prevNeighbor.Value.port, x, y);
                    }
                }
                if (!IsInsideBounds(x, y))
                {
                    var neighbor = regionCfg.ResolveNeighborFor(x, y);
                    if (neighbor != null)
                    {
                        var msg = BuildHandoffMessage(neighbor.Value.host, neighbor.Value.port, x, y);
                        await stream.WriteAsync(msg, 0, msg.Length, ct);
                        logger.LogInformation("handoff host={host} port={port} x={x} y={y}", neighbor.Value.host, neighbor.Value.port, x, y);
                        continue;
                    }
                }
                if (map != null && validator.IsWalkable(map, x, y))
                {
                    pos = new System.Numerics.Vector2(x, y);
                    var ch = chunks.GetChunkForPosition((int)pos.X, (int)pos.Y);
                    currentChunk = ch;
                    players[c] = ((int)pos.X, (int)pos.Y, ch);
                }
                var resp = new byte[3 + 8];
                resp[0] = 1;
                resp[1] = (byte)PacketType.PositionUpdate;
                resp[2] = 8;
                WriteInt(resp, 3, (int)pos.X);
                WriteInt(resp, 7, (int)pos.Y);
                await stream.WriteAsync(resp, 0, resp.Length, ct);
            }
        }
    }
    bool IsInsideBounds(int x, int y)
    {
        return x >= regionCfg.MinX && x <= regionCfg.MaxX && y >= regionCfg.MinY && y <= regionCfg.MaxY;
    }
    (string host, int port)? ResolveNeighborNear(int x, int y, int ghostZoneWidth)
    {
        if (ghostZoneWidth <= 0) return null;
        var east = ParseHostPort(regionCfg.NeighborEast);
        var west = ParseHostPort(regionCfg.NeighborWest);
        var north = ParseHostPort(regionCfg.NeighborNorth);
        var south = ParseHostPort(regionCfg.NeighborSouth);
        if (east != null && x > regionCfg.MaxX - ghostZoneWidth) return east;
        if (west != null && x < regionCfg.MinX + ghostZoneWidth) return west;
        if (north != null && y > regionCfg.MaxY - ghostZoneWidth) return north;
        if (south != null && y < regionCfg.MinY + ghostZoneWidth) return south;
        return null;
    }
    static byte[] BuildHandoffMessage(string host, int port, int x, int y)
    {
        var hostBytes = System.Text.Encoding.ASCII.GetBytes(host);
        var len = 4 + hostBytes.Length + 4 + 4 + 4;
        var buf = new byte[3 + len];
        buf[0] = 1;
        buf[1] = (byte)PacketType.Handoff;
        buf[2] = (byte)len;
        WriteInt(buf, 3, hostBytes.Length);
        System.Array.Copy(hostBytes, 0, buf, 7, hostBytes.Length);
        var off = 7 + hostBytes.Length;
        WriteInt(buf, off, port);
        WriteInt(buf, off + 4, x);
        WriteInt(buf, off + 8, y);
        return buf;
    }
    static byte[] BuildGhostHintMessage(PacketType type, string host, int port)
    {
        var hostBytes = System.Text.Encoding.ASCII.GetBytes(host);
        var len = 4 + hostBytes.Length + 4;
        var buf = new byte[3 + len];
        buf[0] = 1;
        buf[1] = (byte)type;
        buf[2] = (byte)len;
        WriteInt(buf, 3, hostBytes.Length);
        System.Array.Copy(hostBytes, 0, buf, 7, hostBytes.Length);
        var off = 7 + hostBytes.Length;
        WriteInt(buf, off, port);
        return buf;
    }
    static (string host, int port)? ParseHostPort(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[1], out var p))
            return (parts[0], p);
        return null;
    }
    static void WriteInt(byte[] buf, int offset, int value)
    {
        buf[offset + 0] = (byte)((value >> 24) & 0xFF);
        buf[offset + 1] = (byte)((value >> 16) & 0xFF);
        buf[offset + 2] = (byte)((value >> 8) & 0xFF);
        buf[offset + 3] = (byte)(value & 0xFF);
    }
    static int ReadInt(byte[] buf, int offset)
    {
        var b0 = buf[offset + 0];
        var b1 = buf[offset + 1];
        var b2 = buf[offset + 2];
        var b3 = buf[offset + 3];
        return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
    }
}

public class ChunkManager
{
    public int ChunkSize { get; } = 32;
    public (int cx, int cy) GetChunkForPosition(int x, int y, int? size = null)
    {
        var s = size.HasValue && size.Value > 0 ? size.Value : ChunkSize;
        var cx = (int)Math.Floor(x / (double)s);
        var cy = (int)Math.Floor(y / (double)s);
        return (cx, cy);
    }
    public object DescribeGrid(MapData map, int? size = null)
    {
        var s = size.HasValue && size.Value > 0 ? size.Value : ChunkSize;
        var chunksX = (int)Math.Ceiling(map.Width / (double)s);
        var chunksY = (int)Math.Ceiling(map.Height / (double)s);
        return new
        {
            chunkSize = s,
            width = map.Width,
            height = map.Height,
            chunksX = chunksX,
            chunksY = chunksY
        };
    }
}
