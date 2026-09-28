using System.IO.Compression;
using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>How big a screen is when the server sends it to a player. Mirrors the runtime's UiCompression limits:
/// the JSON is deflate-compressed and must stay under both limits.</summary>
public static class NetworkSize
{
    public const int MaxPacked = 900_000, MaxJsonBytes = 8_000_000;
    public static (int Json, int Packed) Of(UiDefinition screen)
    {
        byte[] raw = System.Text.Encoding.UTF8.GetBytes(Json.Write(screen));
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(raw);
        return (raw.Length, (int)output.Length);
    }
}
