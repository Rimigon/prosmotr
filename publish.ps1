#!/usr/bin/env pwsh
# Универсальный скрипт: закрыть процессы, очистить кэш и опубликовать приложение.
# plugins.dat LibVLC кэшируется в LocalAppData, поэтому его не нужно сохранять вручную,
# но для скорости публикации оставляем резервную копию рядом с app\ на время очистки.

Get-Process -Name "Prosmotr" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$root = $PSScriptRoot
if ([string]::IsNullOrEmpty($root)) { $root = Get-Location }

$pluginsCache = "$root\app\libvlc\win-x64\plugins\plugins.dat"
$pluginsCacheBackup = "$root\plugins.dat.bak"
if (Test-Path $pluginsCache)
{
    try { Copy-Item $pluginsCache $pluginsCacheBackup -Force -ErrorAction Stop } catch { }
}

Remove-Item -Path "$root\app" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$root\src\Prosmotr\bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$root\src\Prosmotr\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$root\tests\Prosmotr.Tests\bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$root\tests\Prosmotr.Tests\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\Prosmotr*" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TEMP\.NET*" -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish "$root\src\Prosmotr\Prosmotr.csproj" -c Release -o app

# Восстановить plugins.dat, если dotnet publish не принёс свой (обычно не приносит).
if (!(Test-Path "$root\app\libvlc\win-x64\plugins\plugins.dat") -and (Test-Path $pluginsCacheBackup))
{
    try
    {
        New-Item -ItemType Directory -Path "$root\app\libvlc\win-x64\plugins" -Force -ErrorAction Stop | Out-Null
        Copy-Item $pluginsCacheBackup "$root\app\libvlc\win-x64\plugins\plugins.dat" -Force -ErrorAction Stop
    }
    catch { }
}

try { Remove-Item $pluginsCacheBackup -Force -ErrorAction SilentlyContinue } catch { }
