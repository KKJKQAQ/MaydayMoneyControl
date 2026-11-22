$ErrorActionPreference = 'Stop'
$record = Join-Path $PSScriptRoot 'game-path.txt'
$game = if (Test-Path $record) { (Get-Content $record -Raw).Trim() }
if (!$game -or !(Test-Path (Join-Path $game 'MaydayAirways.exe'))) {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = 'Select the Mayday Airways Demo folder containing MaydayAirways.exe'
    try {
        if ($dialog.ShowDialog() -ne 'OK') { exit }
        $game = $dialog.SelectedPath
    } finally { $dialog.Dispose() }
    if (!(Test-Path (Join-Path $game 'MaydayAirways.exe'))) { throw 'MaydayAirways.exe not found in the selected folder.' }
    Set-Content -LiteralPath $record -Value $game -Encoding UTF8
}
switch ($args[0]) {
    'Install' { & "$PSScriptRoot\Install.ps1" -GamePath $game }
    'Upgrade' { & "$PSScriptRoot\Upgrade.ps1" -GamePath $game }
    'Uninstall' { & "$PSScriptRoot\Uninstall.ps1" -GamePath $game }
    'LaunchGame' { if (!(Test-Path (Join-Path $game 'winhttp.dll')) -or !(Test-Path (Join-Path $game 'doorstop_config.ini'))) { throw 'Mod loader missing. Close the game and run Install.cmd.' }; Start-Process -FilePath (Join-Path $game 'MaydayAirways.exe') -WorkingDirectory $game }
    'OpenPanel' {
        $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
        if (!(Test-Path $dotnet) -or !(& $dotnet --list-runtimes | Select-String '^Microsoft.WindowsDesktop.App 8\.')) { throw 'Install the x64 .NET 8 Desktop Runtime first.' }
        $env:MAYDAY_MONEY_GAME_PATH = $game
        Start-Process $dotnet -ArgumentList ('"' + (Join-Path $PSScriptRoot 'MaydayMoneyPanel.dll') + '"')
    }
}



