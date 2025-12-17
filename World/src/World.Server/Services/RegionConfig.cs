using System;

namespace World.Server.Services;

public class RegionConfig
{
    public string? Name { get; }
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
        Name = Environment.GetEnvironmentVariable("REGION_NAME");
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
