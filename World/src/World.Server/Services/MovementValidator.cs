using System;
using World.Server.Models;

namespace World.Server.Services;

public class MovementValidator
{
    public bool IsWalkable(MapData map, int x, int y)
    {
        if (map.Objects.TryGetValue((x, y), out var objId))
        {
            if (map.ObjectDefs.TryGetValue(objId, out var od))
            {
                if (od.blocksMovement) return false;
            }
        }
        if (map.Tiles.TryGetValue((x, y), out var tileId))
        {
            if (map.TileDefs.TryGetValue(tileId, out var def))
            {
                if (!def.isWalkable) return false;
                return true;
            }
            return true;
        }
        return true;
    }
    public bool IsAreaWalkable(MapData map, float centerX, float centerY, float halfSize = 0.5f)
    {
        var left = centerX - halfSize;
        var right = centerX + halfSize;
        var bottom = centerY - halfSize;
        var top = centerY + halfSize;
        var txMin = (int)Math.Ceiling(left - 0.5f);
        var txMax = (int)Math.Floor(right + 0.5f);
        var tyMin = (int)Math.Ceiling(bottom - 0.5f);
        var tyMax = (int)Math.Floor(top + 0.5f);
        for (var tx = txMin; tx <= txMax; tx++)
        {
            for (var ty = tyMin; ty <= tyMax; ty++)
            {
                var tileLeft = tx - 0.5f;
                var tileRight = tx + 0.5f;
                var tileBottom = ty - 0.5f;
                var tileTop = ty + 0.5f;
                var intersects = left < tileRight && right > tileLeft && bottom < tileTop && top > tileBottom;
                if (!intersects) continue;
                if (map.Objects.TryGetValue((tx, ty), out var objId))
                {
                    if (map.ObjectDefs.TryGetValue(objId, out var od))
                    {
                        if (od.blocksMovement) return false;
                    }
                }
                if (map.Tiles.TryGetValue((tx, ty), out var tileId))
                {
                    if (map.TileDefs.TryGetValue(tileId, out var def))
                    {
                        if (!def.isWalkable) return false;
                    }
                }
            }
        }
        return true;
    }
}
