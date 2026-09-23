# Switch NSP 解包记录

在仓库根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Extract-SwitchNsp.ps1
```

脚本默认使用 `Files` 中唯一的 `.nsp`、`hactool.exe` 和 `prod.keys`，输出到 `Files/SwitchExtract`。也可传入 `-NspPath` 和 `-OutputPath`。输出目录中的 `nsp` 是原始 NCA；`contents` 是按 Control、Manual、Meta、Program 分类的解包结果，其中 Program 的 `romfs` 是游戏资源，`exefs` 是可执行内容。

对应的 hactool 命令形式：

```powershell
.\Files\hactool.exe -t pfs0 --outdir=Files\SwitchExtract\nsp '<NSP路径>'
.\Files\hactool.exe -k Files\SwitchExtract\compat.keys --disablekeywarns --suppresskeys -t nca -i '<NCA路径>'
.\Files\hactool.exe -k Files\SwitchExtract\compat.keys --disablekeywarns --suppresskeys -t nca -x --romfsdir='<RomFS输出目录>' --exefsdir='<ExeFS输出目录>' '<Program NCA路径>'
```

当前 `hactool.exe` 构建于 2020 年，直接读取现有 `prod.keys` 会因新版密钥条目报错。脚本会在忽略版本控制的输出目录生成仅含解包所需条目的 `compat.keys`。不要提交 NSP、密钥或解包内容；`.gitignore` 已排除它们。脚本不显示 hactool 的原始诊断，因为其中可能包含密钥值。

本次 NSP 解出了 4 个 NCA：Program、Control、Manual、Meta。Program 的 RomFS 内有 `SCRIPT.PAK`、`FONT.PAK`、图像与音频 PAK，以及 `movie` 目录；ExeFS 内有 `main`、`main.npdm` 等文件。当前包的 NCA 不需要单独提供 `title.keys`。

## 解开指定的 Program PAK

在仓库根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Extract-SwitchPaks.ps1
```

脚本使用项目现有的 `Files/lucksystem.exe`，解开 `FONT`、`SCRIPT`、`OTHCG`、`PARTS`、`SYSCG`、`SYSCG2`、`GM` 七个 PAK。每个包位于 `Files/SwitchExtract/pak-unpacked/<包名>/`，其中 `files/` 是原始文件，`file-list.txt` 是 LuckSystem 输出的索引。可用 `-OutputPath` 指定另一个输出目录。对应的核心命令是：

```powershell
.\Files\lucksystem.exe pak extract -i '<Program RomFS\FONT.PAK>' -o '<输出目录\FONT\file-list.txt>' --all '<输出目录\FONT\files>'
```

这一步只展开 PAK，不转换其中的字体、图片或脚本格式。

本次解包结果：FONT 138、SCRIPT 175、OTHCG 918、PARTS 136、SYSCG 32、SYSCG2 122、GM 171 个文件。
