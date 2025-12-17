using System;
using World.Server.Models;

namespace World.Server.Services;

public class ChunkManager
{
    public int ChunkSize { get; } = 32;
    public (int cx, int cy) GetChunkForPosition(int x, int y, int? size = null)
    {
        var s = size.HasValue && size.Value > 0 ? size.Value : ChunkSize;
        var cx = (int)Math.Floor(x / (double)s);
        var cy = (int)Math.Floor(y / (double)s);
        return (cx, cy);
    }
    public object DescribeGrid(MapData map, int? size = null)
    {
        var s = size.HasValue && size.Value > 0 ? size.Value : ChunkSize;
        var chunksX = (int)Math.Ceiling(map.Width / (double)s);
        var chunksY = (int)Math.Ceiling(map.Height / (double)s);
        return new
        {
            chunkSize = s,
            width = map.Width,
            height = map.Height,
            chunksX = chunksX,
            chunksY = chunksY
        };
    }
}
