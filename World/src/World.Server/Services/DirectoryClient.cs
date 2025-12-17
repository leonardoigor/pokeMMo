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
    static List<RegionStatus> TryLoadLocalRegions()
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
    public List<RegionStatus> GetRegions()
    {
        try
        {
            var local = TryLoadLocalRegions();
            if (disableHttp && local.Count == 0)
            {
                try { Console.Error.WriteLine("world.regions.json não encontrado. Encerrando o processo."); } catch { }
                try { Environment.Exit(2); } catch { }
                return new List<RegionStatus>();
            }
            if (local.Count > 0 || disableHttp) return local;
            var url = $"{baseUrl}/directory/regions";
            var text = http.GetStringAsync(url).GetAwaiter().GetResult();
            var list = JsonSerializer.Deserialize<List<RegionStatus>>(text);
            return list ?? new List<RegionStatus>();
        }
        catch { return new List<RegionStatus>(); }
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
