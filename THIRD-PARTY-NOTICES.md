# 第三方组件与字体许可清单（THIRD-PARTY NOTICES）

**适用产品**：PlotWorks 1.0.0（Windows x64，两种分发包通用）
**本文件位置**：发行包根目录 `THIRD-PARTY-NOTICES.md`

**核对方法**：从 `PolyCut/obj/project.assets.json` 取出完整依赖闭包（67 个库），逐个读取 `%USERPROFILE%\.nuget\packages\<id>\<version>\*.nuspec` 中的 `license` 字段，并在包目录内查找随包许可文件；字体与嵌入二进制按其生成脚本与上游仓库核对。**凡是本地没有取得确凿依据的，都标为「未核实」，不做猜测。**

「需随包附许可文本」一列的判断标准：

- **是（必须）**：该许可的条款本身要求随二进制保留版权声明或许可全文（MIT / BSD / Apache-2.0 / BSL-1.0 / MS-PL / Arphic），或明确要求保留许可文件；
- **是（推荐）**：法律上只要求保留声明，但附全文成本极低、争议成本很高；
- **否**：编译期工具，不进入分发包；
- **待定**：见备注。

---

## 1. 本作品与上游

| 组件 | 版本 / 来源 | 许可 | 需随包附许可文本 | 依据与备注 |
|---|---|---|---|---|
| PlotWorks（本作品） | 1.0.0，本项目 | **GPL-3.0** | **是（必须）** | 仓库根 `LICENSE.txt`（GPL-3.0 全文，680 行）；两个发布包内均已随附且哈希一致 |
| PolyCut（上游基础） | IridiumIO，<https://github.com/IridiumIO/PolyCut> | **GPL-3.0** | **是（必须）** | 同上；`README.md:111-117` 已声明衍生关系与出处 |
| GCodePlot | @arpruss，<https://github.com/arpruss/gcodeplot> | **未核实** | **否（本包不含）** | 仅在 `LICENSE.txt:1-2` 的 "Additional Components" 提及；**已核对：两个发布包内均无 `GCodePlot.exe`**。若将来并入，须另行核实其许可 |

---

## 2. 源码级项目引用（随源码分发，编译进本程序）

| 组件 | 版本 / 来源 | 许可 | 需随包附许可文本 | 依据与备注 |
|---|---|---|---|---|
| IxMilia.Converters | 0.1.0，git 子模块 `Extern/IxMilia.Converters`（<https://github.com/ixmilia/converters>，见 `.gitmodules`） | **MIT** | 是（必须） | `Extern/IxMilia.Converters/License.txt:1-3`（Copyright (c) Microsoft Corporation） |
| IxMilia.Dwg | 子模块 `src/IxMilia.Dwg`（<https://github.com/ixmilia/dwg>） | **MIT** | 是（必须） | `Extern/IxMilia.Converters/src/IxMilia.Dwg/LICENSE.txt` |
| IxMilia.Dxf | 子模块 `src/IxMilia.Dxf`（<https://github.com/ixmilia/dxf>） | **MIT** | 是（必须） | `Extern/IxMilia.Converters/src/IxMilia.Dxf/LICENSE.txt` |
| IxMilia.Pdf | 子模块 `src/IxMilia.Pdf`（<https://github.com/ixmilia/pdf>） | **MIT** | 是（必须） | `Extern/IxMilia.Converters/src/IxMilia.Pdf/LICENSE.txt` |

> 注意：这三个子模块是**嵌套子模块**，在 `Extern/IxMilia.Converters/.gitmodules` 中声明。发布源码时必须一并提供其内容，否则买家无法重建二进制（GPL-3.0 第 6 条要求的"对应源码"）。

---

## 3. 运行时 NuGet 依赖（会随二进制进入分发包）

