﻿namespace LBEE_TranslationPatch
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Newtonsoft.Json;
    using System.Collections;
    using System.Text;
    using System.Xml;
    using Newtonsoft.Json.Linq;
    using System.Security.Cryptography;
    using System.Diagnostics;
    using System.Xml.Linq;
    using System.Reflection.Metadata;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;
    using System.Runtime.ExceptionServices;
    using System.Numerics;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int flagsEx;
    }

    class Program
    {
        static bool DEBUG_DumpInstructionLayout = false;
        static bool DEBUG_EnableDebugJump = false;
        static string DEBUG_DebugJumpScript = "SEEN2803";
        static uint DEBUG_DebugJumpPtr = 0x14d0;

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileName(ref OpenFileName ofn);

        private static string Select_LBEEEXE()
        {
            var CacheWD = Directory.GetCurrentDirectory();
            var ofn = new OpenFileName();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            // Define Filter for your extensions (Excel, ...)
            ofn.lpstrFilter = "LBEE EXE\0*.exe";
            ofn.lpstrFile = new string(new char[256]);
            ofn.nMaxFile = ofn.lpstrFile.Length;
            ofn.lpstrFileTitle = new string(new char[64]);
            ofn.nMaxFileTitle = ofn.lpstrFileTitle.Length;
            ofn.lpstrTitle = "选择Little Busters English Edition的主程序(LITBUS_WIN32.exe)";
            var Result = GetOpenFileName(ref ofn);
            Directory.SetCurrentDirectory(CacheWD);
            return Result?ofn.lpstrFile:string.Empty;
        }

#if RELEASE
        static string LBEEGamePath = "";
#else
        static string LBEEGamePath = @"E:\SteamLibrary\steamapps\common\Little Busters! English Edition";
#endif
        static string LBEE_EXE = "";
        static string LBEECharset = @".\Files\Charset.txt";
        static string TMPPath = Path.GetFullPath(@".\.tmp");
        static string TextMappingPath = Path.GetFullPath(@".\TextMapping");
        static string ExtractedScriptPath = Path.Combine(TMPPath, "Scripts");
        static string ExtractedFontPath = Path.Combine(TMPPath, "Fonts");
        static string PendingReplacePath = Path.Combine(TMPPath, "PendingReplace");
        static string[] Operators = new string[0];
        static StreamWriter? InstructionLayoutHandle = null;
        public static Stack<string> ScriptNameContext = new();
        static List<string> IgnoredScriptList = new List<string> 
        { 
            "SEEN8500", "SEEN8501",

            // 这些脚本应该是一些数值变量或者控制逻辑，看起来没有翻译的必要
            "_ARFLAG","_BUILD_COUNT","_CGMODE","_QUAKE",
            "_SCR_LABEL","_TASK","_VARNUM","_VOICE_PARAM"
        };

        public static void ProcessScript(string scriptFile,bool PreComputeLayout)
        {
            Dictionary<int, int> CommandsRedirectors = new();
            var fileName = Path.GetFileNameWithoutExtension(scriptFile);
            if (IgnoredScriptList.Contains(fileName))
            {
                return;
            }
            ScriptNameContext.Push(fileName.ToLower());
            // 首先读出所有指令
            var allCommands = new List<LucaCommand>();
            var commandBytes = File.ReadAllBytes(scriptFile);
            int index = 0;
            while (index < commandBytes.Length)
            {
                var curCommand = new LucaCommand();
                index += curCommand.ReadCommand(commandBytes, index);
                allCommands.Add(curCommand);
            }

            // 对所有指令执行AssignCommand，绑定到对应的指令上
            // 由于有跳转指令的存在，以及脚本翻译后各指令的指针会发生变化
            // 此处需要解析部分跳转指令，绑定到要跳转的目标指令上，然后在翻译结束后执行FixPtr修正跳转指针
            for (int i = 0; i < allCommands.Count; i++)
            {
                allCommands[i].AssignCommand(allCommands, i);
            }

            // 没有对应翻译的话，新建一个翻译文件
            var scriptTextMapping = Path.Combine(TextMappingPath, $"{fileName}.json");
            if (!File.Exists(scriptTextMapping))
            {
                var translationJson = new Dictionary<string, List<JObject>>();
                foreach (var command in allCommands)
                {
                    if (InstructionProcessor.InstructionGetMapping.ContainsKey(command.GetInstruction()))
                    {
                        var commandName = Operators[command.GetInstruction()];
                        if (!translationJson.ContainsKey(commandName))
                        {
                            translationJson[commandName] = new List<JObject>();
                        }
                        var TranslationObj = command.GetTranslationObj();
                        if (TranslationObj != null)
                        {
                            translationJson[commandName].Add(TranslationObj);
                        }
                    }
                }
                File.WriteAllText(scriptTextMapping, JsonConvert.SerializeObject(translationJson, Newtonsoft.Json.Formatting.Indented));
                ScriptNameContext.Pop();
                return;
            }

            // 记下原有脚本中各指令的数量用于验证
            var translationJsonCounts = new Dictionary<string, int>();
            foreach (var command in allCommands)
            {
                if (InstructionProcessor.InstructionGetMapping.ContainsKey(command.GetInstruction()))
                {
                    var commandName = Operators[command.GetInstruction()];
                    if (!translationJsonCounts.ContainsKey(commandName))
                    {
                        translationJsonCounts[commandName] = 0;
                    }
                    var TranslationObj = command.GetTranslationObj();
                    if (TranslationObj != null)
                    {
                        translationJsonCounts[commandName] += 1;
                    }
                }
            }
            // 如果有翻译文件的话，执行翻译
            List<byte> NewScriptBuffer = new List<byte>();
            var ScriptTextMappingObj = JObject.Parse(File.ReadAllText(scriptTextMapping));
            var CmdIndexMap = new Dictionary<byte, int>();
            //Console.WriteLine(scriptFile);
            int CommandPtr = 0;

            foreach (var command in allCommands)
            {
                var CurInstruction = command.GetInstruction();
                /*if(Operators[CurInstruction]== "BATTLE" && command.GetCmdLength()>8)
                {
                    Console.WriteLine($"{Operators[CurInstruction]} {command.CmdPtr}");
                }*/
                if (InstructionProcessor.InstructionSetMapping.ContainsKey(CurInstruction))
                {
                    var commandName = Operators[CurInstruction];
                    if (ScriptTextMappingObj.ContainsKey(commandName))
                    {
                        int CurCmdIndex;
                        if (CmdIndexMap.ContainsKey(CurInstruction))
                        {
                            CmdIndexMap[CurInstruction]++;
                            CurCmdIndex = CmdIndexMap[CurInstruction];
                        }
                        else
                        {
                            CmdIndexMap.Add(CurInstruction, 0);
                            CurCmdIndex = 0;
                        }
                        var CommandTranslationCollection = ScriptTextMappingObj.GetValue(commandName)!.ToArray();
                        if (CommandTranslationCollection != null && CommandTranslationCollection.Length > CurCmdIndex)
                        {
                            var CommandTranslationObj = CommandTranslationCollection[CurCmdIndex].Value<JObject>();
                            if (CommandTranslationObj != null)
                            {
                                if (!command.SetTranslationObj(CommandTranslationObj))
                                {
                                    // 如果指令未被接受，则往前退一步
                                    CmdIndexMap[CurInstruction]--;
                                }
                            }
                            else
                            {
                                Console.Error.WriteLine("Error: Invalid TextMapping Json!");
                                Console.Error.WriteLine(Operators[CurInstruction] + ":" + CurCmdIndex.ToString() + " is not a JsonObject!");
                                Environment.Exit(-1);
                            }
                        }
                        else
                        {
                            // 如果指令未被接受，则往前退一步
                            CmdIndexMap[CurInstruction]--;
                        }
                    }
                }
                // 为Command设置翻译后的指针
                CommandsRedirectors.Add(command.CmdPtr, CommandPtr);
                command.SetCmdPtr(CommandPtr);
                CommandPtr += command.GetCmdLength() + command.GetPendingLength();
            }
            if (!InstructionProcessor.ScriptCommandRedirectors.ContainsKey(fileName.ToLower()))
            {
                InstructionProcessor.ScriptCommandRedirectors.Add(fileName.ToLower(), CommandsRedirectors);
            }
            if(PreComputeLayout)
            {
                // 如果是预计算布局的话，直接返回，不进行保存
                return;
            }
            if (InstructionLayoutHandle != null)
            {
                InstructionLayoutHandle.WriteLine("Script:" + fileName);
            }
            foreach (var command in allCommands)
            {
                // 修正跳转指令的指针
                command.FixCommandPtr();
                if (InstructionLayoutHandle != null)
                {
                    string OPCode = Operators[command.GetInstruction()];
                    InstructionLayoutHandle.WriteLine("\t" + OPCode + "\t" + NewScriptBuffer.Count.ToString());
                }
                NewScriptBuffer.AddRange(command.Command!);
                if (command.GetPendingLength() > 0)
                {
                    NewScriptBuffer.Add(0);
                }
            }
            foreach (var cmdIndexKV in CmdIndexMap)
            {
                if (translationJsonCounts.ContainsKey(Operators[cmdIndexKV.Key]) &&
                    translationJsonCounts[Operators[cmdIndexKV.Key]] != cmdIndexKV.Value + 1)
                {
                    Console.Error.WriteLine("Error: TextMapping Instruction Count Mismatch(" + fileName + "," + Operators[cmdIndexKV.Key] + "), Exit!");
                    Environment.Exit(-1);
                }
            }
            File.WriteAllBytes(scriptFile, NewScriptBuffer.ToArray());
            ScriptNameContext.Pop();
        }

        static void Main(string[] args)
        {
            if(args.Length>0)
            {
                LBEE_EXE = args[0];
                // 获取LBEE_Exe所在文件夹
                LBEEGamePath = Path.GetDirectoryName(LBEE_EXE) ??"";
            }
            if(!Path.Exists(LBEEGamePath))
            {
                LBEE_EXE = Select_LBEEEXE();
                LBEEGamePath = Path.GetDirectoryName(LBEE_EXE) ?? "";
                if (!Directory.Exists(LBEEGamePath))
                {
                    Environment.Exit(-1);
                }
            }

            Directory.CreateDirectory(TMPPath);
            Directory.CreateDirectory(TextMappingPath);
            Directory.CreateDirectory(ExtractedScriptPath);
            if (Directory.Exists(PendingReplacePath))
            {
                Directory.Delete(PendingReplacePath, true);
            }
            Directory.CreateDirectory(PendingReplacePath);

            string LBEEScriptPak = Path.Combine(LBEEGamePath, @"files\SCRIPT.PAK");
            string LBEEFontPak = Path.Combine(LBEEGamePath, @"files\FONT.PAK");
            string TemplateLBEEScriptPak = Path.Combine(LBEEGamePath, @"files\template\SCRIPT.PAK");
            string TemplateLBEEFontPak = Path.Combine(LBEEGamePath, @"files\template\FONT.PAK");
            if (!Directory.Exists(Path.Combine(LBEEGamePath, @"files\template")))
            {
                if (File.Exists(LBEEScriptPak) && File.Exists(LBEEFontPak))
                {
                    Directory.CreateDirectory(Path.Combine(LBEEGamePath, @"files\template"));
                    File.Copy(LBEEScriptPak, TemplateLBEEScriptPak);
                    File.Copy(LBEEFontPak, TemplateLBEEFontPak);
                }
                else
                {
                    Console.WriteLine("Need template files");
                    return;
                }
            }

            // Assuming LuckSystem is a separate executable that needs to be run
            Process.Start(".\\Files\\lucksystem.exe", $"pak extract -i \"{TemplateLBEEScriptPak}\" -o {Path.Combine(TMPPath, "ScriptFileList.txt")} --all {ExtractedScriptPath}").WaitForExit();

            Operators = File.ReadAllText(".\\Files\\OPCODE.txt").ReplaceLineEndings().Split(Environment.NewLine);
            var scriptFiles = Directory.GetFiles(ExtractedScriptPath);

            if (DEBUG_DumpInstructionLayout)
            {
                InstructionLayoutHandle = new StreamWriter("InstructionLayout.txt");
            }

            Dictionary<string, Dictionary<int, int>> ScriptCommandRedirectors = new();

            foreach (var scriptFile in scriptFiles)
            {
                // 由于FARCALL指令的存在，需要先进行一次预计算，然后再进行翻译
                ProcessScript(scriptFile,true);
            }
            foreach (var scriptFile in scriptFiles)
            {
                ProcessScript(scriptFile,false);
            }

            if (InstructionLayoutHandle != null)
            {
                InstructionLayoutHandle.Close();
            }

            // Debug跳转。通过构造指令，在NewGame时直接跳转到对应的脚本。
            // 跳转之后看起来是正常的，但跑了没多久就会崩溃，感觉还是因为部分前置条件没有设置
            // 需要配合DebugJumpPtr做更为精确的跳转，直接跳到出问题的地方。
            if (DEBUG_EnableDebugJump && DEBUG_DebugJumpScript.Length==8)
            {
                var JumpCommandsPre = new byte[]
                {
                    0x13,0x00,0x14,0x01,0x21,0x0a
                };
                var JumpCommandsPost = new byte[]
                {
                    0x00,0x06,0x00,0x18,0x01,0x14,0x03
                };
                var JumpCommands = new List<byte>();
                JumpCommands.AddRange(JumpCommandsPre);
                JumpCommands.AddRange(Encoding.ASCII.GetBytes(DEBUG_DebugJumpScript.ToLower()));
                JumpCommands.Add(0);
                JumpCommands.AddRange(BitConverter.IsLittleEndian ?
                    BitConverter.GetBytes(DEBUG_DebugJumpPtr) :
                    BitConverter.GetBytes(DEBUG_DebugJumpPtr).Reverse());
                JumpCommands.AddRange(JumpCommandsPost);
                File.WriteAllBytes(ExtractedScriptPath + "\\SEEN0513", JumpCommands.ToArray());
            }

            Process.Start("Files\\lucksystem.exe", $"pak replace -s \"{TemplateLBEEScriptPak}\" -i \"{ExtractedScriptPath}\" -o \"{LBEEScriptPak}\"").WaitForExit();

            // 解开字体
            Process.Start("Files\\lucksystem.exe", $"pak extract -i \"{TemplateLBEEFontPak}\" -o {Path.Combine(TMPPath, "FontFileList.txt")} --all {ExtractedFontPath}").WaitForExit();

            // 重绘字体
            var FontSize = new int[]
            {
                // 这些字体貌似有点问题,重绘后会导致游戏崩溃，先放着不动
                //12,14,72,36,16,
                18,20,24,28,30,32
                //28
            };

            var FontName = new string[]
            {
                "モダン","明朝","ゴシック","丸ゴシック"
            };

            var FontTemplate = "ゴシック";
            string TargetFontPath = "C:\\Windows\\Fonts\\simhei.ttf";

            var Charset = File.ReadAllText(LBEECharset);
            HashSet<char> OriginalCharCollection =new (InstructionProcessor.CharCollection);
            InstructionProcessor.CharCollection.Remove('　');
            InstructionProcessor.CharCollection.Remove('\n');
            foreach (var oldChar in Charset.ToCharArray())
            {
                InstructionProcessor.CharCollection.Remove(oldChar);
            }
            var PendingOverrideChars = Charset[^(InstructionProcessor.CharCollection.Count)..];
            foreach(char PendingOverrideChar in PendingOverrideChars)
            {
                if(OriginalCharCollection.Contains(PendingOverrideChar))
                {
                    InstructionProcessor.CharCollection.Add(PendingOverrideChar);
                }
            }
            int FontReplaceIndex = Charset.Length - PendingOverrideChars.Length;
            string AllNewChar = new string(InstructionProcessor.CharCollection.ToArray().Order().ToArray());
            string AllNewCharFile = Path.Combine(TMPPath, "AllNewChar.txt");
            File.WriteAllText(AllNewCharFile, AllNewChar);
            foreach (var fSize in FontSize)
            {
                // 针对Template进行重绘，然后复制到各个字体
                // 如果每个字体都进行重绘，那么重绘后的游戏会崩溃，但只用一份的话就正常，很奇怪，不清楚原因
                // 看起来很像是字体过大了，这里指定一下ReplaceIndex，把一部分原有字体替换掉
                Process.Start("Files\\lucksystem.exe", $"font edit -s \"{ExtractedFontPath}\\{FontTemplate}{fSize}\" -i {FontReplaceIndex} -S \"{ExtractedFontPath}\\info{fSize}\" -f {TargetFontPath} -c {AllNewCharFile} -o {Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}.png")} -O {Path.Combine(PendingReplacePath, $"info{fSize}")}").WaitForExit();
                Process.Start("Files\\czutil.exe", $"replace \"{ExtractedFontPath}\\{FontTemplate}{fSize}\" {Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}.png")} {Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}")}").WaitForExit();
                File.Delete(Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}.png"));
                foreach (var fName in FontName)
                {
                    if (fName != FontTemplate)
                    {
                        File.Copy(Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}"), Path.Combine(PendingReplacePath, $"{fName}{fSize}"));
                    }
                }
            }

            Process.Start("Files\\lucksystem.exe", $"pak replace -s \"{TemplateLBEEFontPak}\" -i \"{PendingReplacePath}\" -o \"{LBEEFontPak}\"").WaitForExit();

            //针对EXE的Patch，这里逐字节扫描所有的数据，直到找到文字的位置，然后替换
            //LBEE的EXE有SteamDRM保护，任何修改都会导致游戏无法启动
            //但是万一呢？先把代码放在这里，或许有朝一日会有办法的。
            /*var ProgramTextPath = Path.Combine(TextMappingPath, "$PROGRAM.json");
            if (File.Exists(ProgramTextPath))
            {
                var LBEE_Vanilla_EXE = Path.Combine(LBEEGamePath, "LITBUS_WIN32.vanilla.bak");
                if(!File.Exists(LBEE_Vanilla_EXE))
                {
                    File.Copy(LBEE_EXE, LBEE_Vanilla_EXE);
                }
                var LBEEBinaries = File.ReadAllBytes(LBEE_Vanilla_EXE);
                var ProgramTextJson = JArray.Parse(File.ReadAllText(ProgramTextPath));
                foreach(var ProgramTextItem in ProgramTextJson)
                {
                    var Source = ProgramTextItem.Value<JObject>()?["Source"]?.Value<string>() ?? "";
                    var Target = ProgramTextItem.Value<JObject>()?["Target"]?.Value<string>() ?? "";
                    if(Source == "" || Target == "" || Source==Target)
                    {
                        continue;
                    }
                    // 逐字节扫描UTF16
                    var SourceU16 = Encoding.Unicode.GetBytes(Source);
                    var TargetU16 = Encoding.Unicode.GetBytes(Target);
                    if(SourceU16.Count()< TargetU16.Count())
                    {
                        Console.WriteLine("Program Text Error: Target Text is longer than Source Text");
                    }
                    else
                    {
                        for (int SearchOffset = 0; SearchOffset <= 1; SearchOffset++)
                        {
                            var SearchEnd = LBEEBinaries.Length - (SourceU16.Length + 2 + SearchOffset);
                            for (var i = SearchOffset; i < SearchEnd; i += 2)
                            {
                                var Matched = true;
                                for (var j = 0; j < SourceU16.Length; j++)
                                {
                                    if (LBEEBinaries[i + j] != SourceU16[j])
                                    {
                                        Matched = false;
                                        break;
                                    }
                                }
                                if (Matched && LBEEBinaries[i + SourceU16.Length] == 0 && LBEEBinaries[i + SourceU16.Length + 1] == 0)
                                {
                                    Array.Copy(TargetU16, 0, LBEEBinaries, i, TargetU16.Length);
                                    LBEEBinaries[i + TargetU16.Length] = 0;
                                    LBEEBinaries[i + TargetU16.Length + 1] = 0;
                                }
                            }
                        }
                    }
                    // 逐字节扫描UTF8
                    var SourceU8 = Encoding.UTF8.GetBytes(Source);
                    var TargetU8 = Encoding.UTF8.GetBytes(Target);
                    if (SourceU8.Count() < TargetU8.Count())
                    {
                        Console.WriteLine("Program Text Error: Target Text is longer than Source Text");
                    }
                    else
                    {
                        var SearchEnd = LBEEBinaries.Length - (SourceU16.Length + 1);
                        for (var i = 0; i < SearchEnd; i++)
                        {
                            var Matched = true;
                            for (var j = 0; j < SourceU8.Length; j++)
                            {
                                if (LBEEBinaries[i + j] != SourceU8[j])
                                {
                                    Matched = false;
                                    break;
                                }
                            }
                            if (Matched && LBEEBinaries[i + SourceU8.Length] == 0)
                            {
                                Array.Copy(TargetU8, 0, LBEEBinaries, i, TargetU8.Length);
                                LBEEBinaries[i + TargetU8.Length] = 0;
                            }
                        }
                    }
                }
                if(File.Exists(LBEE_EXE))
                {
                    File.Delete(LBEE_EXE);
                }
                File.WriteAllBytes(LBEE_EXE, LBEEBinaries);
            }*/

        }
    }

    public class LucaCommand
    {
        public byte[]? Command { get; set; }

        public int CmdPtr = 0;

        public LucaCommand[]? AssignedCommand = null;

        public int GetCmdLength()
        {
            return Command != null ? Command[0] + Command[1] * 256 : 0;
        }

        public int GetPendingLength()
        {
            if (Command == null)
            {
                return 0;
            }
            return Command.Length % 2;
        }

        public byte GetInstruction()
        {
            if (Command == null)
            {
                return 0;
            }
            return Command[2];
        }

        public int ReadCommand(byte[] scriptBytes, int index)
        {
            int commandLength = scriptBytes[index] + scriptBytes[index + 1] * 256;
            Command = scriptBytes.Skip(index).Take(commandLength).ToArray();
            CmdPtr = index;
            return commandLength + commandLength % 2;
        }

        public void SetCmdPtr(int Ptr)
        {
            this.CmdPtr = Ptr;
        }

        public JObject? GetTranslationObj()
        {
            if (Command == null)
            {
                return null;
            }
            if (InstructionProcessor.InstructionGetMapping.ContainsKey(GetInstruction()))
            {
                return InstructionProcessor.InstructionGetMapping[GetInstruction()](Command);
            }
            return null;
        }

        public bool SetTranslationObj(JObject inJsonObj)
        {
            if (Command == null)
            {
                return false;
            }
            if (InstructionProcessor.InstructionSetMapping.ContainsKey(GetInstruction()))
            {
                byte[]? NewCommand = InstructionProcessor.InstructionSetMapping[GetInstruction()](Command, inJsonObj);
                if (NewCommand == null)
                {
                    return false;
                }
                Command = NewCommand;
                Command[1] = (byte)(Command.Length / 256);
                Command[0] = (byte)(Command.Length % 256);
                return true;
            }
            return false;
        }

        public void AssignCommand(List<LucaCommand> InAllCommands, int CmdIndex)
        {
            if (InstructionProcessor.AssignCmdMapping.TryGetValue(GetInstruction(), out var AssignCmdFunc))
            {
                AssignedCommand = AssignCmdFunc(InAllCommands, CmdIndex);
            }
        }

        public void FixCommandPtr()
        {
            if (AssignedCommand != null)
            {
                if (InstructionProcessor.FixPtrMapping.TryGetValue(GetInstruction(), out var FixPtrFunc))
                {
                    FixPtrFunc(this, AssignedCommand);
                }
            }
        }
    }
}
