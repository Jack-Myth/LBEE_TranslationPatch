param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string]$NsoPath,
    [string]$MappingPath,
    [string]$WorkPath,
    [string]$PatchSetName = 'LBEE_CN_JP'
)

# Build an Atmosphere IPS patch for the Japanese strings in exefs/main.
# OutputPath is the root of an SD-card-ready atmosphere directory.
# TextMapping/$PROGRAM.json supplies JP -> Target; English Source is untouched.

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $MappingPath) { $MappingPath = Join-Path $root 'TextMapping\$PROGRAM.json' }
if (-not $WorkPath) { $WorkPath = Join-Path $root '.tmp\SwitchExtract\exefs-text-work-jp' }
if (-not $NsoPath) {
    $programs = @(Get-ChildItem -LiteralPath (Join-Path $root '.tmp\SwitchExtract\contents') -Directory -Filter 'program-*')
    if ($programs.Count -ne 1) { throw 'Expected one extracted program-* directory; specify -NsoPath.' }
    $NsoPath = Join-Path $programs[0].FullName 'exefs\main'
}
if ($PatchSetName -notmatch '^[A-Za-z0-9_-]+$') { throw 'PatchSetName must contain only letters, digits, _ or -.' }
$hactool = Join-Path $root 'Files\hactool.exe'
if (-not (Test-Path -LiteralPath $hactool -PathType Leaf)) { throw 'Files/hactool.exe is missing.' }
$NsoPath = (Resolve-Path -LiteralPath $NsoPath).Path
$MappingPath = (Resolve-Path -LiteralPath $MappingPath).Path
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$WorkPath = [IO.Path]::GetFullPath($WorkPath)
New-Item -ItemType Directory -Force -Path $OutputPath, $WorkPath | Out-Null

$uncompressed = Join-Path $WorkPath 'main-uncompressed.nso'
Write-Host "Uncompressing $NsoPath"
& $hactool -t nso0 "--uncompressed=$uncompressed" $NsoPath | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $uncompressed -PathType Leaf)) {
    throw 'hactool did not produce an uncompressed NSO.'
}

$helper = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class SwitchExeFsJapaneseIps
{
    public static int[] FindWhole(byte[] data, byte[] needle, int start, int end, int unit)
    {
        var hits = new List<int>();
        if (needle.Length == 0) return hits.ToArray();
        for (int at = start; at + needle.Length + unit <= end; at++)
        {
            bool boundary = at == start;
            if (!boundary && at >= start + unit)
            {
                boundary = true;
                for (int n = 1; n <= unit; n++) if (data[at - n] != 0) boundary = false;
            }
            if (!boundary) continue;
            bool match = true;
            for (int n = 0; n < needle.Length; n++)
            {
                if (data[at + n] != needle[n]) { match = false; break; }
            }
            if (!match) continue;
            for (int n = 0; n < unit; n++)
            {
                if (data[at + needle.Length + n] != 0) { match = false; break; }
            }
            if (match) hits.Add(at);
        }
        return hits.ToArray();
    }

    public static int ZeroRun(byte[] data, int start, int end)
    {
        int at = start;
        while (at < end && data[at] == 0) at++;
        return at - start;
    }

    public static byte[] MakeIps(byte[] original, byte[] modified)
    {
        if (original.Length != modified.Length) throw new InvalidDataException("NSO size changed.");
        using (var stream = new MemoryStream())
        {
            byte[] header = Encoding.ASCII.GetBytes("PATCH");
            stream.Write(header, 0, header.Length);
            for (int at = 0; at < original.Length;)
            {
                if (original[at] == modified[at]) { at++; continue; }
                int first = at;
                while (at < original.Length && original[at] != modified[at] && at - first < 65535) at++;
                int length = at - first;
                if (first > 0xFFFFFF || first == 0x454F46) throw new InvalidDataException("Unsupported IPS offset.");
                stream.WriteByte((byte)(first >> 16));
                stream.WriteByte((byte)(first >> 8));
                stream.WriteByte((byte)first);
                stream.WriteByte((byte)(length >> 8));
                stream.WriteByte((byte)length);
                stream.Write(modified, first, length);
            }
            byte[] end = Encoding.ASCII.GetBytes("EOF");
            stream.Write(end, 0, end.Length);
            return stream.ToArray();
        }
    }

    public static byte[] ApplyIps(byte[] original, byte[] ips)
    {
        var result = (byte[])original.Clone();
        if (ips.Length < 8 || Encoding.ASCII.GetString(ips, 0, 5) != "PATCH")
            throw new InvalidDataException("Invalid IPS header.");
        int at = 5;
        while (at < ips.Length)
        {
            if (at + 3 == ips.Length && Encoding.ASCII.GetString(ips, at, 3) == "EOF") return result;
            if (at + 5 > ips.Length) throw new InvalidDataException("Truncated IPS.");
            int offset = (ips[at] << 16) | (ips[at + 1] << 8) | ips[at + 2];
            int length = (ips[at + 3] << 8) | ips[at + 4];
            at += 5;
            if (length == 0 || at + length > ips.Length || offset + length > result.Length)
                throw new InvalidDataException("Invalid IPS record.");
            Array.Copy(ips, at, result, offset, length);
            at += length;
        }
        throw new InvalidDataException("Missing IPS EOF.");
    }
}
'@
if (-not ('SwitchExeFsJapaneseIps' -as [type])) { Add-Type -TypeDefinition $helper -Language CSharp }

