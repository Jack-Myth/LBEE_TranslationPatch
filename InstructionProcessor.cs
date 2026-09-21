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
            return In.Replace('—', 'ー').Replace('－', 'ー').Replace('·', '・');
        }

        public static int GetStrLength(byte[] Command,int StartIndex)
        {
            int index = StartIndex;
            while (Command[index] != 0 || Command[index + 1] != 0)
            {
                index += 2;
            }
            return index - StartIndex;
        }

        public static int GetSingleByteStrLength(byte[] Command, int StartIndex)
        {
            int index = StartIndex;
            while (Command[index] != 0)
            {
                index++;
            }
            return index - StartIndex;
        }

        public static Dictionary<byte, Func<byte[], JsonObject?>> InstructionGetMapping = new ()
        {
            { 0x1F, MESSAGE_GET },
            { 0x21, SELECT_GET },
            { 0x19, VARSTR_SET_GET },
            { 0x5A, TASK_GET },
            { 0x5C, BATTLE_GET },
            { 0x69, SAYAVOICETEXT_GET }
        };

        public static Dictionary<byte, Func<byte[], JsonObject, byte[]?>> InstructionSetMapping = new ()
        {
            { 0x1F, MESSAGE_SET },
            { 0x21, SELECT_SET },
            { 0x19, VARSTR_SET_SET },
            { 0x5A, TASK_SET },
            { 0x5C, BATTLE_SET },
            { 0x69, SAYAVOICETEXT_SET }
        };

        public static Dictionary<byte, Func<List<LucaCommand>, int, LucaCommand[]?>> AssignCmdMapping = new ()
        {
            { 14, TAIL4Ptr_ASSIGN_CMD },    // GOTO
            { 16, TAIL4Ptr_ASSIGN_CMD },    // GOSUB
            { 17, TAIL4Ptr_ASSIGN_CMD },    // IFY
            { 18, TAIL4Ptr_ASSIGN_CMD },    // IFN
            { 20, JUMP_ASSIGN_CMD },        // JUMP
            { 21, FARCALL_ASSIGN_CMD },     // FARCALL
            { 15, ONGOTO_ASSIGN_CMD }       // ONGOTO
        };

        public static Dictionary<byte, Action<LucaCommand,LucaCommand[]>> FixPtrMapping = new()
        {
            { 14, TAIL4Ptr_FIX_PTR },
            { 16, TAIL4Ptr_FIX_PTR },
            { 17, TAIL4Ptr_FIX_PTR },
            { 18, TAIL4Ptr_FIX_PTR },
            { 20, JUMP_FIX_PTR },
            { 21, FARCALL_FIX_PTR },
            { 15, ONGOTO_FIX_PTR }
        };

        public static JsonObject? MESSAGE_GET(byte[] command)
        {
            int index = GetCmdHeaderLength(command)+2;
            int strALength = GetStrLength(command,index);
            int strBLength = GetStrLength(command, index + strALength + 2);

            var outObj = new JsonObject
            {
                ["JP"] = Encoding.Unicode.GetString(command, index,strALength),
                ["EN"] = Encoding.Unicode.GetString(command, index+ strALength + 2, strBLength),
            };

            outObj["Translation"] = outObj["EN"]?.DeepClone();

            return outObj;
        }

        public static byte[]? MESSAGE_SET(byte[] command, JsonObject inJsonObj)
        {
            int index = GetCmdHeaderLength(command)+2;
            int strStart = index + GetStrLength(command, index) + 2;
            int strEnd = GetStrLength(command, strStart) + strStart;
            List<byte> newCommand = new List<byte>(command[..strStart]);
            string Translation = PostProcessText(inJsonObj["Translation"]?.GetValue<string>()??"");
            string EN = inJsonObj["EN"]?.GetValue<string>()??"";
            if(Translation!=EN)
            {
                foreach(var newChar in Translation.ToCharArray())
                {
                    CharCollection.Add(newChar);
                }
            }
            newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
            newCommand.AddRange(command.Skip(strEnd));

            // 不需要修正指令长度，交由上层修复
            return newCommand.ToArray();
        }

        public static JsonObject? VARSTR_SET_GET(byte[] command)
        {
            JsonObject TrasnlationObj = new JsonObject();
            int index = GetCmdHeaderLength(command) + 2; // Header+ID
            TrasnlationObj["Text"] = Encoding.Unicode.GetString(command[index..(index+GetStrLength(command, index))]);
            TrasnlationObj["Translation"] = TrasnlationObj["Text"]?.DeepClone();
            return TrasnlationObj;
        }

        public static byte[]? VARSTR_SET_SET(byte[] command, JsonObject inJsonObj)
        {
            int index = GetCmdHeaderLength(command) + 2; // Header+ID
            string Translation = PostProcessText(inJsonObj["Translation"]?.GetValue<string>() ?? "");
            List<byte> newCommand = new List<byte>();
            newCommand.AddRange(command[..index]);
            newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
            newCommand.Add(0);
            newCommand.Add(0);
            foreach (var newChar in Translation.ToCharArray())
            {
                CharCollection.Add(newChar);
            }
            return newCommand.ToArray();
        }

        public static JsonObject? SELECT_GET(byte[] command)
        {
            JsonObject TrasnlationObj = new JsonObject();
            int index = GetCmdHeaderLength(command) + 4*2; // Header+ID+VAR123
            int StrLength = GetStrLength(command, index);
            TrasnlationObj["JP"] = Encoding.Unicode.GetString(command[index..(index + StrLength)]);
            index += StrLength + 2;
            StrLength = GetStrLength(command, index);
            TrasnlationObj["EN"] = Encoding.Unicode.GetString(command[index..(index + StrLength)]);
            TrasnlationObj["Translation"] = TrasnlationObj["EN"]?.DeepClone();
            return TrasnlationObj;
        }

        public static byte[]? SELECT_SET(byte[] command, JsonObject inJsonObj)
        {
            int index = GetCmdHeaderLength(command) + 4*2; // Header+ID
            int StrLength = GetStrLength(command, index); // Jp
            index += StrLength + 2;
            StrLength = GetStrLength(command, index);
            string Translation = PostProcessText(inJsonObj["Translation"]?.GetValue<string>() ?? "");
            List<byte> newCommand = new List<byte>();
            newCommand.AddRange(command[..index]);
            newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
            newCommand.AddRange(command.Skip(index + StrLength));
            foreach (var newChar in Translation.ToCharArray())
            {
                CharCollection.Add(newChar);
            }
            return newCommand.ToArray();
        }

        public static JsonObject? TASK_GET(byte[] command)
        {
            JsonObject TrasnlationObj = new JsonObject();
            int index = GetCmdHeaderLength(command); // Header
            int TaskID = command[index] + command[index + 1] * 256;
            index += 2;
            string? msgStr_jp1 = null;
            string? msgStr_en1 = null;
            string? msgStr_jp2 = null;
            string? msgStr_en2 = null;
            if (command.Length <= index)
            {
                return null;
            }
            if (TaskID == 4)
            {
                int TaskVar1 = command[index]+command[index+1]*256;
                index += 2;
                if (command.Length <= index)
                {
                    return null;
                }
                if (TaskVar1 == 0 || TaskVar1 == 4 || TaskVar1 == 5 || TaskVar1 == 6)
                {
                    index += 2; // TaskVar2
                    if (TaskVar1 == 6)
                    {
                        index += 2; //TaskVar3
                    }
                    int strLength = GetStrLength(command, index);
                    msgStr_jp1 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength+2; // Include \0;
                    msgStr_en1 = Encoding.Unicode.GetString(command[index..(index + GetStrLength(command, index))]);
                }
                else if (TaskVar1 == 1)
                {
                    index += 2 * 3; // TaskVar2,3,4
                    int strLength = GetStrLength(command, index);
                    msgStr_jp1 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength+2;
                    strLength = GetStrLength(command, index);
                    msgStr_en1 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength + 2;
                    strLength = GetStrLength(command, index);
                    msgStr_jp2 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength+2;
                    msgStr_en2 = Encoding.Unicode.GetString(command[index..(index + GetStrLength(command, index))]);
                }
            }
            else if (TaskID == 54)
            {
                // 只有英文？有点怪
                int strLength = GetStrLength(command, index);
                msgStr_en1 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
            }
            else if (TaskID == 69)
            {
                index += 2;
                int strLength = GetStrLength(command, index);
                msgStr_jp1 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                index += strLength + 2;
                strLength = GetStrLength(command, index);
                msgStr_en1 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                index += strLength + 2;
                strLength = GetStrLength(command, index);
                msgStr_jp2 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                index += strLength + 2;
                msgStr_en2 = Encoding.Unicode.GetString(command[index..(index + GetStrLength(command, index))]);
            }

            if (msgStr_en1 == null && msgStr_jp1 == null &&
                msgStr_en2 == null && msgStr_jp2 == null)
            {
                return null;
            }

            if (msgStr_jp1 != null)
            {
                TrasnlationObj["JP1"] = msgStr_jp1;
            }
            if (msgStr_jp2 != null)
            {
                TrasnlationObj["JP2"] = msgStr_jp2;
            }
            if (msgStr_en1 != null)
            {
                TrasnlationObj["EN1"] = msgStr_en1;
                TrasnlationObj["Translation1"] = msgStr_en1;
            }
            if (msgStr_en2 != null)
            {
                TrasnlationObj["EN2"] = msgStr_en2;
                TrasnlationObj["Translation2"] = msgStr_en2;
            }
            return TrasnlationObj;
        }

        public static byte[]? TASK_SET(byte[] command, JsonObject inJsonObj)
        {
            int index = GetCmdHeaderLength(command); // Header
            int TaskID = command[index] + command[index + 1] * 256;
            index += 2;
            if (command.Length <= index)
            {
                return null;
            }
            if (TaskID == 4)
            {
                int TaskVar1 = command[index] + command[index + 1] * 256;
                index += 2;
                if (command.Length <= index)
                {
                    return null;
                }
                if (TaskVar1 == 0 || TaskVar1 == 4 || TaskVar1 == 5 || TaskVar1 == 6)
                {
                    var newCommand = new List<byte>();
                    index += 2; // TaskVar2
                    if (TaskVar1 == 6)
                    {
                        index += 2; //TaskVar3
                    }
                    index += GetStrLength(command, index) + 2;
                    newCommand.AddRange(command[..index]);
                    int strLength = GetStrLength(command, index);
                    string Translation = PostProcessText(inJsonObj["Translation1"]?.GetValue<string>() ?? "");
                    newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
                    newCommand.AddRange(command.Skip(index+strLength));
                    return newCommand.ToArray();
                }
                else if (TaskVar1 == 1)
                {
                    var newCommand = new List<byte>();
                    index += 2 * 3; // TaskVar2,3,4
                    index += GetStrLength(command, index) + 2;
                    newCommand.AddRange(command[..index]); //str1

                    int strLength = GetStrLength(command, index);
                    string Translation = PostProcessText(inJsonObj["Translation1"]?.GetValue<string>() ?? "");
                    newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
                    index += strLength + 2;  // str2

                    strLength = GetStrLength(command, index);
                    // -2包含str2的\0
                    newCommand.AddRange(command[(index-2)..(index + strLength + 2)]);
                    index += strLength + 2; //str3

                    strLength = GetStrLength(command, index);
                    string Translation2 = PostProcessText(inJsonObj["Translation2"]?.GetValue<string>() ?? "");
                    newCommand.AddRange(Encoding.Unicode.GetBytes(Translation2));
                    newCommand.AddRange(command.Skip(index + strLength)); //str4

                    foreach (var newChar in Translation.ToCharArray())
                    {
                        CharCollection.Add(newChar);
                    }
                    foreach (var newChar in Translation2.ToCharArray())
                    {
                        CharCollection.Add(newChar);
                    }

                    return newCommand.ToArray();
                }
            }
            else if (TaskID == 54)
            {
                // 只有英文？有点怪
                int strLength = GetStrLength(command, index);
                string Translation = PostProcessText(inJsonObj["Translation1"]?.GetValue<string>() ?? "");
                foreach (var newChar in Translation.ToCharArray())
                {
                    CharCollection.Add(newChar);
                }
                List<byte> newCommand = new List<byte>(command[..index]);
                newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
                newCommand.AddRange(command.Skip(index + strLength));
                return newCommand.ToArray();
            }
            else if (TaskID == 69)
            {
                var newCommand = new List<byte>();
                index += 2;
                index += GetStrLength(command, index) + 2;
                newCommand.AddRange(command[..index]); //str1

                int strLength = GetStrLength(command, index);
                string Translation = PostProcessText(inJsonObj["Translation1"]?.GetValue<string>() ?? "");
                newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
                index += strLength + 2;  // str2

                strLength = GetStrLength(command, index);
                newCommand.AddRange(command[(index-2)..(index + strLength + 2)]);
                index += strLength + 2; //str3

                strLength = GetStrLength(command, index);
                string Translation2 = PostProcessText(inJsonObj["Translation2"]?.GetValue<string>() ?? "");
                newCommand.AddRange(Encoding.Unicode.GetBytes(Translation2));
                newCommand.AddRange(command.Skip(index + strLength)); //str4
                foreach (var newChar in Translation.ToCharArray())
                {
                    CharCollection.Add(newChar);
                }
                foreach (var newChar in Translation2.ToCharArray())
                {
                    CharCollection.Add(newChar);
                }
                return newCommand.ToArray();
            }
            return null;
        }

        public static JsonObject? BATTLE_GET(byte[] command)
        {
            JsonObject TrasnlationObj = new JsonObject();
            int index = GetCmdHeaderLength(command); // Header+ID
            int BattleID = command[index] + command[index+1] * 256;
            string? msgStr_jp = null;
            string? msgStr_jp2 = null;
            string? msgStr_jp3 = null;
            string? msgStr_en = null;
            string? msgStr_en2 = null;
            string? msgStr_en3 = null;
            index += 2;
            if(index >= command.Length)
            {
                return null;
            }
            /*if(BattleID == 300)
            {
                // LucaSystem中认为BattleID为300的指令只有日文文本，不确定是否需要翻译
                // 英文语言不全？先返回空白,不做进一步处理
                return null;
            }*/
            else if (BattleID == 101)
            {
                index += 2; //Skip Var1
                int Var2 = command[index] + command[index + 1] * 256;
                if (Var2 == 0)
                {
                    index += 4; //Skip Var2,3
                    int strLength = GetSingleByteStrLength(command, index);
                    index += strLength + 1; // Skip ExprStr
                    strLength = GetStrLength(command, index);
                    msgStr_jp = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength + 2;
                    strLength = GetStrLength(command, index);
                    msgStr_en = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                }
                else
                {
                    // 当下的var2就是文本
                    int strLength = GetStrLength(command, index);
                    msgStr_jp = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength + 2;
                    strLength = GetStrLength(command, index);
                    msgStr_en = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                }
            }
            else if (BattleID == 102||BattleID == 103)
            {
                int Var1 = command[index] + command[index + 1] * 256;
                if (Var1 == 0)
                {
                    index += 4; //Skip Var1,2
                    int strLength = GetSingleByteStrLength(command, index);
                    index += strLength + 1; // 跳过ExprStr，ExprStr为单字节字符串
                    strLength = GetStrLength(command, index);
                    msgStr_jp = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength + 2;
                    strLength = GetStrLength(command, index);
                    msgStr_en = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength + 2;
                }
                else
                {
                    // 当下的Var1就是文本
                    int strLength = GetStrLength(command, index);
                    msgStr_jp = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength + 2;
                    strLength = GetStrLength(command, index);
                    msgStr_en = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength + 2;
                }
                // 有时候会有多余的文本，感觉这个指令真的很复杂
                if (index < command.Length)
                {
                    int strLength = GetSingleByteStrLength(command, index);
                    index += strLength + 1; // Skip ExprStr
                    strLength = GetStrLength(command, index);
                    msgStr_jp2 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength + 2;
                    strLength = GetStrLength(command, index);
                    msgStr_en2 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength + 2;
                }
                // 居然有Translation3？这个是没限制的吗？
                if (index < command.Length)
                {
                    int strLength = GetSingleByteStrLength(command, index);
                    index += strLength + 1; // Skip ExprStr
                    strLength = GetStrLength(command, index);
                    msgStr_jp3 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                    index += strLength + 2;
                    strLength = GetStrLength(command, index);
                    msgStr_en3 = Encoding.Unicode.GetString(command[index..(index + strLength)]);
                }
            }

            if (msgStr_en == null && msgStr_jp == null)
            {
                return null;
            }
            if (msgStr_jp != null)
            {
                TrasnlationObj["JP"] = msgStr_jp;
            }
            if (msgStr_en != null)
            {
                TrasnlationObj["EN"] = msgStr_en;
                TrasnlationObj["Translation"] = msgStr_en;
            }
            if (msgStr_jp2 != null)
            {
                TrasnlationObj["JP2"] = msgStr_jp2;
            }
            if (msgStr_en2 != null)
            {
                TrasnlationObj["EN2"] = msgStr_en2;
                TrasnlationObj["Translation2"] = msgStr_en2;
            }
            if (msgStr_jp3 != null)
            {
                TrasnlationObj["JP3"] = msgStr_jp3;
            }
            if (msgStr_en3 != null)
            {
                TrasnlationObj["EN3"] = msgStr_en3;
                TrasnlationObj["Translation3"] = msgStr_en3;
            }
            return TrasnlationObj;
        }

        public static byte[]? BATTLE_SET(byte[] command, JsonObject inJsonObj)
        {
            JsonObject TrasnlationObj = new JsonObject();
            int index = GetCmdHeaderLength(command); // Header+ID
            int BattleID = command[index] + command[index+1] * 256;
            index += 2;
            if (index >= command.Length)
            {
                return null;
            }
            /*if (BattleID == 300)
            {
                return command;
            }*/
            else if (BattleID == 101)
            {
                index += 2; //Skip Var1
                int Var2 = command[index] + command[index + 1] * 256;
                string Translation = PostProcessText(inJsonObj["Translation"]?.GetValue<string>() ?? "");
                foreach (var newChar in Translation.ToCharArray())
                {
                    CharCollection.Add(newChar);
                }
                List<byte>? newCommand = null;
                if (Var2 == 0)
                {
                    index += 4; //Skip Var2,3
                    int strLength = GetSingleByteStrLength(command, index);
                    index += strLength + 1; // Skip ExprStr
                    strLength = GetStrLength(command, index);
                    index += strLength + 2; // Skip JP
                    newCommand = new List<byte>(command[..index]);
                    strLength = GetStrLength(command, index);
                    newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
                    index += strLength;
                }
                else
                {
                    // 当下的var2就是文本
                    int strLength = GetStrLength(command, index);
                    index += strLength + 2;
                    newCommand = new List<byte>(command[..index]);
                    strLength = GetStrLength(command, index);
                    newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
                    index += strLength;
                }
                newCommand.AddRange(command.Skip(index));
                return newCommand.ToArray();
            }
            else if (BattleID == 102 || BattleID == 103)
            {
                string Translation = PostProcessText(inJsonObj["Translation"]?.GetValue<string>() ?? "");
                foreach (var newChar in Translation.ToCharArray())
                {
                    CharCollection.Add(newChar);
                }
                List<byte>? newCommand = null;
                int Var1 = command[index] + command[index + 1] * 256;
                if (Var1 == 0)
                {
                    index += 4; //Skip Var1,2
                    int strLength = GetSingleByteStrLength(command, index);
                    index += strLength + 1; // Skip ExprStr
                    strLength = GetStrLength(command, index);
                    index += strLength + 2;
                    newCommand = new List<byte>(command[..index]);
                    strLength = GetStrLength(command, index);
                    newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
                    newCommand.Add(0);
                    newCommand.Add(0);
                    index += strLength + 2;
                }
                else
                {
                    // 当下的Var1就是文本
                    int strLength = GetStrLength(command, index);
                    index += strLength + 2;
                    newCommand = new List<byte>(command[..index]);
                    strLength = GetStrLength(command, index);
                    newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
                    newCommand.Add(0);
                    newCommand.Add(0);
                    index += strLength + 2;
                }
                if (index < command.Length)
                {
                    string? Translation2 = inJsonObj["Translation2"]?.GetValue<string>() is string translation2 ? PostProcessText(translation2) : null;
                    if (Translation2 != null)
                    {
                        int strLength = GetSingleByteStrLength(command, index);
                        newCommand.AddRange(command[index..(index + strLength + 1)]);
                        index += strLength + 1; // Skip ExprStr
                        strLength = GetStrLength(command, index);
                        newCommand.AddRange(command[index..(index + strLength + 2)]);
                        index += strLength + 2;
                        strLength = GetStrLength(command, index);
                        newCommand.AddRange(Encoding.Unicode.GetBytes(Translation2));
                        newCommand.Add(0);
                        newCommand.Add(0);
                        index += strLength + 2;
                    }
                }
                if (index < command.Length)
                {
                    string? Translation3 = inJsonObj["Translation3"]?.GetValue<string>() is string translation3 ? PostProcessText(translation3) : null;
                    if (Translation3 != null)
                    {
                        int strLength = GetSingleByteStrLength(command, index);
                        newCommand.AddRange(command[index..(index + strLength + 1)]);
                        index += strLength + 1; // Skip ExprStr
                        strLength = GetStrLength(command, index);
                        newCommand.AddRange(command[index..(index + strLength + 2)]);
                        index += strLength + 2;
                        strLength = GetStrLength(command, index);
                        newCommand.AddRange(Encoding.Unicode.GetBytes(Translation3));
                        index += strLength;
                    }
                }
                newCommand.AddRange(command.Skip(index));
                return newCommand.ToArray();
            }
            return null;
        }

        public static JsonObject? SAYAVOICETEXT_GET(byte[] command)
        {
            JsonObject TrasnlationObj = new JsonObject();
            int index = GetCmdHeaderLength(command) + 2; // Header+ID
            int StrLength = GetStrLength(command, index);
            TrasnlationObj["JP"] = Encoding.Unicode.GetString(command[index..(index + StrLength)]);
            index += StrLength + 2;
            StrLength = GetStrLength(command, index);
            TrasnlationObj["EN"] = Encoding.Unicode.GetString(command[index..(index + StrLength)]);
            TrasnlationObj["Translation"] = TrasnlationObj["EN"]?.DeepClone();
            return TrasnlationObj;
        }

        public static byte[]? SAYAVOICETEXT_SET(byte[] command, JsonObject inJsonObj)
        {
            int index = GetCmdHeaderLength(command) + 2; // Header+ID
            int StrLength = GetStrLength(command, index); // Jp
            index += StrLength + 2;
            StrLength = GetStrLength(command, index);
            string Translation = PostProcessText(inJsonObj["Translation"]?.GetValue<string>() ?? "");
            List<byte> newCommand = new List<byte>();
            newCommand.AddRange(command[..index]);
            newCommand.AddRange(Encoding.Unicode.GetBytes(Translation));
            newCommand.AddRange(command.Skip(index + StrLength));
            foreach (var newChar in Translation.ToCharArray())
            {
                CharCollection.Add(newChar);
            }
            return newCommand.ToArray();
        }

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
        public static LucaCommand[]? TAIL4Ptr_ASSIGN_CMD(List<LucaCommand> InAllCommands,int CmdIndex)
        {
            var CurCmd = InAllCommands[CmdIndex];
            if(CurCmd.Command==null)
            {
                return null;
            }
            int TargetCmdPtr = LittleEndian2Int(CurCmd.Command[^4..]);
            for (int i = 0; i < InAllCommands.Count; i++)
            {
                if (InAllCommands[i].CmdPtr == TargetCmdPtr)
                {
                    return new LucaCommand[] { InAllCommands[i] };
                }
            }
            return null;
        }

        public static void TAIL4Ptr_FIX_PTR(LucaCommand CurCmd, LucaCommand[] InCommands)
        {
            if (CurCmd.Command != null && InCommands.Length>0)
            {
                Int2LittleEndian(CurCmd.Command, CurCmd.Command.Length - 4, InCommands[0].CmdPtr);
            }
        }

        public static LucaCommand[]? FARCALL_ASSIGN_CMD(List<LucaCommand> InAllCommands, int CmdIndex)
        {
            // 这里只是一个占位符，即使不再需要ASSIGN_CMD，但还是传回一个非null值，确保接下来能正常执行FIX_PTR
            return new LucaCommand[0];
        }

        public static void FARCALL_FIX_PTR(LucaCommand CurCmd, LucaCommand[] InCommands)
        {
            if (CurCmd.Command == null)
            {
                return;
            }
            int index = GetCmdHeaderLength(CurCmd.Command);
            int ExpLength = GetSingleByteStrLength(CurCmd.Command, index + 2);
            string TargetScript = Encoding.ASCII.GetString(CurCmd.Command[(index + 2)..(index + 2 + ExpLength)]).ToLower();
            int SourceCmdPtr = LittleEndian2Int(CurCmd.Command[(index + 2 + ExpLength + 1)..(index + 2 + ExpLength + 1 + 4)]);
            if (!ScriptCommandRedirectors.ContainsKey(TargetScript))
            {
                Console.Error.WriteLine("Error: Failed to find redirectors for script " + TargetScript);
                Environment.Exit(-1);
            }
            var TargetRedirectors = ScriptCommandRedirectors[TargetScript];
            if(!TargetRedirectors.ContainsKey(SourceCmdPtr))
            {
                Console.Error.WriteLine("Error: Failed to find redirectors for script " + TargetScript + " SourcePtr " + SourceCmdPtr);
                Environment.Exit(-1);
            }
            Int2LittleEndian(CurCmd.Command, index + 2 + ExpLength + 1, TargetRedirectors[SourceCmdPtr]);
        }

        public static LucaCommand[]? JUMP_ASSIGN_CMD(List<LucaCommand> InAllCommands, int CmdIndex)
        {
            return new LucaCommand[0];
        }

        // 与FARCALL_FIX_PTR相同，但Header和脚本名之间少了2个字节的变量。
        public static void JUMP_FIX_PTR(LucaCommand CurCmd, LucaCommand[] InCommands)
        {
            if (CurCmd.Command == null)
            {
                return;
            }
            int index = GetCmdHeaderLength(CurCmd.Command);
            int ExpLength = GetSingleByteStrLength(CurCmd.Command, index);
            if (index + ExpLength + 1 >= CurCmd.GetCmdLength())
            {
                // 这个JUMP可能不带参数，直接返回
                return;
            }
            string TargetScript = Encoding.ASCII.GetString(CurCmd.Command[index..(index + ExpLength)]).ToLower();
            int SourceCmdPtr = LittleEndian2Int(CurCmd.Command[(index + ExpLength + 1)..(index + ExpLength + 1 + 4)]);
            if (!ScriptCommandRedirectors.ContainsKey(TargetScript))
            {
                Console.Error.WriteLine("Error: Failed to find redirectors for script " + TargetScript);
                Environment.Exit(-1);
            }
            var TargetRedirectors = ScriptCommandRedirectors[TargetScript];
            if (!TargetRedirectors.ContainsKey(SourceCmdPtr))
            {
                Console.Error.WriteLine("Error: Failed to find redirectors for script " + TargetScript + " SourcePtr " + SourceCmdPtr);
                Environment.Exit(-1);
            }
            Int2LittleEndian(CurCmd.Command, index + ExpLength + 1, TargetRedirectors[SourceCmdPtr]);
        }

        public static LucaCommand[]? ONGOTO_ASSIGN_CMD(List<LucaCommand> InAllCommands, int CmdIndex)
        {
            return new LucaCommand[0];
        }

        // 原来ONGOTO的含义不是当跳转结束后XXX，而是当XXX时跳转
        // 感觉当初的思路完全错误了。
        public static void ONGOTO_FIX_PTR(LucaCommand CurCmd, LucaCommand[] InCommands)
        {
            if (CurCmd.Command == null)
            {
                return;
            }
            int index = GetCmdHeaderLength(CurCmd.Command);
            int ExpLength = GetSingleByteStrLength(CurCmd.Command, index);
            byte[] PtrArray = CurCmd.Command.Skip(index + ExpLength + 1).ToArray();
            if(PtrArray.Length % 4!=0)
            {
                // 怎么会不能被整除呢？
                Console.Error.WriteLine("Error: ONGOTO PtrArray length is not multiple of 4,Script may contains error,Exit!");
                Environment.Exit(-1);
            }
            var CurScript = Program.ScriptNameContext.Peek();
            var TargetRedirectors = ScriptCommandRedirectors[CurScript];
            for (int i = 0; i < PtrArray.Length; i += 4)
            {
                byte[] PtrByteArray = PtrArray[i..(i + 4)];
                // 这里其实应该是uint的，但鉴于脚本里不可能出现大于2G的指针，所以用int还是uint都无所谓了
                int SourceCmdPtr = BitConverter.IsLittleEndian ?
                    BitConverter.ToInt32(PtrArray, i) :
                    BitConverter.ToInt32(PtrArray.Reverse().ToArray(), i);
                if (!TargetRedirectors.ContainsKey(SourceCmdPtr))
                {
                    Console.Error.WriteLine("Error: Failed to find redirectors for script " + CurScript + " SourcePtr " + SourceCmdPtr);
                    Environment.Exit(-1);
                }
                Int2LittleEndian(CurCmd.Command, index + ExpLength + 1 + i, TargetRedirectors[SourceCmdPtr]);
            }
        }
    }

}
