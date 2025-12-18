using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using World.Server.Models;
using World.Server.Networking;

namespace World.Server.Services;

public class WorldManager
{
    private readonly MapStore _store;
    private readonly MovementValidator _validator;
    private readonly ChunkManager _chunks;
    private readonly RegionConfig _regionCfg;
    private readonly DirectoryClient _directory;
    private readonly ILogger<WorldManager> _logger;

    private readonly ConcurrentDictionary<TcpClient, ClientSession> _sessions = new();
    private int _nextClientId = 1;
    
    // Topology loaded from local JSON (Source of Truth for Bounds)
    private readonly List<RegionStatus> _localTopology;
    private readonly RegionStatus _myLocalDef;

    public WorldManager(MapStore store, MovementValidator validator, ChunkManager chunks, RegionConfig regionCfg, DirectoryClient directory, ILogger<WorldManager> logger)
    {
        _store = store;
        _validator = validator;
        _chunks = chunks;
        _regionCfg = regionCfg;
        _directory = directory;
        _logger = logger;

        // Load topology from local JSON immediately. Fail fast if missing.
        _localTopology = DirectoryClient.LoadLocalRegions();
        if (_localTopology == null || _localTopology.Count == 0)
        {
            Console.Error.WriteLine("CRITICAL: Failed to load world.regions.json topology. Process will exit.");
            Environment.Exit(1);
        }

        var myName = _regionCfg.Name ?? "";
        _myLocalDef = _localTopology.FirstOrDefault(r => string.Equals(r.Name, myName, StringComparison.OrdinalIgnoreCase));
        
        if (_myLocalDef == null)
        {
            Console.Error.WriteLine($"CRITICAL: Region '{myName}' not found in world.regions.json. Process will exit.");
            Environment.Exit(1);
        }
        
        _logger.LogInformation("topology_loaded source=json region={region} bounds=[{minX},{maxX}]x[{minY},{maxY}]", 
            myName, _myLocalDef.MinX, _myLocalDef.MaxX, _myLocalDef.MinY, _myLocalDef.MaxY);

        // Background task to ping Directory API every 20 seconds
        // Ensures we have up-to-date neighbor ports (Service Ports)
        Task.Run(async () => 
        {
            while (true)
            {
                try 
                { 
                    // This updates the cache in DirectoryClient
                    await _directory.GetRegionsAsync(); 
                } 
                catch (Exception e) 
                { 
                    _logger.LogError(e, "Failed to refresh regions from directory"); 
                }
                await Task.Delay(20000);
            }
        });
    }

    public ClientSession CreateSession(TcpClient client)
    {
        var session = new ClientSession(client);
        _sessions.TryAdd(client, session);
        return session;
    }

    public void RemoveSession(TcpClient client)
    {
        if (_sessions.TryRemove(client, out var session))
        {
            _logger.LogInformation("player_disconnected remote={remote} x={x} y={y}", session.Client.Client.RemoteEndPoint, session.X, session.Y);
        }
    }

    public int AssignClientId(ClientSession session)
    {
        if (session.ClientId == 0)
        {
            session.ClientId = _nextClientId++;
        }
        return session.ClientId;
    }

    public void BroadcastPlayersSnapshot()
    {
        var entries = _sessions.Values
            .Where(s => s.ClientId != 0)
            .Select(s => new { s.ClientId, s.X, s.Y, s.IsMoving, Username = s.Username ?? "" })
            .ToList();
        
        foreach (var session in _sessions.Values)
        {
            var stream = session.Client.GetStream();
            if (stream == null || !stream.CanWrite) continue;
            
            // Calculate payload length
            // Header: MyId (4) + Count (4) = 8 bytes
            var payloadLen = 8;
            foreach (var e in entries)
            {
                // Each entry: Id (4) + X (4) + Y (4) + IsMoving (1) + UserLen (4) + UserBytes (N)
                payloadLen += 12 + 1 + 4 + Encoding.UTF8.GetByteCount(e.Username);
            }

            byte[] buf;
            int headerOffset;
            
            if (payloadLen < 255)
            {
                buf = new byte[3 + payloadLen];
                buf[0] = 1;
                buf[1] = (byte)PacketType.PlayersSnapshot;
                buf[2] = (byte)payloadLen;
                headerOffset = 3;
            }
            else
            {
                buf = new byte[3 + 2 + payloadLen];
                buf[0] = 1;
                buf[1] = (byte)PacketType.PlayersSnapshot;
                buf[2] = 255;
                buf[3] = (byte)((payloadLen >> 8) & 0xFF);
                buf[4] = (byte)(payloadLen & 0xFF);
                headerOffset = 5;
            }

            PacketUtils.WriteInt(buf, headerOffset, session.ClientId);
            PacketUtils.WriteInt(buf, headerOffset + 4, entries.Count);
            var off = headerOffset + 8;
            
            foreach (var e in entries)
            {
                PacketUtils.WriteInt(buf, off, e.ClientId);
                PacketUtils.WriteInt(buf, off + 4, e.X);
                PacketUtils.WriteInt(buf, off + 8, e.Y);
                buf[off + 12] = e.IsMoving ? (byte)1 : (byte)0;
                off += 13;
                
                PacketUtils.WriteString(buf, off, e.Username);
                off += 4 + Encoding.UTF8.GetByteCount(e.Username);
            }
            
            try { stream.Write(buf, 0, buf.Length); } catch { }
        }
    }

