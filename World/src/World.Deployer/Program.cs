using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Linq;

string Arg(string name, string def = "")
{
    for (int i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (a.StartsWith("--"))
        {
            var eq = a.IndexOf('=');
            if (eq > 2)
            {
                var k = a.Substring(2, eq - 2);
                if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
                {
                    return a.Substring(eq + 1);
                }
            }
            else if (string.Equals(a.Substring(2), name, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) return args[i + 1];
            }
        }
    }
    return def;
}

int ArgInt(string name, int def = 0)
{
    var s = Arg(name, "");
    if (int.TryParse(s, out var n)) return n;
    return def;
}

void Log(string msg) => Console.WriteLine("[World.Deployer] " + msg);

string DetectConfigPath()
{
    var p = Arg("config", "");
    if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
    if (args.Length > 0 && !args[0].StartsWith("--") && File.Exists(args[0])) return args[0];
    var baseDir = AppContext.BaseDirectory;
    var defaultPath = Path.Combine(baseDir, "world.regions.json");
    if (File.Exists(defaultPath)) return defaultPath;
    var anyJson = Directory.EnumerateFiles(baseDir, "*.json").FirstOrDefault();
    if (!string.IsNullOrEmpty(anyJson)) return anyJson;
    return "";
}
var configPath = DetectConfigPath();
string ns = Arg("namespace", "creature-realms");
string image = Arg("image", "igormendonca/world:latest");
bool skipBuild = string.Equals(Arg("skip-build", "false"), "true", StringComparison.OrdinalIgnoreCase);
bool skipPush = string.Equals(Arg("skip-push", "false"), "true", StringComparison.OrdinalIgnoreCase);

string ResolveNeighbor(string n)
{
    if (string.IsNullOrWhiteSpace(n)) return "";
    if (n.Contains(":")) return n;
    return $"world-{n}:9090";
}

string RepoRoot()
{
    var cwd = Directory.GetCurrentDirectory();
    return cwd;
}

int Run(string fileName, string args, string? cwd = null)
{
    Log($"exec {fileName} {args}");
    var psi = new ProcessStartInfo(fileName, args);
    psi.RedirectStandardOutput = true;
    psi.RedirectStandardError = true;
    psi.UseShellExecute = false;
    if (!string.IsNullOrWhiteSpace(cwd)) psi.WorkingDirectory = cwd;
    var p = Process.Start(psi);
    if (p == null)
    {
        Log("Falha ao iniciar processo");
        return -1;
    }
    var outTask = p.StandardOutput.ReadToEndAsync();
    var errTask = p.StandardError.ReadToEndAsync();
    p.WaitForExit();
    var stdout = outTask.Result;
    var stderr = errTask.Result;
    if (!string.IsNullOrEmpty(stdout)) Console.WriteLine(stdout);
    if (!string.IsNullOrEmpty(stderr)) Console.Error.WriteLine(stderr);
    Log($"exit {p.ExitCode}");
    return p.ExitCode;
}

