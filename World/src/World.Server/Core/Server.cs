using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using World.Server.Models;
using World.Server.Networking;
using World.Server.Services;

namespace World.Server.Core;

public class Server : BackgroundService
{
    private readonly MapStore _store;
    private readonly WorldManager _world;
    private readonly PacketRouter _router;
    private readonly ILogger<Server> _logger;

    public Server(MapStore store, WorldManager world, PacketRouter router, ILogger<Server> logger)
    {
        _store = store;
        _world = world;
        _router = router;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_store.Maps.Any())
        {
            _logger.LogCritical("no_maps_loaded");
            Environment.Exit(2);
        }

        var listener = new TcpListener(System.Net.IPAddress.Any, 9090);
        listener.Start();
        _logger.LogInformation("socket_listen port={port}", 9090);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClient(client, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "socket_accept_error");
            }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        using var c = client;
        var session = _world.CreateSession(c);
        
        try
        {
            var stream = c.GetStream();
            var header = new byte[3];
            
            while (!ct.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(header, 0, 3, ct);
                if (read != 3) break;
                
                var type = (PacketType)header[1];
                var len = header[2];
                var payload = new byte[len];
                
                var off = 0;
                while (off < len)
                {
                    var r = await stream.ReadAsync(payload, off, len - off, ct);
                    if (r <= 0) break;
                    off += r;
                }
                
                if (off != len) break;

                await _router.RouteAsync(session, type, payload, ct);
            }
        }
        catch (Exception)
        {
            // Ignored
        }
        finally
        {
            _world.RemoveSession(c);
        }
    }
}
