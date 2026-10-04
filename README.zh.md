# Advanced Clipboard Manager

[English](README.md) • [Tiếng Việt](README.vi.md) • [简体中文](README.zh.md) • [日本語](README.ja.md) • [한국어](README.ko.md) • [Español](README.es.md) • [Français](README.fr.md) • [Deutsch](README.de.md) • [Русский](README.ru.md) • [Português](README.pt.md)

基于本地优先 (Local-first)、键盘优先 (Keyboard-first) 设计的 Windows 10/11 和 macOS 剪贴板管理工具。它能自动保存剪贴板历史记录，按类型智能分类，并通过 **Ctrl+Shift+V**（macOS 上支持菜单栏）瞬间查找任何内容。

> 状态：**已完成 Phase 1–3，Phase 4 (macOS) 进行中**（详见路线图）。

<p align="center">
  <img src="docs/assets/screenshot.png" alt="Advanced Clipboard Manager UI and Settings" width="850" />
</p>

## 功能特性

| 功能 | 说明 |
|---|---|
| 剪贴板历史 | 自动保存每次复制的内容，最新的排在最前。支持纯文本、富文本、图片和文件。重复复制的内容会自动合并并记录复制次数。 |
| 快捷粘贴 (Quick Paste) | 按 **Ctrl+Shift+V** 唤出搜索面板。方向键移动，**Enter** 键直接粘贴到刚才使用的应用程序中。 |
| 智能自动分类 | 基于本地规则引擎与置信度评分，自动识别：SQL、JSON、XML、YAML、Shell 脚本、代码、日志、URL（GitHub 等）、邮箱、电话号码、数字、IP 地址和 Markdown。 |
| 内容自适应预览 | 根据内容类型动态调整预览区域：为 SQL、JSON、XML、YAML 和代码提供语法高亮；图片查看器显示分辨率（`PNG · 1103 × 593`）及离线 OCR 文字识别；富 URL 卡片支持一键浏览器启动与链接结构分解；敏感数据安全遮罩并支持 **Ctrl+R** 解锁查看。 |
| 智能搜索 | 内置 SQLite FTS5 全文索引，支持前缀匹配、越南语无声调模糊搜索（如 `chao` 匹配 `chào`），以及多种过滤语法：`type:sql`、`type:snippet`、`type:image`、`pinned:true`、`after:2026-09-01`、`sensitive:true` 等。 |
| 格式保留 | 复制时完整保留 HTML/RTF 样式，按 **Enter** 原样粘贴富文本。按 **Ctrl+Shift+Enter** 则粘贴纯文本（图片则粘贴识别出的 OCR 文字）。 |
| 数字快捷粘贴 | 前 9 行编号显示：**Ctrl+1…9** 直接粘贴对应行（加 `Shift` 粘贴为纯文本）。 |
| 文本转换 (Transforms) | 按 **Ctrl+K**（或右键）在粘贴前进行快速转换：大写/小写/首字母大写/句首大写、去除两端空格、单行合并、删除空行、JSON 格式化/压缩、SQL 美化、Base64 与 URL 编解码。 |
| 连续粘贴堆栈 (Paste stack) | 用 **Ctrl+Space** 按顺序勾选多个条目，按 **Ctrl+S** 启动堆栈。随后在任何应用中每次按 **Ctrl+V** 都会依次粘贴下一条 —— 填写表单与批量录入的神器。全部粘贴完毕、复制新内容或通过托盘菜单均可停止。 |
| 常用片段与模板 (Snippets) | 永久保存常用文本：按 **Ctrl+N** 将选中内容存为片段，**Ctrl+E** 快速编辑，或在“设置”窗口中统一管理。支持动态变量：`{date}`、`{time}`、`{datetime}`、`{date:yyyy-MM-dd}`、`{clipboard}`、`{uuid}`。 |
| OCR 图片离线文字识别 | 复制的截图由 Windows 系统内置离线 OCR（Windows 10/11）自动识别文字，支持按图内文字搜索图片，并可将图片直接作为文本粘贴。 |
| 屏幕边缘固定停靠 (Sidebar) | **Ctrl+D** 将面板停靠至屏幕左侧或右侧（AppBar 模式，像任务栏一样独占空间）。窗口常驻并自动向最后一个活跃窗口粘贴。 |
| 本地数据加密 | 可选安全加密：使用 Windows DPAPI（绑定当前 Windows 账户）加密数据库，无需设置密码。文本、格式、OCR 和图片在磁盘上加密存储，搜索索引仅存在于内存，重复检测使用哈希键。 |
| 收藏置顶 (Pin) | 按 **Ctrl+P** 收藏。置顶条目永不过期，始终排在列表最前。 |
| 自动过期清理 | 可按数据类型分别设置保留时长：敏感数据 5 分钟、密码 1 分钟、文本 1 天、代码/URL 7 天、图片 1 小时。均可在设置中调整。 |
| 敏感数据保护 | 自动检测 API 密钥、Token、JWT、AWS Key、私钥、数据库连接串及密码；列表中自动遮罩，不存入磁盘搜索索引。预览区默认隐藏并需 **Ctrl+R** 查看，过期后从系统剪贴板自动擦除。私钥永不写入本地。 |
| 隐私保护 | 所有数据完全保存在本地 `%LOCALAPPDATA%\ClipboardManager` (Windows) 或 `~/Library/Application Support/ClipboardManager` (macOS)。严格尊重 1Password、KeePass、Bitwarden 等密码管理器的防记录标记。应用程序没有任何网络外连请求。 |
| 多选与合并 | **Ctrl+Space** 标记多个条目，按 **Enter** 自动将所选条目合并（每项换行）一并粘贴。 |
| 系统托盘功能 | 暂停监听、清空历史（保留收藏和片段）、停止粘贴堆栈、系统设置、检查更新、开机自启、打开数据目录。 |

