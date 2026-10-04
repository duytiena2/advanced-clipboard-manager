# Advanced Clipboard Manager

[English](README.md) • [Tiếng Việt](README.vi.md) • [简体中文](README.zh.md) • [日本語](README.ja.md) • [한국어](README.ko.md) • [Español](README.es.md) • [Français](README.fr.md) • [Deutsch](README.de.md) • [Русский](README.ru.md) • [Português](README.pt.md)

Un gestor de portapapeles diseñado con prioridad local (**local-first**) y optimizado para teclado (**keyboard-first**) para Windows 10/11 y macOS. Guarda automáticamente el historial, clasifica el contenido por tipo y te permite encontrar y pegar cualquier elemento al instante con **Ctrl+Shift+V** (o barra de menú en macOS).

> Estado: **Fases 1–3 completadas, Fase 4 (macOS) en progreso** (ver Hoja de ruta).

<p align="center">
  <img src="docs/assets/screenshot.png" alt="Advanced Clipboard Manager UI and Settings" width="850" />
</p>

## Características

| Característica | Descripción |
|---|---|
| Historial del portapapeles | Guarda automáticamente cada copia, de más reciente a más antigua. Admite texto con formato, imágenes y archivos. Las copias duplicadas se combinan con un contador. |
| Pegado rápido (Quick Paste) | **Ctrl+Shift+V** abre la paleta de búsqueda. Teclas de flecha para navegar, **Enter** pega directamente en la aplicación activa. |
| Clasificación inteligente | Reglas locales detectan automáticamente: SQL, JSON, XML, YAML, shell, código fuente, logs, URLs (GitHub...), correos, teléfonos, números, IPs y Markdown. |
| Vista previa interactiva | Se adapta según el contenido: resaltado de sintaxis para código/SQL/JSON; visor de imágenes con resolución (`PNG · 1103 × 593`) y lector OCR; tarjeta informativa para enlaces URL; y protección de datos confidenciales con **Ctrl+R** para revelar. |
| Búsqueda avanzada | SQLite FTS5 con búsqueda por prefijos, compatibilidad con acentos y filtros: `type:sql`, `type:snippet`, `type:image`, `pinned:true`, `sensitive:true`, etc. |
| Conservación de formato | Mantiene estilos HTML/RTF al pegar con **Enter**. Usa **Ctrl+Shift+Enter** para pegar texto sin formato (o el texto extraído por OCR de una imagen). |
| Pegado por número | Las primeras 9 filas están numeradas: **Ctrl+1…9** pega directamente (añade `Shift` para texto plano). |
| Transformación de texto | **Ctrl+K** (o clic derecho) para transformar antes de pegar: MAYÚSCULAS/minúsculas/tipo título, recortar espacios, unir líneas, formatear JSON/SQL, codificar/decodificar Base64 y URLs. |
| Pila de pegado secuencial (Paste stack) | Selecciona varios elementos en orden con **Ctrl+Space** y pulsa **Ctrl+S**. Cada **Ctrl+V** en cualquier app pegará el siguiente elemento en la lista. |
| Fragmentos y plantillas (Snippets) | Guarda textos reutilizables que nunca caducan: **Ctrl+N** para crear, **Ctrl+E** para editar. Admite variables: `{date}`, `{time}`, `{datetime}`, `{date:yyyy-MM-dd}`, `{clipboard}`, `{uuid}`. |
| OCR sin conexión para imágenes | Las imágenes copiadas son procesadas por el motor OCR de Windows (Windows 10/11) para buscar por el texto que contienen y pegarlas como texto. |
| Fijación lateral (Sidebar) | **Ctrl+D** fija la paleta en el lateral de la pantalla como barra de aplicaciones (AppBar). |
| Cifrado local seguro | Cifrado opcional con tu cuenta de Windows (DPAPI) sin contraseñas. Todo se cifra en disco y el índice de búsqueda opera solo en memoria RAM. |
| Fijar elementos (Pin) | **Ctrl+P** fija elementos favoritos. Nunca caducan y siempre se muestran al inicio. |
| Expiración automática | Tiempos de retención configurables por tipo: secretos 5 min, contraseñas 1 min, texto 1 día, código/URL 7 días, imágenes 1 hora. |
| Privacidad absoluta | Todo se guarda localmente en `%LOCALAPPDATA%\ClipboardManager` (Windows) o `~/Library/Application Support/ClipboardManager` (macOS). Respeta los gestores de contraseñas y no realiza conexiones a internet. |

### Atajos de teclado

| Tecla | Acción |
|---|---|
| `↑` `↓` `PgUp` `PgDn` | Navegar por la lista |
| `Enter` | Pegar (combina si hay varios seleccionados) |
| `Ctrl+Shift+Enter` | Pegar como texto sin formato |
| `Ctrl+1` … `Ctrl+9` | Pegar elemento 1…9 (`Shift` para texto plano) |
| `Ctrl+K` / Clic derecho | Menú de transformaciones y pegado |
| `Ctrl+C` | Copiar al portapapeles sin pegar |
| `Ctrl+P` | Fijar / Desfijar |
| `Ctrl+Space` | Selección múltiple (mantiene el orden) |
| `Ctrl+S` | Iniciar pila de pegado secuencial |
| `Ctrl+N` / `Ctrl+E` | Guardar como snippet / Editar snippet |
| `Ctrl+R` | Revelar contenido confidencial |
| `Ctrl+T` | Mantener ventana siempre visible |
| `Ctrl+D` | Anclar a la barra lateral (Derecha → Izquierda → Desactivar) |
| `Ctrl+L` | Cambiar proporción de división: 25/75, 30/70, 40/60, 50/50 |
| `Ctrl+M` | Alternar entre Widget compacto y Ventana completa |
| `Ctrl+Shift+T` | Alternar transparencia acrílica |
| `Ctrl+,` | Abrir ventana de Configuración |
| `F1` | Mostrar referencia de atajos de teclado |
| `Del` | Eliminar elemento (al final de la barra de búsqueda) |
| `Esc` | Cerrar ventana |

## Compilación y ejecución (Windows)

Requisitos: Windows 10/11 x64 y [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`).

```powershell
.\build.ps1            # Compilar + pruebas
.\build.ps1 -Run       # Compilar y ejecutar (Ctrl+Shift+V)
.\build.ps1 -Publish   # Generar exe independiente en .\publish\
.\build.ps1 -Installer # Crear instalador Setup.exe en .\dist\
.\build.ps1 -Msix      # Crear paquete de Microsoft Store (.msix)
```

## Licencia

[MIT](LICENSE) © [duytiena2](https://github.com/duytiena2)
