# Switch 文本适配记录

当前补丁入口选择直接含有 `SCRIPT.PAK` 和 `FONT.PAK` 的 `romfs` 文件夹。也可从补丁根目录运行：

```powershell
.\LBEE_TranslationPatch.exe --romfs '<romfs路径>' --TextOnly --Skip_Description
```

`--TextOnly` 只处理 SCRIPT.PAK，用于当前文本适配阶段；省略时继续原有字体和图片流程，这两部分尚未做 Switch 游戏内验证。PAK 原始备份保存在 `romfs/template`，临时处理目录是 `.tmp/switch-patch`。Switch 流程不生成或安装 Windows 的 `dsound.dll`。

## 字符串

根据当前 `Little Busters Converted Edition [0100943010310000][v0].nsp` 中的 SCRIPT.PAK 实测：

| 前缀 int16（小端） | 后续结构 |
|---|---|
| 正数 N | N 个 UTF-16LE 代码单元，共 2N 字节，再加 `00 00` |
| 负数 -N | N 字节 UTF-8，再加 `00` |
| 0 | 空字符串；仅有前缀 `00 00`，没有额外终止符 |

前缀本身始终占 2 字节；非空字符串的长度均不计终止符。例：`_varstr` 首条文本前缀 `03 00` 后有 6 字节 UTF-16 内容；`seen8500` 中“カップゼリー”的前缀为 `06 00`。

`SwitchString` 负责读写、编码校验、长度范围和终止符校验。修改后保留原字符串的 UTF-8/UTF-16 编码；空字符串按零长度表示。未修改的字符串保持原始字节。指令长度与偶数字节对齐、跨脚本跳转偏移均重新计算。

## 指令表与特殊结构

`Files/OPCODE-Switch.txt` 来自本次 Program NCA 的 `exefs/main`，NSO Build ID 为 `DF3FAB24E1D0C6E8F5EBB006009A2B1CCAAEFF0D0`。用 hactool 的 `-t nso0 --uncompressed=<输出>` 解压 NSO 后，通过 ELF 相对重定位项中的字符串地址还原表：EQU 到 UNKNOWN 共 132 项。当前解压文件中第 0 项名称地址位于 `0x3949E0`，后续相隔 24 字节；这些地址只对应本次构建。

主要映射：VARSTR_SET `0x1C`、MESSAGE `0x23`、SELECT `0x26`、TASK `0x72`、BATTLE `0x78`、CSAYAVOICETEXT `0x82`。最后一个继续使用既有翻译 JSON 中的 `SAYAVOICETEXT` 字段名。

JUMP/FARCALL/ONGOTO 的字符串操作数也使用长度前缀。ONGOTO 每个表项为 2 字节字段加 4 字节跳转偏移。`seen8500/seen8501` 的 BTFUNC 文本表位于启动指令之后，单独解析，保留图标数据及末尾 END；seen8501 的原始指令长度字段为零，继续保留该特殊值。

## 验证与翻译数据差异

```powershell
dotnet run --project .\Tests\SwitchTextTests.csproj -c Release
```

测试读取已解包的 `Files/SwitchExtract/pak-unpacked/SCRIPT/files`，在 `.tmp/switch-adaptation/translation-test` 内写入测试副本。覆盖编码、空串、Int16 边界、损坏长度、原文无损往返、全部文本修改后的读回和跳转目标验证。

当前语料包含 165 个指令脚本、418,228 条指令、108,066 条可处理的文本指令。两个特殊文本表分别有 284 和 1,492 条字符串。

Debug 构建与 Release NativeAOT 发布通过。发布版在临时 romfs 副本上完成 `--TextOnly` 流程（使用条数匹配的测试翻译），重新解开生成的 SCRIPT.PAK 后，175 个文件与待回包文件逐字节相同。使用原有 Steam 翻译表的运行则在不匹配时报错退出，原始 SCRIPT.PAK 哈希保持不变。

当前 Steam 翻译表与 Switch 文本条数不一致的脚本共 13 个：`seen0515`、`seen0516`、`seen0519`、`seen1004`、`seen2000`、`seen2001`、`seen2005`、`seen2521`、`seen2830`、`seen3001`、`seen4000`、`seen8030`、`_varstr`。程序在写回 SCRIPT.PAK 前拒绝条数不匹配的表，详细数量可见测试生成的 `existing-mapping-mismatches.txt`。这些翻译数据尚需按 Switch 内容对齐；通过格式测试不代表已完成游戏内汉化验证。
