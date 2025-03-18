namespace LBEE_TranslationPatch
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

        [DllImport("user32.dll")]
        public static extern int MessageBox(IntPtr hWnd, String text, String caption, int options);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, uint cbData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);


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
        static string TMPPath = Path.GetFullPath(@".\.tmp");
        static string TextMappingPath = Path.GetFullPath(@".\TextMapping");
        static string ImageMappingPath = Path.GetFullPath(@".\ImageMapping");
        static string CzTempPath = Path.Combine(TMPPath, "CzTemp");
        static string ExtractedScriptPath = Path.Combine(TMPPath, "Scripts");
        static string ExtractedFontPath = Path.Combine(TMPPath, "Fonts");
        static string PendingReplacePath = Path.Combine(TMPPath, "PendingReplace");
        static string TargetFontPath = Path.GetFullPath(@".\Files\simhei.ttf");
        static string[] FontName = new string[]
            {
                "モダン","明朝","ゴシック","丸ゴシック"
            };

        static string FontTemplate = "ゴシック";
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

        public static void ProcessFont(int[] FontSize,HashSet<char> FullCharset,bool AddMode)
        {
            foreach (var fSize in FontSize)
            {
                string TmpPng = Path.Combine(TMPPath, "tmp.png");
                if (File.Exists(TmpPng))
                {
                    File.Delete(TmpPng);
                }
                string TmpCharset = Path.Combine(TMPPath, "FontCharset.txt");
                if (File.Exists(TmpCharset))
                {
                    File.Delete(TmpCharset);
                }
                // 从字符集Dump出字符集
                Process.Start("Files\\lucksystem.exe", $"font extract -s \"{ExtractedFontPath}\\{FontTemplate}{fSize}\" -S \"{ExtractedFontPath}\\info{fSize}\" -o \"{TmpPng}\" -O \"{TmpCharset}\"").WaitForExit();
                string Charset = File.ReadAllText(TmpCharset);
                HashSet<char> CurCharset = new HashSet<char>(FullCharset);
                CurCharset.Remove('　');
                CurCharset.Remove('\n');
                HashSet<char> OriginalCharCollection = new(CurCharset);
                foreach (var oldChar in Charset.ToCharArray())
                {
                    CurCharset.Remove(oldChar);
                }
                List<char>? AllNewCharArray = null;
                int FontReplaceIndex = 0;
                int AddOffset = 0;
                if (!AddMode)
                {
                    bool OverrideOriginalChar = true;
                    int LastCharsetIndex = Charset.Length;
                    HashSet<char> ExistedChars = new HashSet<char>();
                    while (OverrideOriginalChar)
                    {
                        OverrideOriginalChar = false;
                        var PendingOverrideChars = Charset[(Charset.Length - CurCharset.Count)..LastCharsetIndex];
                        LastCharsetIndex = Charset.Length - CurCharset.Count;
                        foreach (char PendingOverrideChar in PendingOverrideChars)
                        {
                            if (OriginalCharCollection.Contains(PendingOverrideChar))
                            {
                                CurCharset.Add(PendingOverrideChar);
                                ExistedChars.Add(PendingOverrideChar);
                                OverrideOriginalChar = true;
                            }
                        }
                    }

                    // 对字符集中已有的字符进行重排序，放在最后
                    // LuckSystem对已有字符的替换有bug，如果如果已有字符在新字符集中的位置在原字符集中的位置之前
                    // 那么导致后面的字符将前面的字符清除，会出现字符丢失的情况
                    // 这里将已有字符全都放在最后面，这样就不会出现这个问题
                    // 如果要从根源解决，需要魔改LuckSystem，不过不是很有必要，先做个标记，之后如果有需要再说。
                    // TODO: LuckSystem/font/info.go:203
                    FontReplaceIndex = Charset.Length - CurCharset.Count;
                    AllNewCharArray = CurCharset.ToArray().Order().ToList();
                    foreach (var ExistedChar in ExistedChars)
                    {
                        AllNewCharArray.Remove(ExistedChar);
                        AllNewCharArray.Add(ExistedChar);
                    }
                    for (int i = 0; i < Charset.Length; i++)
                    {
                        int NewCharIndex = AllNewCharArray.FindIndex(0, (A) => A == Charset[i]);
                        if (NewCharIndex != -1 && NewCharIndex + FontReplaceIndex <= i)
                        {
                            // 新字符集中的字符会在原字符集之前，不能接受，要把这个字符放到最后
                            AllNewCharArray.Remove(Charset[i]);
                            AllNewCharArray.Add(Charset[i]);
                        }
                    }
                    AddOffset = 0;
                    for (int i = AllNewCharArray.Count - 1; i >= 0; i--)
                    {
                        int OldCharIndex = Charset.ToList().FindIndex(0, (A) => A == AllNewCharArray[i]);
                        if (OldCharIndex >= i + FontReplaceIndex + AddOffset)
                        {
                            AddOffset++;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else
                {
                    AllNewCharArray = CurCharset.ToList().Order().ToList();
                    FontReplaceIndex = Charset.Length;
                    AddOffset = 0;
                }

                string AllNewChar = new string(AllNewCharArray.ToArray());
                string AllNewCharFile = Path.Combine(TMPPath, "AllNewChar.txt");
                if(AllNewChar.Length==0)
                {
                    continue;
                }
                File.WriteAllText(AllNewCharFile, AllNewChar);
                // 针对Template进行重绘，然后复制到各个字体
                // 如果每个字体都进行重绘，那么重绘后的游戏会崩溃，但只用一份的话就正常，很奇怪，不清楚原因
                // 看起来很像是字体过大了，这里指定一下ReplaceIndex，把一部分原有字体替换掉
                Process.Start("Files\\lucksystem.exe", $"font edit -s \"{ExtractedFontPath}\\{FontTemplate}{fSize}\" -i {FontReplaceIndex + AddOffset} -S \"{ExtractedFontPath}\\info{fSize}\" -f \"{TargetFontPath}\" -c \"{AllNewCharFile}\" -o \"{Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}.png")}\" -O \"{Path.Combine(PendingReplacePath, $"info{fSize}")}\"").WaitForExit();
                Process.Start("Files\\czutil.exe", $"replace \"{ExtractedFontPath}\\{FontTemplate}{fSize}\" \"{Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}.png")}\" \"{Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}")}\"").WaitForExit();
                File.Delete(Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}.png"));
                foreach (var fName in FontName)
                {
                    if (fName != FontTemplate)
                    {
                        File.Copy(Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}"), Path.Combine(PendingReplacePath, $"{fName}{fSize}"));
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            // 检查必须的组件
            if (!(File.Exists(".\\Files\\lucksystem.exe") && 
                File.Exists(".\\Files\\czutil.exe") &&
                File.Exists(".\\Files\\OPCODE.txt") &&
                File.Exists(".\\Files\\DxHook.base") &&
                File.Exists(TargetFontPath)))
            {
                string Notice = "组件缺失，你是否将补丁文件夹完整解压出来了？";
                Console.Error.WriteLine(Notice);
                MessageBox(IntPtr.Zero, Notice, "LBEE_TranslationPatch", 0);
                return;
            }

            int DescriptionWaitingTime = 20;
            for (int i = 0; i < args.Count(); i++)
            {
                switch (args[i])
                {
                    case "--LBEE_EXE":
                        LBEE_EXE = args[++i];
                        // 获取LBEE_Exe所在文件夹
                        LBEEGamePath = Path.GetDirectoryName(LBEE_EXE) ?? "";
                        break;
                    case "--Skip_Description":
                        DescriptionWaitingTime = 0;
                        break;
#if !RELEASE
                    case "--DxHookOnly":
                        goto DxHook;
#endif
                }
            }
            Console.WriteLine("《Little Busters! English Edition》汉化程序 ——By JackMyth\n");
            Console.WriteLine("参考了来自LittleBusters贴吧的翻译文本，替换了原有的英文。仅支持Steam正式版本。");
            Console.WriteLine("应用补丁后切换至英文即可看到汉化翻译。\n");
            Console.WriteLine("已知问题：\n为避免查看历史文本出现Bug，限制了选项的字库，部分选项显示为繁体中文。\n");
            Console.WriteLine("若发现文本错误或遗漏，或汉化后游戏存在Bug，请访问 https://github.com/Jack-Myth/LBEE_TranslationPatch 并提交Issue，欢迎讨论。\n");
            Console.Write("请注意，汉化程序会修改游戏脚本，");
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("大概率导致现有存档损坏");
            Console.ResetColor();
            Console.WriteLine("，请使用新存档进行游戏。\n");
            Console.WriteLine("如果需要恢复原版，请删除游戏文件夹下的dsound.dll，并使用Steam验证游戏文件完整性，会自动还原被修改的文件。");
            Console.WriteLine("如果之前安装过汉化补丁，建议先还原，再进行安装，避免出现奇怪的问题。\n");
            for (int i = DescriptionWaitingTime; i>0;i--)
            {
                char[] TimerIcon = ['-', '\\', '|', '/'];
                for (int j = 0; j < 4; j++)
                {
                    Console.Write("\r" + new String(' ', Console.CursorLeft));
                    Console.CursorLeft = 0;
                    Console.Write($"[{TimerIcon[j]}]请阅读上述说明。{i}");
                    Thread.Sleep(250);
                }
            }
            Console.CursorLeft = 0;
            Console.WriteLine("若已阅读上述说明，请按任意键开始汉化流程。");
            Console.ReadKey();
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
            Directory.CreateDirectory(CzTempPath);
            if (Directory.Exists(PendingReplacePath))
            {
                Directory.Delete(PendingReplacePath, true);
            }
            Directory.CreateDirectory(PendingReplacePath);

            // 首先将所有的Program.json里的文本加入字符集
            {
                var ProgmJson = JArray.Parse(File.ReadAllText(Path.Combine(TextMappingPath, "$PROGRAM.json")));
                foreach(var ProgmItem in ProgmJson)
                {
                    var ProgmItemObj = ProgmItem.ToObject<JObject>();
                    var TargetStr = ProgmItemObj?.GetValue("Target")?.Value<string>() ?? "";
                    foreach(char StrChar in TargetStr)
                    {
                        InstructionProcessor.CharCollection.Add(StrChar);
                    }
                }
            }

            string TemplateDir = Path.Combine(LBEEGamePath, "files", "template");
            string LBEEScriptPak = Path.Combine(LBEEGamePath, @"files\SCRIPT.PAK");
            string LBEEFontPak = Path.Combine(LBEEGamePath, @"files\FONT.PAK");
            string TemplateLBEEScriptPak = Path.Combine(TemplateDir, "SCRIPT.PAK");
            string TemplateLBEEFontPak = Path.Combine(TemplateDir, "FONT.PAK");
            if (!Directory.Exists(TemplateDir))
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
            Process.Start(".\\Files\\lucksystem.exe", $"pak extract -i \"{TemplateLBEEScriptPak}\" -o \"{Path.Combine(TMPPath, "ScriptFileList.txt")}\" --all \"{ExtractedScriptPath}\"").WaitForExit();

            Operators = File.ReadAllText(".\\Files\\OPCODE.txt").ReplaceLineEndings().Split(Environment.NewLine);
            var scriptFiles = Directory.GetFiles(ExtractedScriptPath);

            if (DEBUG_DumpInstructionLayout)
            {
                InstructionLayoutHandle = new StreamWriter("InstructionLayout.txt");
            }

            Dictionary<string, Dictionary<int, int>> ScriptCommandRedirectors = new();

            for(int i = 0;i<scriptFiles.Length;i++)
            {
                // 由于FARCALL指令的存在，需要先进行一次预计算，然后再进行翻译
                ProcessScript(scriptFiles[i], true);
                Console.Write("\r" + new String(' ', Console.CursorLeft));
                Console.Write($"\rPrecompute Instruction Layout...[{i + 1}/{scriptFiles.Length}]");
            }
            Console.WriteLine("");
            for (int i = 0; i < scriptFiles.Length; i++)
            {
                ProcessScript(scriptFiles[i], false);
                Console.Write("\r" + new String(' ', Console.CursorLeft));
                Console.Write($"\rProcess Script...[{i + 1}/{scriptFiles.Length}]");
            }
            Console.WriteLine("");

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

            // 对8500和8501两个脚本进行处理，这两个脚本看起来是专门放字符串的，格式和其他都不一样。
            var TextScriptNames = new string[] { "SEEN8500", "SEEN8501" };
            HashSet<char> TitleUsedCharset = new HashSet<char>(); // 称号所需的字符集后面要单独处理
            foreach (var TextScriptName in TextScriptNames)
            {
                string TextScriptPath = Path.Combine(ExtractedScriptPath, TextScriptName);
                string TextScriptJson = Path.Combine(TextMappingPath, $"{TextScriptName}.json");
                byte[] ScirptBuffer = File.ReadAllBytes(TextScriptPath);
                int StartIndex = 0x0a;
                bool UseIcon = ScirptBuffer[8] != 1;
                const int TailLength = 8;  // 尾部固定保留8字节
                List<string> StrList = new List<string>();
                List<byte> StrBytes = new List<byte>();
                List <byte[]> IconBytes = new List<byte[]>();
                bool NeedParseIcon = UseIcon;
                for (int i = StartIndex; i < ScirptBuffer.Length - TailLength; i += 2)
                {
                    if(NeedParseIcon)
                    {
                        IconBytes.Add(ScirptBuffer[i..(i + 3)]);
                        i++; // i=i+3-2;
                        NeedParseIcon = false;
                        continue;
                    }
                    if (ScirptBuffer[i] == 0 && ScirptBuffer[i + 1] == 0)
                    {
                        StrList.Add(Encoding.Unicode.GetString(StrBytes.ToArray()));
                        StrBytes.Clear();
                        if(UseIcon && StrList.Count % 2==0)
                        {
                            NeedParseIcon = true;
                        }
                    }
                    else
                    {
                        StrBytes.Add(ScirptBuffer[i]);
                        StrBytes.Add(ScirptBuffer[i + 1]);
                    }
                }
                if (!File.Exists(TextScriptJson))
                {
                    JArray TargetJson = new JArray();
                    foreach (var Str in StrList)
                    {
                        TargetJson.Add(Str);
                    }
                    File.WriteAllText(TextScriptJson, TargetJson.ToString());
                }
                else
                {
                    var TargetJson = JArray.Parse(File.ReadAllText(TextScriptJson));
                    if(TargetJson.Count != StrList.Count)
                    {
                        Console.Error.WriteLine("Error: TextScript Json Count Mismatch(" + TextScriptName + "), Exit!");
                        Environment.Exit(-1);
                    }
                    StrList.Clear();
                    for(int i=0;i<TargetJson.Count;i++)
                    {
                        StrList.Add(TargetJson[i].Value<string>() ?? "");
                    }
                    List<byte> NewScript = new List<byte>();
                    NewScript.AddRange(ScirptBuffer[0..StartIndex]);
                    for (int i = 0; i < StrList.Count; i++)
                    {
                        if(UseIcon && i % 2==0)
                        {
                            NewScript.AddRange(IconBytes[i / 2]);
                        }
                        byte[] TmpStrBytes = Encoding.Unicode.GetBytes(StrList[i]);
                        NewScript.AddRange(TmpStrBytes);
                        foreach(char StrChar in StrList[i])
                        {
                            TitleUsedCharset.Add(StrChar);
                        }
                        NewScript.Add(0);
                        NewScript.Add(0);
                    }
                    NewScript.AddRange(ScirptBuffer[(ScirptBuffer.Length - TailLength)..]);
                    File.WriteAllBytes(TextScriptPath, NewScript.ToArray());
                }
            }

            Process.Start("Files\\lucksystem.exe", $"pak replace -s \"{TemplateLBEEScriptPak}\" -i \"{ExtractedScriptPath}\" -o \"{LBEEScriptPak}\"").WaitForExit();

            // 解开字体
            Process.Start("Files\\lucksystem.exe", $"pak extract -i \"{TemplateLBEEFontPak}\" -o \"{Path.Combine(TMPPath, "FontFileList.txt")}\" --all \"{ExtractedFontPath}\"").WaitForExit();

            // 重绘字体
            var FontSize = new int[]
            {
                // 这些字体貌似有点问题,重绘后会导致游戏崩溃，先放着不动
                //36,72,12,14
                16,18,20,24,28,30,32
                //28
            };

            ProcessFont(FontSize, InstructionProcessor.CharCollection, false);
            ProcessFont([36], TitleUsedCharset, true);

            /*{
                string Charset36 = File.ReadAllText(LBEECharset36);
                HashSet<char> Charset36Set = new HashSet<char>();
                foreach (char c in Charset36)
                {
                    Charset36Set.Add(c);
                }
                List<char> PendingAddChar = new List<char>();
                foreach (char c in TitleUsedCharset)
                {
                    if (!Charset36Set.Contains(c))
                    {
                        PendingAddChar.Add(c);
                    }
                }
                string AllNewChar36 = new string(PendingAddChar.Order().ToArray());
                string AllNewCharFile36 = Path.Combine(TMPPath, "AllNewChar36.txt");
                File.WriteAllText(AllNewCharFile36, AllNewChar36);
                Process.Start("Files\\lucksystem.exe", $"font edit -s \"{ExtractedFontPath}\\{FontTemplate}36\" -i {Charset36.Length} -S \"{ExtractedFontPath}\\info36\" -f \"{TargetFontPath}\" -c \"{AllNewCharFile36}\" -o \"{Path.Combine(PendingReplacePath, $"{FontTemplate}36.png")}\" -O \"{Path.Combine(PendingReplacePath, $"info36")}\"").WaitForExit();
                //Process.Start("Files\\lucksystem.exe", $"font edit -s \"{ExtractedFontPath}\\{FontTemplate}36\" -a -S \"{ExtractedFontPath}\\info36\" -f \"{TargetFontPath}\" -c \"{AllNewCharFile36}\" -o \"{Path.Combine(PendingReplacePath, $"{FontTemplate}36.png")}\" -O \"{Path.Combine(PendingReplacePath, $"info36")}\"").WaitForExit();
                Process.Start("Files\\czutil.exe", $"replace \"{ExtractedFontPath}\\{FontTemplate}36\" \"{Path.Combine(PendingReplacePath, $"{FontTemplate}36.png")}\" \"{Path.Combine(PendingReplacePath, $"{FontTemplate}36")}\"").WaitForExit();
                File.Delete(Path.Combine(PendingReplacePath, $"{FontTemplate}36.png"));
                foreach (var fName in FontName)
                {
                    if (fName != FontTemplate)
                    {
                        File.Copy(Path.Combine(PendingReplacePath, $"{FontTemplate}36"), Path.Combine(PendingReplacePath, $"{fName}36"));
                    }
                }
            }*/

            Process.Start("Files\\lucksystem.exe", $"pak replace -s \"{TemplateLBEEFontPak}\" -i \"{PendingReplacePath}\" -o \"{LBEEFontPak}\"").WaitForExit();

            var ImgPakDirList = Directory.GetDirectories(ImageMappingPath);
            foreach (var ImgPakDir in ImgPakDirList)
            {
                Directory.Delete(PendingReplacePath, true);
                Directory.CreateDirectory(PendingReplacePath);
                var ImgPakName = Path.GetFileName(ImgPakDir);
                string TemplatePak = Path.Combine(TemplateDir, $"{ImgPakName}.PAK");
                string SourcePak = Path.Combine(LBEEGamePath, $"files\\{ImgPakName}.PAK");
                if (!File.Exists(SourcePak))
                {
                    continue;
                }
                if(!File.Exists(TemplatePak))
                {
                    File.Copy(SourcePak, TemplatePak);
                }

                // 将PNG图片转为CZ格式
                // 先获取图片列表
                var PendingReplacementPNGs = Directory.GetFiles(ImgPakDir, "*.png");
                // czutils不能直接创建cz图片，所以先解出来，然后再替换图像数据
                var ImageFileListTxt = Path.Combine(TMPPath, "ImageFileList.txt");
                if (File.Exists(ImageFileListTxt))
                {
                    File.Delete(ImageFileListTxt); // 清除可能存在的临时文件
                }
                if(Directory.Exists(CzTempPath))
                {
                    Directory.Delete(CzTempPath, true);
                    Directory.CreateDirectory(CzTempPath);
                }
                Process.Start("Files\\lucksystem.exe", $"pak extract -i \"{TemplatePak}\" -o \"{ImageFileListTxt}\" --all \"{CzTempPath}\"").WaitForExit();

                // czutil的速度还是比较慢的，这里使用多线程处理
                int ProcessedImg = 0;
                object SyncLock = new object();
                ParallelOptions parallelOptions = new ParallelOptions();
                parallelOptions.MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2);
                Console.Write("Replace CzImg...\r");
                Parallel.ForEach(PendingReplacementPNGs, parallelOptions, (PendingReplacementPNG) =>
                {
                    var ImgFileName = Path.GetFileNameWithoutExtension(PendingReplacementPNG);
                    var ExtractedImgName = Path.Combine(CzTempPath, ImgFileName);
                    if (File.Exists(ExtractedImgName))
                    {
                        // 如果确实有对应的czImg被提取出来了，那么就替换
                        var PendingReplaceCzImg = Path.Combine(PendingReplacePath, ImgFileName);
                        Process.Start("Files\\czutil.exe", $"replace \"{ExtractedImgName}\" \"{PendingReplacementPNG}\" \"{PendingReplaceCzImg}\"").WaitForExit();
                    }

                    // 同步输出进度，不会让进度混乱
                    lock (SyncLock)
                    {
                        ProcessedImg++;
                        Console.Write("\r" + new String(' ', Console.CursorLeft));
                        Console.CursorLeft = 0;
                        Console.Write($"\rReplace CzImg...[{ProcessedImg}/{PendingReplacementPNGs.Length}]");
                    }
                });
                Console.WriteLine("");
                Process.Start("Files\\lucksystem.exe", $"pak replace -s \"{TemplatePak}\" -i \"{PendingReplacePath}\" -o \"{SourcePak}\"").WaitForExit();
            }
#if !RELEASE
        DxHook:
#endif
            // 将$Program.json塞进DxHook.base中
            string DXHookPath = Path.Combine(TMPPath, "dsound.dll");
            string DxHookSourcePath = "Files\\DxHook.base";
            if(File.Exists(DXHookPath))
            {
                File.Delete(DXHookPath);
            }
            File.Copy(DxHookSourcePath, DXHookPath);
            IntPtr hUpdate = BeginUpdateResource(DXHookPath, false);
            IntPtr lpType = Marshal.StringToHGlobalAuto("JSON");
            byte[] PROGRAMBytes = File.ReadAllBytes(Path.Combine(TextMappingPath, "$PROGRAM.json"));
            UpdateResource(hUpdate, lpType, 101, 0, PROGRAMBytes, (uint)PROGRAMBytes.Length);
            Marshal.FreeHGlobal(lpType);
            EndUpdateResource(hUpdate, false);
            File.Move(DXHookPath, Path.Combine(LBEEGamePath, "dsound.dll"), true);
#if RELEASE
            Directory.Delete(TemplateDir, true);
#endif
            {
                string Notice = "汉化完成。\n如果需要恢复原版，请删除游戏文件夹下的dsound.dll，并使用Steam验证游戏文件完整性，会自动还原被修改的文件。";
                Console.WriteLine(Notice);
                MessageBox(IntPtr.Zero, Notice, "LBEE_TranslationPatch", 0);
            }
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