$original = [IO.File]::ReadAllBytes($uncompressed)
if ($original.Length -lt 0x100 -or [Text.Encoding]::ASCII.GetString($original, 0, 4) -ne 'NSO0') {
    throw 'hactool output is not NSO0.'
}
if (([BitConverter]::ToInt32($original, 0x0C) -band 7) -ne 0) { throw 'NSO is still compressed.' }
$roStart = [BitConverter]::ToInt32($original, 0x20)
$roSize = [BitConverter]::ToInt32($original, 0x28)
$roEnd = [long]$roStart + $roSize
if ($roStart -lt 0x100 -or $roSize -le 0 -or $roEnd -gt $original.Length) {
    throw 'Invalid NSO .rodata bounds.'
}
$buildIdBytes = New-Object byte[] 32
[Array]::Copy($original, 0x40, $buildIdBytes, 0, 32)
$buildIdLength = 32
while ($buildIdLength -gt 0 -and $buildIdBytes[$buildIdLength - 1] -eq 0) { $buildIdLength-- }
if ($buildIdLength -eq 0) { throw 'NSO Build ID is empty.' }
$buildId = [BitConverter]::ToString($buildIdBytes, 0, $buildIdLength).Replace('-', '')

# Stop counting at NUL and exclude NUL padding when appending a UTF-8 range.
# Both code paths were verified only for this game build.
if ($buildId -cne 'DF3FAB24E1D0C6E8F5EBB006009A2B1CCAAEFF0D') {
    throw "Unsupported Build ID for the UTF-8 counter fix: $buildId."
}
$textStart = [BitConverter]::ToInt32($original, 0x10)
$textAddress = [BitConverter]::ToInt32($original, 0x14)
$textSize = [BitConverter]::ToInt32($original, 0x18)
# Rewrite B4470's UTF-8 branch in place, using its shared epilogue at B44A4.
# Scan at most [begin,end), then pass only the bytes before NUL to the existing
# UTF-8 copy / UTF-16 conversion routines. The UTF-16 input branch is untouched.
# The last instruction uses one verified padding word before the next function.
$appendBeforeHex = '01 21 40 A9 02 01 01 EB 20 01 00 54 68 42 40 39 1F 0D 00 71 40 01 00 54 68 00 00 35 E8 03 1F 32 68 42 00 39 E0 03 13 AA 99 84 FF 97 FD 7B 41 A9 E0 03 13 AA F3 07 42 F8 C0 03 5F D6 E0 03 13 AA FB 83 FF 97 FD 7B 41 A9 E0 03 13 AA F3 07 42 F8 C0 03 5F D6 00 00 00 00'
$appendAfterHex = '01 21 40 A9 02 00 80 D2 3F 00 08 EB 22 FF FF 54 29 68 62 38 A9 00 00 34 42 04 00 91 29 00 02 8B 3F 01 08 EB 63 FF FF 54 42 FE FF B4 68 42 40 39 1F 0D 00 71 C0 00 00 54 48 00 00 35 E8 03 1F 32 68 42 00 39 92 84 FF 97 EA FF FF 17 E0 03 13 AA F7 83 FF 97 E7 FF FF 17'
$codePatches = @(
    [pscustomobject]@{
        Name = 'Utf8CounterStopAtNul'; Address = 0xCC710; Offset = 0L
        Before = [byte[]](0x6A, 0x00, 0x00, 0x34) # cbz w10, 0xCC71C
        After = [byte[]](0x6A, 0x02, 0x00, 0x34)  # cbz w10, 0xCC75C (ret)
    },
    [pscustomobject]@{
        Name = 'Utf8AppendStopAtNul'; Address = 0xB44B4; Offset = 0L
        Before = [byte[]]@($appendBeforeHex.Split(' ') | ForEach-Object { [Convert]::ToByte($_, 16) })
        After = [byte[]]@($appendAfterHex.Split(' ') | ForEach-Object { [Convert]::ToByte($_, 16) })
    }
)
if ($textStart -lt 0x100 -or $textSize -le 0 -or
    [long]$textStart + $textSize -gt $original.Length) {
    throw 'Invalid NSO .text bounds.'
}
foreach ($codePatch in $codePatches) {
    if ($codePatch.Before.Length -ne $codePatch.After.Length -or
        $codePatch.Address -lt $textAddress -or
        [long]$codePatch.Address + $codePatch.Before.Length -gt [long]$textAddress + $textSize) {
        throw "$($codePatch.Name) is outside the NSO .text segment or changes code size."
    }
    $codePatch.Offset = [long]$textStart + $codePatch.Address - $textAddress
    for ($i = 0; $i -lt $codePatch.Before.Length; $i++) {
        if ($original[$codePatch.Offset + $i] -ne $codePatch.Before[$i]) {
            throw "Unexpected $($codePatch.Name) code at file offset 0x$('{0:X}' -f $codePatch.Offset)."
        }
    }
}