int Deploy(string ns, string image, string name, int minX, int maxX, int minY, int maxY, string east, string west, string north, string south, int nodePortTcp, int nodePortHttp)
{
    var httpNodeLine = nodePortHttp > 0 ? $"      nodePort: {nodePortHttp}" : "";
    var tcpNodeLine = nodePortTcp > 0 ? $"      nodePort: {nodePortTcp}" : "";
    var yaml = new StringBuilder();
    yaml.AppendLine("apiVersion: apps/v1");
    yaml.AppendLine("kind: Deployment");
    yaml.AppendLine("metadata:");
    yaml.AppendLine($"  name: world-{name}");
    yaml.AppendLine($"  namespace: {ns}");
    yaml.AppendLine("  labels:");
    yaml.AppendLine("    app.kubernetes.io/name: world-" + name);
    yaml.AppendLine("    app.kubernetes.io/part-of: creature-realms");
    yaml.AppendLine("    app.kubernetes.io/component: backend");
    yaml.AppendLine("spec:");
    yaml.AppendLine("  replicas: 1");
    yaml.AppendLine("  selector:");
    yaml.AppendLine("    matchLabels:");
    yaml.AppendLine("      app.kubernetes.io/name: world-" + name);
    yaml.AppendLine("  template:");
    yaml.AppendLine("    metadata:");
    yaml.AppendLine("      labels:");
    yaml.AppendLine("        app.kubernetes.io/name: world-" + name);
    yaml.AppendLine("        app.kubernetes.io/part-of: creature-realms");
    yaml.AppendLine("        app.kubernetes.io/component: backend");
    yaml.AppendLine("    spec:");
    yaml.AppendLine("      containers:");
    yaml.AppendLine("        - name: world");
    yaml.AppendLine("          image: " + image);
    yaml.AppendLine("          imagePullPolicy: Always");
    yaml.AppendLine("          ports:");
    yaml.AppendLine("            - name: http");
    yaml.AppendLine("              containerPort: 8082");
    yaml.AppendLine("            - name: tcp");
    yaml.AppendLine("              containerPort: 9090");
    yaml.AppendLine("              protocol: TCP");
    yaml.AppendLine("          env:");
    yaml.AppendLine("            - name: ASPNETCORE_URLS");
    yaml.AppendLine("              value: http://+:8082");
    yaml.AppendLine("            - name: MAP_DATA_DIR");
    yaml.AppendLine("              value: /app/data");
    yaml.AppendLine("            - name: PLAYER_MOVE_INTERVAL_MS");
    yaml.AppendLine("              value: \"200\"");
    yaml.AppendLine("            - name: OTEL__Endpoint");
    yaml.AppendLine("              value: http://otel-collector:4318");
    yaml.AppendLine("            - name: Logging__Elasticsearch__ShipTo__NodeUris__0");
    yaml.AppendLine("              value: http://elasticsearch:9200");
    yaml.AppendLine("            - name: Logging__Elasticsearch__Index");
    yaml.AppendLine("              value: dotnet-{0:yyyy.MM.dd}");
    yaml.AppendLine("            - name: REGION_NAME");
    yaml.AppendLine($"              value: \"{name}\"");
    yaml.AppendLine("            - name: REGION_MIN_X");
    yaml.AppendLine($"              value: \"{minX}\"");
    yaml.AppendLine("            - name: REGION_MAX_X");
    yaml.AppendLine($"              value: \"{maxX}\"");
    yaml.AppendLine("            - name: REGION_MIN_Y");
    yaml.AppendLine($"              value: \"{minY}\"");
    yaml.AppendLine("            - name: REGION_MAX_Y");
    yaml.AppendLine($"              value: \"{maxY}\"");
    yaml.AppendLine("            - name: NEIGHBOR_EAST");
    yaml.AppendLine($"              value: \"{ResolveNeighbor(east)}\"");
    yaml.AppendLine("            - name: NEIGHBOR_WEST");
    yaml.AppendLine($"              value: \"{ResolveNeighbor(west)}\"");
    yaml.AppendLine("            - name: NEIGHBOR_NORTH");
    yaml.AppendLine($"              value: \"{ResolveNeighbor(north)}\"");
    yaml.AppendLine("            - name: NEIGHBOR_SOUTH");
    yaml.AppendLine($"              value: \"{ResolveNeighbor(south)}\"");
    yaml.AppendLine("          readinessProbe:");
    yaml.AppendLine("            httpGet:");
    yaml.AppendLine("              path: /healthz");
    yaml.AppendLine("              port: 8082");
    yaml.AppendLine("            initialDelaySeconds: 5");
    yaml.AppendLine("            periodSeconds: 10");
    yaml.AppendLine("          livenessProbe:");
    yaml.AppendLine("            httpGet:");
    yaml.AppendLine("              path: /healthz");
    yaml.AppendLine("              port: 8082");
    yaml.AppendLine("            initialDelaySeconds: 30");
    yaml.AppendLine("            periodSeconds: 10");
    yaml.AppendLine("---");
    yaml.AppendLine("apiVersion: v1");
    yaml.AppendLine("kind: Service");
    yaml.AppendLine("metadata:");
    yaml.AppendLine("  name: world-" + name);
    yaml.AppendLine("  namespace: " + ns);
    yaml.AppendLine("  labels:");
    yaml.AppendLine("    app.kubernetes.io/name: world-" + name);
    yaml.AppendLine("    app.kubernetes.io/part-of: creature-realms");
    yaml.AppendLine("    app.kubernetes.io/component: backend");
    yaml.AppendLine("spec:");
    yaml.AppendLine("  selector:");
    yaml.AppendLine("    app.kubernetes.io/name: world-" + name);
    yaml.AppendLine("  type: NodePort");
    yaml.AppendLine("  ports:");
    yaml.AppendLine("    - name: http");
    yaml.AppendLine("      port: 8082");
    yaml.AppendLine("      targetPort: 8082");
    yaml.AppendLine("      protocol: TCP");
    if (!string.IsNullOrWhiteSpace(httpNodeLine)) yaml.AppendLine(httpNodeLine);
    yaml.AppendLine("    - name: tcp");
    yaml.AppendLine("      port: 9090");
    yaml.AppendLine("      targetPort: 9090");
    yaml.AppendLine("      protocol: TCP");
    if (!string.IsNullOrWhiteSpace(tcpNodeLine)) yaml.AppendLine(tcpNodeLine);
    var tmp = Path.Combine(Path.GetTempPath(), $"world-{name}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.yaml");
    File.WriteAllText(tmp, yaml.ToString(), Encoding.UTF8);
    Log("YAML escrito em " + tmp);
    var nsExists = Run("kubectl", $"get namespace {ns} --no-headers");
    if (nsExists != 0)
    {
        var createNs = Run("kubectl", $"create namespace {ns}");
        if (createNs != 0) return createNs;
    }
    var apply = Run("kubectl", $"apply -f \"{tmp}\"");
    if (apply != 0) return apply;
    var restart = Run("kubectl", $"rollout restart deployment world-{name} -n {ns}");
    if (restart != 0) return restart;
    var status = Run("kubectl", $"rollout status deployment/world-{name} -n {ns} --timeout=60s");
    return status;
}

