# Logotipo do Trilha Estudos

Símbolo original de livro aberto com uma trilha ascendente e uma estrela de chegada. Usa verde escuro, branco suave e dourado, em continuidade com a interface do aplicativo. O símbolo não contém texto para funcionar nos tamanhos pequenos do Windows.

## Arquivos e uso

- `TrilhaEstudos.logo.png`: matriz raster com transparência, gerada pela ferramenta integrada de imagens. Também é um recurso WPF incorporado à aplicação para a marca na barra lateral.
- `TrilhaEstudos.ico`: ícone Windows com quadros de 16, 20, 24, 32, 40, 48, 64, 128 e 256 pixels. É incorporado ao recurso de ícone do executável pelo campo `ApplicationIcon` e usado nas janelas WPF.
- `scripts/build-icon.ps1`: conversão determinística da matriz para os quadros do ICO, com redimensionamento de alta qualidade e preservação da transparência. Não altera criativamente o desenho.

Para regenerar o ICO a partir da matriz existente, na raiz do projeto:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-icon.ps1
```

Depois compile e publique normalmente. PNG e ICO são recursos internos; não precisam ficar ao lado do executável publicado. A geração de imagens não é necessária para compilar nem executar o projeto.

## Registro da criação

Ferramenta usada: `image_gen.imagegen`, modo integrado, sem API ou chave do usuário. A segunda imagem foi selecionada como matriz final. O original gerado foi copiado para o projeto; o arquivo ICO foi exportado dessa matriz.

Prompt inicial:

```text
Use case: logo-brand. Asset type: production Windows 11 desktop app icon for an existing Brazilian Portuguese study-organizer app named Trilha Estudos. Create one original, polished, minimal geometric app symbol: an open book whose bold page shapes integrate a simple ascending trail/steps, conveying learning and steady progress. No letters, no words. A dark teal rounded-square tile (#183D42 to #216A62 palette, preferably flat solid dark teal) with a crisp warm-white or pale mint symbol (#E3F1ED), and at most one restrained small warm-gold accent at the trail endpoint. Modern calm Fluent-adjacent aesthetic, independent original identity. Strong thick shapes, generous negative space, precise balance, excellent silhouette legibility at 16x16 and 32x32. Centered single symbol on a single tile, square composition. Tile fills about 90% of the image with consistent transparent outer padding and rounded corners. Actually transparent background outside the tile; preserve alpha. Flat vector-like graphic, smooth clean edges. Avoid: text, initials, slogans, multiple alternatives, contact sheets, gradients, photorealism, 3D effects, drop shadows, mockups, watermark, thin strokes, tiny decoration, borrowed brand marks.
```

Prompt da edição final:

```text
Edit this app logo only to clean the fills for production icon use. Preserve the exact book, ascending steps, small four-point gold star and rounded-square composition. Replace the entire teal tile interior with a perfectly uniform flat solid dark teal #183D42, with absolutely no texture, no dark patches, no mottling, no highlights, no gradient and no shadow. Make the book and steps a single flat solid pale mint-white #E3F1ED and the star a single flat warm gold #F3BC54. Keep the strong silhouette and smooth antialiased edges. Keep the background outside the rounded square genuinely transparent. No text, no new elements. Output one square production logo.
```
