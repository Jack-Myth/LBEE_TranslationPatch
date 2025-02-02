$IgnoreList =@(
    "GM_CI_BG00_EN.png"
)

Remove-Item .\bin -Recurse -Force
Remove-Item .\obj -Recurse -Force
dotnet publish ./LBEE_TranslationPatch.csproj --configuration Release /p:PublishProfile=.\Properties\PublishProfiles\FolderProfile.pubxml
Rename-Item .\bin\Release\net8.0\publish\win-x86 LBEE_TranslationPatch
Copy-Item .\Files .\bin\Release\net8.0\publish\LBEE_TranslationPatch\ -Recurse
Copy-Item .\TextMapping .\bin\Release\net8.0\publish\LBEE_TranslationPatch\ -Recurse
Copy-Item .\ImageMapping .\bin\Release\net8.0\publish\LBEE_TranslationPatch\ -Recurse

$ProgramStrArray = ConvertFrom-Json (Get-Content ".\bin\Release\net8.0\publish\LBEE_TranslationPatch\TextMapping\`$PROGRAM.json" -Raw)
$ProgramStrArray[0].Target = $ProgramStrArray[0].Target.TrimEnd('`n')+"`n`n汉化补丁版本："+[DateTime]::Now.ToString("yyyy.MM.dd")
$ProgramStrArray | ConvertTo-Json | Set-Content ".\bin\Release\net8.0\publish\LBEE_TranslationPatch\TextMapping\`$PROGRAM.json" -Encoding utf8

$Images = Get-ChildItem .\ImageMapping -Recurse -File
$IgnoreImgs = @()
foreach($Image in $Images)
{
    $relativePath = Resolve-Path -Relative $Image.FullName
    foreach($ignore in $IgnoreList)
    {
        if($relativePath.Contains($ignore))
        {
            $IgnoreImgs+=$Image
        }
    }
}
foreach($IgnoreImg in $IgnoreImgs)
{
    $Images = $Images|ForEach-Object{
        if($_ -ne $IgnoreImg)
        {
            $_
        }
    }
}
$Images|ForEach-Object -ThrottleLimit 5 -Parallel {
    $relativePath = Resolve-Path -Relative $_.FullName
    $FileHistory = @(git log --pretty=format:"%h" $relativePath)
    if($FileHistory.Count -eq 1)
    {
        Remove-Item .\bin\Release\net8.0\publish\LBEE_TranslationPatch\$relativePath
    }
}