| 组件 | 版本 / 来源 | 许可 | 需随包附许可文本 | 依据与备注 |
|---|---|---|---|---|
| Clipper2 | 2.0.0，<https://www.angusj.com/clipper2/> | **BSL-1.0**（Boost Software License 1.0） | 是（必须） | nuspec `license type="file"` → 包内 `License.txt` 首行即 "Boost Software License - Version 1.0"；判定以**文件内容**为准，不是字段名 |
| CommunityToolkit.Mvvm | 8.2.2，<https://github.com/CommunityToolkit/dotnet> | **MIT** | 是（推荐） | nuspec `license type="expression"` = `MIT` |
| FamFamFam.Flags.Wpf | 1.2.1，<https://github.com/drewnoakes/famfamfam-flags-wpf> | **Apache-2.0** | 是（必须，需附全文） | nuspec `Apache-2.0` |
| FluentFTP | 54.2.1，<https://github.com/robinrodricks/FluentFTP> | **MIT** | 是（推荐） | nuspec `MIT`；A1 的 FTPS 上传依赖它 |
| gong-wpf-dragdrop | 4.0.0，<https://github.com/punker76/gong-wpf-dragdrop> | **BSD-3-Clause** | 是（必须） | nuspec `license type="file"` → 包内 `LICENSE` 首行 "BSD 3-Clause License"（Copyright (c) Jan Karger, Steven Kirk and Contributors） |
| LazyTranslate | 0.1.6，<https://github.com/IridiumIO/LazyTranslate> | **MIT** | 是（推荐） | nuspec `MIT`；作者同为上游 IridiumIO，注意与"本分支"区分 |
| Microsoft.Extensions.*（27 个程序集：Configuration×9、DependencyInjection×2、Diagnostics×2、FileProviders×2、Hosting×2、Logging×7、Options×2、Primitives、FileSystemGlobbing） | 8.0.0，<https://dot.net/> | **MIT** | 是（推荐） | 逐个 nuspec 均为 `MIT`；实际发布目录中可数到 27 个 `Microsoft.Extensions.*.dll` |
| Microsoft.Web.WebView2 | 1.0.2210.55，<https://aka.ms/webview> | **微软 WebView2 SDK 许可**（BSD-3 式三条款） | 是（必须，含 `NOTICE.txt`） | 包内 `LICENSE.txt`（"Copyright (C) Microsoft Corporation. All rights reserved." + 三条款）+ `NOTICE.txt`（"NOTICES AND INFORMATION / Do Not Translate or Localize"）。**两份都要随包** |
| Microsoft.Xaml.Behaviors.Wpf | 1.1.77，<https://github.com/Microsoft/XamlBehaviorsWpf> | **MIT** | 是（推荐） | nuspec `MIT` |
| MQTTnet | 4.3.7.1207，<https://github.com/dotnet/MQTTnet> | **MIT** | 是（推荐） | nuspec `MIT`；A1 的 MQTT over TLS 依赖它 |
| NuGet.Versioning | 7.9.0，<https://aka.ms/nugetprj> | **Apache-2.0** | 是（必须，需附全文） | nuspec `Apache-2.0` |
| SharpVectors.Wpf（含 Core / Css / Dom / Model / Converters.Wpf / Rendering.Wpf / Runtime.Wpf） | 1.8.5，<https://github.com/ElinamLLC/SharpVectors> | **BSD-3-Clause** | 是（必须） | nuspec `BSD-3-Clause` |
| Svg | 3.4.6，<https://github.com/svg-net/SVG> | **MS-PL**（Microsoft Public License） | 是（必须） | nuspec `MS-PL` |
| ValueConverters（含 ValueConverters.Annotations） | 3.0.26，<https://github.com/thomasgalliker/ValueConverters.NET> | **Apache-2.0** | 是（必须，需附全文） | nuspec 无 `license` 表达式，`licenseUrl` = `http://opensource.org/licenses/Apache-2.0` |
| WPF-UI / WPF-UI.Abstractions | 4.3.0，<https://github.com/lepoco/wpfui> | **MIT** | 是（必须，另附其 `ThirdPartyNotices.txt`） | 包内 `LICENSE.md` = MIT（Copyright (c) 2021-2025 Leszek Pomianowski and WPF UI Contributors）；包内 `ThirdPartyNotices.txt` 列明 5 项内嵌组件：VirtualizingWrapPanel 2.0.6（MIT）、fluentui-system-icons 1.1.242（MIT）、dotnet/wpf 8.0（MIT）、microsoft-ui-xaml 3.0（MIT）、segoe-fluent-icons-font 3.0（**未核实**，随 WPF-UI 声明原文列出） |
| WPF-UI.DependencyInjection | 4.2.0，<https://github.com/lepoco/wpfui> | **MIT** | 是（推荐） | nuspec `MIT` |
| WpfObservableRangeCollection | 2.2.2，<https://github.com/CodingOctocat/WpfObservableRangeCollection> | **MIT** | 是（推荐） | nuspec `MIT` |
| ExCSS（传递依赖，经 WPF-UI 引入） | 4.2.3 | **MIT** | 是（推荐） | nuspec `MIT` |
| Tomlyn（传递依赖，经 WPF-UI 引入） | 2.10.1 | **BSD-2-Clause** | 是（必须） | nuspec `BSD-2-Clause` |
| runtime.osx.10.10-x64.CoreCompat.System.Drawing | 5.8.64 | **MIT**（包外层） | **见备注** | nuspec 无 `license` 字段，`licenseUrl` 指向 `CoreCompat/libgdiplus-packaging` 的 LICENSE（已联网核对：MIT 文本）。**但该包带入 14 个 macOS 原生库**，其内部各库许可见第 6 节「未核实」。**建议做法：直接从 Windows 包内剔除，而不是逐一致谢** |

