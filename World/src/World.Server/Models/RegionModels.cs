using System;
using System.Collections.Generic;

namespace World.Server.Models;

public class RegionStatus
{
    public string Name { get; set; } = string.Empty;
    public bool Online { get; set; }
    public string ClusterTcp { get; set; } = string.Empty;
    public string LocalTcp { get; set; } = string.Empty;
    public DateTime LastChecked { get; set; }
    public int MinX { get; set; }
    public int MaxX { get; set; }
    public int MinY { get; set; }
    public int MaxY { get; set; }
}

public class ResolveRequest
{
    public string RegionName { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int GhostZoneWidth { get; set; }
}

public class ResolveResponse
{
    public string CurrentRegion { get; set; } = string.Empty;
    public bool Online { get; set; }
    public string ClusterTcp { get; set; } = string.Empty;
    public string LocalTcp { get; set; } = string.Empty;
    public string NextRegion { get; set; } = string.Empty;
}

public class WorldRegionsDoc
{
    public string Namespace { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public int BasePort { get; set; }
    public List<WorldRegion> Regions { get; set; } = new List<WorldRegion>();
}

public class WorldRegion
{
    public string Name { get; set; } = string.Empty;
    public int MinX { get; set; }
    public int MaxX { get; set; }
    public int MinY { get; set; }
    public int MaxY { get; set; }
    public int TcpPort { get; set; }
    public WorldNeighbors Neighbors { get; set; } = new WorldNeighbors();
}

public class WorldNeighbors
{
    public string East { get; set; } = string.Empty;
    public string West { get; set; } = string.Empty;
    public string North { get; set; } = string.Empty;
    public string South { get; set; } = string.Empty;
}