    public RegionStatus? FindRegionByPosition(int x, int y)
    {
        // Use local JSON topology exclusively for geometry/bounds
        var statuses = _localTopology;
        foreach (var s in statuses)
        {
            if (x >= s.MinX && x <= s.MaxX && y >= s.MinY && y <= s.MaxY)
                return s;
        }
        return null;
    }

    public RegionStatus? GetCurrentRegionStatus(string name)
    {
        // Use local JSON topology exclusively for geometry/bounds
        return _localTopology.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public (int minX, int maxX, int minY, int maxY) GetEffectiveBounds()
    {
        // 1. Source of Truth: Local JSON (already loaded in _myLocalDef)
        // We do NOT use Directory API or Env Vars for bounds anymore.
        var minX = _myLocalDef.MinX;
        var maxX = _myLocalDef.MaxX;
        var minY = _myLocalDef.MinY;
        var maxY = _myLocalDef.MaxY;

        // 2. Clamp to Map Data dimensions if available
        // This prevents walking into void if Config > Map
        if (_store != null && !string.IsNullOrEmpty(_regionCfg.Name) && _store.Maps.TryGetValue(_regionCfg.Name, out var map))
        {
            // We assume the map is anchored at (MinX, MinY)
            // So valid range is [MinX, MinX + Width - 1]
            var mapMaxX = minX + map.Width - 1;
            var mapMaxY = minY + map.Height - 1;
            
            if (mapMaxX < maxX) maxX = mapMaxX;
            if (mapMaxY < maxY) maxY = mapMaxY;
        }
        
        return (minX, maxX, minY, maxY);
    }

    public bool IsInsideBounds(int x, int y)
    {
        var b = GetEffectiveBounds();
        var inside = x >= b.minX && x <= b.maxX && y >= b.minY && y <= b.maxY;
        
        if (!inside)
        {
             // Only log if it was previously considered inside by laxer checks, to avoid spam
             // But here we just log.
             // _logger.LogInformation("bounds_check fail x={x} y={y} bounds=[{minX},{maxX}]x[{minY},{maxY}]", x, y, b.minX, b.maxX, b.minY, b.maxY);
        }
        return inside;
    }

    public List<(string region, string host, int port)> ResolveNeighborsNear(string currentRegion, int x, int y, int ghostZoneWidth)
    {
        var list = new List<(string region, string host, int port)>();
        // Use local JSON topology to determine WHO is a neighbor
        var statuses = _localTopology;
        
        var (minX, maxX, minY, maxY) = GetEffectiveBounds();
        
        // Increased max ghost zone width to 3
        var gzEff = ghostZoneWidth <= 0 ? 1 : (ghostZoneWidth > 3 ? 3 : ghostZoneWidth);
        var nearEast = x >= maxX - gzEff;
        var nearWest = x <= minX + gzEff;
        var nearNorth = y >= maxY - gzEff;
        var nearSouth = y <= minY + gzEff;
        
        if (nearEast)
        {
            foreach (var s in statuses)
            {
                var isNeighbor = (s.MinX == maxX || s.MinX == maxX + 1) && RangesOverlap(minY, maxY, s.MinY, s.MaxY);
                if (isNeighbor)
                {
                    var ep = ResolveRegionEndpoint(s.Name);
                    if (ep != null) list.Add((s.Name, ep.Value.host, ep.Value.port));
                }
            }
        }
        if (nearWest)
        {
            foreach (var s in statuses)
            {
                var isNeighbor = (s.MaxX == minX || s.MaxX == minX - 1) && RangesOverlap(minY, maxY, s.MinY, s.MaxY);
                if (isNeighbor)
                {
                    var ep = ResolveRegionEndpoint(s.Name);
                    if (ep != null) list.Add((s.Name, ep.Value.host, ep.Value.port));
                }
            }
        }
        if (nearNorth)
        {
            foreach (var s in statuses)
            {
                var isNeighbor = (s.MinY == maxY || s.MinY == maxY + 1) && RangesOverlap(minX, maxX, s.MinX, s.MaxX);
                if (isNeighbor)
                {
                    var ep = ResolveRegionEndpoint(s.Name);
                    if (ep != null) list.Add((s.Name, ep.Value.host, ep.Value.port));
                }
            }
        }
        if (nearSouth)
        {
            foreach (var s in statuses)
            {
                var isNeighbor = (s.MaxY == minY || s.MaxY == minY - 1) && RangesOverlap(minX, maxX, s.MinX, s.MaxX);
                if (isNeighbor)
                {
                    var ep = ResolveRegionEndpoint(s.Name);
                    if (ep != null) list.Add((s.Name, ep.Value.host, ep.Value.port));
                }
            }
        }
        list = list.Where(t => !string.Equals(t.region, currentRegion, StringComparison.OrdinalIgnoreCase)).ToList();
        
        // Removed fallback to Directory API for topology resolution.
        // Neighbors are determined exclusively by local JSON geometry.
        
        return list;
    }

    public (string host, int port)? ResolveRegionEndpoint(string regionName)
    {
        try
        {
            var statuses = _directory.GetRegions();
            var status = statuses.FirstOrDefault(s => string.Equals(s.Name, regionName, StringComparison.OrdinalIgnoreCase));
            if (status != null)
            {
                (string host, int port)? chosen = null;

                // Priority 1: Local/External Endpoint (Preferred per user instruction for local dev)
                // "voce precisa o localTcp" - allows external clients (like Unity Editor) to connect via NodePort/Localhost
                var hpLocal = ParseHostPort(status.LocalTcp);
                if (hpLocal != null) chosen = hpLocal;
                
                // Priority 2: Cluster/Service Endpoint (Fallback for internal cluster traffic)
                if (chosen == null)
                {
                    var hpCluster = ParseHostPort(status.ClusterTcp);
                    if (hpCluster != null) chosen = hpCluster;
                }

                if (chosen != null) return chosen;
            }
        }
        catch { }
        return null;
    }

    private static bool RangesOverlap(int aMin, int aMax, int bMin, int bMax)
    {
        return aMin <= bMax && bMin <= aMax;
    }

    private static string? ParseRegionNameServer(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.StartsWith("world-", StringComparison.OrdinalIgnoreCase))
        {
            return t.Substring("world-".Length);
        }
        return t;
    }

