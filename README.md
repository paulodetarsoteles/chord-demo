# Detector de Acordes (API .NET)

Este repositório contém um protótipo de detector de acordes construído como uma Web API mínima em .NET 8. O sistema permite fazer upload de músicas, processá-las em backend para extrair um timeline de acordes (labels + confiança) e reproduzir o áudio no navegador mostrando o acorde atual em tempo real.

Resumo técnico
- Backend: .NET 8 Web API minimal (arquivo `Program.cs`).
- Processamento de áudio: NAudio (leitura/resampling) + MathNet.Numerics (FFT).
- Lógica DSP: STFT (Hann window) → extração de cromagrama multi-banda → normalização → comparação com templates (maior / menor) → suavização temporal → timeline.
- Armazenamento: arquivos em `storage/` (áudio + JSON com timeline). `StorageService` gerencia salvar/carregar e limpar storage.
- Frontend: arquivos estáticos em `wwwroot/` (HTML, CSS, JS) com player, upload e seletor de músicas.

Fluxo de uso
1. Envie um arquivo via `POST /upload` (campo `file`).
2. O servidor salva o arquivo em `storage/`, executa o analisador e persiste um JSON de timeline (mesmo id). O endpoint retorna `{ id }`.
3. No frontend o player carrega `/audio/{id}` e pode requerer `/timeline/{id}` para mostrar acordes enquanto toca.

Endpoints principais
- `POST /upload` — envia e inicia análise; retorna `{ id }`.
- `GET /audio/{id}` — stream do arquivo salvo.
- `GET /timeline/{id}` — retorna o JSON da timeline (array de segmentos `{ start, end, label, confidence }`).
- `GET /storage/list` — lista as músicas presentes em `storage/`.

Como executar
```powershell
dotnet run
```
Abra http://localhost:5000/ no navegador.

Configuração e presets
Todas as opções do analisador ficam em `appsettings.json` na seção `Analyzer`. Existem arquivos de preset (`appsettings.Balanced.json`, `appsettings.VocalHeavy.json`, etc.) já no repositório; basta copiar/mesclar ou apontar a configuração usada na inicialização.

Parâmetros configuráveis (descrição e efeito prático)

- `TargetRate` (Hz)
  - O sample rate alvo para processamento (ex.: 22050). Aumentar melhora resolução temporal e frequência (mais amostras), mas aumenta custo computacional e memória; diminuir reduz custo porém perde detalhe.

- `FftSize` (amostras)
  - Comprimento da FFT/Hop window (ex.: 2048, 4096). Aumentar melhora resolução de frequência (útil para distinguir notas próximas) e suaviza energia espectral, mas diminui resolução temporal e aumenta custo. Diminuir melhora resolução temporal (mais responsivo) mas reduz precisão de frequência.

- `Hop` (amostras)
  - Salto entre frames STFT. A menor `Hop` produz mais frames por segundo (melhor detalhe temporal) e aumenta custo I/O/CPU; maior `Hop` reduz custo e aumenta latência temporal. A duração de frame ≈ `Hop / TargetRate` segundos.

- `BassCutoff` (Hz)
  - Frequência mínima para considerar parte 'alta' (`chromaHigh`). Aumentar: mais conteúdo é considerado 'alto' (reduz influência do grave no cromagrama high). Diminuir: inclui mais graves no cromagrama high (útil se o baixo carrega harmonia).

- `MidCutLow` / `MidCutHigh` (Hz)
  - Define a faixa média (tipicamente onde a voz aparece). Ajuste para o range vocal do material.
  - Aumentar `MidCutLow` ou diminuir `MidCutHigh` reduz a faixa que será atenuada; diminuir `MidCutLow` ou aumentar `MidCutHigh` amplia a faixa afetada.

- `MidAttenuation` (0.0..1.0)
  - Fator multiplicador aplicado às magnitudes dentro da faixa média. Valores menores → mais atenuação (reduz voz). Valores próximos a 1.0 → quase sem atenuação.

- `HighFreqCutoff` (Hz) / `HighFreqAttenuation` (0.0..1.0)
  - Atenuação aplicada a bins muito agudos. Diminuir `HighFreqCutoff` fará com que menos bins sejam tratados como 'muito agudos'. Aumentar `HighFreqAttenuation` reduz sua atenuação (menos efeito).

- `HighWeight` / `FullWeight` (0.0..1.0)
  - Pesos usados para combinar `chromaHigh` (ênfase em regiões acima de `BassCutoff`) e `chromaFull` (todo espectro). Ex.: `final = fullWeight*chromaFull + highWeight*chromaHigh` antes da normalização.
  - Aumentar `HighWeight` prioriza informação das faixas mais altas (pode reduzir impacto do baixo/baixo elétrico). Aumentar `FullWeight` equilibra com energia total (útil quando o timbre do instrumento cobre todas as faixas).

- `SmoothingWindow` (inteiro, frames)
  - Janela de majority-vote usada por `SmoothLabels`. Maior valor → menos rótulos transitórios (menos flicker), porém perde reatividade a mudanças rápidas de acorde. Use valores ímpares (ex.: 5,7,9). Tempo de suavização aproximado = `(Hop / TargetRate) * SmoothingWindow` segundos.

- `MinSegmentDurationSeconds` (double, segundos)
  - Novo parâmetro que pós-processa a timeline: segmentos com duração menor que esse limiar são mesclados ao vizinho mais adequado (prev/next) para reduzir rótulos muito curtos e instáveis. Aumentar o valor reduz flicker de segmentos curtos; reduzir preserva mudanças rápidas.

- `PresetName` / `PresetDescription`
  - Metadados usados nos `appsettings.*.json` que descrevem o comportamento do preset.

Recomendações práticas
- Para reduzir influência de vocais: diminuir `MidAttenuation` (ex.: 0.2–0.4) e aumentar `SmoothingWindow` (7–9).
- Para preservar instrumentos com texturas rápidas: reduzir `SmoothingWindow` (3–5) e usar `FftSize` maior (4096) para melhor precisão de frequência.
- Para evitar rótulos muito curtos: defina `MinSegmentDurationSeconds` entre 0.2 e 0.6 e teste.

Arquivos e pontos de entrada importantes
- `Program.cs` — mapeia endpoints e carrega `AnalyzerConfig` de `appsettings`.
- `Services/ChordAnalyzer.cs` — implementação do pipeline DSP (STFT → chroma → matching → smoothing → timeline).
- `Models/AnalyzerConfig.cs` — propriedades configuráveis (veja comentários no código).
- `Services/StorageService.cs` — lógica para salvar arquivos, gerar ids (baseado em nome sanitizado) e carregar timelines.
- `wwwroot/` — frontend estático (index.html, script.js, styles.css).

Testes rápidos
1. Rode `dotnet run`.
2. Abra a UI, envie um arquivo mp3/wav. O nome original do arquivo será preservado em `storage/` e exibido acima do player.
3. Se a nova música não aparecer no dropdown, aguarde a análise terminar ou recarregue — o frontend foi atualizado para refrescar a lista automaticamente após upload.

Observações finais
Este protótipo funciona bem para detectar tríades maior/menor em músicas com acompanhamento harmônico claro. Para acordes estendidos, progressões complexas, ou para separar voz/instrumentos, técnicas mais avançadas (HPSS, modelos de separação de fontes, redes neurais) melhoram resultados, mas exigem muito mais trabalho e dependências.

Se quiser, eu posso:
- Adicionar uma UI para alternar presets em runtime.
- Gerar scripts para rodar em Docker.
- Adicionar um endpoint para ajustar parâmetros dinamicamente sem reiniciar.

