param(
    [Parameter(Mandatory = $true)][ValidateSet('Prepare', 'Publish')][string]$Stage,
    [Parameter(Mandatory = $true)][string]$RequestedVersion,
    [Parameter(Mandatory = $true)][string]$Repository
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path $PSScriptRoot -Parent)
if ($env:GITHUB_REF -ne 'refs/heads/main') { throw 'Releases devem ser executadas na branch main.' }
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Repositório inválido.' }
if ($RequestedVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Informe MAJOR.MINOR.PATCH, sem prefixo v ou pré-release.' }
$taskVersion = $null
if (-not [Version]::TryParse($RequestedVersion, [ref]$taskVersion) -or $taskVersion.Major -gt 65534 -or $taskVersion.Minor -gt 65534 -or $taskVersion.Build -gt 65534) { throw 'Versão fora dos limites suportados pelo executável.' }
$taskTag = "v$RequestedVersion"
function Invoke-Git([string[]]$Arguments) {
    $taskResult = & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git falhou: $($Arguments[0])" }
    $taskResult
}
function Invoke-Gh([string[]]$Arguments) {
    $taskResult = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub falhou: $($Arguments[0])" }
    $taskResult
}
function Get-Release {
    $taskResult = & gh release view $taskTag --repo $Repository --json tagName,isDraft,url 2>&1
    if ($LASTEXITCODE -eq 0) { return (($taskResult -join "`n") | ConvertFrom-Json) }
    if (($taskResult -join "`n") -match 'release not found|HTTP 404|Not Found') { return $null }
    throw "Não foi possível consultar a release: $($taskResult -join ' ')"
}
function Set-WorkflowValue([string]$Name, [string]$Value) {
    Add-Content -LiteralPath $env:GITHUB_ENV -Value "$Name=$Value" -Encoding utf8
    if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "$Name=$Value" -Encoding utf8 }
}
$taskRelease = Get-Release
if ($taskRelease -and -not $taskRelease.isDraft) { throw "A release $taskTag já está publicada e não será sobrescrita." }
if ($Stage -eq 'Prepare') {
    Invoke-Gh @('auth', 'setup-git') | Out-Null
    Invoke-Git @('fetch', 'origin', 'main', '--tags') | Out-Null
    $taskBase = (Invoke-Git @('rev-parse', 'origin/main')).Trim()
    $taskExistingTag = & git rev-parse --verify --quiet "refs/tags/$taskTag^{commit}"
    if ($LASTEXITCODE -eq 0) {
        Invoke-Git @('checkout', '--detach', $taskTag) | Out-Null
        $taskCurrent = [xml](Get-Content Version.props -Raw)
        if ([string]$taskCurrent.Project.PropertyGroup.Version -ne $RequestedVersion) { throw 'A tag existente tem outra versão no código.' }
        Set-WorkflowValue 'RELEASE_EXISTING_TAG' 'true'
    } else {
        $taskHead = (Invoke-Git @('rev-parse', 'HEAD')).Trim()
        if ($taskHead -ne $taskBase) { throw 'A branch main mudou. Execute o workflow novamente.' }
        $taskCurrent = [xml](Get-Content Version.props -Raw)
        $taskOldVersion = [Version][string]$taskCurrent.Project.PropertyGroup.Version
        if ($taskVersion -lt $taskOldVersion) { throw 'A versão solicitada deve ser superior à versão atual.' }
        if ($taskVersion -eq $taskOldVersion -and $RequestedVersion -ne '1.0.0') { throw 'A versão solicitada deve ser superior à versão atual.' }
        if ($taskVersion -gt $taskOldVersion) {
            $taskText = Get-Content Version.props -Raw
            $taskText = [regex]::Replace($taskText, '<Version>[^<]+</Version>', "<Version>$RequestedVersion</Version>")
            [IO.File]::WriteAllText((Join-Path (Get-Location).Path 'Version.props'), $taskText, [Text.UTF8Encoding]::new($false))
            Invoke-Git @('config', 'user.name', 'github-actions[bot]') | Out-Null
            Invoke-Git @('config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com') | Out-Null
            Invoke-Git @('add', 'Version.props') | Out-Null
            Invoke-Git @('commit', '-m', "Release $taskTag") | Out-Null
        }
        Set-WorkflowValue 'RELEASE_EXISTING_TAG' 'false'
    }
    Set-WorkflowValue 'RELEASE_BASE' $taskBase
    Set-WorkflowValue 'RELEASE_COMMIT' ((Invoke-Git @('rev-parse', 'HEAD')).Trim())
    Set-WorkflowValue 'RELEASE_TAG' $taskTag
    Set-WorkflowValue 'RELEASE_VERSION' $RequestedVersion
    New-Item -ItemType Directory -Force -Path artifacts | Out-Null
    $taskNotes = Invoke-Gh @('api', "repos/$Repository/releases/generate-notes", '-f', "tag_name=$taskTag", '-f', "target_commitish=$taskBase")
    $taskBody = (($taskNotes -join "`n") | ConvertFrom-Json).body
    [IO.File]::WriteAllText((Join-Path (Get-Location).Path 'artifacts\release-notes.md'), "Trilha Estudos $RequestedVersion`n`n$taskBody", [Text.UTF8Encoding]::new($false))
    Write-Output "Release preparada: $taskTag"
    exit 0
}
if ($env:RELEASE_TAG -ne $taskTag -or -not $env:RELEASE_COMMIT) { throw 'Execute a preparação antes de publicar.' }
$taskDirectory = Join-Path (Get-Location).Path 'artifacts\releases'
$taskFiles = @(Get-ChildItem -LiteralPath $taskDirectory -File | Select-Object -ExpandProperty FullName)
if (-not ($taskFiles | Where-Object { $_ -like '*Setup.exe' }) -or -not ($taskFiles | Where-Object { $_ -like '*-full.nupkg' }) -or -not (Test-Path (Join-Path $taskDirectory 'releases.win-x64.json')) -or -not (Test-Path (Join-Path $taskDirectory 'SHA256SUMS.txt'))) { throw 'Instalador, pacote, manifesto ou hashes ausentes.' }
if ($env:RELEASE_EXISTING_TAG -ne 'true') {
    Invoke-Git @('fetch', 'origin', 'main') | Out-Null
    if ((Invoke-Git @('rev-parse', 'origin/main')).Trim() -ne $env:RELEASE_BASE) { throw 'A main mudou durante a compilação. Execute novamente.' }
    Invoke-Git @('tag', $taskTag, $env:RELEASE_COMMIT) | Out-Null
    Invoke-Git @('push', '--atomic', 'origin', 'HEAD:refs/heads/main', "refs/tags/$taskTag") | Out-Null
}
if (-not $taskRelease) {
    Invoke-Gh @('release', 'create', $taskTag, '--repo', $Repository, '--draft', '--verify-tag', '--title', "Trilha Estudos $RequestedVersion", '--notes-file', 'artifacts/release-notes.md') | Out-Null
}
Invoke-Gh (@('release', 'upload', $taskTag, '--repo', $Repository, '--clobber') + $taskFiles) | Out-Null
Invoke-Gh @('release', 'edit', $taskTag, '--repo', $Repository, '--draft=false', '--latest') | Out-Null
Write-Output "Release publicada: https://github.com/$Repository/releases/tag/$taskTag"