    private static (string host, int port)? ParseHostPort(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.Contains("://"))
        {
            try
            {
                if (Uri.TryCreate(t, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host;
                    var port = uri.Port;
                    if (!string.IsNullOrWhiteSpace(host) && port > 0)
                        return (host, port);
                }
            }
            catch { }
        }
        if (t.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("udp://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var idx = t.IndexOf("://", StringComparison.Ordinal);
                t = t.Substring(idx + 3);
            }
            catch { }
        }
        t = t.Trim('/');
        if (t.StartsWith("[") && t.Contains("]"))
        {
            var rb = t.IndexOf(']');
            var hostPart = t.Substring(1, rb - 1);
            var portPart = t.IndexOf(':', rb + 1) >= 0 ? t.Substring(t.IndexOf(':', rb + 1) + 1) : "";
            if (int.TryParse(portPart, out var p) && !string.IsNullOrWhiteSpace(hostPart))
                return (hostPart, p);
        }
        var lastColon = t.LastIndexOf(':');
        if (lastColon > 0 && lastColon < t.Length - 1)
        {
            var host = t.Substring(0, lastColon);
            var portStr = t.Substring(lastColon + 1);
            if (int.TryParse(portStr, out var p) && !string.IsNullOrWhiteSpace(host))
                return (host, p);
        }
        return null;
    }
}