param([Parameter(Mandatory)][string]$GamePath)
$ErrorActionPreference = 'Stop'
$game = $GamePath
$manifest = Join-Path $game 'MaydayMoneyControl.install.json'
$relative = 'BepInEx\plugins\MaydayMoneyControl\MaydayMoneyControl.dll'
if (Get-Process MaydayAirways -ErrorAction SilentlyContinue) { throw 'Close the game before upgrading.' }
if (!(Test-Path $manifest) -or !(Test-Path (Join-Path $game $relative))) { throw 'Existing installation not found. Use Install.cmd.' }
$record = Get-Content $manifest -Raw | ConvertFrom-Json
$appIdPath = Join-Path $game 'steam_appid.txt'
if ((Test-Path $appIdPath) -and (Get-Content $appIdPath -Raw).Trim() -ne '5480890') { throw 'Existing steam_appid.txt has a different app ID.' }
$plugin = Join-Path $game $relative
if (!(Test-Path "$plugin.pre102.bak")) { Copy-Item $plugin "$plugin.pre102.bak" }
Copy-Item (Join-Path "$PSScriptRoot\payload" $relative) $plugin -Force
$entry = $record.Files | Where-Object Path -eq $relative
if ($entry) { $entry.Hash = (Get-FileHash $plugin).Hash }
if (!(Test-Path $appIdPath)) {
    Copy-Item "$PSScriptRoot\payload\steam_appid.txt" $appIdPath
    $record.Files = @($record.Files) + [pscustomobject]@{Path='steam_appid.txt';Hash=(Get-FileHash $appIdPath).Hash}
}
$record | ConvertTo-Json -Depth 4 | Set-Content $manifest -Encoding UTF8
Write-Host 'Updated. Use LaunchGame.cmd to start the game.'

