using System.Collections.Generic;
using System.Linq;
using World.Server.Models;

namespace World.Server.Services;

public class MapStore
{
    readonly Dictionary<string, MapData> maps = new();
    public void AddMap(string name, MapData data) => maps[name] = data;
    public IReadOnlyDictionary<string, MapData> Maps => maps;
    public object ListRegions() => maps.Keys.Select(k => new { name = k });
}
