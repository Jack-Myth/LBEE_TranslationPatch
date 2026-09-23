using System.Buffers.Binary;
using System.Text;

namespace LBEE_TranslationPatch;

/// <summary>Switch script string: signed little-endian Int16 length, payload, terminator.</summary>
public readonly record struct SwitchString(string Text, bool IsUtf8, int Offset, int Size)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);

    public int End => Offset + Size;

    public static SwitchString Read(byte[] data, int offset)
    {
        if (offset < 0 || offset > data.Length - 2)
            throw new InvalidDataException($"String length missing at 0x{offset:X}.");
        int length = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, 2));
        // Empty strings are represented by the length alone, with no payload/terminator.
        if (length == 0) return new SwitchString(string.Empty, false, offset, 2);
        bool utf8 = length < 0;
        // Positive lengths count UTF-16 code units; negative lengths count UTF-8 bytes.
        int bytes = utf8 ? -length : length * 2;
        int terminator = utf8 ? 1 : 2;
        int size = 2 + bytes + terminator;
        if (size > data.Length - offset)
            throw new InvalidDataException($"String at 0x{offset:X} extends past the buffer (length {length}).");
        for (int i = 0; i < terminator; i++)
            if (data[offset + 2 + bytes + i] != 0)
                throw new InvalidDataException($"String at 0x{offset:X} has no terminator at its declared end.");
        string text = (utf8 ? Utf8 : Utf16).GetString(data, offset + 2, bytes);
        if (text.Contains('\0')) throw new InvalidDataException($"Embedded null in string at 0x{offset:X}.");
        return new SwitchString(text, utf8, offset, size);
    }

    public static byte[] Encode(string text, bool utf8)
    {
        if (text.Contains('\0')) throw new InvalidDataException("Script text cannot contain null characters.");
        if (text.Length == 0) return [0, 0];
        byte[] payload = (utf8 ? Utf8 : Utf16).GetBytes(text);
        int length = utf8 ? -payload.Length : payload.Length / 2;
        if (length < short.MinValue || length > short.MaxValue)
            throw new InvalidDataException("Script string exceeds the signed Int16 length limit.");
        byte[] result = new byte[2 + payload.Length + (utf8 ? 1 : 2)];
        BinaryPrimitives.WriteInt16LittleEndian(result, (short)length);
        payload.CopyTo(result, 2);
        return result;
    }
}
