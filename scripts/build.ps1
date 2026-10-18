param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$taskDotnet = Join-Path $taskRoot '.tools\sdk8\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
& $taskDotnet restore .\TrilhaEstudos.sln -m:1
if ($LASTEXITCODE -ne 0) { throw 'Falha ao restaurar dependências.' }
& $taskDotnet build .\TrilhaEstudos.sln -c $Configuration --no-restore -m:1
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
& $taskDotnet run --project .\tests\TrilhaEstudos.Checks -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw 'As verificações encontraram falhas.' }