$items = @(Get-Content -LiteralPath $MappingPath -Raw -Encoding UTF8 | ConvertFrom-Json)
if ($items.Count -eq 0) { throw 'Mapping JSON has no rows.' }
$patched = [byte[]]$original.Clone()
$claimed = New-Object 'System.Collections.Generic.Dictionary[int,byte]'
$rows = New-Object 'System.Collections.Generic.List[object]'
$counts = [ordered]@{ Patched = 0; SlackPatched = 0; TooLong = 0; NotFound = 0; MissingJapanese = 0; Conflict = 0; Unchanged = 0; Invalid = 0 }
$encodings = @(
    @{ Name = 'UTF-8'; Codec = [Text.UTF8Encoding]::new($false, $true); Unit = 1 },
    @{ Name = 'UTF-16LE'; Codec = [Text.Encoding]::Unicode; Unit = 2 }
)

for ($i = 0; $i -lt $items.Count; $i++) {
    $item = $items[$i]
    $jp = [string]$item.JP
    $mappingTarget = [string]$item.Target
    $target = $mappingTarget
    # PC dialog text uses `Speaker@... while Switch stores @Speaker@....
    # Keep the mapping's Chinese wording but write the Switch speaker marker.
    if ($jp -match '^@[^@]+@') {
        if ($target -match '^`[^@]+@') {
            $target = '@' + $target.Substring(1)
        } elseif ($target -match '^[^@`\r\n]{1,30}@') {
            $target = '@' + $target
        }
    }
    $row = [ordered]@{ Index = $i; JP = $jp; Target = $target; Status = ''; Hits = @(); Reason = '' }
    if ($target -cne $mappingTarget) { $row.MappingTarget = $mappingTarget }
    if (-not $jp) {
        $row.Status = 'MissingJapanese'; $row.Reason = 'Mapping row has no JP source.'
    } elseif (-not $target -or $jp.Contains([char]0) -or $target.Contains([char]0)) {
        $row.Status = 'Invalid'; $row.Reason = 'Empty or NUL-containing text.'
    } elseif ($jp -ceq $target) {
        $row.Status = 'Unchanged'; $row.Reason = 'JP equals Target.'
    } else {
        $hits = New-Object 'System.Collections.Generic.List[object]'
        foreach ($encoding in $encodings) {
            $jpBytes = $encoding.Codec.GetBytes($jp)
            $targetBytes = $encoding.Codec.GetBytes($target)
            $offsets = [SwitchExeFsJapaneseIps]::FindWhole($original, $jpBytes, $roStart, [int]$roEnd, $encoding.Unit)
            foreach ($offset in $offsets) {
                $zeros = [SwitchExeFsJapaneseIps]::ZeroRun($original, ($offset + $jpBytes.Length), [int]$roEnd)
                $fits = $targetBytes.Length + $encoding.Unit -le $jpBytes.Length + $zeros
                if ($item.Strict -eq $true -and $targetBytes.Length -gt $jpBytes.Length) { $fits = $false }
                $hits.Add([pscustomobject]@{
                    Offset = $offset; Encoding = $encoding.Name; Unit = $encoding.Unit
                    SourceBytes = $jpBytes.Length; TargetBytes = $targetBytes.Length
                    ZeroRunBytes = $zeros; Fits = $fits; Replacement = $targetBytes
                })
            }
        }
        if ($hits.Count -eq 0) {
            $row.Status = 'NotFound'; $row.Reason = 'No complete JP string in .rodata.'
        } else {
            $row.Hits = @($hits | ForEach-Object {
                [ordered]@{
                    Offset = ('0x{0:X}' -f $_.Offset); Encoding = $_.Encoding
                    SourceBytes = $_.SourceBytes; TargetBytes = $_.TargetBytes
                    ZeroRunBytes = $_.ZeroRunBytes; Fits = $_.Fits
                }
            })
            if (@($hits | Where-Object { -not $_.Fits }).Count -gt 0) {
                $row.Status = 'TooLong'; $row.Reason = 'Target and terminator exceed the JP string plus following NUL bytes.'
            } else {
                $writes = New-Object 'System.Collections.Generic.Dictionary[int,byte]'
                foreach ($hit in $hits) {
                    $length = [Math]::Max($hit.SourceBytes + $hit.Unit, $hit.TargetBytes + $hit.Unit)
                    for ($j = 0; $j -lt $length; $j++) {
                        $value = [byte]0
                        if ($j -lt $hit.TargetBytes) { $value = $hit.Replacement[$j] }
                        $writes[$hit.Offset + $j] = $value
                    }
                }
                $conflict = $false
                foreach ($write in $writes.GetEnumerator()) {
                    if ($claimed.ContainsKey($write.Key) -and $claimed[$write.Key] -ne $write.Value) {
                        $conflict = $true; break
                    }
                }
                if ($conflict) {
                    $row.Status = 'Conflict'; $row.Reason = 'Another row writes different bytes at this offset.'
                } else {
                    $changed = $false
                    foreach ($write in $writes.GetEnumerator()) {
                        $claimed[$write.Key] = $write.Value
                        if ($patched[$write.Key] -ne $write.Value) { $changed = $true }
                        $patched[$write.Key] = $write.Value
                    }
                    if ($changed) {
                        $row.Status = 'Patched'
                        if (@($hits | Where-Object { $_.TargetBytes -gt $_.SourceBytes }).Count -gt 0) { $counts.SlackPatched++ }
                    } else {
                        $row.Status = 'Unchanged'; $row.Reason = 'Already patched by an identical row.'
                    }
                }
            }
        }
    }
    $counts[$row.Status]++
    $rows.Add([pscustomobject]$row)
}

