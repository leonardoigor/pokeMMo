using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Observability.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<RegionCatalog>();
builder.Services.AddSingleton<RegionMonitor>();
builder.Services.AddHostedService<RegionMonitorService>();
builder.Services.AddObservability(builder.Configuration, "directory");
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseRequestResponseLogging();
var catalog = app.Services.GetRequiredService<RegionCatalog>();
var monitor = app.Services.GetRequiredService<RegionMonitor>();
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapGet("/directory/spawn", () =>
{
    var region = catalog.Regions.FirstOrDefault();
    if (region is null) return Results.NotFound();
    var x = (region.MinX + region.MaxX) / 2;
    var y = (region.MinY + region.MaxY) / 2;
    var clusterTcp = $"world-{region.Name}:9090";
    var localTcp = region.TcpPort > 0 ? $"localhost:{region.TcpPort}" : null;
    var payload = new SpawnResponse
    {
        RegionName = region.Name,
        X = x,
        Y = y,
        ClusterTcp = clusterTcp,
        LocalTcp = localTcp
    };
    return Results.Json(payload);
});
app.MapGet("/directory/regions", () =>
{
    var list = monitor.GetStatuses();
    return Results.Json(list);
});
app.MapPost("/directory/resolve", async (ResolveRequest req) =>
{
    var regions = catalog.Regions;
    var region = regions.Find(r => string.Equals(r.Name, req.RegionName, StringComparison.OrdinalIgnoreCase));
    if (region == null) return Results.NotFound();
    var ghost = req.GhostZoneWidth <= 0 ? 1 : req.GhostZoneWidth;
    string nextRegion = null;
    if (req.X <= region.MinX + ghost && !string.IsNullOrWhiteSpace(region.Neighbors.West)) nextRegion = region.Neighbors.West;
    else if (req.X >= region.MaxX - ghost && !string.IsNullOrWhiteSpace(region.Neighbors.East)) nextRegion = region.Neighbors.East;
    else if (req.Y <= region.MinY + ghost && !string.IsNullOrWhiteSpace(region.Neighbors.South)) nextRegion = region.Neighbors.South;
    else if (req.Y >= region.MaxY - ghost && !string.IsNullOrWhiteSpace(region.Neighbors.North)) nextRegion = region.Neighbors.North;
    var status = monitor.GetStatus(region.Name);
    var result = new ResolveResponse
    {
        CurrentRegion = region.Name,
        Online = status?.Online ?? false,
        ClusterTcp = status?.ClusterTcp,
        LocalTcp = status?.LocalTcp,
        NextRegion = nextRegion
    };
    return Results.Json(result);
});
app.Run();

record ResolveRequest(string RegionName, int X, int Y, int GhostZoneWidth);
record ResolveResponse
{
    public string CurrentRegion { get; set; }
    public bool Online { get; set; }
    public string ClusterTcp { get; set; }
    public string LocalTcp { get; set; }
    public string NextRegion { get; set; }
}

class RegionCatalog
{
    public string Namespace { get; }
    public int BasePort { get; }
    public List<RegionDef> Regions { get; }
    public RegionCatalog()
    {
        var path = Environment.GetEnvironmentVariable("REGIONS_JSON_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "d:\\Dev\\pokeMMo\\world.regions.json";
        }
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Namespace = root.TryGetProperty("namespace", out var nsEl) ? nsEl.GetString() ?? "creature-realms" : "creature-realms";
        BasePort = root.TryGetProperty("basePort", out var bpEl) ? bpEl.GetInt32() : 9100;
        Regions = new List<RegionDef>();
        foreach (var r in root.GetProperty("regions").EnumerateArray())
        {
            var name = r.GetProperty("name").GetString();
            var minX = r.GetProperty("minX").GetInt32();
            var maxX = r.GetProperty("maxX").GetInt32();
            var minY = r.GetProperty("minY").GetInt32();
            var maxY = r.GetProperty("maxY").GetInt32();
            var tcpPort = r.TryGetProperty("tcpPort", out var tpEl) ? tpEl.GetInt32() : -1;
            var neighborsEl = r.TryGetProperty("neighbors", out var nEl) ? nEl : default;
            var neighbors = new Neighbors
            {
                East = neighborsEl.ValueKind == JsonValueKind.Object && neighborsEl.TryGetProperty("east", out var e) ? e.GetString() : null,
                West = neighborsEl.ValueKind == JsonValueKind.Object && neighborsEl.TryGetProperty("west", out var w) ? w.GetString() : null,
                North = neighborsEl.ValueKind == JsonValueKind.Object && neighborsEl.TryGetProperty("north", out var n) ? n.GetString() : null,
                South = neighborsEl.ValueKind == JsonValueKind.Object && neighborsEl.TryGetProperty("south", out var s) ? s.GetString() : null
            };
            Regions.Add(new RegionDef
            {
                Name = name,
                MinX = minX,
                MaxX = maxX,
                MinY = minY,
                MaxY = maxY,
                TcpPort = tcpPort,
                Neighbors = neighbors
            });
        }
    }
}

class RegionMonitor
{
    private readonly RegionCatalog _catalog;
    private readonly Dictionary<string, RegionStatus> _statuses = new();
    public RegionMonitor(RegionCatalog catalog)
    {
        _catalog = catalog;
        foreach (var r in _catalog.Regions)
        {
            _statuses[r.Name] = new RegionStatus { Name = r.Name };
        }
    }
    public IReadOnlyCollection<RegionStatus> GetStatuses() => _statuses.Values;
    public RegionStatus GetStatus(string name) => _statuses.TryGetValue(name, out var s) ? s : null;
    public async Task CheckAsync()
    {
        var ns = _catalog.Namespace;
        foreach (var r in _catalog.Regions)
        {
            var clusterTcp = $"world-{r.Name}:9090";
            var localTcp = r.TcpPort > 0 ? $"localhost:{r.TcpPort}" : null;
            var httpUrl = $"http://world-{r.Name}:8082/healthz";
            var online = false;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var resp = await client.GetAsync(httpUrl);
                online = resp.IsSuccessStatusCode;
            }
            catch
            {
                try
                {
                    using var tcp = new TcpClient();
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await tcp.ConnectAsync($"world-{r.Name}", 9090, cts.Token);
                    online = tcp.Connected;
                }
                catch { online = false; }
            }
            var s = _statuses[r.Name];
            s.Name = r.Name;
            s.Online = online;
            s.ClusterTcp = clusterTcp;
            s.LocalTcp = localTcp;
            s.LastChecked = DateTime.UtcNow;
        }
    }
}

class RegionMonitorService : BackgroundService
{
    private readonly RegionMonitor _monitor;
    public RegionMonitorService(RegionMonitor monitor) { _monitor = monitor; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _monitor.CheckAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}

class RegionDef
{
    public string Name { get; set; }
    public int MinX { get; set; }
    public int MaxX { get; set; }
    public int MinY { get; set; }
    public int MaxY { get; set; }
    public int TcpPort { get; set; }
    public Neighbors Neighbors { get; set; }
}

class Neighbors
{
    public string East { get; set; }
    public string West { get; set; }
    public string North { get; set; }
    public string South { get; set; }
}

class RegionStatus
{
    public string Name { get; set; }
    public bool Online { get; set; }
    public string ClusterTcp { get; set; }
    public string LocalTcp { get; set; }
    public DateTime LastChecked { get; set; }
}

class SpawnResponse
{
    public string RegionName { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string ClusterTcp { get; set; }
    public string LocalTcp { get; set; }
}
