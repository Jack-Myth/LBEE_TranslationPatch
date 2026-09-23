using System.Buffers.Binary;

namespace LBEE_TranslationPatch;

/// <summary>String tables in seen8500/seen8501, inside a BTFUNC following startup commands.</summary>
public sealed class SwitchTextTable
{
    private readonly byte[] source;
    private readonly int commandOffset;
    private readonly int payloadEnd;
    private readonly int tailOffset;
    public List<SwitchString> Strings { get; } = [];

    public SwitchTextTable(byte[] data)
    {
        source = data;
        int index = 0;
        while (index + 4 <= data.Length && data[index + 2] != 0x7A)
        {
            var command = new LucaCommand();
            index += command.ReadCommand(data, index);
        }
        if (index + 10 > data.Length || data[index + 2] != 0x7A)
            throw new InvalidDataException("String table BTFUNC not found.");
        commandOffset = index;
        int length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(index, 2));
        // seen8501 uses zero as its special table length; both scripts finish with END.
        payloadEnd = length == 0 ? data.Length - 6 : index + length;
        tailOffset = payloadEnd + (payloadEnd % 2);
        if (tailOffset + 6 != data.Length || data[tailOffset] != 6 || data[tailOffset + 2] != 0x19)
            throw new InvalidDataException("Unexpected string table tail.");
        bool icons = data[index + 8] != 1;
        index += 10;
        while (index < payloadEnd)
        {
            if (icons && Strings.Count % 2 == 0) index += 3;
            var text = SwitchString.Read(data, index);
            if (text.End > payloadEnd) throw new InvalidDataException("String crosses table boundary.");
            Strings.Add(text);
            index = text.End;
        }
        if (index != payloadEnd || Strings.Count % 2 != 0)
            throw new InvalidDataException("Incomplete bilingual string table.");
    }

    public byte[] Replace(IReadOnlyList<string> translations)
    {
        if (translations.Count != Strings.Count) throw new InvalidDataException("String table translation count mismatch.");
        var result = new List<byte>();
        int copied = 0;
        for (int i = 0; i < Strings.Count; i++)
        {
            var text = Strings[i];
            result.AddRange(source[copied..text.Offset]);
            result.AddRange(translations[i] == text.Text
                ? source[text.Offset..text.End] : SwitchString.Encode(translations[i], text.IsUtf8));
            copied = text.End;
        }
        result.AddRange(source[copied..payloadEnd]);
        int length = result.Count - commandOffset;
        if (source[commandOffset] != 0 || source[commandOffset + 1] != 0)
        {
            if (length > ushort.MaxValue) throw new InvalidDataException("String table command exceeds UInt16 length.");
            result[commandOffset] = (byte)length;
            result[commandOffset + 1] = (byte)(length >> 8);
        }
        if (result.Count % 2 != 0) result.Add(0);
        result.AddRange(source[tailOffset..]);
        return result.ToArray();
    }
}
