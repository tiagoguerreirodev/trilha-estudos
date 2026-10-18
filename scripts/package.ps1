$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$taskArtifacts = Join-Path $taskRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $taskArtifacts | Out-Null
$taskFiles = @('.gitignore', 'Directory.Build.props', 'Version.props', 'global.json', 'README.md', 'TrilhaEstudos.sln')
foreach ($taskDirectory in @('.config', '.github', 'scripts', 'src', 'tests')) {
    $taskFiles += Get-ChildItem -LiteralPath (Join-Path $taskRoot $taskDirectory) -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object { $_.FullName.Substring($taskRoot.Length + 1).Replace('\', '/') }
}
$taskFiles = @($taskFiles | Sort-Object)
function Get-SourceTree([string[]]$Paths, [string]$Prefix = '') {
    $taskNames = @($Paths | ForEach-Object { ($_ -split '/')[0] } | Sort-Object -Unique)
    for ($taskIndex = 0; $taskIndex -lt $taskNames.Count; $taskIndex++) {
        $taskName = $taskNames[$taskIndex]
        $taskLast = $taskIndex -eq $taskNames.Count - 1
        $taskConnector = if ($taskLast) { '└── ' } else { '├── ' }
        $Prefix + $taskConnector + $taskName
        $taskChildren = @($Paths | Where-Object { $_.StartsWith($taskName + '/') } | ForEach-Object { $_.Substring($taskName.Length + 1) })
        if ($taskChildren.Count -gt 0) {
            $taskIndent = if ($taskLast) { '    ' } else { '│   ' }
            Get-SourceTree -Paths $taskChildren -Prefix ($Prefix + $taskIndent)
        }
    }
}
$taskReadme = Get-Content -LiteralPath (Join-Path $taskRoot 'README.md') -Raw
function Get-ReadmeSection([string]$Start, [string]$End) {
    $taskStart = $taskReadme.IndexOf($Start, [StringComparison]::Ordinal)
    $taskEnd = $taskReadme.IndexOf($End, $taskStart + $Start.Length, [StringComparison]::Ordinal)
    if ($taskStart -lt 0 -or $taskEnd -lt 0) { throw 'Seções da documentação não encontradas.' }
    $taskReadme.Substring($taskStart, $taskEnd - $taskStart).Trim()
}
$taskDocument = [Text.StringBuilder]::new()
[void]$taskDocument.AppendLine('# Entrega completa — Trilha Estudos')
[void]$taskDocument.AppendLine()
[void]$taskDocument.AppendLine('## 1. Resumo, suposições e verificações')
[void]$taskDocument.AppendLine()
[void]$taskDocument.AppendLine('Aplicativo nativo Windows 11 com C#, .NET 8, WPF e SQLite: editais manuais, disciplinas, assuntos e subassuntos, status, prioridades, observações, sessões, filtros, progresso e ciclos automáticos de revisão configuráveis. Inicia vazio e mantém os dados localmente. Logotipo próprio incorporado ao executável e à interface. Duração manual, uso individual e datas locais. Versão centralizada, instalador x64 por usuário, atualizações confirmadas com backup SQLite e workflows de CI/releases. Sem assinatura digital.')
[void]$taskDocument.AppendLine()
[void]$taskDocument.AppendLine('Referência funcional consultada: [tutoriais oficiais do Estudei](https://wp.estudei.com.br/tutoriais/). Foram usados somente conceitos de organização de estudos, com textos e aparência próprios. Cada intervalo de revisão parte da etapa anterior efetivamente registrada; o primeiro estudo inicia o ciclo e a última revisão o encerra.')
[void]$taskDocument.AppendLine()
$taskValidation = Join-Path $taskArtifacts 'VALIDACAO.md'
if (Test-Path -LiteralPath $taskValidation) {
    [void]$taskDocument.AppendLine((Get-Content -LiteralPath $taskValidation -Raw))
} else {
    [void]$taskDocument.AppendLine('Este empacotamento não executa compilação ou testes. Consulte os resultados das verificações executadas separadamente.')
}
[void]$taskDocument.AppendLine('## 2. Árvore completa dos arquivos-fonte entregues')
[void]$taskDocument.AppendLine()
[void]$taskDocument.AppendLine('Ferramentas locais, bancos e arquivos gerados em `.tools`, `bin`, `obj` e `artifacts` não integram os arquivos necessários para reconstruir o projeto. Todos os arquivos-fonte abaixo estão reproduzidos integralmente na seção 3.')
[void]$taskDocument.AppendLine()
[void]$taskDocument.AppendLine('```text')
[void]$taskDocument.AppendLine('TrilhaEstudos/')
[void]$taskDocument.AppendLine(((Get-SourceTree -Paths $taskFiles) -join "`n"))
[void]$taskDocument.AppendLine('```')
[void]$taskDocument.AppendLine()
[void]$taskDocument.AppendLine('## 3. Conteúdo integral dos arquivos')
[void]$taskDocument.AppendLine()
foreach ($taskFile in $taskFiles) {
    $taskExtension = [IO.Path]::GetExtension($taskFile)
    if ($taskExtension -in '.png', '.ico') {
        [void]$taskDocument.AppendLine('### Caminho: `' + $taskFile + '`')
        [void]$taskDocument.AppendLine()
        [void]$taskDocument.AppendLine('Arquivo binário integral em Base64. Execute o bloco a partir da raiz do projeto para restaurá-lo; o ZIP já contém o arquivo binário pronto.')
        [void]$taskDocument.AppendLine()
        [void]$taskDocument.AppendLine('````powershell')
        [void]$taskDocument.AppendLine('$taskAssetBytes = @''')
        $taskBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $taskRoot $taskFile)))
        [void]$taskDocument.AppendLine(([regex]::Replace($taskBase64, '.{120}', '$0' + "`n")).TrimEnd())
        [void]$taskDocument.AppendLine('''@')
        [void]$taskDocument.AppendLine('New-Item -ItemType Directory -Force -Path ''' + (Split-Path $taskFile -Parent).Replace('\', '/') + ''' | Out-Null')
        [void]$taskDocument.AppendLine('[IO.File]::WriteAllBytes(''' + $taskFile + ''', [Convert]::FromBase64String($taskAssetBytes))')
        [void]$taskDocument.AppendLine('````')
        [void]$taskDocument.AppendLine()
        continue
    }
    $taskLanguage = switch ($taskExtension) {
        '.cs' { 'csharp' }; '.ps1' { 'powershell' }; '.json' { 'json' }; '.md' { 'markdown' }
        { $_ -in '.xaml', '.csproj', '.props', '.manifest' } { 'xml' }; default { 'text' }
    }
    [void]$taskDocument.AppendLine('### Caminho: `' + $taskFile + '`')
    [void]$taskDocument.AppendLine()
    [void]$taskDocument.AppendLine(('````' + $taskLanguage))
    [void]$taskDocument.AppendLine((Get-Content -LiteralPath (Join-Path $taskRoot $taskFile) -Raw).TrimEnd("`r", "`n"))
    [void]$taskDocument.AppendLine('````')
    [void]$taskDocument.AppendLine()
}
[void]$taskDocument.AppendLine('## 4. Pré-requisitos, restauração, compilação e execução')
[void]$taskDocument.AppendLine()
[void]$taskDocument.AppendLine((Get-ReadmeSection '## Requisitos e instalação das ferramentas' '## Publicar uma versão executável local'))
[void]$taskDocument.AppendLine()
[void]$taskDocument.AppendLine('## 5. Publicação e distribuição local')
[void]$taskDocument.AppendLine()
[void]$taskDocument.AppendLine((Get-ReadmeSection '## Publicar uma versão executável local' '## Banco e segurança das operações'))
[void]$taskDocument.AppendLine()
[void]$taskDocument.AppendLine('## 6. Testes manuais')
[void]$taskDocument.AppendLine()
[void]$taskDocument.AppendLine((Get-ReadmeSection '## Testes manuais' '## Estrutura e verificações automatizadas'))
$taskDelivery = Join-Path $taskArtifacts 'ENTREGA_COMPLETA.md'
[IO.File]::WriteAllText($taskDelivery, $taskDocument.ToString(), [Text.UTF8Encoding]::new($false))
$taskZipPath = Join-Path $taskArtifacts 'TrilhaEstudos-fonte.zip'
if (Test-Path -LiteralPath $taskZipPath) { Remove-Item -LiteralPath $taskZipPath }
$taskZip = [IO.Compression.ZipFile]::Open($taskZipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskFile in $taskFiles) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip, (Join-Path $taskRoot $taskFile), $taskFile)
    }
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip, $taskDelivery, 'ENTREGA_COMPLETA.md')
    if (Test-Path -LiteralPath $taskValidation) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip, $taskValidation, 'VALIDACAO.md')
    }
} finally { $taskZip.Dispose() }
Write-Output "$($taskFiles.Count) arquivos-fonte incluídos integralmente."
Write-Output $taskDelivery
Write-Output $taskZipPath
