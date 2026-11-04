using System.Buffers.Binary;

namespace Sketchora.Core;

public static class EmbeddedPng
{
    public const int MaximumBytes = 12 * 1024 * 1024;
    public static (uint Width, uint Height) Dimensions(string data)
    {
        Validate(data); var bytes = Convert.FromBase64String(data);
        return (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)), BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)));
    }
    public static void Validate(string data)
    {
        if (data.Length > MaximumBytes / 3 * 4 + 4) throw new InvalidDataException("An imported image exceeds 12 MiB.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid embedded image.", ex); }
        ValidateBytes(bytes);
    }
    public static void ValidateBytes(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length is < 33 or > MaximumBytes || !bytes[..8].SequenceEqual(signature)
            || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8)) throw new InvalidDataException("The page image must be a bounded PNG.");
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        if (width == 0 || height == 0 || width > 32768 || height > 32768 || (long)width * height > 32_000_000)
            throw new InvalidDataException("The imported image exceeds the 32 megapixel limit.");
    }
}
