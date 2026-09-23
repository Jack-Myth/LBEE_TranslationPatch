using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace LBEE_TranslationPatch
{
    public static class InstructionProcessor
    {

        public static HashSet<char> CharCollection = new HashSet<char>();
        public static Dictionary<string, Dictionary<int, int>> ScriptCommandRedirectors = new();

        public static int GetCmdHeaderLength(byte[] command)
        {
            int SpecByte = command[3];
            return Math.Min(SpecByte, 2) * 2 + 4;
        }

        public static string PostProcessText(string In)
        {
            // 这两个字体在绘制的时候有问题，由于字体太小，所以直接绘制到了字符的顶端
            // 通过调整绘制的位置大概可以解决问题，但这里先替换为相近的字符，暂时规避问题。
            string InStr = In;
            if(InStr.StartsWith('`') && InStr.Contains('@'))
            {
                // 这是个旧版格式，将第一个字节改为@
                InStr = string.Concat("@", InStr.AsSpan(1));
            }
            return InStr.Replace('—', 'ー').Replace('－', 'ー').Replace('·', '・');
        }

        public static Dictionary<byte, Func<byte[], JsonObject?>> InstructionGetMapping = new()
        {
            { 0x23, MESSAGE_GET }, { 0x26, SELECT_GET }, { 0x1C, VARSTR_SET_GET },
            { 0x72, TASK_GET }, { 0x78, BATTLE_GET }, { 0x82, SAYAVOICETEXT_GET }
        };
        public static Dictionary<byte, Func<byte[], JsonObject, byte[]?>> InstructionSetMapping = new()
        {
            { 0x23, MESSAGE_SET }, { 0x26, SELECT_SET }, { 0x1C, VARSTR_SET_SET },
            { 0x72, TASK_SET }, { 0x78, BATTLE_SET }, { 0x82, SAYAVOICETEXT_SET }
        };
        public static Dictionary<byte, Func<List<LucaCommand>, int, LucaCommand[]?>> AssignCmdMapping = new()
        {
            { 0x0F, TAIL4Ptr_ASSIGN_CMD }, { 0x11, TAIL4Ptr_ASSIGN_CMD },
            { 0x12, TAIL4Ptr_ASSIGN_CMD }, { 0x13, TAIL4Ptr_ASSIGN_CMD },
            { 0x15, JUMP_ASSIGN_CMD }, { 0x16, FARCALL_ASSIGN_CMD }, { 0x10, ONGOTO_ASSIGN_CMD }
        };
        public static Dictionary<byte, Action<LucaCommand, LucaCommand[]>> FixPtrMapping = new()
        {
            { 0x0F, TAIL4Ptr_FIX_PTR }, { 0x11, TAIL4Ptr_FIX_PTR },
            { 0x12, TAIL4Ptr_FIX_PTR }, { 0x13, TAIL4Ptr_FIX_PTR },
            { 0x15, JUMP_FIX_PTR }, { 0x16, FARCALL_FIX_PTR }, { 0x10, ONGOTO_FIX_PTR }
        };

        private record TextField(string Name, string? Translation, SwitchString Value);

        // GET and SET share the same field layout, including UTF-8 expressions between texts.
        private static List<TextField> GetTextFields(byte[] command)
        {
            var fields = new List<TextField>();
            int index = GetCmdHeaderLength(command);
            int ReadNumber() { int value = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(command.AsSpan(index, 2)); index += 2; return value; }
            void SkipString() { index = SwitchString.Read(command, index).End; }
            void Add(string name, string? translation = null)
            {
                var value = SwitchString.Read(command, index);
                fields.Add(new(name, translation, value));
                index = value.End;
            }
            void Pair(string suffix = "", string? translation = null)
            {
                Add("JP" + suffix);
                Add("EN" + suffix, translation ?? "Translation" + suffix);
            }
            switch (command[2])
            {
                case 0x23: // MESSAGE
                case 0x82: // CSAYAVOICETEXT
                    index += 2;
                    Pair();
                    break;
                case 0x26: // SELECT
                    index += 8;
                    Pair();
                    break;
                case 0x1C: // VARSTR_SET
                    index += 2;
                    Add("Text", "Translation");
                    break;
                case 0x72: // TASK
                    int task = ReadNumber();
                    if (index >= command.Length) break;
                    if (task == 4)
                    {
                        int variant = ReadNumber();
                        if (index >= command.Length) break;
                        if (variant is 0 or 4 or 5 or 6)
                        {
                            index += variant == 6 ? 4 : 2;
                            Pair("1");
                        }
                        else if (variant == 1)
                        {
                            index += 6;
                            Pair("1"); Pair("2");
                        }
                    }
                    else if (task == 54) Add("EN1", "Translation1");
                    else if (task == 69)
                    {
                        index += 2;
                        Pair("1"); Pair("2");
                    }
                    break;
                case 0x78: // BATTLE
                    int battle = ReadNumber();
                    if (index >= command.Length || battle is not (101 or 102 or 103)) break;
                    if (battle == 101) index += 2;
                    if (System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(command.AsSpan(index, 2)) == 0)
                    {
                        index += 4;
                        SkipString();
                    }
                    Pair();
                    if (battle is 102 or 103)
                    {
                        for (int n = 2; index < command.Length; n++)
                        {
                            SkipString();
                            Pair(n.ToString());
                        }
                    }
                    break;
            }
            return fields;
        }

        private static JsonObject? GetText(byte[] command)
        {
            var fields = GetTextFields(command);
            if (fields.Count == 0) return null;
            var result = new JsonObject();
            foreach (var field in fields)
            {
                result[field.Name] = field.Value.Text;
                if (field.Translation != null) result[field.Translation] = field.Value.Text;
            }
            return result;
        }

        private static byte[]? SetText(byte[] command, JsonObject translations)
        {
            var fields = GetTextFields(command);
            if (fields.Count == 0) return null;
            var result = new List<byte>();
            int copied = 0;
            foreach (var field in fields)
            {
                if (field.Translation == null || translations[field.Translation] is not JsonValue value) continue;
                string translation = value.GetValue<string>();
                // Preserve untouched strings exactly, including their original encoding.
                if (translation == field.Value.Text) continue;
                translation = PostProcessText(translation);
                result.AddRange(command[copied..field.Value.Offset]);
                result.AddRange(SwitchString.Encode(translation, field.Value.IsUtf8));
                copied = field.Value.End;
                CharCollection.UnionWith(translation);
            }
            result.AddRange(command[copied..]);
            return result.ToArray();
        }

        public static JsonObject? MESSAGE_GET(byte[] command) => GetText(command);
        public static byte[]? MESSAGE_SET(byte[] command, JsonObject json) => SetText(command, json);
        public static JsonObject? SELECT_GET(byte[] command) => GetText(command);
        public static byte[]? SELECT_SET(byte[] command, JsonObject json) => SetText(command, json);
        public static JsonObject? VARSTR_SET_GET(byte[] command) => GetText(command);
        public static byte[]? VARSTR_SET_SET(byte[] command, JsonObject json) => SetText(command, json);
        public static JsonObject? TASK_GET(byte[] command) => GetText(command);
        public static byte[]? TASK_SET(byte[] command, JsonObject json) => SetText(command, json);
        public static JsonObject? BATTLE_GET(byte[] command) => GetText(command);
        public static byte[]? BATTLE_SET(byte[] command, JsonObject json) => SetText(command, json);
        public static JsonObject? SAYAVOICETEXT_GET(byte[] command) => GetText(command);
        public static byte[]? SAYAVOICETEXT_SET(byte[] command, JsonObject json) => SetText(command, json);
        public static int LittleEndian2Int(byte[] InBytes)
        {
            int result = 0;
            for (int i = 0; i < 4; i++)
            {
                result |= InBytes[i] << (8 * i);
            }
            return result;
        }

        public static void Int2LittleEndian(byte[] InBytes, int Offset, int Value)
        {
            for (int i = 0; i < 4; i++)
            {
                InBytes[Offset + i] = (byte)((Value >> (8 * i)) & 0xFF);
            }
        }

        // 用于修正指令中的指针
        // 感觉在有了CommandRedirectors之后是不需要Assign的步骤了，但是为了避免出Bug，旧代码就不动了
        // 感觉屎山正在慢慢堆积。。
        public static LucaCommand[] TAIL4Ptr_ASSIGN_CMD(List<LucaCommand> commands, int index) => [];
        public static void TAIL4Ptr_FIX_PTR(LucaCommand current, LucaCommand[] commands)
        {
            if (current.Command != null)
                Redirect(current.Command, current.Command.Length - 4, Program.ScriptNameContext.Peek());
        }
        public static LucaCommand[] FARCALL_ASSIGN_CMD(List<LucaCommand> commands, int index) => [];
        public static LucaCommand[] JUMP_ASSIGN_CMD(List<LucaCommand> commands, int index) => [];
        public static LucaCommand[] ONGOTO_ASSIGN_CMD(List<LucaCommand> commands, int index) => [];

        private static void Redirect(byte[] command, int offset, string script)
        {
            int original = LittleEndian2Int(command[offset..(offset + 4)]);
            if (!ScriptCommandRedirectors.TryGetValue(script, out var redirects) ||
                !redirects.TryGetValue(original, out int replacement))
                throw new InvalidDataException($"No jump target for {script} at 0x{original:X}.");
            Int2LittleEndian(command, offset, replacement);
        }

        public static void FARCALL_FIX_PTR(LucaCommand current, LucaCommand[] commands)
        {
            if (current.Command == null) return;
            var target = SwitchString.Read(current.Command, GetCmdHeaderLength(current.Command) + 2);
            Redirect(current.Command, target.End, target.Text.ToLowerInvariant());
        }

        public static void JUMP_FIX_PTR(LucaCommand current, LucaCommand[] commands)
        {
            if (current.Command == null) return;
            var target = SwitchString.Read(current.Command, GetCmdHeaderLength(current.Command));
            if (target.End == current.Command.Length) return; // JUMP without an explicit offset.
            if (target.End + 4 != current.Command.Length) throw new InvalidDataException("Invalid JUMP operands.");
            Redirect(current.Command, target.End, target.Text.ToLowerInvariant());
        }

        public static void ONGOTO_FIX_PTR(LucaCommand current, LucaCommand[] commands)
        {
            if (current.Command == null) return;
            int index = SwitchString.Read(current.Command, GetCmdHeaderLength(current.Command)).End;
            // Switch entries contain a 16-bit field followed by a 32-bit script offset.
            if ((current.Command.Length - index) % 6 != 0) throw new InvalidDataException("Invalid ONGOTO table.");
            string script = Program.ScriptNameContext.Peek();
            for (; index < current.Command.Length; index += 6) Redirect(current.Command, index + 2, script);
        }
    }
}
