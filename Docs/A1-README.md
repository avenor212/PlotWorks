# PolyCut for Bambu Lab A1（魔改说明）

本仓库从 PolyCut 官方源码魔改而来，**仅面向 Bambu Lab A1**。通过 A1 的局域网 MQTT（TLS，8883）
接收状态、发送 G-code，通过隐式 TLS 的 FTPS（990）上传待打印文件。Klipper / Moonraker 上传路径已不再使用。

> **一句话**：画图 → 生成 → 上传到打印机根目录 → **在打印机屏幕上手动启动**。
> A1 固件不支持从局域网远程自动开始打印（见 [5.1](#51-自动开始打印不可用a1-固件限制)）。

## 1. 当前状态（2026-11-05）

### 1.1 连接与打印（`PolyCut/Services/BambuA1Service.vb`）

- MQTT over TLS：端口 `8883`，用户名固定 `bblp`，密码为打印机 8 位访问码，自动接受自签证书。
- 订阅 `device/<序列号>/report`，解析 `gcode_state`、喷嘴/床温、打印进度等字段。
- 发送 `gcode_line`（G-code 片段）、`pushall`（拉取全量状态）。
- FTPS（990，隐式 TLS）上传 G-code 到 FTPS 根目录（`/`），上传后在打印机文件列表中可见；
  内部路径按 `/model → /sdcard/model` 映射（见 `ToPrinterPath`），供 `gcode_file` 使用（该命令目前被 A1 拒绝，仅保留代码）。
- 所有连接、收发、状态变化写入日志文件 `%LocalAppData%\PlotWorks\a1.log`（超过 2 MB 自动清空）。
- 连接状态文本已汉化（连接中 / 已连接 / 未连接）。
- **没有自动重连**：MQTT 掉线后显示“未连接”，需要手动点 Connect。

### 1.2 Preview → Printer Control（原 Monitor 页已移除）

Monitor 页及其导航入口已删除（`MainWindow.xaml` 现在只有 Home / Preview）。相机占位、视频、
UI 日志窗口随页移除，控制功能全部并入 **Preview 页右侧的 Printer Control 页签**：

- **分割线上方**
  - 连接状态 + `Connect` / `Disconnect`
  - `Home / Zero point (G28)`
  - 刀路四角（Front/Rear Left/Right）：移动到**已生成 G-code** 的 XY 包围盒四角（含 Toolhead / Cut / Draw 偏移）；
    未生成时回退到打印机工作区，并在日志提示。移动前先抬 Z 到 `TravelZ`。
  - 笔高度：`Lift pen` / `Drop pen`（发 `G90` + `G1 Z`，速度 `F600`）、`Work Z` 微调
- **分割线下方**
  - Jog：步长 0.1 / 1 / 10 mm + 圆盘（`G91` 相对移动后回 `G90`）
  - `Tool X/Y` 累计坐标
  - `Capture reference point`：把当前位置记成设计参考点
  - 位置校准：输入实测 `X/Y` → `Set as current position`，把累计坐标直接改成该值
- **再一条分割线**：Live status（State / Nozzle / Bed / Progress）

### 1.3 参考点与位置校准

- 参考点按**画布坐标**（Y 向下）保存，捕获时已减去 Toolhead + 当前工具模式偏移；
  在 SVG 画布上显示为带编号标记，拖动对象时可吸附，右键双击删除并自动重新编号。
- 参考点随项目保存/加载（`ProjectSerializationService`）。
- 位置校准用于修正 jog 累计误差：输入已知物理坐标后，`Tool X/Y` 直接以该值为基准继续；
  输入范围自动限制在 `0…床宽/床高`（256）。

### 1.4 Tool Offsets（`Tab_Configure.xaml`）

| 配置项 | 属性 | 作用 |
|---|---|---|
| Toolhead Offset X / Y | `ProcessorConfiguration.ToolOffsetX/Y` | 共享的工具头安装偏移 |
| Cut Offset X / Y | `ProcessorConfiguration.CutOffsetX/Y` | Cut 模式叠加到 Toolhead Offset |
| Draw Offset X / Y | `ProcessorConfiguration.DrawOffsetX/Y` | Draw 模式叠加到 Toolhead Offset |

G-code 生成（`GCodeGenerator`）、预览、四角按钮、参考点捕获都使用
“Toolhead Offset + 当前工具模式偏移”，保证一致。旧项目缺字段时按 0 处理。

### 1.5 Export 页（`Views/ExportPage.xaml`）

- `Save GCode`：保存到磁盘。
- `Send to A1`：自动连接 MQTT → FTPS 上传 → 提示“已上传到 …，请在打印机屏幕上手动启动”。
- `Send Preview (A1)`：上传包围盒预览 G-code（不启动打印，同样要在屏幕上手动运行）。
- 上传前会弹出文件名输入框（默认取项目名）。
- **`Automatically Begin Printing` 选项已移除**，原位置改为一行红色提示：
  “A1 不支持远程自动开始打印，上传后请在打印机屏幕上手动启动。”
- 配置里的 `ExportConfig.AutoPrint` 字段仅为兼容旧项目而保留，默认 `False`，代码不再读取。

### 1.6 G-code 生成链

1. 画布坐标 → `RedefineOrigin`（Y 翻转，原点移到左下角）
2. 叠加 Toolhead + 当前工具模式偏移
3. 头部注释 → **Custom Start GCode**（默认 `G0 E0 / G21 / G28`）→ 路径 → **Custom End GCode**
4. 生成器**不再**自行插入 `G28`：回零统一由 Start GCode 负责，避免每次任务归零两次。

### 1.7 汉化

- 新增 `PolyCut/Resources/Localisation/zh-CN.toml`（约 340 条，键为英文原文，值为中文），
  在 `manifest.toml` 注册，作为 `EmbeddedResource` 打包。
- 默认界面语言改为 `zh-CN`；可在 设置 → 界面语言 切回英文。
- 本地盘语言目录 `%LocalAppData%\PlotWorks\Localisation\` 已同步到新版；
  `# @version` 提升为 2，保证下次启动时嵌入版会覆盖旧文件（LazyTranslate 只在程序版本更旧、
  或嵌入版 `@version` 更高时才替换本地文件）。
- 新增的 Printer Control / Snackbar / 连接状态文本直接中文化，不依赖语言表。

## 2. 使用步骤

1. **打印机**：`设置 → 局域网模式 → 开启局域网模式`（A1 需同时开启开发者模式），记录 IP、访问码、序列号。
2. **配置连接**：运行 PolyCut，右上角打印机下拉框旁齿轮 → **Printer Configuration** →
   **Bambu Lab A1 Connection** 填写 Host Address / Access Code / Serial Number，保存。
3. **连接**：Preview → Printer Control → `Connect`；Live status 出现 State / Nozzle / Bed 即成功。
4. **对位**：`Home` 回零（A1 会把工具头停在床中心上方），用四角按钮检查内容边界；
   需要更精确时用 Jog 走到已知点 → 输入实测坐标 → `Set as current position`。
5. **生成**：画好图后生成 G-code（Preview 页可先看路径与包围盒）。
6. **上传**：Export 页 `Send to A1`，输入文件名 → 上传成功。
7. **启动**：在打印机屏幕上打开该 `.gcode` 手动开始（这是目前唯一可用的启动方式）。

日志：`%LocalAppData%\PlotWorks\a1.log`。

## 3. 构建

```powershell
dotnet build PolyCut/PolyCut.vbproj -c Debug
```

构建注意事项（本仓库已处理）：

- `NuGet.Config` 指向 `nuget.org`。
- `Extern/IxMilia.Converters` 为 git 子模块；`src/IxMilia.Dwg`、`src/IxMilia.Dxf`、`src/IxMilia.Pdf`
  需检出对应提交并运行各自的代码生成器生成 `Generated/` 目录（见 `Extern/IxMilia.Converters/src/*/generate-code.*`）。
- 新增 NuGet 依赖：`MQTTnet 4.3.7.1207`、`FluentFTP 54.2.1`。

### 3.1 发布包

```powershell
# 方式 A：框架依赖（约 30 MB），目标机需要 .NET 10 Desktop Runtime
dotnet publish PolyCut/PolyCut.vbproj -c Release

# 方式 B：自包含单文件（约 150 MB），目标机不需要装运行时
dotnet publish PolyCut/PolyCut.vbproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

两种方式的产物都会汇总到 `PolyCut/bin/publish/FinalOutput/`。

**发布目录必须带 `Resources\Fonts\` 下的 5 个 ttf。** `BundledFontRegistry` 是按
`<exe 目录>\Resources\Fonts` 找字体的，缺了整个中文单线会退化回系统字体/空心轮廓，
而且界面上不会有任何报错——只会发现"写出来的字不是单线"。`PolyCut.vbproj` 的发布 target
已经处理了这件事（含 `PublishSingleFile` 的情况），不需要手工补文件。

> 之前这条链路从没跑通过：`CopyToOutputDirectory` 只把字体放进 `bin\Debug`，发布目录原来一个
> ttf 都没有；`MovePublishedFiles` 用 `DestinationFolder` 会把所有文件平铺到 `FinalOutput` 根下，
> 丢掉了 `Resources\Fonts` 层级。两处都已修正，发布后请照上面的方式验一遍字体下拉里是否有
> 5 款「单线 · …」。

## 4. 安全提示

- 访问码等于打印机完整控制权。它**明文保存**在 `%LocalAppData%\PlotWorks\Printers\*.json`
  并可能出现在 `a1.log` 中，不要提交、外传或截图分享。
- **`WorkZ` 默认是 5 mm，仍须按实际笔/刀安装高度确认**：对 A1 而言 Z=0 等于喷嘴/刀尖贴床，
  负值会被程序直接拒绝（生成与抬落笔两处都有校验）。第一次使用前先用画纸实测，把 `Work Z`
  调成贴合纸面的正值（通常 0.5–2 mm 起，再微调）。
- A1 的 `gcode_line` 能执行任意 G-code；四角按钮和抬落笔会实际移动工具头，
  执行前先确认 Z 高度与床上没有障碍。
- 仅局域网 + 开发者模式下云端功能（Handy、远程打印、OTA）不可用，这是拓竹的设计。

## 5. 已知限制

### 5.1 自动开始打印不可用（A1 固件限制）

- `gcode_file` 命令被 A1 固件直接拒绝；`project_file` 能返回 success、也能把 3mf 解包到 `/cache`，
  但不会真正启动打印。
- 因此 UI 不再提供自动打印，`Send to A1` 只负责上传，必须到打印机屏幕上手动启动。
- `BambuA1Service.UploadGCodeAsync` 里的 `autoPrint` 分支仅为将来固件可能支持而保留，所有调用方都传 `False`。

### 5.2 其它限制

- 视频流未接入，也没有摄像头 UI（A1 的 6000 端口为私有 TLS 协议，无 RTSP）。
- 上传文件放在 FTPS 根目录；A1 对该目录的清理策略由固件决定。
- 手动启动要求文件是可直接执行的 `.gcode`（不是 `.3mf` 工程文件）。
- 移动/点动是开环的：发送后不校验打印机响应；A1 拒绝命令（未归零、超行程、固件报错）时
  `Tool X/Y` 仍会累加，需要用位置校准纠正。
- `HomePrinter` 按“A1 在 G28 后把工具头停在床中心”的实测行为写死 `Tool X/Y = 128 / 128`；
  换机型或固件改动后需要重新校准。
- MQTT 断线不会自动重连，需手动 Connect。
- 文本框拖动问题在 2026-11-05 13:40 之后没有再回归验证，若复现请记录操作步骤。

## 6. 变更记录

- **2026-10-31**：初版。A1 MQTT/FTPS 连接、Monitor 页、Tool Offsets、Export 页。
- **2026-11-05**：
  - Monitor 页移除，控制并入 Preview → Printer Control（相机、视频、UI 日志窗口一并移除）；
  - 日志改为写 `a1.log`；
  - 新增参考点捕获/吸附/持久化、位置校准、四角按钮、上传文件名对话框；
  - 整机汉化（zh-CN），默认语言中文；
  - 自动打印选项移除，改为红字提示“上传后屏幕手动启动”；
  - 生成器不再重复插入 `G28`（回零由 Start GCode 负责）。
