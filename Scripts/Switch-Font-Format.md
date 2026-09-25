# Switch 版字体适配与故障记录

本文记录《Little Busters! Converted Edition》Switch 版 `FONT.PAK` 的两次字体故障、修复和验证。对照样本是 Steam 版 `Little Busters! English Edition/files/FONT.PAK` 与 Switch 原版 `romfs/template/FONT.PAK`。下述格式结论针对这两份样本，不应直接推广到其他游戏。

## 现象与结论

1. 最初的 LayeredFS 补丁可以进入游戏，但文字显示为色块。原因是旧流程把 LuckSystem 生成的 PNG 交给 `czutil replace`，而该工具不能正确重写本作 Switch 版的 18 字节头 CZ1 字体。即使输入从原字库解出的原图，生成的 CZ1 也不能再正常解码。
2. 改由 LuckSystem 直接写 CZ1 后，字形恢复，但一句话显示成数字和符号。原因是 LuckSystem 读取 Switch `info` 后，把其双字段字符数头规范化为单字段形式，写回时没有保留原格式。修复后的 `info` 保留原来的 `100 + 实际字符数` 布局。用户只替换修正后的 `FONT.PAK`，游戏内文字即恢复正常；同一次验证没有改动 `SCRIPT.PAK`。

这两个故障相互独立：CZ1 管字形图像，`info` 管字符到图块的索引与尺寸。仅能解码 CZ1，不代表游戏能正确显示句子。

## PC 与 Switch 原版的差异

| 项目 | PC 版样本 | Switch 版样本 |
| --- | --- | --- |
| 字体图像 | CZ1，8 位调色板 | CZ1，8 位调色板 |
| CZ1 头长度 | 15 字节 | 18 字节；标准头后还有 3 个字节 |
| `ゴシック16` 画布宽度 | 1704 像素 | 1728 像素 |
| `info16` 字符数 | 偏移 4 的 `7112` | 偏移 4 的标记 `100`，偏移 6 的 `7188` |
| `info16` 文件长度 | 283486 字节 | 283716 字节 |

Switch 字体的画布宽度有对齐空间。`LuckSystem/font/font.go` 生成 PNG 时使用 `BlockSize * 100 + 4`，例如 16 号字体生成宽度 1704；写回 CZ1 时要补足至原画布宽度 1728。若新增字符使图像变高，则更新 CZ1 高度，不能静默裁掉新增行。

`info` 的两种字符数表示由 `LuckSystem/font/info.go` 读取。对于 Switch 样本，实际布局为：

```text
uint16 FontSize
uint16 BlockSize
uint16 marker = 100
uint16 CharNum
DrawSize[CharNum]      // 每项 3 字节
UnicodeIndex[65536]    // 每项 2 字节
UnicodeSize[65536]     // 每项 2 字节
```

以 `info16` 为例，原版长度是 `8 + 3 × 7188 + 65536 × 4 = 283716` 字节。旧版 LuckSystem 写回时丢掉标记和第二个字符数字段；实际测试补丁中的 `info16` 以偏移 4 的 `7189` 开头，长度 283717。修复后的同一字体仍以 `100, 7189` 开头，长度 283719。

## 代码改动

- `LuckSystem/czimage/cz1.go`：读取并原样保存 CZ1 的扩展头与调色板；将新图像补至原画布宽度；按原调色板透明度选择像素索引；写出完整 CZ1 与压缩块表。超过原画布宽度时报错，新增字符需要更多行时扩高画布。
- `LuckSystem/cmd/fontEdit.go`：增加 `font edit --output_cz`，让字体编辑命令直接输出 CZ1，同时保持默认 PNG 输出行为。
- `Program.cs`：字体处理改用 `lucksystem font edit --output_cz`，不再对字体调用 `czutil replace`。其他图片的处理路径仍使用 `czutil`。
- `LuckSystem/font/info.go`：记住输入是否使用 `100 + CharNum` 形式，写回时保留该形式。PC 版的单字段形式保持不变。
- `LuckSystem/font/info_test.go`：增加扩展字符数头在增添字符后的回写与重读测试。
- `Files/lucksystem.exe`：使用 Go 1.20.14 编译的 Windows x86 程序；`BuildLuckSystem.Win7.ps1` 是仓库内的编译入口。

## 已做的验证

- PC 版 16 号字体：原图解码、LuckSystem 原样写回、再次解码均成功；`info16` 原样写回逐字节一致。
- Switch 版 16 号字体：原图写回后的 CZ1 保留 18 字节头、1728 像素宽度和原调色板；可以再次解码。抽样 17696 个像素的透明度与原图一致；`info16` 原样写回逐字节一致。
- Switch 版 16 号字体追加一个汉字、36 号字体追加字符并扩高画布后，生成的 CZ1 和 `info` 都能重新读取。
- 使用 LuckSystem 把修改后的字库及 `info` 放入 `FONT.PAK`，再提取出的文件与放入时逐字节一致。
- 从用户实际测试的补丁中仅修正 14 个 `info` 的字符数头，保持字体图像和 `SCRIPT.PAK` 不变；用户确认游戏内字形与文字均正常。

今后回归检查应同时覆盖 CZ1 解码、画布尺寸、调色板、`info` 头与映射，以及游戏内显示。单独通过资源解码或 PAK 重打包，无法代替游戏内验证。

## 暂未处理的 19、22 号字体

Switch 原版 `info19`、`info22` 各声明 237 个图块，但各有 142 条 Unicode 映射指向第 237～389 号图块。LuckSystem 建立反向索引时会数组越界。这与上述两个已修复故障不同；当前汉化程序的字体处理列表没有这两个字号，按用户决定暂不修改。若以后需要处理它们，须先明确这些越界映射在游戏中的含义，再决定如何过滤或保留。
