namespace LBEE_TranslationPatch
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Diagnostics;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BrowseInfo
    {
        public IntPtr hwndOwner;
        public IntPtr root;
        public IntPtr displayName;
        public string title;
        public uint flags;
        public IntPtr callback;
        public IntPtr parameter;
        public int image;
    }

    class Program
    {
        private static readonly JsonSerializerOptions JsonWriteOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        static bool DEBUG_DumpInstructionLayout = false;
        static bool DEBUG_EnableDebugJump = false;
        static string DEBUG_DebugJumpScript = "SEEN2803";
        static uint DEBUG_DebugJumpPtr = 0x14d0;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHBrowseForFolderW(ref BrowseInfo info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SHGetPathFromIDListW(IntPtr idList, StringBuilder path);

        [DllImport("user32.dll")]
        public static extern int MessageBox(IntPtr hWnd, String text, String caption, int options);

        private static string SelectRomfsFolder()
        {
            var info = new BrowseInfo
            {
                title = "选择 Switch 版 romfs 文件夹（内含 SCRIPT.PAK 和 FONT.PAK）", flags = 1,
                displayName = Marshal.AllocHGlobal(260 * sizeof(char))
            };
            IntPtr idList = IntPtr.Zero;
            try
            {
                idList = SHBrowseForFolderW(ref info);
                if (idList == IntPtr.Zero) return string.Empty;
                var path = new StringBuilder(260);
                return SHGetPathFromIDListW(idList, path) ? path.ToString() : string.Empty;
            }
            finally
            {
                if (idList != IntPtr.Zero) Marshal.FreeCoTaskMem(idList);
                Marshal.FreeHGlobal(info.displayName);
            }
        }

        static string RomfsPath = "";
        static string TMPPath = Path.GetFullPath(@".\.tmp\switch-patch");
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
        internal static HashSet<string> IgnoredScriptList = new(StringComparer.OrdinalIgnoreCase)
        { 
            "SEEN8500", "SEEN8501",

            // 这些脚本应该是一些数值变量或者控制逻辑，看起来没有翻译的必要
            "_ARFLAG","_BUILD_COUNT","_CGMODE","_QUAKE",
            "_SCR_LABEL","_TASK","_VARNUM","_VOICE_PARAM","_VARNAME"
        };

        internal static void ConfigureScriptProcessing(string mappingPath)
        {
            TextMappingPath = Path.GetFullPath(mappingPath);
            Operators = File.ReadAllLines("Files/OPCODE-Switch.txt");
            // Keep the established JSON key used by the translation project.
            Operators[0x82] = "SAYAVOICETEXT";
            InstructionProcessor.ScriptCommandRedirectors.Clear();
            InstructionProcessor.CharCollection.Clear();
            ScriptNameContext.Clear();
        }

        public static void ProcessScript(string scriptFile, bool preComputeLayout)
        {
            string fileName = Path.GetFileNameWithoutExtension(scriptFile);
            if (IgnoredScriptList.Contains(fileName)) return;
            string scriptName = fileName.ToLowerInvariant();
            ScriptNameContext.Push(scriptName);
            try
            {
                byte[] original = File.ReadAllBytes(scriptFile);
                var commands = new List<LucaCommand>();
                for (int index = 0; index < original.Length;)
                {
                    var command = new LucaCommand();
                    index += command.ReadCommand(original, index);
                    commands.Add(command);
                }
                for (int i = 0; i < commands.Count; i++) commands[i].AssignCommand(commands, i);

                var textCommands = new Dictionary<string, List<(LucaCommand Command, JsonObject Text)>>();
                foreach (var command in commands)
                {
                    JsonObject? text;
                    try { text = command.GetTranslationObj(); }
                    catch (Exception error) { throw new InvalidDataException($"Text at 0x{command.CmdPtr:X}: {error.Message}", error); }
                    if (text == null) continue;
                    string op = Operators[command.GetInstruction()];
                    if (!textCommands.TryGetValue(op, out var group)) textCommands[op] = group = [];
                    group.Add((command, text));
                }
                string mappingFile = Path.Combine(TextMappingPath, fileName + ".json");
                if (!File.Exists(mappingFile))
                {
                    var exported = new JsonObject();
                    foreach (var (op, group) in textCommands)
                        exported[op] = new JsonArray(group.Select(item => (JsonNode)item.Text).ToArray());
                    File.WriteAllText(mappingFile, exported.ToJsonString(JsonWriteOptions));
                }
                var mapping = JsonNode.Parse(File.ReadAllText(mappingFile))!.AsObject();
                foreach (var (op, node) in mapping)
                {
                    if (node is not JsonArray entries) throw new InvalidDataException($"{op}: expected a translation array.");
                    int count = textCommands.TryGetValue(op, out var group) ? group.Count : 0;
                    if (entries.Count != count)
                        throw new InvalidDataException($"{op}: translation count {entries.Count}, Switch script count {count}.");
                    for (int i = 0; i < count; i++)
                    {
                        if (entries[i] is not JsonObject translation) throw new InvalidDataException($"{op}[{i}]: expected a translation object.");
                        group![i].Command.SetTranslationObj(translation);
                    }
                }
                var redirects = new Dictionary<int, int>();
                int position = 0;
                foreach (var command in commands)
                {
                    redirects.Add(command.CmdPtr, position);
                    command.SetCmdPtr(position);
                    position += command.GetCmdLength() + command.GetPendingLength();
                }
                redirects[original.Length] = position;
                InstructionProcessor.ScriptCommandRedirectors[scriptName] = redirects;
                if (preComputeLayout) return;

                var output = new List<byte>(position);
                InstructionLayoutHandle?.WriteLine("Script:" + fileName);
                foreach (var command in commands)
                {
                    command.FixCommandPtr();
                    InstructionLayoutHandle?.WriteLine($"\t{Operators[command.GetInstruction()]}\t{output.Count}");
                    output.AddRange(command.Command!);
                    if (command.GetPendingLength() != 0) output.Add(0);
                }
                File.WriteAllBytes(scriptFile, output.ToArray());
            }
            catch (Exception error) { throw new InvalidDataException($"{fileName}: {error.Message}", error); }
            finally { ScriptNameContext.Pop(); }
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
                RunTool("Files\\lucksystem.exe", $"font extract -s \"{ExtractedFontPath}\\{FontTemplate}{fSize}\" -S \"{ExtractedFontPath}\\info{fSize}\" -o \"{TmpPng}\" -O \"{TmpCharset}\"");
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
                RunTool("Files\\lucksystem.exe", $"font edit --output_cz -s \"{ExtractedFontPath}\\{FontTemplate}{fSize}\" -i {FontReplaceIndex + AddOffset} -S \"{ExtractedFontPath}\\info{fSize}\" -f \"{TargetFontPath}\" -c \"{AllNewCharFile}\" -o \"{Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}")}\" -O \"{Path.Combine(PendingReplacePath, $"info{fSize}")}\"");
                foreach (var fName in FontName)
                {
                    if (fName != FontTemplate)
                    {
                        File.Copy(Path.Combine(PendingReplacePath, $"{FontTemplate}{fSize}"), Path.Combine(PendingReplacePath, $"{fName}{fSize}"));
                    }
                }
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            try { Run(args); }
            catch (Exception error)
            {
                Console.Error.WriteLine("汉化失败：" + error.Message);
                Environment.ExitCode = 1;
            }
        }

        private static void RunTool(string executable, string arguments)
        {
            using var process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false })
                ?? throw new IOException($"Failed to start {executable}.");
            process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException($"{executable} failed with exit code {process.ExitCode}.");
        }

        static void Run(string[] args)
        {
            HashSet<char> TitleUsedCharset = new HashSet<char>(); // 称号所需的字符集后面要单独处理

            // 检查必须的组件
            if (!(File.Exists(".\\Files\\lucksystem.exe") && 
                File.Exists(".\\Files\\czutil.exe") &&
                File.Exists(".\\Files\\OPCODE-Switch.txt") &&
                File.Exists(TargetFontPath)))
            {
                string Notice = "组件缺失，你是否将补丁文件夹完整解压出来了？";
                Console.Error.WriteLine(Notice);
                MessageBox(IntPtr.Zero, Notice, "LBEE_TranslationPatch", 0);
                return;
            }

            int DescriptionWaitingTime = 30;
            bool textOnly = false;
            for (int i = 0; i < args.Count(); i++)
            {
                switch (args[i])
                {
                    case "--romfs":
                        if (++i >= args.Length) throw new ArgumentException("--romfs requires a folder path.");
                        RomfsPath = Path.GetFullPath(args[i]);
                        break;
                    case "--TextOnly":
                        textOnly = true;
                        break;
                    case "--Skip_Description":
                        DescriptionWaitingTime = 0;
                        break;
                }
            }
            Console.WriteLine("《Little Busters! Converted Edition》Switch 汉化程序 ——By JackMyth\n");
            Console.WriteLine("参考了来自LittleBusters贴吧的翻译文本，替换 romfs 中的英文资源。");
            Console.WriteLine("应用补丁后切换至英文即可看到汉化翻译。\n");
            Console.WriteLine("已知问题：\n为避免查看历史文本出现Bug，限制了选项的字库，部分选项显示为繁体中文。\n");
            Console.WriteLine("若发现文本错误或遗漏，或汉化后游戏存在Bug，请访问 https://github.com/Jack-Myth/LBEE_TranslationPatch 并提交Issue，欢迎讨论。\n");
            Console.Write("请注意，汉化程序会修改游戏脚本，");
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("大概率导致现有存档损坏");
            Console.ResetColor();
            Console.WriteLine("，请使用新存档进行游戏。\n");
            Console.WriteLine("原始 PAK 保存在 romfs/template 中，恢复时可将其复制回 romfs。");
            Console.Write("如果之前安装过汉化补丁，建议先还原，再进行安装，否则可能出现奇怪的问题(如");
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write("字体缺字");
            Console.ResetColor();
            Console.WriteLine("之类的情况)。\n");
            for (int i = DescriptionWaitingTime; i>0;i--)
            {
                char[] TimerIcon = ['-', '\\', '|', '/'];
                for (int j = 0; j < 4; j++)
                {
                    if (!Console.IsOutputRedirected) Console.Write("\r" + new String(' ', Console.CursorLeft));
                    if (!Console.IsOutputRedirected) Console.CursorLeft = 0;
                    Console.Write($"[{TimerIcon[j]}]请阅读上述说明。{i}");
                    Thread.Sleep(250);
                }
            }
            if (!Console.IsOutputRedirected) Console.CursorLeft = 0;
            if (DescriptionWaitingTime > 0)
            {
                Console.WriteLine("若已阅读上述说明，请按任意键开始汉化流程。");
                Console.ReadKey();
            }
            if (string.IsNullOrEmpty(RomfsPath)) RomfsPath = SelectRomfsFolder();
            if (string.IsNullOrEmpty(RomfsPath)) return;
            if (!File.Exists(Path.Combine(RomfsPath, "SCRIPT.PAK")) || !File.Exists(Path.Combine(RomfsPath, "FONT.PAK")))
                throw new InvalidDataException("请选择直接包含 SCRIPT.PAK 和 FONT.PAK 的 romfs 文件夹。");

            Directory.CreateDirectory(TMPPath);
            Directory.CreateDirectory(TextMappingPath);
            Directory.CreateDirectory(ExtractedScriptPath);
            Directory.CreateDirectory(CzTempPath);
            if (Directory.Exists(PendingReplacePath))
            {
                Directory.Delete(PendingReplacePath, true);
            }
            Directory.CreateDirectory(PendingReplacePath);

            string TemplateDir = Path.Combine(RomfsPath, "template");
            string LBEEScriptPak = Path.Combine(RomfsPath, "SCRIPT.PAK");
            string LBEEFontPak = Path.Combine(RomfsPath, "FONT.PAK");
            string TemplateLBEEScriptPak = Path.Combine(TemplateDir, "SCRIPT.PAK");
            string TemplateLBEEFontPak = Path.Combine(TemplateDir, "FONT.PAK");
            if (!Directory.Exists(TemplateDir))
            {
                if (File.Exists(LBEEScriptPak) && File.Exists(LBEEFontPak))
                {
                    Directory.CreateDirectory(TemplateDir);
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
            RunTool(".\\Files\\lucksystem.exe", $"pak extract -i \"{TemplateLBEEScriptPak}\" -o \"{Path.Combine(TMPPath, "ScriptFileList.txt")}\" --all \"{ExtractedScriptPath}\"");

            ConfigureScriptProcessing(TextMappingPath);
            var scriptFiles = Directory.GetFiles(ExtractedScriptPath);

            if (DEBUG_DumpInstructionLayout)
            {
                InstructionLayoutHandle = new StreamWriter("InstructionLayout.txt");
            }

            for(int i = 0;i<scriptFiles.Length;i++)
            {
                // 由于FARCALL指令的存在，需要先进行一次预计算，然后再进行翻译
                ProcessScript(scriptFiles[i], true);
                if (!Console.IsOutputRedirected) Console.Write("\r" + new String(' ', Console.CursorLeft));
                Console.Write($"\rPrecompute Instruction Layout...[{i + 1}/{scriptFiles.Length}]");
            }
            Console.WriteLine("");
            for (int i = 0; i < scriptFiles.Length; i++)
            {
                ProcessScript(scriptFiles[i], false);
                if (!Console.IsOutputRedirected) Console.Write("\r" + new String(' ', Console.CursorLeft));
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
                    0x15,0x00,0x15,0x01,0x21,0x0a
                };
                var JumpCommandsPost = new byte[]
                {
                    0x00,0x06,0x00,0x19,0x01,0x14,0x03
                };
                var JumpCommands = new List<byte>();
                JumpCommands.AddRange(JumpCommandsPre);
                JumpCommands.AddRange(SwitchString.Encode(DEBUG_DebugJumpScript.ToLowerInvariant(), true));
                JumpCommands.AddRange(BitConverter.IsLittleEndian ?
                    BitConverter.GetBytes(DEBUG_DebugJumpPtr) :
                    BitConverter.GetBytes(DEBUG_DebugJumpPtr).Reverse());
                JumpCommands.AddRange(JumpCommandsPost);
                File.WriteAllBytes(ExtractedScriptPath + "\\SEEN0513", JumpCommands.ToArray());
            }

            // 对8500和8501两个脚本进行处理，这两个脚本看起来是专门放字符串的，格式和其他都不一样。
            var TextScriptNames = new string[] { "SEEN8500", "SEEN8501" };
            foreach (var TextScriptName in TextScriptNames)
            {
                string TextScriptPath = Path.Combine(ExtractedScriptPath, TextScriptName);
                string TextScriptJson = Path.Combine(TextMappingPath, $"{TextScriptName}.json");
                var table = new SwitchTextTable(File.ReadAllBytes(TextScriptPath));
                if (!File.Exists(TextScriptJson))
                {
                    var json = new JsonArray();
                    foreach (var text in table.Strings) json.Add((JsonNode?)JsonValue.Create(text.Text));
                    File.WriteAllText(TextScriptJson, json.ToJsonString(JsonWriteOptions));
                }
                var translations = JsonNode.Parse(File.ReadAllText(TextScriptJson))!.AsArray()
                    .Select(node => node?.GetValue<string>() ?? "").ToArray();
                File.WriteAllBytes(TextScriptPath, table.Replace(translations));
                foreach (var text in translations) TitleUsedCharset.UnionWith(text);
            }

            RunTool("Files\\lucksystem.exe", $"pak replace -s \"{TemplateLBEEScriptPak}\" -i \"{ExtractedScriptPath}\" -o \"{LBEEScriptPak}\"");

            if (textOnly)
            {
                Console.WriteLine("SCRIPT.PAK 文本处理完成。");
                return;
            }

            // 解开字体
            RunTool("Files\\lucksystem.exe", $"pak extract -i \"{TemplateLBEEFontPak}\" -o \"{Path.Combine(TMPPath, "FontFileList.txt")}\" --all \"{ExtractedFontPath}\"");

            // 重绘字体
            var FontSize = new int[]
            {
                // 这些字体貌似有点问题,重绘后会导致游戏崩溃，先放着不动
                //36,72,12,14
                16,20,24,27,28,29,30,32,33,34,35,37,38
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
                RunTool("Files\\lucksystem.exe", $"font edit --output_cz -s \"{ExtractedFontPath}\\{FontTemplate}36\" -i {Charset36.Length} -S \"{ExtractedFontPath}\\info36\" -f \"{TargetFontPath}\" -c \"{AllNewCharFile36}\" -o \"{Path.Combine(PendingReplacePath, $"{FontTemplate}36")}\" -O \"{Path.Combine(PendingReplacePath, $"info36")}\"");
                //RunTool("Files\\lucksystem.exe", $"font edit -s \"{ExtractedFontPath}\\{FontTemplate}36\" -a -S \"{ExtractedFontPath}\\info36\" -f \"{TargetFontPath}\" -c \"{AllNewCharFile36}\" -o \"{Path.Combine(PendingReplacePath, $"{FontTemplate}36.png")}\" -O \"{Path.Combine(PendingReplacePath, $"info36")}\"");
                foreach (var fName in FontName)
                {
                    if (fName != FontTemplate)
                    {
                        File.Copy(Path.Combine(PendingReplacePath, $"{FontTemplate}36"), Path.Combine(PendingReplacePath, $"{fName}36"));
                    }
                }
            }*/

            RunTool("Files\\lucksystem.exe", $"pak replace -s \"{TemplateLBEEFontPak}\" -i \"{PendingReplacePath}\" -o \"{LBEEFontPak}\"");

            var ImgPakDirList = Directory.GetDirectories(ImageMappingPath);
            foreach (var ImgPakDir in ImgPakDirList)
            {
                Directory.Delete(PendingReplacePath, true);
                Directory.CreateDirectory(PendingReplacePath);
                var ImgPakName = Path.GetFileName(ImgPakDir);
                string TemplatePak = Path.Combine(TemplateDir, $"{ImgPakName}.PAK");
                string SourcePak = Path.Combine(RomfsPath, $"{ImgPakName}.PAK");
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
                RunTool("Files\\lucksystem.exe", $"pak extract -i \"{TemplatePak}\" -o \"{ImageFileListTxt}\" --all \"{CzTempPath}\"");

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
                        RunTool("Files\\czutil.exe", $"replace \"{ExtractedImgName}\" \"{PendingReplacementPNG}\" \"{PendingReplaceCzImg}\"");
                    }

                    // 同步输出进度，不会让进度混乱
                    lock (SyncLock)
                    {
                        ProcessedImg++;
                        if (!Console.IsOutputRedirected) Console.Write("\r" + new String(' ', Console.CursorLeft));
                        if (!Console.IsOutputRedirected) Console.CursorLeft = 0;
                        Console.Write($"\rReplace CzImg...[{ProcessedImg}/{PendingReplacementPNGs.Length}]");
                    }
                });
                Console.WriteLine("");
                RunTool("Files\\lucksystem.exe", $"pak replace -s \"{TemplatePak}\" -i \"{PendingReplacePath}\" -o \"{SourcePak}\"");
            }
            {
                string Notice = "romfs 汉化处理完成。\n原始 PAK 保存在 romfs/template 中，可复制回去恢复。";
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
            if (index < 0 || index > scriptBytes.Length - 4) throw new InvalidDataException($"Incomplete command at 0x{index:X}.");
            int commandLength = scriptBytes[index] + scriptBytes[index + 1] * 256;
            if (commandLength < 4 || commandLength + commandLength % 2 > scriptBytes.Length - index || scriptBytes[index + 3] > 3)
                throw new InvalidDataException($"Invalid command at 0x{index:X} (length {commandLength}).");
            Command = scriptBytes.Skip(index).Take(commandLength).ToArray();
            CmdPtr = index;
            return commandLength + commandLength % 2;
        }

        public void SetCmdPtr(int Ptr)
        {
            this.CmdPtr = Ptr;
        }

        public JsonObject? GetTranslationObj()
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

        public bool SetTranslationObj(JsonObject inJsonObj)
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
                if (NewCommand.Length > ushort.MaxValue) throw new InvalidDataException("Translated command exceeds UInt16 length.");
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
