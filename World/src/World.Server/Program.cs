using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using Observability.Extensions;
using World.Server.Core;
using World.Server.Models;
using World.Server.Networking;
using World.Server.Networking.Handlers;
using World.Server.Services;
using System;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddSingleton<MapStore>();
builder.Services.AddSingleton<MapLoader>();
builder.Services.AddSingleton<MovementValidator>();
builder.Services.AddSingleton<ChunkManager>();
builder.Services.AddSingleton<RegionConfig>();
builder.Services.AddSingleton<DirectoryClient>();
builder.Services.AddSingleton<WorldManager>();

// Networking
builder.Services.AddSingleton<PacketRouter>();
builder.Services.AddSingleton<IPacketHandler, ClientConfigHandler>();
builder.Services.AddSingleton<IPacketHandler, MoveRequestHandler>();
builder.Services.AddSingleton<IPacketHandler, ProvisionalConnectionHandler>();

// Core
builder.Services.AddHostedService<Server>();
builder.Services.AddObservability(builder.Configuration, "world");

var app = builder.Build();
app.UseRequestResponseLogging();

var store = app.Services.GetRequiredService<MapStore>();
var loader = app.Services.GetRequiredService<MapLoader>();
var chunks = app.Services.GetRequiredService<ChunkManager>();
var regionCfg = app.Services.GetRequiredService<RegionConfig>();
var world = app.Services.GetRequiredService<WorldManager>(); // Ensure WorldManager is created

// Map Loading Logic
var dataDir = Environment.GetEnvironmentVariable("MAP_DATA_DIR");
if (string.IsNullOrWhiteSpace(dataDir))
{
    var cand1 = Path.Combine(AppContext.BaseDirectory, "data");
    var cand2 = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data");
    if (Directory.Exists(cand1)) dataDir = cand1;
    else if (Directory.Exists(cand2)) dataDir = cand2;
    else dataDir = "";
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
            app.Logger.LogInformation("map_load name={name} map={map} tiles={tiles} objects={objects}", mapName, mapPath, tileDefsPath, objDefsPath);
            var data = loader.Load(mapPath, tileDefsPath, objDefsPath);
            if (data != null)
            {
                store.AddMap(mapName, data);
                app.Logger.LogInformation(
                    "map_loaded name={name} width={width} height={height} tiles_count={tiles} objects_count={objects} tiledefs_count={tiledefs} objectdefs_count={objdefs}",
                    mapName,
                    data.Width,
                    data.Height,
                    data.Tiles.Count,
                    data.Objects.Count,
                    data.TileDefs.Count,
                    data.ObjectDefs.Count
                );
            }
        }
    }
}

if (!store.Maps.Any())
{
    app.Logger.LogCritical("no_maps_loaded dataDir={dir}", dataDir);
    Environment.Exit(2);
}

// Endpoints
app.MapGet("/world/regions", () =>
{
    return store.ListRegions();
});

app.MapGet("/world/chunks", (string? region, int? size) =>
{
    var name = region ?? store.Maps.Keys.FirstOrDefault();
    if (string.IsNullOrEmpty(name)) return Results.NotFound();
    if (!store.Maps.TryGetValue(name, out var map)) return Results.NotFound();
    var info = chunks.DescribeGrid(map, size);
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

app.MapGet("/world/deadzones", (string? region) =>
{
    var name = region ?? store.Maps.Keys.FirstOrDefault();
    if (string.IsNullOrEmpty(name)) return Results.NotFound();
    if (!store.Maps.TryGetValue(name, out var map)) return Results.NotFound();
    var blocked = new HashSet<(int x, int y)>();
    var items = new List<object>();
    foreach (var kv in map.Objects)
    {
        var pos = kv.Key;
        var id = kv.Value;
        if (map.ObjectDefs.TryGetValue(id, out var od) && od.blocksMovement)
        {
            if (blocked.Add(pos)) items.Add(new { x = pos.x, y = pos.y, reason = "object" });
        }
    }
    foreach (var kv in map.Tiles)
    {
        var pos = kv.Key;
        var id = kv.Value;
        if (map.TileDefs.TryGetValue(id, out var td) && !td.isWalkable)
        {
            if (blocked.Add(pos)) items.Add(new { x = pos.x, y = pos.y, reason = "tile" });
        }
    }
    return Results.Ok(new { region = name, count = items.Count, items = items });
});

app.MapGet("/world/ghostzone", (string? region, int? gz) =>
{
    var name = region ?? store.Maps.Keys.FirstOrDefault();
    var g = gz.HasValue && gz.Value > 0 ? (gz.Value > 2 ? 2 : gz.Value) : 1;
    var minX = regionCfg.MinX;
    var maxX = regionCfg.MaxX;
    var minY = regionCfg.MinY;
    var maxY = regionCfg.MaxY;
    var edges = new
    {
        west = new { xStart = minX, xEnd = minX + g - 1, yStart = minY, yEnd = maxY },
        east = new { xStart = maxX - g + 1, xEnd = maxX, yStart = minY, yEnd = maxY },
        north = new { xStart = minX, xEnd = maxX, yStart = maxY - g + 1, yEnd = maxY },
        south = new { xStart = minX, xEnd = maxX, yStart = minY, yEnd = minY + g - 1 }
    };
    return Results.Ok(new { region = name, minX = minX, maxX = maxX, minY = minY, maxY = maxY, gz = g, edges = edges });
});

app.Run();
