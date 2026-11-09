# 改名遗留项（rename debt）

品牌从 **PolyCut** 改为 **PlotWorks** 时，只做了显示层和低风险改动。这份文档记录**故意没做**的部分、为什么没做、以及以后要动的时候怎么动。

最后更新：2026-11-09

---

## 一、已完成

| 项 | 位置 |
|---|---|
| 窗口标题、标题栏文字 | `MainWindow.xaml` Title / Content |
| 三个品牌资源 | `Resources/logo.svg`、`logo.ico`、`banner_dark.svg`（上游原文件收在 `Resources/_originals/`，后缀 `.bak`） |
| 崩溃对话框、Snackbar、文件对话框 | `Application.xaml.vb`、`MainViewModel.vb` |
| G-code 头部注释 | `GCodeGenerator.vb:152,156` |
| 可执行文件名 | `PolyCut.vbproj` 的 `<AssemblyName>PlotWorks</AssemblyName>` |
| 程序集资源 URI | XAML 里 2 处 `pack://…/PlotWorks;component/…` |
| 配置目录 | `%LocalAppData%\PlotWorks`（四处硬编码收敛到 `SettingsHandler.AppDataFolderName`） |
| 便携目录 | `.PlotWorksData`；目录名 `plotworks` 或 `polycut` 都触发便携模式 |
| 开始菜单快捷方式 | `PlotWorks.lnk` |
| README、A1-README、测试脚本 | 品牌名与路径 |

---

## 二、待做（发版前或建仓库后再处理）

### 1. 项目扩展名 `.polycut`

**现状**：`MainViewModel.vb` 6 处、`RecoveryStore.vb:11`（`recovery.polycut`）、README 与文档多处。

**为什么没做**：改了以后用户双击旧项目打不开。目前没有系统级文件关联注册（仓库里没有 `.reg` / 安装包），所以核心只是“打开时的扩展名判断”。

**做法**：

- `MainViewModel.vb:696` 的 `Path.GetExtension(...).Equals(".polycut")` 改为接受 `.plotworks` **和** `.polycut` 两个
- 三处文件对话框 Filter / DefaultExt（`:1581-1582`、`:1603-1605`、`:1652`）改显示名与默认扩展名
- `RecoveryStore.vb:11` 的恢复文件改名（无兼容负担，它只活一个会话）
- 若以后加文件关联，注册表键名一并使用新扩展名

**触发时机**：准备对外发版、确定要给用户一个“双击打开”的体验时。

### 2. 自动更新 / 语言包地址

**现状**：三处指向上游仓库，对本分支无效。

- `UpdateService.vb:5` `versionURL` → `raw.githubusercontent.com/IridiumIO/PolyCut/...`
- `UpdateService.vb:7` `updateURL` → `github.com/IridiumIO/PolyCut/releases/latest`
- `LocalisationService.vb:4` `LocalisationBaseUri` → 同上

**为什么没做**：现在没有新仓库，改了只是换个失效地址。而且 `LocalisationService` 的 `ApplicationName = "PolyCut"`（`:12`）影响 LazyTranslate 的本地化缓存与版本比较，改动要连同语言文件重新生成一起验证。

**做法**：

1. 先建仓库、上传 `Data/version.json` 与语言文件
2. 三个 URL 指向新仓库
3. `ApplicationName` 改为 `PlotWorks`，把 `Localisation/*.toml` 的 `# @version` 提一级，保证旧安装的本地缓存被覆盖
4. 在真机上验证：启动 → 检查更新 → 切换语言

**触发时机**：仓库建好、第一次发版前。

---

## 三、决定不做

| 项 | 现状 | 决定理由 |
|---|---|---|
| 字体族名 | `PolyCut Hershey Script Simplex` 等 5 个 | 项目文件里固化了 `bundled:PolyCut Hershey Xxx` 这一 token（`ProjectSerializationService.vb:534,615`）。改名会让旧项目降级为系统字体、版式变化。字体名只出现在字体选择器里，不影响对外观感 |
| 剪贴板格式 | `application/x-polycut-projectdata+json`（`ClipboardService.vb:8`） | 只在两个实例之间复制粘贴时生效，用户基本无感知；改了反而破坏新旧版本共存 |
| 命名空间 / 项目名 | `PolyCut`、`PolyCut.Core`、`PolyCut.Shared`、`PolyCut.RichCanvas` | 内部代号，用户看不见。改它要动 74 个文件的 99 行 `Imports` 与 79 行 XAML 引用，收益为零。`AssemblyName` 已覆盖对外可见的部分 |
| `.slnx` 与目录名 | `PolyCut.slnx`、`PolyCut/` 等 4 个目录 | 同上，属内部结构。**注意**：`PolyCut/` 这个目录名同时是便携模式的识别依据之一，改目录要同步 `ApplicationHostService.vb:43` |
| G-code 尾部 `OrcaSlicer PolyCut` | `GCodeGenerator.vb:173` | 位于 Klipper 元数据块内，属兼容标记，改了有未知风险；A1 不读这一段 |
| `InitialiseSettings("PolyCut", …)` 参数 | `SettingsHandler.vb:41-43` | 该参数在 `SettingsBase.vb:33` 的函数体里**未被使用**，是死参数，不影响任何路径 |
| 历史测试记录 | `Docs/trial-reports/*.md` | 记录的是当时真实跑的程序与版本，改动等于篡改实验记录 |

---

## 四、检查清单（以后动这些地方之前先看）

改名相关的字符串一共散落在这些地方，容易漏：

- `SettingsHandler.AppDataFolderName` —— 唯一的数据目录来源，四处引用它
- `ApplicationHostService.vb:43` —— 便携模式的目录名判断
- `PolyCut.vbproj` —— `AssemblyName`、`ApplicationIcon`、发布脚本里的 exe 名
- XAML 的 `pack://…;component/…` —— 改程序集名时必须同步
- `tools/polycut-ui.ps1` —— UI 自动化脚本里的 exe 路径与进程名
