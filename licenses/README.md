# 第三方许可文本（licenses/）

本目录存放 PlotWorks 分发包内第三方组件与字体数据的许可全文。它们各自的**用途与归属**见仓库根目录的 `THIRD-PARTY-NOTICES.md`；这里只放原文，不放解释。

分发 PlotWorks 时，`licenses/` 目录必须**原样随包**，不能删减。

| 文件 | 对应组件 | 许可 |
|---|---|---|
| `Apache-2.0.txt` | FamFamFam.Flags.Wpf、NuGet.Versioning、ValueConverters | Apache License 2.0 |
| `ARPHICPL.TXT` | 汉字单线骨架数据 `hanzi-strokes.json`（派生自 Arphic PL KaitiM GB / UKai） | Arphic Public License（Arphic-1999） |
| `BSD-2-Clause.txt` | Tomlyn | BSD 2-Clause |
| `BSD-3-Clause.txt` | 通用 BSD-3 文本 | BSD 3-Clause |
| `BSD-3-Clause-gong-wpf-dragdrop.txt` | gong-wpf-dragdrop | BSD 3-Clause |
| `BSD-3-Clause-SharpVectors.txt` | SharpVectors.Wpf | BSD 3-Clause |
| `BSL-1.0.txt` | 通用 Boost 许可文本 | Boost Software License 1.0 |
| `BSL-1.0-clipper2.txt` | Clipper2 | Boost Software License 1.0 |
| `MIT.txt` | 通用 MIT 文本 | MIT |
| `MIT-FluentFTP.txt` | FluentFTP | MIT |
| `MIT-IxMilia.txt` | IxMilia.Converters / Dwg / Dxf / Pdf | MIT |
| `MIT-CommunityToolkit.Mvvm.txt` | CommunityToolkit.Mvvm | MIT |
| `MIT-CommunityToolkit.Mvvm-ThirdPartyNotices.txt` | CommunityToolkit.Mvvm 自带的第三方声明 | MIT（含其依赖声明） |
| `MS-PL.txt` | Svg | Microsoft Public License |
| `WebView2-LICENSE.txt` | Microsoft.Web.WebView2 | WebView2 SDK 许可 |
| `WebView2-NOTICE.txt` | Microsoft.Web.WebView2 | WebView2 的 NOTICE（必附） |
| `WPF-UI-ThirdPartyNotices.txt` | WPF-UI（含 fluentui-system-icons 等） | MIT + 其自带第三方声明 |

**注意区分两条义务链**：

- 5 个 `PolyCut Hershey *.ttf` 只由公有领域 Hershey 数据生成，致谢写在 TTF name 表与 `tools/build-hersey-fonts.py` 的 NOTICE 常量里，不涉及 Arphic。
- `hanzi-strokes.json` 适用 **Arphic Public License**，与整个程序的 GPL-3.0 是两套独立义务：程序整体是 GPL，这份数据还额外要求保留 `ARPHICPL.TXT` 与声明修改方式。

新增 NuGet 依赖时，按 `THIRD-PARTY-NOTICES.md` 的流程补文本并更新本表；`tools/pack-release.ps1` 会把整个目录复制进包，缺文件即打包失败。