if ($counts.Patched -eq 0) { throw 'No Japanese text could be patched.' }
foreach ($codePatch in $codePatches) {
    [Array]::Copy($codePatch.After, 0, $patched, $codePatch.Offset, $codePatch.After.Length)
}
$ips = [SwitchExeFsJapaneseIps]::MakeIps($original, $patched)
$verified = [SwitchExeFsJapaneseIps]::ApplyIps($original, $ips)
for ($i = 0; $i -lt $patched.Length; $i++) {
    if ($verified[$i] -ne $patched[$i]) { throw "IPS verification failed at 0x$('{0:X}' -f $i)." }
}
$patchDir = Join-Path $OutputPath (Join-Path 'atmosphere\exefs_patches' $PatchSetName)
New-Item -ItemType Directory -Force -Path $patchDir | Out-Null
$ipsPath = Join-Path $patchDir "$buildId.ips"
$reportPath = Join-Path $OutputPath 'exe-text-ips-report.json'
$charsetPath = Join-Path $OutputPath 'exe-text-font-characters.txt'
$characters = New-Object 'System.Collections.Generic.SortedSet[char]'
foreach ($row in $rows) {
    if ($row.Status -ne 'Patched') { continue }
    foreach ($character in $row.Target.ToCharArray()) {
        if ([int]$character -gt 127) { [void]$characters.Add($character) }
    }
}
$report = [ordered]@{
    InputNso = $NsoPath; Mapping = $MappingPath; SourceField = 'JP'; BuildId = $buildId
    RoDataOffset = ('0x{0:X}' -f $roStart); RoDataSize = $roSize
    IpsPath = $ipsPath; FontCharactersPath = $charsetPath
    CodePatches = @($codePatches | ForEach-Object { [ordered]@{
        Name = $_.Name; Address = ('0x{0:X}' -f $_.Address); Offset = ('0x{0:X}' -f $_.Offset)
        Before = [BitConverter]::ToString($_.Before); After = [BitConverter]::ToString($_.After)
    }
    })
    Counts = $counts; Rows = $rows.ToArray()
}
[IO.File]::WriteAllBytes($ipsPath, $ips)
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($charsetPath, (-join @($characters)), [Text.UTF8Encoding]::new($false))
Write-Host "IPS: $ipsPath"
Write-Host "Report: $reportPath"
Write-Host "Font characters: $charsetPath"
Write-Host 'UTF-8 fixes: counting and range appending stop at NUL.'
Write-Host "Rows: $($items.Count); patched: $($counts.Patched) (zero slack: $($counts.SlackPatched)); too long: $($counts.TooLong); missing JP: $($counts.MissingJapanese); not found: $($counts.NotFound); conflict: $($counts.Conflict)"
if ($counts.TooLong -or $counts.NotFound -or $counts.MissingJapanese -or $counts.Conflict -or $counts.Invalid) {
    Write-Warning 'The IPS contains only safely matched Japanese strings. Review the report before distribution.'
}
