param([string]$GamePath='E:\SteamLibrary\steamapps\common\Mayday Airways Demo')
$ErrorActionPreference='Stop'
try {
    if(Get-Process -Name MaydayAirways -ErrorAction SilentlyContinue) { throw 'Close the game first.' }
    $GamePath=[IO.Path]::GetFullPath($GamePath).TrimEnd('\')
    $manifestPath=Join-Path $GamePath 'MaydayMoneyControl.install.json'
    $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach($item in $manifest.Files) {
        $target=[IO.Path]::GetFullPath((Join-Path $GamePath $item.Path))
        if(!$target.StartsWith($GamePath+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid installation manifest path.' }
        if(Test-Path -LiteralPath $target) {
            if((Get-FileHash -LiteralPath $target).Hash -eq $item.Hash) { Remove-Item -LiteralPath $target }
            else { Write-Host "Kept changed file: $($item.Path)" }
        }
    }
    Remove-Item -LiteralPath $manifestPath
    Write-Host 'Uninstalled added files. Game files and generated logs/configs were preserved.'
} catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }
