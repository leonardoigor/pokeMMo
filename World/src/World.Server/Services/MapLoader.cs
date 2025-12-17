using System.IO;
using System.Text.Json;
using World.Server.Models;

namespace World.Server.Services;

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
        if (map.teleports != null)
        {
            foreach (var tp in map.teleports) data.Teleports[(tp.x, tp.y)] = tp;
        }
        foreach (var td in tileDefs) data.TileDefs[td.tileId] = td;
        foreach (var od in objDefs) data.ObjectDefs[od.objectId] = od;
        return data;
    }
}
