# Trilha Estudos

Primeira versão: **1.0** (pacote **1.0.0**). A versão instalada aparece nas Configurações. [Repositório](https://github.com/tiagoguerreirodev/trilha-estudos) · [Baixar a versão estável](https://github.com/tiagoguerreirodev/trilha-estudos/releases/latest).

Aplicativo nativo WPF para Windows 11 em português do Brasil. O conteúdo começa vazio e é cadastrado manualmente. Funciona offline após a instalação/restauração das ferramentas e dependências.

## Funcionalidades

- Editais e planos com instituição/prova, data opcional e observações.
- Disciplinas, assuntos e um nível de subassuntos, com edição, exclusão e reordenação entre irmãos.
- Status, prioridade e observações. O percentual conta somente os itens finais. Grupos agregam o status dos filhos; alterar o status de um grupo aplica o novo status aos filhos.
- Árvore de Editais e conteúdo com fundos por status, inclusive nas disciplinas: verde para concluído, azul para em andamento e vermelho para não iniciado. Disciplinas vazias e editais permanecem neutros. Os planos começam recolhidos a cada entrada na aba.
- Marcação de estudo anterior por assunto, grupo ou disciplina inteira: conclui o conteúdo já estudado sem criar sessão, somar tempo ou iniciar revisões. A indicação histórica permanece disponível na árvore, nos detalhes e na busca de assuntos.
- Sessões manuais de estudo e revisão, com data, minutos e anotação, histórico, correção e exclusão.
- Painel com progresso, tempo, atividades recentes e revisões de hoje/atrasadas. Busca sem distinção de acentos ou maiúsculas, com filtros combináveis.
- Ciclos automáticos de revisão, confirmação de remoções e descarte de formulários modificados.
- Persistência SQLite local, sem conta, sincronização, conteúdo pré-carregado ou telemetria.
- Logotipo próprio de livro e trilha ascendente, incorporado ao executável, às janelas e à identificação visual da aplicação.

Referências funcionais consultadas: https://wp.estudei.com.br/tutoriais/ e https://estudei.com.br/lp-c/. A página inicial bloqueou a consulta direta; as páginas públicas e os tutoriais puderam ser consultados. Foram considerados somente os recursos de organização. Marca, textos, imagens e identidade visual não foram copiados.

## Requisitos e instalação das ferramentas

1. Use Windows 11 x64 e abra o PowerShell.
2. Instale o **SDK do .NET 8**, não apenas o runtime:

```powershell
winget install --id Microsoft.DotNet.SDK.8 --exact --source winget
```

3. Feche e reabra o terminal. Confirme:

```powershell
dotnet --list-sdks
```

Deve aparecer uma versão `8.0.xxx`. Alternativa: instalador gratuito em https://dotnet.microsoft.com/download/dotnet/8.0. O Visual Studio é opcional; se usado, instale a carga de desenvolvimento para desktop com .NET e o SDK 8.

4. Abra a pasta deste projeto:

```powershell
Set-Location -LiteralPath 'C:\Projetos\trilha-estudos'
```

Se tiver extraído o projeto em outro local, adapte esse caminho. O arquivo `global.json` seleciona um SDK estável da série 8.0.

## Restaurar, compilar, verificar e executar

```powershell
dotnet restore .\TrilhaEstudos.sln
dotnet build .\TrilhaEstudos.sln -c Release --no-restore
dotnet run --project .\tests\TrilhaEstudos.Checks -c Release --no-build
dotnet run --project .\src\TrilhaEstudos.App -c Release --no-build
```

Ou execute as três primeiras etapas com:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

As dependências diretas são `Microsoft.Data.Sqlite` 8.0.32 e `Velopack` 1.2.161, gratuitas. O NuGet traz o SQLite nativo. WPF faz parte do SDK para Windows; não há pacote visual pago nem banco instalado separadamente. O manifesto `.config/dotnet-tools.json` fixa o empacotador `vpk` na versão 1.2.161. A restauração inicial usa a internet; depois, os estudos funcionam offline. Somente a consulta e o download opcionais de atualizações precisam de internet.

Se o ambiente não permitir baixar pacotes, restaure em outro computador com acesso ao NuGet e distribua a publicação autocontida. Não substitua o aplicativo por uma página web.

## Publicar uma versão executável local

```powershell
dotnet tool restore
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

Para gerar outro pacote local, sem alterar a versão central nem publicar uma release:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Version 1.1.2 -OutputDirectory artifacts\teste-1.1.2
```

O instalador é `artifacts\releases\TrilhaEstudos.App-win-x64-Setup.exe`. Distribua esse arquivo e execute-o normalmente no Windows 11 x64. A instalação é por usuário, sem administrador, inclui o runtime e cria atalhos e desinstalador. O destino padrão é `%LOCALAPPDATA%\TrilhaEstudos.App`; os estudos ficam separadamente em `%LOCALAPPDATA%\TrilhaEstudos`. Não exige .NET instalado no computador de destino. O pacote completo, o manifesto `releases.win-x64.json` e `SHA256SUMS.txt` servem à distribuição das atualizações.

Essa publicação inclui o runtime e o SQLite nativo; por isso o executável é maior. Componentes nativos são extraídos automaticamente para `%TEMP%\.net` na primeira execução. O banco permanece em LocalAppData, separado do executável. Não ative trimming para WPF.

Esta entrega não cria MSI/MSIX nem tem assinatura digital. O Windows pode mostrar aviso de editor desconhecido. Uma assinatura exigiria certificado e configuração adicionais. Outras arquiteturas não integram a validação x64. Não passe argumentos do aplicativo ao instalador: o Velopack 1.2.161 tem uma falha conhecida nesse repasse; use argumentos somente ao executar o aplicativo instalado.

## Versões, GitHub Actions e atualização

`Version.props` centraliza a versão semântica `MAJOR.MINOR.PATCH` e o repositório público. Metadados do executável usam quatro números, por exemplo `1.0.0.0`; a interface mostra `1.0`; pacote e instalador usam `1.0.0`; a tag é `v1.0.0`. Correções incrementam PATCH; funcionalidades compatíveis, MINOR; mudanças incompatíveis, MAJOR.

O workflow **CI** executa restauração, compilação Release e verificações em pushes na `main` e pull requests. As ações oficiais estão fixadas por SHA. Para publicar, abra **Actions → Publicar release → Run workflow**, selecione `main`, informe uma versão superior à registrada em `Version.props` e execute. Somente a primeira publicação aceita a versão atual `1.0.0`; as seguintes exigem uma versão superior.

O fluxo prepara um commit local de versão, compila, verifica e empacota. Confere que a `main` remota não mudou e envia commit e tag juntos, sem force push. Cria uma release em rascunho, anexa todos os arquivos e só então publica como estável mais recente. Publicações são serializadas. Se houver falha após criar a tag ou o rascunho, execute novamente com a mesma versão para retomar o mesmo commit. Uma release já publicada é recusada. O token temporário do GitHub tem escrita somente no job de publicação e nunca entra no aplicativo. Regras de proteção que impeçam escrita na `main` pelo `GITHUB_TOKEN` precisam ser ajustadas pelo proprietário antes de usar esse fluxo.

No aplicativo instalado, **Configurações** mostra versão, estado, notas da release e as ações **Verificar atualizações**, **Baixar atualização**, **Cancelar download** e **Reiniciar e instalar**. A consulta inicial ocorre em segundo plano; indisponibilidade de rede não bloqueia os estudos. Apenas versões estáveis superiores do canal `win-x64` são oferecidas, sem token ou login do usuário. O Velopack valida o pacote antes de habilitar a instalação. Baixar não instala automaticamente, inclusive ao reabrir o aplicativo.

A correção **1.0.1** aumenta o prazo da consulta para dois minutos, permitindo a resposta do GitHub em redes lentas ou com proxy. A versão 1.0.0 pode cancelar essa consulta após 15 segundos; nesse caso, instale a versão corrigida pelo link da versão estável. Os dados são reutilizados. A release 1.0.0 permanece preservada.

A partir da **1.0.2**, cada verificação faz somente uma requisição a `GET /repos/tiagoguerreirodev/trilha-estudos/releases/latest`. Uma versão igual ou inferior encerra a consulta imediatamente. Uma versão superior apresenta a versão e as notas da API, sem baixar manifesto ou pacote. A conexão HTTP é reutilizada, sem cache persistente, e a frequência de consulta permanece a mesma.

Ao solicitar o download, o aplicativo lê apenas o manifesto `releases.win-x64.json` da release selecionada, confere identificador, canal e versão e entrega o pacote ao Velopack para validar sua integridade. Os endereços ficam vinculados àquela release; publicar outra versão durante o download não troca o pacote escolhido. Nenhuma tag móvel `latest` é criada: as tags versionadas continuam preservadas, e a indicação de release mais recente é mantida pelo GitHub.

Ao confirmar a instalação, novas gravações são bloqueadas, operações anteriores terminam e a API de backup do SQLite cria uma cópia consistente em `%LOCALAPPDATA%\TrilhaEstudos\backups`. A cópia é validada antes do encerramento normal e da substituição dos binários. Uma falha mantém o aplicativo aberto. Guarde os backups conforme sua necessidade; não há exclusão automática deles.

Quem usava o executável portátil precisa instalar **uma vez** o instalador 1.0. O banco existente será reutilizado. As próximas versões poderão ser instaladas pelo próprio aplicativo. Execuções diretas pelo SDK indicam que a atualização requer uma instalação empacotada.

Referências: [integração do Velopack](https://docs.velopack.io/getting-started/csharp), [fontes de atualização](https://docs.velopack.io/integrating/update-sources) e [execução manual de workflows](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow).

Documentação: https://learn.microsoft.com/dotnet/core/install/windows e https://learn.microsoft.com/dotnet/core/deploying/single-file/overview.

## Logotipo e ícone do Windows

O logotipo está em `src\TrilhaEstudos.App\Assets\TrilhaEstudos.logo.png`, e o ícone multirresolução em `src\TrilhaEstudos.App\Assets\TrilhaEstudos.ico`. Os dois são incorporados ao aplicativo; o executável publicado não depende de arquivos de imagem externos. O ICO contém quadros de 16, 20, 24, 32, 40, 48, 64, 128 e 256 pixels.

O projeto já inclui o ícone pronto. Para regenerá-lo a partir do PNG, execute `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-icon.ps1` antes de compilar/publicar. O conceito e os prompts de criação estão em `src\TrilhaEstudos.App\Assets\LOGOTIPO.md`.

## Banco e segurança das operações

O banco é criado automaticamente em `%LOCALAPPDATA%\TrilhaEstudos\estudos.db`, com chaves estrangeiras, versão de esquema e transações. Falhas de acesso não apagam nem recriam um banco existente. Uma versão de esquema futura é recusada para preservar o arquivo.

Cada formulário grava somente ao confirmar **Salvar**. Excluir mostra nome e quantidades removidas e exige confirmação. Fechar um formulário alterado solicita confirmação para descartar o rascunho. Os campos são preservados quando há erro de validação ou gravação.

Para backup manual, feche o aplicativo e copie a **pasta inteira** `%LOCALAPPDATA%\TrilhaEstudos` para outro local. Para restaurar, feche o aplicativo, guarde a pasta atual como segurança e recoloque a cópia no mesmo local. A pasta inteira cobre os arquivos auxiliares SQLite que eventualmente existirem.

Para inspeção isolada, sem usar o banco pessoal:

```powershell
& "$env:LOCALAPPDATA\TrilhaEstudos.App\current\TrilhaEstudos.exe" --data-dir "$env:TEMP\TrilhaEstudos-Inspecao"
```

Esse parâmetro troca somente a pasta do banco; não cria exemplos. Apenas uma instância do aplicativo é permitida por sessão do Windows.

## Regras de progresso e revisões

- Datas de sessões são datas locais, sem horário; não podem estar no futuro. Durações são minutos inteiros de 1 a 1440. A prova pode ter data passada. Nomes têm 1 a 200 caracteres após remover espaços; observações têm até 10.000.
- Sessões não alteram o status automaticamente. O usuário controla o avanço manualmente.
- Na árvore de **Editais e conteúdo**, a cor segue o status atual: verde claro para concluído, azul claro para em andamento e vermelho claro para não iniciado. O histórico de estudo anterior não prevalece sobre esse status. Grupos usam seu status agregado; os filhos mantêm suas próprias cores. Disciplinas usam apenas as folhas: todas concluídas = verde, todas não iniciadas = vermelho, qualquer outra combinação = azul. Disciplinas vazias e editais são neutros. A seleção usa contorno azul-escuro, mantendo o fundo de status; o alto contraste usa as cores do Windows. Os textos continuam identificando os estados.
- A cada entrada comum na aba, os planos ficam recolhidos; uma seleção de descendente passa para seu plano. Durante o trabalho na aba, alterações e filtros preservam a expansão escolhida. Abrir um assunto pela busca ou pelas revisões expande somente seu caminho, assim como os cadastros revelam o item criado. A expansão fica apenas em memória.
- **Marcar como já estudado**, disponível nos detalhes do conteúdo e na busca, conclui o assunto e registra a indicação **Estudado anteriormente**. Não solicita data ou duração e não cria sessões nem altera revisões. O tempo continua sendo calculado exclusivamente pelas sessões reais.
- Em grupos, marcar ou remover a marcação pede confirmação com a quantidade de subassuntos afetados. A ação inclui o grupo e todos os filhos existentes; novos filhos começam sem marcação. O grupo mostra nenhum, alguns ou todos os filhos marcados.
- Ao selecionar uma disciplina, as mesmas ações incluem todos os assuntos e subassuntos existentes, após confirmação com as duas quantidades. Disciplinas vazias têm as ações desabilitadas. A marcação e uma única atividade são gravadas em uma transação; repetir sem mudanças não gera outra atividade. Remover preserva os status. O histórico agregado mostra nenhum, alguns ou todos os itens finais marcados, sem contar pais e filhos duas vezes. Conteúdo adicionado depois começa não iniciado e sem marcação. Não há nova migração: permanece o esquema 2.
- **Remover marcação de estudo anterior** retira somente a indicação histórica e mantém o status atual. Para alterar a conclusão, use **Editar**. Editar nome, prioridade, observações ou status preserva a marcação histórica; não é preciso registrar uma sessão para concluir um assunto.
- Pai com subassuntos é um grupo: concluído quando todos os filhos estão concluídos; não iniciado quando todos estão não iniciados; em andamento nos demais casos. O percentual usa apenas folhas, com o mesmo peso.
- Sessões e ciclos do pai são preservados ao subdividi-lo. O pai pode receber sessões sobre o tema inteiro. O tempo do grupo soma sessões próprias e dos filhos; o painel soma cada sessão apenas uma vez.
- Adicionar um subassunto novo não iniciado pode reduzir o percentual de conclusão. Remover o último subassunto conserva o último status agregado do pai.
- O **primeiro registro** do tipo estudo inicia o ciclo com a data informada. Um estudo retroativo cadastrado depois não altera essa base. Revisão sem ciclo pendente é apenas um registro histórico.
- Os intervalos iniciais são 1, 7 e 30 dias. São aceitos de 1 a 12 intervalos inteiros entre 1 e 3650 dias. Alterar as configurações afeta somente novos ciclos.
- Cada intervalo parte da etapa anterior efetivamente registrada: primeira revisão = estudo + 1; segunda = primeira revisão + 7; terceira = segunda revisão + 30. Revisões antecipadas também usam a data real informada.
- Uma sessão de revisão vinculada ao ciclo avança uma etapa. A sequência é recalculada pelas revisões em ordem de data e, no empate, de cadastro. Corrigir ou excluir uma revisão pode reabrir uma etapa.
- Revisões vinculadas não podem anteceder a data-base. Editar a sessão inicial altera a base se não ultrapassar uma revisão existente. Excluir a sessão inicial conserva a data-base.
- O ciclo termina após sua última etapa. Novas sessões não o reiniciam. **Iniciar novo ciclo** usa hoje e os intervalos vigentes, somente quando não há ciclo pendente. Os ciclos anteriores ficam arquivados, preservando o histórico e evitando duas pendências após correções antigas.
- As pendências são atualizadas ao abrir, salvar e mudar o dia com a janela aberta. Não há serviço, notificação externa ou execução em segundo plano após fechar.
- Assunto e tipo de uma sessão existente são fixos. Para corrigir esses vínculos, exclua e cadastre novamente; data, duração e anotação são editáveis.

Na versão 1.1.0, bancos do esquema 1 são migrados automaticamente para o esquema 2, após criação e validação de backup SQLite na pasta `backups`, ao lado do banco. Se o backup falhar, a migração não começa. Nenhum assunto antigo é marcado automaticamente. Versões até 1.0.2 não abrem o esquema 2; uma restauração para essas versões exige o backup anterior, com o aplicativo fechado.

## Testes manuais

Use um banco isolado pelo parâmetro acima, se desejar. Os exemplos abaixo são apenas instruções de teste, não conteúdo distribuído no aplicativo.

1. **Primeira execução:** confirmar painel vazio e orientação acolhedora. Criar edital com nome e campos opcionais. Fechar sem salvar um formulário alterado, responder Não e verificar que o rascunho permanece.
2. **Conteúdo e edição:** selecionar o edital, criar duas disciplinas, assuntos e um subassunto. Renomear, mudar prioridade e observações. Testar reordenação. Confirmar que um terceiro nível não é oferecido.
3. **Progresso:** concluir um de dois subassuntos. Confirmar pai em andamento e percentual de 50% se forem as únicas folhas. Editar somente o nome do pai e conferir que os filhos mantêm seus status. Concluir o grupo e verificar os filhos.
4. **Exclusão:** tentar excluir edital/disciplina/assunto e responder Não. Conferir que nada mudou. Repetir e confirmar; verificar a remoção dos descendentes e registros vinculados, com totais atualizados.
5. **Busca:** pesquisar texto sem acento ou em maiúsculas. Combinar edital, disciplina, status e prioridade. Usar Somente pendentes e Limpar filtros; abrir um resultado na árvore.
6. **Sessões e revisões:** registrar estudo com data passada, duração e anotação. Conferir tempo e próxima revisão. Registrar revisão, editar data/duração e excluir; conferir totais e prazos. Testar duração zero, texto inválido, data futura e revisão antes da base: não devem salvar.
7. **Ciclos:** configurar um único intervalo para novos ciclos, criar um assunto e registrar estudo/revisão. Conferir encerramento; iniciar novo ciclo e verificar que usa hoje. O ciclo anterior não deve reaparecer ao corrigir seus registros.
8. **Persistência e interface:** fechar e reabrir. Conferir editais, ordem, status, notas, sessões, intervalos e revisões. Redimensionar, testar Tab/Shift+Tab/Enter/Esc e escala do Windows em 150%/200%; conferir rolagem e acesso aos botões.
9. **Atualizações:** conferir versão nas Configurações; consultar sem conexão e continuar estudando. Com uma release superior, conferir notas, cancelar download e baixar novamente. Instalar somente ao confirmar, conferir backup, versão nova e os mesmos estudos após reiniciar. Pacotes locais de teste 1.0.0 → 1.0.1 devem usar um identificador e banco exclusivos; não publique a versão de teste no GitHub.
10. **Estudo anterior:** marcar um assunto nos detalhes e outro pela busca. Conferir conclusão, indicação textual e tempo inalterado, sem novas sessões ou revisões. Em um grupo, cancelar a confirmação e conferir que nada mudou; depois confirmar e verificar os filhos. Remover a marcação, confirmar que os status permanecem e cadastrar um novo subassunto sem marcação. Fechar e reabrir para verificar persistência. Na atualização 1.0.2 → 1.1.0, conferir a criação do backup da migração e a preservação dos registros anteriores.
11. **Cores da árvore:** conferir os três status, grupos mistos e disciplina vazia. Concluir a última pendência de uma disciplina e conferir fundo verde; adicionar ou reabrir conteúdo e conferir azul. Uma disciplina com todas as folhas não iniciadas fica vermelha. Remover apenas a marca histórica não deve mudar o verde de itens concluídos. Selecionar, navegar pelo teclado, expandir/recolher, reordenar e redimensionar com nomes longos, conferindo contorno, cores, quebra de linhas e rolagem. Em alto contraste do Windows, conferir legibilidade e seleção com as cores do sistema.
12. **Disciplina estudada:** selecionar uma disciplina, cancelar e confirmar as duas ações de estudo anterior. Conferir as quantidades, conclusão de todos os assuntos/subassuntos, uma única atividade e ausência de novas sessões ou revisões. Remover deve manter os status. Adicionar conteúdo deve produzir indicação parcial e fundo azul. Em disciplina vazia, os botões ficam desabilitados.
13. **Planos recolhidos:** entrar na aba e conferir todos os planos fechados. Expandir um plano e recolher um grupo; editar, marcar e filtrar sem perder essa escolha. Trocar de aba e voltar: os planos devem fechar novamente e a seleção deve ficar no nível do plano. Abrir pela busca ou pelas revisões deve revelar somente o caminho do assunto. Novos cadastros devem aparecer. Reiniciar não deve restaurar a expansão anterior.

## Estrutura e verificações automatizadas

A solução separa a interface WPF, a biblioteca de domínio/persistência e um executável de verificações sem framework adicional. Os testes usam bancos temporários exclusivos e nunca abrem o banco pessoal. Retornam código de saída diferente de zero se houver falha.

Para incluir a verificação da interface WPF e gerar imagens das telas:

```powershell
dotnet run --project .\tests\TrilhaEstudos.Checks -c Release --no-build -- --ui .\artifacts\ui-checks
```

A verificação de interface abre janelas próprias fora da área visível, carrega os bindings, cadastra edital/disciplina/assunto/sessão pelos formulários, verifica a busca e renderiza as seis telas, a primeira execução e um formulário. Também verifica as cores das disciplinas, confirmação e cancelamento das ações em lote, preservação de tempo e revisões, expansão bidirecional, retorno à aba, filtros, navegação por busca/revisão e foco de teclado. O conteúdo usado nessa verificação existe apenas em um banco temporário excluído ao terminar. Não é um modo demonstrativo do aplicativo. A verificação automatizada não substitui os testes manuais de teclado e escala do Windows.

As verificações HTTP usam respostas simuladas, sem acessar o GitHub: contam as requisições, impedem downloads durante a consulta, simulam outra publicação entre consulta e download, validam manifesto e pacote e cobrem falhas de rede/API e cancelamento efetivo. Medições com a rede real e testes de atualização entre pacotes são registrados separadamente no relatório de validação.

O arquivo `artifacts\ENTREGA_COMPLETA.md`, quando presente, reúne a árvore completa e o conteúdo integral dos arquivos necessários, nesta ordem: resumo, árvore, arquivos, instalação/execução, publicação e testes manuais. Arquivos gerados (`bin`, `obj`, ferramentas e artefatos) não são necessários para reconstruir o projeto.

Para gerar novamente esse documento e o ZIP dos fontes, execute `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1`. O empacotamento inclui código, logotipo, `Version.props`, manifesto de ferramentas e workflows; exclui ferramentas, credenciais, bancos e compilações. Não executa testes; apenas inclui o relatório de validação existente, quando houver.

As verificações executadas nesta entrega são registradas separadamente em `artifacts\VALIDACAO.md`; essa distinção evita tratar cenários apenas documentados como testes realizados.
