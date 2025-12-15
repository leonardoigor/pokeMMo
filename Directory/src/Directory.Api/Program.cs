using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Observability.Extensions;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<RegionCatalog>();
builder.Services.AddSingleton<KubeClient>();
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
app.MapGet("/directory/spawn", async () =>
{
    var kube = app.Services.GetRequiredService<KubeClient>();
    var statuses = monitor.GetStatuses();
    var ns = catalog.Namespace;
    var services = await kube.GetWorldServicesAsync(ns);
    var svc = services.Where(s => s.TcpNodePort > 0).FirstOrDefault()
              ?? services.FirstOrDefault();
    var chosenName = svc?.Name ?? (statuses.Where(s => s.Online).FirstOrDefault() ?? statuses.FirstOrDefault())?.Name;
    if (string.IsNullOrWhiteSpace(chosenName)) return Results.NotFound();
    var boundsUrl = $"http://{chosenName}:8082/world/region/bounds";
    int x = 0, y = 0;
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var resp = await client.GetAsync(boundsUrl);
        if (resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var minX = root.GetProperty("minX").GetInt32();
            var maxX = root.GetProperty("maxX").GetInt32();
            var minY = root.GetProperty("minY").GetInt32();
            var maxY = root.GetProperty("maxY").GetInt32();
            x = (minX + maxX) / 2;
            y = (minY + maxY) / 2;
        }
    }
    catch { }
    var tcpNodePort = services.FirstOrDefault(s => s.Name == chosenName)?.TcpNodePort ?? 0;
    var clusterTcp = $"{chosenName}:9090";
    var localTcp = tcpNodePort > 0 ? $"localhost:{tcpNodePort}" : null;
    var payload = new SpawnResponse
    {
        RegionName = chosenName,
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
    private readonly KubeClient _kube;
    private readonly Dictionary<string, RegionStatus> _statuses = new();
    public RegionMonitor(RegionCatalog catalog, KubeClient kube)
    {
        _catalog = catalog;
        _kube = kube;
    }
    public IReadOnlyCollection<RegionStatus> GetStatuses() => _statuses.Values;
    public RegionStatus GetStatus(string name) => _statuses.TryGetValue(name, out var s) ? s : null;
    public async Task CheckAsync()
    {
        var ns = _catalog.Namespace;
        var services = await _kube.GetWorldServicesAsync(ns);
        var names = new HashSet<string>(services.Select(s => s.Name));
        foreach (var key in _statuses.Keys.ToList())
        {
            if (!names.Contains(key)) _statuses.Remove(key);
        }
        foreach (var svc in services)
        {
            var clusterTcp = $"{svc.Name}:9090";
            var localTcp = svc.TcpNodePort > 0 ? $"localhost:{svc.TcpNodePort}" : null;
            var httpUrl = $"http://{svc.Name}:8082/healthz";
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
                    await tcp.ConnectAsync(svc.Name, 9090, cts.Token);
                    online = tcp.Connected;
                }
                catch { online = false; }
            }
            var boundsUrl = $"http://{svc.Name}:8082/world/region/bounds";
            int minX = 0, maxX = 0, minY = 0, maxY = 0;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var resp = await client.GetAsync(boundsUrl);
                if (resp.IsSuccessStatusCode)
                {
                    var text = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    minX = root.GetProperty("minX").GetInt32();
                    maxX = root.GetProperty("maxX").GetInt32();
                    minY = root.GetProperty("minY").GetInt32();
                    maxY = root.GetProperty("maxY").GetInt32();
                }
            }
            catch { }
            if (!_statuses.TryGetValue(svc.Name, out var s)) { s = new RegionStatus(); _statuses[svc.Name] = s; }
            s.Name = svc.Name;
            s.Online = online;
            s.ClusterTcp = clusterTcp;
            s.LocalTcp = localTcp;
            s.TcpNodePort = svc.TcpNodePort;
            s.HttpNodePort = svc.HttpNodePort;
            s.MinX = minX;
            s.MaxX = maxX;
            s.MinY = minY;
            s.MaxY = maxY;
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
        await _monitor.CheckAsync();
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
    public int TcpNodePort { get; set; }
    public int HttpNodePort { get; set; }
    public int MinX { get; set; }
    public int MaxX { get; set; }
    public int MinY { get; set; }
    public int MaxY { get; set; }
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

class KubeWorldService
{
    public string Name { get; set; }
    public int TcpNodePort { get; set; }
    public int HttpNodePort { get; set; }
}

class KubeClient
{
    public async Task<List<KubeWorldService>> GetWorldServicesAsync(string ns)
    {
        var list = new List<KubeWorldService>();
        var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
        var port = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");
        if (string.IsNullOrWhiteSpace(host)) host = "kubernetes.default.svc";
        if (string.IsNullOrWhiteSpace(port)) port = "443";
        var baseUrl = $"https://{host}:{port}";
        var tokenPath = "/var/run/secrets/kubernetes.io/serviceaccount/token";
        string token = null;
        if (File.Exists(tokenPath)) token = await File.ReadAllTextAsync(tokenPath);
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        if (!string.IsNullOrWhiteSpace(token)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var url = $"{baseUrl}/api/v1/namespaces/{ns}/services";
        try
        {
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return list;
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            var items = doc.RootElement.GetProperty("items");
            foreach (var it in items.EnumerateArray())
            {
                var meta = it.GetProperty("metadata");
                var name = meta.GetProperty("name").GetString();
                var spec = it.GetProperty("spec");
                var type = spec.GetProperty("type").GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!name.StartsWith("world-")) continue;
                if (!string.Equals(type, "NodePort", StringComparison.OrdinalIgnoreCase)) continue;
                int tcpPort = 0, httpPort = 0;
                foreach (var p in spec.GetProperty("ports").EnumerateArray())
                {
                    var pname = p.GetProperty("name").GetString();
                    var nodePortProp = p.TryGetProperty("nodePort", out var np) ? np : default;
                    var nodePort = nodePortProp.ValueKind == JsonValueKind.Number ? nodePortProp.GetInt32() : 0;
                    if (string.Equals(pname, "tcp", StringComparison.OrdinalIgnoreCase)) tcpPort = nodePort;
                    else if (string.Equals(pname, "http", StringComparison.OrdinalIgnoreCase)) httpPort = nodePort;
                }
                list.Add(new KubeWorldService { Name = name, TcpNodePort = tcpPort, HttpNodePort = httpPort });
            }
        }
        catch
        {
        }
        return list;
    }
}
