$IgnoreList =@(
    "GM_CI_BG00_EN.png"
)

Remove-Item .\bin -Recurse -Force
Remove-Item .\obj -Recurse -Force
dotnet publish ./LBEE_TranslationPatch.csproj --configuration Release /p:PublishProfile=.\Properties\PublishProfiles\FolderProfile.pubxml
Rename-Item .\bin\Release\net10.0\publish\win-x64 LBEE_TranslationPatch
Copy-Item .\Files .\bin\Release\net10.0\publish\LBEE_TranslationPatch\ -Recurse
Copy-Item .\TextMapping .\bin\Release\net10.0\publish\LBEE_TranslationPatch\ -Recurse
Copy-Item .\ImageMapping .\bin\Release\net10.0\publish\LBEE_TranslationPatch\ -Recurse

$ProgramStrArray = ConvertFrom-Json (Get-Content ".\bin\Release\net10.0\publish\LBEE_TranslationPatch\TextMapping\`$PROGRAM.json" -Raw)
$ProgramStrArray[0].Target = $ProgramStrArray[0].Target.TrimEnd('`n')+"`n`n汉化补丁版本："+[DateTime]::Now.ToString("yyyy.MM.dd")
$ProgramStrArray | ConvertTo-Json | Set-Content ".\bin\Release\net10.0\publish\LBEE_TranslationPatch\TextMapping\`$PROGRAM.json" -Encoding utf8