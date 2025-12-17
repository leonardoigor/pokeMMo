using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using World.Server.Models;

namespace World.Server.Services;

public class DirectoryClient
{
    readonly HttpClient http = new HttpClient();
    readonly string baseUrl;
    readonly bool disableHttp;
    public static List<RegionStatus> LoadLocalRegions()
    {
        var list = new List<RegionStatus>();
        try
        {
            var dataDirEnv = Environment.GetEnvironmentVariable("MAP_DATA_DIR");
            var envPath = Environment.GetEnvironmentVariable("WORLD_REGIONS_JSON");
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(envPath)) candidates.Add(envPath!);
            if (!string.IsNullOrWhiteSpace(dataDirEnv))
            {
                try { candidates.Add(System.IO.Path.Combine(dataDirEnv!, "Game", "world.regions.json")); } catch { }
            }
            try { candidates.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "world.regions.json")); } catch { }
            try { candidates.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "World", "data", "Game", "world.regions.json")); } catch { }
            try { candidates.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "Game", "world.regions.json")); } catch { }
            try { candidates.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "Game", "world.regions.json")); } catch { }
            try { candidates.Add(System.IO.Path.Combine(Environment.CurrentDirectory, "World", "data", "Game", "world.regions.json")); } catch { }
            try { candidates.Add(System.IO.Path.Combine(Environment.CurrentDirectory, "world.regions.json")); } catch { }
            string? found = null;
            foreach (var p in candidates)
            {
                try { if (!string.IsNullOrWhiteSpace(p) && System.IO.File.Exists(p)) { found = p; break; } } catch { }
            }
            if (found == null) return list;
            try { Console.WriteLine($"local_regions_load path={found}"); } catch { }
            var json = System.IO.File.ReadAllText(found!);
            var doc = System.Text.Json.JsonSerializer.Deserialize<WorldRegionsDoc>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (doc == null || doc.Regions == null) return list;
            foreach (var r in doc.Regions)
            {
                var status = new RegionStatus
                {
                    Name = r.Name ?? string.Empty,
                    Online = true,
                    ClusterTcp = string.Empty,
                    LocalTcp = r.TcpPort > 0 ? $"127.0.0.1:{r.TcpPort}" : string.Empty,
                    MinX = r.MinX,
                    MaxX = r.MaxX,
                    MinY = r.MinY,
                    MaxY = r.MaxY,
                };
                list.Add(status);
            }
        }
        catch { }
        return list;
    }
    public DirectoryClient(string? baseUrl = null)
    {
        var env = Environment.GetEnvironmentVariable("DIRECTORY_BASE_URL");
        var chosen = !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl! : (!string.IsNullOrWhiteSpace(env) ? env! : "http://directory:8085");
        this.baseUrl = chosen;
        http.Timeout = TimeSpan.FromSeconds(2);
        var dis = Environment.GetEnvironmentVariable("DIRECTORY_DISABLE");
        disableHttp = string.Equals(dis, "true", StringComparison.OrdinalIgnoreCase);
    }
    private List<RegionStatus>? _cachedRegions;
    private DateTime _lastCacheTime;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(15);

    public List<RegionStatus> GetRegions()
    {
        return GetRegionsAsync().GetAwaiter().GetResult();
    }

    public async Task<List<RegionStatus>> GetRegionsAsync()
    {
        try
        {
            var local = LoadLocalRegions();
            if (disableHttp)
            {
                 if (local.Count == 0)
                 {
                     try { Console.Error.WriteLine("world.regions.json não encontrado. Encerrando o processo."); } catch { }
                     try { Environment.Exit(2); } catch { }
                 }
                 return local;
            }

            // If we have cached regions and they are fresh, use them
            if (_cachedRegions != null && DateTime.UtcNow - _lastCacheTime < _cacheDuration)
            {
                return _cachedRegions;
            }

            try
            {
                var url = $"{baseUrl}/directory/regions";
                var text = await http.GetStringAsync(url);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var list = JsonSerializer.Deserialize<List<RegionStatus>>(text, options);
                
                if (list != null)
                {
                    _cachedRegions = list;
                    _lastCacheTime = DateTime.UtcNow;
                    return list;
                }
            }
            catch (Exception ex)
            {
                // If HTTP fails but we have local regions, fallback to local
                if (local.Count > 0) return local;
                // If we have stale cache, use it
                if (_cachedRegions != null) return _cachedRegions;
                throw; // Or return empty list?
            }

            return local; // Fallback if list was null but no exception
        }
        catch 
        { 
            return _cachedRegions ?? new List<RegionStatus>(); 
        }
    }
    public ResolveResponse? Resolve(string currentRegion, int x, int y, int ghostZoneWidth)
    {
        try
        {
            if (disableHttp) return null;
            var url = $"{baseUrl}/directory/resolve";
            var req = new ResolveRequest { RegionName = currentRegion, X = x, Y = y, GhostZoneWidth = ghostZoneWidth };
            var json = JsonSerializer.Serialize(req);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var resp = http.PostAsync(url, content).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return null;
            var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var rr = JsonSerializer.Deserialize<ResolveResponse>(text);
            return rr;
        }
        catch { return null; }
    }
}