---

## 4. 编译期依赖（不进入分发包，不需随包附文本）

| 组件 | 版本 / 来源 | 许可 | 需随包附许可文本 | 依据与备注 |
|---|---|---|---|---|
| IridiumIO.MVVM.VBSourceGenerators | 0.6.2，<https://github.com/IridiumIO/MVVM.VBSourceGenerators> | **MIT** | 否 | 源码生成器，`Analyzer` 资产，不复制到输出目录 |
| LazyTranslate.CatalogueBuilder | 0.1.6 | **MIT** | 否 | 仅 `Debug` 配置引用（`PolyCut/PolyCut.vbproj:100`） |
| Microsoft.CodeAnalysis.*（Common / VisualBasic / Analyzers） | 5.9.0 等 | **MIT** | 否 | 编译期分析器，随上两项引入 |

---

## 5. 内嵌二进制与工具

| 组件 | 版本 / 来源 | 许可 | 需随包附许可文本 | 依据与备注 |
|---|---|---|---|---|
| vtracer.exe | 2,381,824 B，<https://github.com/visioncortex/vtracer> | **MIT**（Copyright (c) 2024 TSANG, Hao Fung） | 是（必须） | 以 `EmbeddedResource` 内嵌（`PolyCut/PolyCut.vbproj:51`），**不以独立文件出现在发布目录**；许可取自上游仓库 `LICENSE`（已联网核对）。版本号未在二进制内标注，**具体版本未核实**（建议在源码仓库注明下载来源与 commit） |

---

## 6. 字体与字形数据

