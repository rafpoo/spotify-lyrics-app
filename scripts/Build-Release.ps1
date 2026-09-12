param([string]$IsccPath, [switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repository
try {
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    dotnet run --project LyricFloat.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    dotnet publish LyricFloat.App -p:PublishProfile=WindowsPortable
    if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
    $portable = Join-Path $repository 'artifacts\release\portable'
    Copy-Item -LiteralPath (Join-Path $repository 'RELEASE-README.md') -Destination (Join-Path $portable 'README.md')
    $zip = Join-Path $repository 'artifacts\release\LyricFloat-0.1.0-win-x64.zip'
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -Force
    if (-not $SkipInstaller) {
        New-Item -ItemType Directory -Force -Path (Join-Path $repository 'artifacts\release\installer') | Out-Null
        if (-not $IsccPath) {
            $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($command) { $IsccPath = $command.Source }
            else { $IsccPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
        }
        if (Test-Path -LiteralPath $IsccPath) {
            & $IsccPath (Join-Path $repository 'installer\LyricFloat.iss')
            if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
        } else { Write-Warning 'Inno Setup compiler unavailable. Portable release built; run ISCC.exe installer\LyricFloat.iss after installing Inno Setup 6.3+.' }
    }
    Write-Output "Portable release: $portable"
    Write-Output "Portable archive: $zip"
} finally { Pop-Location }
