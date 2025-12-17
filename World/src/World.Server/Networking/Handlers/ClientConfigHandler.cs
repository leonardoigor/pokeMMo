using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using World.Server.Models;
using World.Server.Services;

namespace World.Server.Networking.Handlers;

public class ClientConfigHandler : IPacketHandler
{
    public PacketType Type => PacketType.ClientConfig;
    private readonly WorldManager _world;
    private readonly MapStore _store;
    private readonly RegionConfig _regionCfg;
    private readonly ILogger<ClientConfigHandler> _logger;

    public ClientConfigHandler(WorldManager world, MapStore store, RegionConfig regionCfg, ILogger<ClientConfigHandler> logger)
    {
        _world = world;
        _store = store;
        _regionCfg = regionCfg;
        _logger = logger;
    }

    public async Task HandleAsync(ClientSession session, byte[] payload, CancellationToken ct)
    {
        var gz = PacketUtils.ReadInt(payload, 0);
        // Increased max ghost zone width to 3 per user request
        var clamped = gz <= 0 ? 1 : (gz > 3 ? 3 : gz);
        session.GhostZoneWidth = clamped;

        if (payload.Length > 4)
        {
            session.Username = PacketUtils.ReadString(payload, 4, out _);
        }

        _logger.LogInformation("client_config ghost_zone_width={width} username={username}", clamped, session.Username);

        var myId = _world.AssignClientId(session);

        try
        {
            var region = _store.Maps.Keys.FirstOrDefault();
            var regionName = _regionCfg.Name ?? region ?? "";
            
            var me = _world.GetCurrentRegionStatus(regionName) ?? _world.FindRegionByPosition(0, 0);
            
            // Use centralized logic from WorldManager to determine effective bounds
            // This considers both RegionConfig and MapData (if loaded)
            // Ensures client Gizmos match server validation logic exactly.
            var b = _world.GetEffectiveBounds();

            var infoMsg = PacketUtils.BuildGhostZoneInfoMessage(clamped, b.minX, b.maxX, b.minY, b.maxY);
            await session.Client.GetStream().WriteAsync(infoMsg, 0, infoMsg.Length, ct);

            var hello = new byte[3 + 4];
            hello[0] = 1;
            hello[1] = (byte)PacketType.PlayerInfo;
            hello[2] = 4;
            PacketUtils.WriteInt(hello, 3, myId);
            await session.Client.GetStream().WriteAsync(hello, 0, hello.Length, ct);

            var map = region != null ? _store.Maps[region] : null;
            if (map != null)
            {
                var blocked = new List<(int x, int y)>();
                var set = new HashSet<(int x, int y)>();
                
                foreach (var kv in map.Objects)
                {
                    var id = kv.Value;
                    if (map.ObjectDefs.TryGetValue(id, out var od) && od.blocksMovement)
                    {
                        if (set.Add(kv.Key)) blocked.Add((kv.Key.x, kv.Key.y));
                        if (blocked.Count >= 1024) break;
                    }
                }
                if (blocked.Count < 1024)
                {
                    foreach (var kv in map.Tiles)
                    {
                        var id = kv.Value;
                        if (map.TileDefs.TryGetValue(id, out var td) && !td.isWalkable)
                        {
                            if (set.Add(kv.Key)) blocked.Add((kv.Key.x, kv.Key.y));
                            if (blocked.Count >= 1024) break;
                        }
                    }
                }

                var dzMsg = PacketUtils.BuildDeadZonesMessage(blocked);
                await session.Client.GetStream().WriteAsync(dzMsg, 0, dzMsg.Length, ct);
            }
        }
        catch { }
    }
}
