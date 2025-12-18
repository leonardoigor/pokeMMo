namespace World.Server.Networking;

public enum PacketType : byte
{
    MoveRequest = 0,
    PositionUpdate = 1,
    Handoff = 2,
    ClientConfig = 3,
    GhostZoneEnter = 4,
    GhostZoneLeave = 5,
    GhostZoneInfo = 6,
    DeadZones = 7,
    PlayersSnapshot = 8,
    PlayerInfo = 9,
    ProvisionalConnection = 10,
    MoveState = 11
}
