param([string]$GamePath='E:\SteamLibrary\steamapps\common\Mayday Airways Demo')
$ErrorActionPreference='Stop'
try {
    $GamePath=[IO.Path]::GetFullPath($GamePath)
    if(!(Test-Path -LiteralPath (Join-Path $GamePath 'MaydayAirways.exe'))) { throw 'Game executable not found. Pass -GamePath with the correct folder.' }
    if(Get-Process -Name MaydayAirways -ErrorAction SilentlyContinue) { throw 'Close Mayday Airways before installation.' }
    $manifestPath=Join-Path $GamePath 'MaydayMoneyControl.install.json'
    if(Test-Path -LiteralPath $manifestPath) { throw 'Already installed. Uninstall first before replacing this version.' }
    $payload=Join-Path $PSScriptRoot 'payload'
    $files=@(Get-ChildItem -LiteralPath $payload -Recurse -File -Force)
    foreach($f in $files) {
        $rel=$f.FullName.Substring($payload.Length+1); $dest=Join-Path $GamePath $rel
        if(Test-Path -LiteralPath $dest) {
            if((Get-FileHash -LiteralPath $f.FullName).Hash -ne (Get-FileHash -LiteralPath $dest).Hash) { throw "Existing file differs: $rel. Nothing has been overwritten." }
        }
    }
    if(Test-Path -LiteralPath (Join-Path $GamePath 'MelonLoader')) { throw 'Existing MelonLoader detected. Do not mix loaders.' }
    $installed=@()
    try {
        foreach($f in $files) {
            $rel=$f.FullName.Substring($payload.Length+1); $dest=Join-Path $GamePath $rel
            if(!(Test-Path -LiteralPath $dest)) {
                New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
                Copy-Item -LiteralPath $f.FullName -Destination $dest
                $installed+= [pscustomobject]@{Path=$rel;Hash=(Get-FileHash -LiteralPath $dest).Hash}
            }
        }
    } finally {
        if($installed.Count -gt 0) { [pscustomobject]@{GamePath=$GamePath;Files=$installed;InstalledAt=[DateTime]::UtcNow.ToString('O')} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8 }
    }
    Write-Host 'Installed. Launch the game, host your own lobby, then open the money panel.' -ForegroundColor Green
} catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }
