using System;
using System.Collections.Generic;
using System.Text;

namespace World.Server.Networking;

public static class PacketUtils
{
    public static void WriteInt(byte[] buf, int offset, int value)
    {
        buf[offset + 0] = (byte)((value >> 24) & 0xFF);
        buf[offset + 1] = (byte)((value >> 16) & 0xFF);
        buf[offset + 2] = (byte)((value >> 8) & 0xFF);
        buf[offset + 3] = (byte)(value & 0xFF);
    }

    public static int ReadInt(byte[] buf, int offset)
    {
        var b0 = buf[offset + 0];
        var b1 = buf[offset + 1];
        var b2 = buf[offset + 2];
        var b3 = buf[offset + 3];
        return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
    }

    public static byte[] BuildHandoffMessage(string regionName, string host, int port, int x, int y)
    {
        var regionBytes = Encoding.ASCII.GetBytes(regionName ?? "");
        var hostBytes = Encoding.ASCII.GetBytes(host);
        var len = 4 + regionBytes.Length + 4 + hostBytes.Length + 4 + 4 + 4;
        var buf = new byte[3 + len];
        buf[0] = 1;
        buf[1] = (byte)PacketType.Handoff;
        buf[2] = (byte)len;
        WriteInt(buf, 3, regionBytes.Length);
        Array.Copy(regionBytes, 0, buf, 7, regionBytes.Length);
        var off = 7 + regionBytes.Length;
        WriteInt(buf, off, hostBytes.Length);
        Array.Copy(hostBytes, 0, buf, off + 4, hostBytes.Length);
        var off2 = off + 4 + hostBytes.Length;
        WriteInt(buf, off2, port);
        WriteInt(buf, off2 + 4, x);
        WriteInt(buf, off2 + 8, y);
        return buf;
    }

    public static byte[] BuildGhostHintMessage(PacketType type, string regionName, string host, int port)
    {
        var regionBytes = Encoding.ASCII.GetBytes(regionName ?? "");
        var hostBytes = Encoding.ASCII.GetBytes(host);
        var len = 4 + regionBytes.Length + 4 + hostBytes.Length + 4;
        var buf = new byte[3 + len];
        buf[0] = 1;
        buf[1] = (byte)type;
        buf[2] = (byte)len;
        WriteInt(buf, 3, regionBytes.Length);
        Array.Copy(regionBytes, 0, buf, 7, regionBytes.Length);
        var off = 7 + regionBytes.Length;
        WriteInt(buf, off, hostBytes.Length);
        Array.Copy(hostBytes, 0, buf, off + 4, hostBytes.Length);
        var off2 = off + 4 + hostBytes.Length;
        WriteInt(buf, off2, port);
        return buf;
    }

    public static byte[] BuildGhostZoneInfoMessage(int gz, int minX, int maxX, int minY, int maxY)
    {
        var len = 4 * 5;
        var buf = new byte[3 + len];
        buf[0] = 1;
        buf[1] = (byte)PacketType.GhostZoneInfo;
        buf[2] = (byte)len;
        WriteInt(buf, 3, gz);
        WriteInt(buf, 7, minX);
        WriteInt(buf, 11, maxX);
        WriteInt(buf, 15, minY);
        WriteInt(buf, 19, maxY);
        return buf;
    }

    public static byte[] BuildDeadZonesMessage(List<(int x, int y)> items)
    {
        var count = items?.Count ?? 0;
        var len = 4 + count * 8;
        var buf = new byte[3 + len];
        buf[0] = 1;
        buf[1] = (byte)PacketType.DeadZones;
        buf[2] = (byte)len;
        WriteInt(buf, 3, count);
        var off = 7;
        for (int i = 0; i < count; i++)
        {
            WriteInt(buf, off, items[i].x);
            WriteInt(buf, off + 4, items[i].y);
            off += 8;
        }
        return buf;
    }
}
