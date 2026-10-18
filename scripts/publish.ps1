param(
    [string]$Version,
    [string]$RepositoryUrl,
    [string]$ReleaseNotes,
    [string]$PackId = 'TrilhaEstudos.App',
    [string]$OutputDirectory = 'artifacts\releases'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$taskDotnet = Join-Path $taskRoot '.tools\sdk8\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
$taskProps = [xml](Get-Content -LiteralPath (Join-Path $taskRoot 'Version.props') -Raw)
if (-not $Version) { $Version = [string]$taskProps.Project.PropertyGroup.Version }
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Use uma versão estável MAJOR.MINOR.PATCH.' }
if (-not $RepositoryUrl) { $RepositoryUrl = [string]$taskProps.Project.PropertyGroup.RepositoryUrl.'#text' }
if (-not $ReleaseNotes) {
    $ReleaseNotes = Join-Path $taskRoot 'artifacts\release-notes.md'
    New-Item -ItemType Directory -Force -Path (Split-Path $ReleaseNotes -Parent) | Out-Null
    [IO.File]::WriteAllText($ReleaseNotes, "Trilha Estudos $Version`n`nConsulte as alterações e os arquivos desta versão em $RepositoryUrl/releases/tag/v$Version.", [Text.UTF8Encoding]::new($false))
}
if (-not (Test-Path -LiteralPath $ReleaseNotes -PathType Leaf)) { throw 'Arquivo de notas da versão não encontrado.' }
if ($PackId -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]+$') { throw 'Identificador de pacote inválido.' }
$taskOutput = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $taskRoot $OutputDirectory }
$taskPublish = Join-Path $taskOutput 'app'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
& $taskDotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'Falha ao restaurar o empacotador.' }
& $taskDotnet publish .\src\TrilhaEstudos.App\TrilhaEstudos.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=embedded "-p:Version=$Version" "-p:RepositoryUrl=$RepositoryUrl" -o $taskPublish -m:1
if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação.' }
$taskExe = Join-Path $taskPublish 'TrilhaEstudos.exe'
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($taskExe).FileVersion -ne "$Version.0") { throw 'Versão do executável divergente.' }
& $taskDotnet tool run vpk -- --skip-updates pack --packId $PackId --packVersion $Version --packDir $taskPublish --mainExe TrilhaEstudos.exe --packTitle 'Trilha Estudos' --icon .\src\TrilhaEstudos.App\Assets\TrilhaEstudos.ico --releaseNotes $ReleaseNotes --channel win-x64 --runtime win-x64 --noPortable --delta None --instLocation PerUser --outputDir $taskOutput -y
if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar o instalador e os pacotes de atualização.' }
$taskChecksums = Get-ChildItem -LiteralPath $taskOutput -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
}
[IO.File]::WriteAllLines((Join-Path $taskOutput 'SHA256SUMS.txt'), [string[]]$taskChecksums, [Text.UTF8Encoding]::new($false))
Write-Output "Versão $Version empacotada em $taskOutput"