| 组件 | 版本 / 来源 | 许可 | 需随包附许可文本 | 依据与备注 |
|---|---|---|---|---|
| PolyCut Hershey Script Simplex / Script Complex / Cursive / Gothic English / Roman Simplex（5 个 `.ttf`） | 由 `tools/hershey-src/*.jhf` 经 `tools/build-hershey-fonts.py` 生成 | **公有领域字形数据 + 须保留致谢** | 是（**须致谢**，见下） | 字形数据：Dr. A. V. Hershey（U.S. National Bureau of Standards，1967）；`.jhf` 格式：James Hurt（Cognition, Inc.）。该分发"允许任何目的使用（含商业），条件是保留上述致谢，且不得转成 U.S. NTIS 专有格式"（`Docs/fonts.md:19-25`）。TTF 的 name 表内已内嵌该致谢（`tools/build-hershey-fonts.py:27` 的 `NOTICE` 常量、`:163` `copyright` 字段）。**注意**：本项不含 Arphic 数据，见下一行 |
| `hanzi-strokes.json`（6,032,878 B，嵌入 `PolyCut.Shared.dll`） | 由 Make Me a Hanzi 的 `graphics.txt` 派生（`tools/export-hanzi-json.py`），原始字形来自 Arphic PL KaitiM GB / UKai | **Arphic Public License（Arphic-1999）** | **是（必须，须原样附 `ARPHICPL.TXT` 全文）** | `Docs/fonts.md:56` 已如实记录来源与许可。Arphic PL §1 要求"retain this license file (ARPHICPL.TXT) unaltered in all copies"；§2(a) 要求修改处插入"如何、何时修改"的醒目声明；§2(b) 要求修改整体对第三方 Freely Available。条款文本见 [SPDX Arphic-1999](https://spdx.org/licenses/Arphic-1999.html)。**已补齐**：仓库 `licenses/ARPHICPL.TXT` 为许可全文；修改声明见 `Docs/fonts.md` 第 6 节；`pack-release.ps1` 会整目录复制 `licenses/` |
| `hershey-glyphs.json`（104,858 B，嵌入 `PolyCut.Shared.dll`） | 由 `tools/export-hershey-json.py` 从 `.jhf` 生成 | 同 Hershey 公有领域数据 + 致谢 | 是（同上，合并致谢即可） | `Docs/fonts.md:34`、`:99` |
| Make Me a Hanzi（项目代码本身） | <https://github.com/skishore/makemeahanzi> | **未核实** | 待定 | 若只取 `graphics.txt` 数据（本项目做法），则适用 Arphic PL；若将来引入其代码，须另行核实项目自身许可 |
| 全角标点与 ♥/♡ 字形（16 个） | 本项目手写骨架（`tools/export-hanzi-json.py` 内直接定义） | **本项目自有** | 否 | `Docs/fonts.md:51`；按 GPL-3.0 随本项目源码发布 |

### 6.1 字体许可的三项动作（当前状态）

1. **已完成**：仓库 `licenses/ARPHICPL.TXT` 收录 Arphic Public License 全文，未经修改；`pack-release.ps1` 会把整个 `licenses/` 目录复制进发布包。
2. **已完成**：本节与 `README.md`「许可与出处」保留 Hershey / James Hurt 致谢与 Arphic Technology Co., Ltd. 的版权声明。
3. **已完成**：源码仓库提供 `hanzi-strokes.json` 的完整生成链——`tools/export-hanzi-json.py` 与其输入 `tools/hershey-src/graphics.txt`，满足 Arphic PL §2(b) 的 "Freely Available"。

---

## 7. .NET 运行时与 Windows 组件

| 组件 | 版本 / 来源 | 许可 | 需随包附许可文本 | 依据与备注 |
|---|---|---|---|---|
| .NET 10 Desktop Runtime 与 WPF 原生库（`wpfgfx_cor3.dll`、`PresentationNative_cor3.dll`、`D3DCompiler_47_cor3.dll`、`PenImc_cor3.dll`、`vcruntime140_cor3.dll` 等） | 随 **self-contained** 分发包内置于 `PlotWorks.exe` 与旁侧原生库 | **未核实** | **未核实** | 微软 .NET 运行时由 MIT 许可的源码构建，但运行时本身的再分发受 Microsoft 的运行时许可条款约束；本次**未在包内找到任何微软许可文本**。稳妥做法：随包放入微软官方的 .NET 第三方声明文件（`ThirdPartyNotices.txt`，可从 .NET 安装目录取得），并在本表标注来源 |
| Microsoft.Web.WebView2 Runtime（目标机已安装的 Evergreen Runtime） | 由用户机器提供，**不由本包分发** | 微软运行时许可 | 否（不由本包分发） | `WebView2Loader.dll` 属 SDK 部分，已在第 3 节列出 |

---

## 8. 明确「未核实」的清单（不得作为已合规依据）

以下项目**本次审计未能取得确凿依据**，在补齐之前不要对外声称"许可已全部核实"：

| # | 项目 | 未核实的具体内容 | 建议的核实途径 |
|---|---|---|---|
| 1 | macOS 原生库（`runtimes/osx-x64/native/` 下 14 个文件） | `libcairo.2.dylib`、`libfontconfig.1`、`libfreetype.6`、`libgdiplus`、`libgif.7`、`libglib-2.0.0`、`libintl.8`、`libjpeg.9`、`libpcre.1`、`libpixman-1.0`、`libpng16.16`、`libtiff.5` 各自的确切许可与版本 | **首选方案：从 Windows 包中删除这些文件，问题随之消失**。若必须保留，需逐个追溯其构建来源 |
| 2 | `runtimes/osx-x64/native/libgdiplus-lighthouse.dylib` | 来源与该名称对应的具体项目 | 同上（建议删除） |
| 3 | WPF-UI 内嵌的 `segoe-fluent-icons-font 3.0` | WPF-UI 的 `ThirdPartyNotices.txt` 列出了它，但微软该字体的再分发条款需单独确认 | 查阅微软 Fluent 图标字体许可页 |
| 4 | .NET 10 自包含运行时 | 随包再分发的具体条款、应附的声明文件 | 从 .NET 安装目录取出官方 `ThirdPartyNotices.txt` 随包 |
| 5 | vtracer.exe 的**确切版本号** | 二进制内无版本资源，无法从文件判定 | 记录下载来源 URL 与 commit，写入源码仓库文档 |
| 6 | GIF 编码/解码相关的 `System.Drawing` 兼容层 | 仅存在于 macOS 资产中，实际不使用 | 与第 1 项合并处理 |
| 7 | Hershey 原始数据的公有领域**法理依据原件** | 本项目依据 `Docs/fonts.md` 与 `tools/build-hershey-fonts.py` 的自述；未取得 1967 年 NBS 原始声明原件的副本 | 引用 kamalmostafa/hershey-fonts 仓库中的原始 README 声明作为附件 |

---

## 9. 维护要求

1. 每次新增、升级或移除 NuGet 依赖，都要重跑一遍本清单（方法见文首"核对方法"），**尤其是传递依赖**——直接引用只有 18 个，实际进包的有 60 多个。
2. 每次发布新二进制，必须同步更新本文档的版本号，并确保 `licenses/` 目录内容与表中"需随包附许可文本 = 是"的行一一对应。
3. 表内任何一行从「未核实」变为已核实，都要在对应行的依据栏写明核实来源（文件路径或 URL）。
