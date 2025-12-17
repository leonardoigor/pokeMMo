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

    public WorldManager(MapStore store, MovementValidator validator, ChunkManager chunks, RegionConfig regionCfg, DirectoryClient directory, ILogger<WorldManager> logger)
    {
        _store = store;
        _validator = validator;
        _chunks = chunks;
        _regionCfg = regionCfg;
        _directory = directory;
        _logger = logger;
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
            .Select(s => new { s.ClientId, s.X, s.Y, Username = s.Username ?? "" })
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
                // Each entry: Id (4) + X (4) + Y (4) + UserLen (4) + UserBytes (N)
                payloadLen += 12 + 4 + Encoding.UTF8.GetByteCount(e.Username);
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
                off += 12;
                
                PacketUtils.WriteString(buf, off, e.Username);
                off += 4 + Encoding.UTF8.GetByteCount(e.Username);
            }
            
            try { stream.Write(buf, 0, buf.Length); } catch { }
        }
    }

    public RegionStatus? FindRegionByPosition(int x, int y)
    {
        try
        {
            var statuses = _directory.GetRegions();
            foreach (var s in statuses)
            {
                if (x >= s.MinX && x <= s.MaxX && y >= s.MinY && y <= s.MaxY)
                    return s;
            }
        }
        catch { }
        return null;
    }

    public RegionStatus? GetCurrentRegionStatus(string name)
    {
        try
        {
            var statuses = _directory.GetRegions();
            return statuses.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    public bool IsInsideBounds(int x, int y)
    {
        var me = GetCurrentRegionStatus(_regionCfg.Name ?? "");
        if (me != null)
        {
            var inside = x >= me.MinX && x <= me.MaxX && y >= me.MinY && y <= me.MaxY;
            if (!inside)
            {
                _logger.LogInformation("bounds_check out_of_bounds x={x} y={y} minX={minX} maxX={maxX} minY={minY} maxY={maxY}", x, y, me.MinX, me.MaxX, me.MinY, me.MaxY);
            }
            return inside;
        }
        var fallbackInside = x >= _regionCfg.MinX && x <= _regionCfg.MaxX && y >= _regionCfg.MinY && y <= _regionCfg.MaxY;
        if (!fallbackInside)
        {
            _logger.LogInformation("bounds_check out_of_bounds_fallback x={x} y={y} minX={minX} maxX={maxX} minY={minY} maxY={maxY}", x, y, _regionCfg.MinX, _regionCfg.MaxX, _regionCfg.MinY, _regionCfg.MaxY);
        }
        return fallbackInside;
    }

    public List<(string region, string host, int port)> ResolveNeighborsNear(string currentRegion, int x, int y, int ghostZoneWidth)
    {
        var list = new List<(string region, string host, int port)>();
        var statuses = _directory.GetRegions();
        var me = statuses.FirstOrDefault(s => string.Equals(s.Name, currentRegion, StringComparison.OrdinalIgnoreCase)) ?? FindRegionByPosition(x, y);
        var minX = me?.MinX ?? _regionCfg.MinX;
        var maxX = me?.MaxX ?? _regionCfg.MaxX;
        var minY = me?.MinY ?? _regionCfg.MinY;
        var maxY = me?.MaxY ?? _regionCfg.MaxY;
        var gzEff = ghostZoneWidth <= 0 ? 1 : (ghostZoneWidth > 2 ? 2 : ghostZoneWidth);
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
        if (list.Count == 0)
        {
            try
            {
                var rr = _directory.Resolve(currentRegion, x, y, ghostZoneWidth);
                if (rr != null && !string.IsNullOrWhiteSpace(rr.NextRegion))
                {
                    var ep = ResolveRegionEndpoint(rr.NextRegion);
                    if (ep != null)
                    {
                        var match = statuses.FirstOrDefault(s => string.Equals(s.Name, rr.NextRegion, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(ParseRegionNameServer(s.Name) ?? "", rr.NextRegion, StringComparison.OrdinalIgnoreCase));
                        var regionOut = match?.Name ?? rr.NextRegion;
                        list.Add((regionOut, ep.Value.host, ep.Value.port));
                    }
                }
            }
            catch { }
        }
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
                var hpCluster = ParseHostPort(status.ClusterTcp);
                if (hpCluster != null) chosen = hpCluster;
                if (chosen == null)
                {
                    var hpLocal = ParseHostPort(status.LocalTcp);
                    if (hpLocal != null) chosen = hpLocal;
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