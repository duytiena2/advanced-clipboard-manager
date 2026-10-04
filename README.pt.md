# Advanced Clipboard Manager

[English](README.md) • [Tiếng Việt](README.vi.md) • [简体中文](README.zh.md) • [日本語](README.ja.md) • [한국어](README.ko.md) • [Español](README.es.md) • [Français](README.fr.md) • [Deutsch](README.de.md) • [Русский](README.ru.md) • [Português](README.pt.md)

Um gerenciador de área de transferência focado em armazenamento local (**local-first**) e otimizado para teclado (**keyboard-first**) para Windows 10/11 e macOS. Ele salva automaticamente seu histórico, classifica cada cópia por tipo e encontra qualquer item instantaneamente com **Ctrl+Shift+V** (ou barra de menus no macOS).

> Status: **Fases 1–3 concluídas, Fase 4 (macOS) em andamento** (consulte o Roadmap).

<p align="center">
  <img src="docs/assets/screenshot.png" alt="Advanced Clipboard Manager UI and Settings" width="850" />
</p>

## Funcionalidades

| Funcionalidade | Descrição |
|---|---|
| Histórico da área de transferência | Salva cada cópia em ordem cronológica. Suporta texto formatado, imagens e arquivos. Cópias repetidas são mescladas com contagem de repetições. |
| Colagem rápida (Quick Paste) | **Ctrl+Shift+V** abre a paleta de pesquisa. Teclas direcionais para navegar, **Enter** cola diretamente no aplicativo ativo. |
| Classificação inteligente | Regras locais detectam automaticamente: SQL, JSON, XML, YAML, scripts shell, código-fonte, logs, URLs (GitHub...), e-mails, telefones, números, IPs e Markdown. |
| Pré-visualização dinâmica | Adapta-se ao conteúdo: destaque de sintaxe para código/SQL/JSON; visualizador de imagens com resolução (`PNG · 1103 × 593`) e leitor OCR offline; cartão detalhado para links URL; e proteção de dados confidenciais com **Ctrl+R** para revelar. |
| Pesquisa instantânea | SQLite FTS5 com correspondência de prefixos e filtros poderosos: `type:sql`, `type:snippet`, `type:image`, `pinned:true`, `sensitive:true`, etc. |
| Manutenção de formatação | Mantém a formatação HTML/RTF ao colar com **Enter**. Use **Ctrl+Shift+Enter** para colar como texto simples (ou o texto reconhecido pelo OCR de uma imagem). |
| Colagem rápida por número | As 9 primeiras linhas são numeradas: **Ctrl+1…9** cola diretamente (`Shift` para texto simples). |
| Transformações de texto | **Ctrl+K** (ou clique com o botão direito) para transformar antes de colar: MAIÚSCULAS/minúsculas/estilo título, remover espaços, juntar linhas, formatar JSON/SQL, codificar/decodificar Base64 e URL. |
| Pilha de colagem sequencial (Paste stack) | Selecione vários itens na ordem desejada com **Ctrl+Space** e inicie com **Ctrl+S**. Cada **Ctrl+V** subsequente colará o próximo item em sequência. |
| Trechos e modelos (Snippets) | Textos reutilizáveis que nunca expiram: **Ctrl+N** para salvar, **Ctrl+E** para editar. Suporta variáveis: `{date}`, `{time}`, `{datetime}`, `{date:yyyy-MM-dd}`, `{clipboard}`, `{uuid}`. |
| OCR offline para imagens | Imagens copiadas passam pelo OCR do Windows (Windows 10/11) para permitir busca por texto interno e colagem direta como texto. |
| Fixação na barra lateral (Sidebar) | **Ctrl+D** fixa a paleta na lateral da tela no modo AppBar. |
| Criptografia local segura | Criptografia opcional baseada na sua conta do Windows (DPAPI) sem necessidade de senhas. Seus dados são protegidos no disco. |
| Fixar itens (Pin) | **Ctrl+P** para fixar itens favoritos. Itens fixados nunca expiram e permanecem no topo. |
| Expiração automática | Retenção configurável por tipo: dados confidenciais 5 min, senhas 1 min, texto 1 dia, código/URL 7 dias, imagens 1 hora. |
| Privacidade absoluta | Todos os dados ficam salvos em `%LOCALAPPDATA%\ClipboardManager` (Windows) ou `~/Library/Application Support/ClipboardManager` (macOS). Não realiza nenhuma conexão com a internet. |

### Atalhos de teclado

| Tecla | Ação |
|---|---|
| `↑` `↓` `PgUp` `PgDn` | Navegar na lista |
| `Enter` | Colar (mescla se vários estiverem selecionados) |
| `Ctrl+Shift+Enter` | Colar como texto simples |
| `Ctrl+1` … `Ctrl+9` | Colar item 1…9 (`Shift` para texto simples) |
| `Ctrl+K` / Clique direito | Menu de transformações e colagem |
| `Ctrl+C` | Copiar para a área de transferência sem colar |
| `Ctrl+P` | Fixar / Desafixar |
| `Ctrl+Space` | Seleção múltipla (mantém a ordem) |
| `Ctrl+S` | Iniciar pilha de colagem sequencial |
| `Ctrl+N` / `Ctrl+E` | Salvar como snippet / Editar snippet |
| `Ctrl+R` | Revelar dados confidenciais |
| `Ctrl+T` | Fixar janela no topo |
| `Ctrl+D` | Ancorar na barra lateral: Direita → Esquerda → Desativado |
| `Ctrl+L` | Alternar proporção de divisão: 25/75, 30/70, 40/60, 50/50 |
| `Ctrl+M` | Alternar entre Widget compacto e Janela completa |
| `Ctrl+Shift+T` | Alternar transparência acrílica |
| `Ctrl+,` | Abrir Configurações |
| `F1` | Exibir ajuda de atalhos de teclado |
| `Del` | Excluir item |
| `Esc` | Fechar janela |

## Compilação e execução (Windows)

Requisitos: Windows 10/11 x64 e [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`).

```powershell
.\build.ps1            # Compilar + testes
.\build.ps1 -Run       # Compilar e executar (Ctrl+Shift+V)
.\build.ps1 -Publish   # Gerar executável único em .\publish\
.\build.ps1 -Installer # Criar instalador Setup.exe em .\dist\
.\build.ps1 -Msix      # Criar pacote da Microsoft Store (.msix)
```

## Licença

[MIT](LICENSE) © [duytiena2](https://github.com/duytiena2)