if (!string.IsNullOrWhiteSpace(configPath))
{
    var text = File.ReadAllText(configPath);
    using var doc = JsonDocument.Parse(text);
    var root = doc.RootElement;
    ns = root.TryGetProperty("namespace", out var nsEl) ? (nsEl.GetString() ?? ns) : ns;
    image = root.TryGetProperty("image", out var imgEl) ? (imgEl.GetString() ?? image) : image;
    if (!skipBuild)
    {
        var exit = Run("docker", $"build -f World/Dockerfile -t {image} .", RepoRoot());
        if (exit != 0) Environment.Exit(exit);
    }
    if (!skipPush)
    {
        var exit = Run("docker", $"push {image}");
        if (exit != 0) Environment.Exit(exit);
    }
    var regions = root.GetProperty("regions").EnumerateArray().ToList();
    foreach (var r in regions)
    {
        var name = r.GetProperty("name").GetString() ?? "";
        var minX = r.GetProperty("minX").GetInt32();
        var maxX = r.GetProperty("maxX").GetInt32();
        var minY = r.GetProperty("minY").GetInt32();
        var maxY = r.GetProperty("maxY").GetInt32();
        var neighborsEl = r.TryGetProperty("neighbors", out var nEl) ? nEl : default;
        var east = neighborsEl.ValueKind == JsonValueKind.Object && neighborsEl.TryGetProperty("east", out var e) ? (e.ValueKind == JsonValueKind.String ? e.GetString() : null) : null;
        var west = neighborsEl.ValueKind == JsonValueKind.Object && neighborsEl.TryGetProperty("west", out var w) ? (w.ValueKind == JsonValueKind.String ? w.GetString() : null) : null;
        var north = neighborsEl.ValueKind == JsonValueKind.Object && neighborsEl.TryGetProperty("north", out var n) ? (n.ValueKind == JsonValueKind.String ? n.GetString() : null) : null;
        var south = neighborsEl.ValueKind == JsonValueKind.Object && neighborsEl.TryGetProperty("south", out var s) ? (s.ValueKind == JsonValueKind.String ? s.GetString() : null) : null;
        var exit = Deploy(ns, image, name, minX, maxX, minY, maxY, east ?? "", west ?? "", north ?? "", south ?? "", 0, 0);
        if (exit != 0) Environment.Exit(exit);
    }
    Log("OK");
    Environment.Exit(0);
}
else
{
    var name = Arg("name");
    if (string.IsNullOrWhiteSpace(name))
    {
        Log("Nome vazio. Use --name NOME ou informe um arquivo JSON");
        Environment.Exit(1);
    }
    var minX = ArgInt("minx");
    var maxX = ArgInt("maxx");
    var minY = ArgInt("miny");
    var maxY = ArgInt("maxy");
    var east = Arg("east", "");
    var west = Arg("west", "");
    var north = Arg("north", "");
    var south = Arg("south", "");
    var nodePortTcp = ArgInt("nodeport-tcp", 0);
    var nodePortHttp = ArgInt("nodeport-http", 0);
    if (!skipBuild)
    {
        var exit = Run("docker", $"build -f World/Dockerfile -t {image} .", RepoRoot());
        if (exit != 0) Environment.Exit(exit);
    }
    if (!skipPush)
    {
        var exit = Run("docker", $"push {image}");
        if (exit != 0) Environment.Exit(exit);
    }
    var applyExit = Deploy(ns, image, name, minX, maxX, minY, maxY, east, west, north, south, nodePortTcp, nodePortHttp);
    if (applyExit != 0) Environment.Exit(applyExit);
    Log("OK");
}