### 快捷键指南

| 快捷键 | 功能 |
|---|---|
| `↑` `↓` `PgUp` `PgDn` | 列表上下移动与翻页 |
| `Enter` | 粘贴选中项（多选时合并粘贴） |
| `Ctrl+Shift+Enter` | 粘贴为无格式纯文本 |
| `Ctrl+1` … `Ctrl+9` | 直接粘贴第 1…9 项（配合 `Shift` 粘贴纯文本） |
| `Ctrl+K` / 鼠标右键 | 打开文本转换菜单后粘贴 |
| `Ctrl+C` | 仅复制到剪贴板而不执行粘贴（或复制预览区选中文本） |
| 鼠标在预览区拖拽选中 | 部分文字选取（`Ctrl+C` 复制，`Enter` 粘贴选中部分） |
| `Ctrl+P` | 收藏 / 取消收藏 |
| `Ctrl+Space` | 勾选多项（保持选择顺序） |
| `Ctrl+S` | 使用勾选项目开启连续粘贴堆栈 |
| `Ctrl+N` / `Ctrl+E` | 保存为新代码片段 / 编辑当前选中的代码片段 |
| `Ctrl+R` | 显示被遮罩的敏感内容 |
| `Ctrl+T` | 固定悬浮窗 / 保持窗口常驻并向上一应用粘贴 |
| `Ctrl+D` | 停靠屏幕边框：右侧 → 左侧 → 关闭停靠 |
| `Ctrl+L` | 切换分栏比例：25/75, 30/70, 40/60, 50/50 |
| `Ctrl+M` | 切换极简小组件 (Compact Widget) / 完整窗口模式 |
| `Ctrl+Shift+T` | 切换亚克力毛玻璃背景半透明效果 |
| `Ctrl+,` | 打开设置窗口 |
| `F1` | 打开完整键盘快捷键参考清单 |
| `Del` | 删除选中条目（光标在搜索框末尾时） |
| `Esc` | 关闭面板 |

## 构建与运行 (Windows)

系统要求：Windows 10/11 x64 与 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`)。

```powershell
.\build.ps1            # 编译 + 运行核心单元测试
.\build.ps1 -Run       # 编译并启动应用（托盘图标，按 Ctrl+Shift+V）
.\build.ps1 -Publish   # 在 .\publish\ 输出单文件独立版 exe
.\build.ps1 -Installer # 在 .\dist\ 打包 Setup.exe 安装包 (自动使用 Inno Setup)
.\build.ps1 -Msix      # 在 .\dist\ 打包 Microsoft Store 安装包 (.msix)
```

## macOS 支持

系统要求：macOS 11+（Apple Silicon 或 Intel）与 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```bash
# 在 macOS 上启动
dotnet run --project src/ClipboardManager.Mac

# 打包独立 .app 与 .dmg 安装镜像
./packaging/mac/build-mac.sh osx-arm64 0.2.2   # Apple Silicon (M1/M2/M3/M4)
./packaging/mac/build-mac.sh osx-x64 0.2.2     # Intel Mac
```

## 许可证

[MIT](LICENSE) © [duytiena2](https://github.com/duytiena2)
