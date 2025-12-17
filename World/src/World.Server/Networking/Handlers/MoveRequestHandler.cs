using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using World.Server.Models;
using World.Server.Services;

namespace World.Server.Networking.Handlers;

public class MoveRequestHandler : IPacketHandler
{
    public PacketType Type => PacketType.MoveRequest;
    private readonly WorldManager _world;
    private readonly MapStore _store;
    private readonly MovementValidator _validator;
    private readonly ChunkManager _chunks;
    private readonly RegionConfig _regionCfg;
    private readonly ILogger<MoveRequestHandler> _logger;

    public MoveRequestHandler(WorldManager world, MapStore store, MovementValidator validator, ChunkManager chunks, RegionConfig regionCfg, ILogger<MoveRequestHandler> logger)
    {
        _world = world;
        _store = store;
        _validator = validator;
        _chunks = chunks;
        _regionCfg = regionCfg;
        _logger = logger;
    }

    public async Task HandleAsync(ClientSession session, byte[] payload, CancellationToken ct)
    {
        var x = PacketUtils.ReadInt(payload, 0);
        var y = PacketUtils.ReadInt(payload, 4);
        var stream = session.Client.GetStream();

        var region = _store.Maps.Keys.FirstOrDefault();
        var regionName = _regionCfg.Name ?? region ?? "";

        // Calculate distance to detect "Arrivals" (Handoffs/Teleports)
        // When a player hands off, they "jump" from old pos (or ghost pos) to new pos.
        // We treat any jump > 1 tile as an arrival, making it a "Safe Spot" to avoid immediate re-teleport.
        var dxRaw = x - session.X;
        var dyRaw = y - session.Y;
        var dist = Math.Abs(dxRaw) + Math.Abs(dyRaw);
        bool isArrival = dist > 1;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 1. Initial Setup or Arrival
        if (session.LastMoveAtMs == 0 || isArrival)
        {
            session.SafeX = x;
            session.SafeY = y;
            session.LastArrivalAtMs = now;
        }

        // 2. Check Teleports
        var map = region != null ? _store.Maps[region] : null;
        // Only trigger teleport if it's NOT an arrival jump AND grace period expired
        if (session.LastMoveAtMs > 0 && !isArrival && (now - session.LastArrivalAtMs > 1500) && map != null && map.Teleports.TryGetValue((x, y), out var tp))
        {
            bool isSafe = session.SafeX.HasValue && session.SafeX == x && session.SafeY == y;
            if (!isSafe)
            {
                _logger.LogInformation("teleport_trigger x={x} y={y} target={region} tx={tx} ty={ty}", x, y, tp.targetRegion, tp.targetX, tp.targetY);
                var ep = _world.ResolveRegionEndpoint(tp.targetRegion);
                var handoffMsg = PacketUtils.BuildHandoffMessage(tp.targetRegion, ep?.host ?? "", ep?.port ?? 0, tp.targetX, tp.targetY);
                await stream.WriteAsync(handoffMsg, 0, handoffMsg.Length, ct);
                
                // Async cleanup: Ensure session is removed even if client doesn't disconnect gracefully
                _ = Task.Run(async () => 
                {
                    await Task.Delay(2000); // Short grace period for client to receive packet
                    _world.RemoveSession(session.Client);
                    try { session.Client.Close(); } catch { }
                });

                return;
            }
        }

        // 2. Ghost Zone Logic
        var gz = session.GhostZoneWidth;
        var candidates = _world.ResolveNeighborsNear(regionName, x, y, gz);
        
        var activeNow = new HashSet<string>();
        foreach (var cand in candidates)
        {
            activeNow.Add(cand.region);
            if (!session.ActiveGhosts.Contains(cand.region))
            {
                var hint = PacketUtils.BuildGhostHintMessage(PacketType.GhostZoneEnter, cand.region, cand.host, cand.port);
                await stream.WriteAsync(hint, 0, hint.Length, ct);
            }
        }
        foreach (var was in session.ActiveGhosts)
        {
            if (!activeNow.Contains(was))
            {
                var ep = _world.ResolveRegionEndpoint(was);
                if (ep != null)
                {
                    var hint = PacketUtils.BuildGhostHintMessage(PacketType.GhostZoneLeave, was, ep.Value.host, ep.Value.port);
                    await stream.WriteAsync(hint, 0, hint.Length, ct);
                }
            }
        }
        session.ActiveGhosts = activeNow;

        // 3. Move Logic
        // now is already defined above
        var canMove = true;
        
        if (session.LastMoveAtMs == 0) // Initial
        {
            var chInit = _chunks.GetChunkForPosition(session.X, session.Y);
            session.CurrentChunk = chInit;
            _world.BroadcastPlayersSnapshot();
        }

        if (!_world.IsInsideBounds(x, y))
        {
            canMove = false;
            // Note: Ghost zone lock logic omitted for brevity, but could be added here.
        }

        if (canMove)
        {
            var currX = session.X;
            var currY = session.Y;
            var dx = x - currX;
            var dy = y - currY;
            
            // Validate step size (1 tile only), unless it's the first move (handoff/login)
            if (session.LastMoveAtMs != 0 && (Math.Abs(dx) + Math.Abs(dy) != 1))
            {
                 // Fix diagonals or jumps
                 if (dx != 0 && dy != 0) { dx = dx > 0 ? 1 : -1; dy = 0; }
                 else { dx = dx > 0 ? 1 : (dx < 0 ? -1 : 0); dy = dy > 0 ? 1 : (dy < 0 ? -1 : 0); }
                 x = currX + dx;
                 y = currY + dy;
            }

            var midX = (currX + x) * 0.5f;
            var midY = (currY + y) * 0.5f;
            var areaOk = map != null
                && _validator.IsAreaWalkable(map, midX, midY, 0.5f)
                && _validator.IsAreaWalkable(map, x, y, 0.5f)
                && _validator.IsWalkable(map, x, y);

            if (areaOk)
            {
                // Clear Safe Spot if we moved away
                if (session.SafeX.HasValue && (session.SafeX != x || session.SafeY != y))
                {
                    session.SafeX = null;
                    session.SafeY = null;
                }

                session.X = x;
                session.Y = y;
                var ch = _chunks.GetChunkForPosition(x, y);
                session.CurrentChunk = ch;
                session.LastMoveAtMs = now;
                _world.BroadcastPlayersSnapshot();
            }
        }

        var resp = new byte[3 + 8];
        resp[0] = 1;
        resp[1] = (byte)PacketType.PositionUpdate;
        resp[2] = 8;
        PacketUtils.WriteInt(resp, 3, session.X);
        PacketUtils.WriteInt(resp, 7, session.Y);
        await stream.WriteAsync(resp, 0, resp.Length, ct);
    }
}
