namespace World.Server.Models;

public record TileCell(int x, int y, string tileId);
public record ObjectCell(int x, int y, string objectId);
public record TeleportData(int x, int y, string targetRegion, int targetX, int targetY);
public record MapJson(int width, int height, TileCell[] tiles, ObjectCell[] objects, TeleportData[] teleports);
public record TileDefJson(string tileId, string nome, bool isWalkable, bool blocksVision);
public record ObjectDefJson(string objectId, string nome, bool blocksMovement, bool interactable);
