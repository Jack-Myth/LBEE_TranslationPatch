
Set-Location $PSScriptRoot

$JsonFiles = Get-ChildItem ./TextMapping
foreach($JsonFile in $JsonFiles)
{
    $Source = ConvertFrom-Json -InputObject (Get-Content $JsonFile.FullName -Raw)
    if($null -ne $Source.MESSAGE)
    {
        foreach($MessageItem in $Source.MESSAGE)
        {
            if($MessageItem -eq $null)
            {
                break;
            }
            if($MessageItem.JP.Contains("@") -ne $MessageItem.Translation.Contains("@"))
            {
                Write-Output "Error in $($JsonFile.Name): $($MessageItem.JP) -> $($MessageItem.Translation)"
            }
            elseif ($MessageItem.JP.StartsWith("``") -ne $MessageItem.Translation.StartsWith("``"))
            {
                Write-Output "Error in $($JsonFile.Name): $($MessageItem.JP) -> $($MessageItem.Translation)"
            }
            elseif ($MessageItem.JP.Contains("`$K0","InvariantCultureIgnoreCase") -ne $MessageItem.Translation.Contains("`$K0","InvariantCultureIgnoreCase"))
            {
                Write-Output "Error in $($JsonFile.Name): $($MessageItem.JP) -> $($MessageItem.Translation)"
            }
            if($MessageItem.JP.Length -ge $MessageItem.Translation.Length*2 -or $MessageItem.JP.Length*2 -lt $MessageItem.Translation.Length)
            {
                Write-Output "Length Mismatch in $($JsonFile.Name): $($MessageItem.JP) -> $($MessageItem.Translation)"
            }
        }
    }
    
}