# 修改声明（Modifications Notice）

本文件按 **GNU General Public License 第 3 版第 5(a) 条**的要求，声明本作品是对上游项目的修改版本。

## 一、来源

| 项目 | 内容 |
|---|---|
| 上游项目 | **PolyCut** |
| 上游作者 | IridiumIO |
| 上游地址 | <https://github.com/IridiumIO/PolyCut> |
| 上游许可 | GNU General Public License v3.0 |

## 二、本分支

| 项目 | 内容 |
|---|---|
| 名称 | **PlotWorks** |
| 性质 | 非官方分支（与上游作者无隶属、合作或授权关系） |
| 首次修改日期 | 2026-10-31 |
| 当前版本 | 1.0.0 |
| 许可 | GNU General Public License v3.0（见 `LICENSE.txt`） |

## 三、主要修改范围

1. **打印机通讯层**：新增 `PolyCut/Services/BambuA1Service.vb`，改为 Bambu Lab A1 的局域网协议——MQTT over TLS（8883）收发状态与 G-code，FTPS（990）上传文件。上游使用的 Klipper / Moonraker 路径不再使用。
2. **界面汉化**：新增 `PolyCut/Resources/Localisation/zh-CN.toml`（约 340 条），默认界面语言改为简体中文；新增文本直接在代码中中文化。
3. **中文单线字体**：新增 6,779 个汉字的单线骨架数据（`PolyCut.Shared/Resources/hanzi-strokes.json`）与字形生成脚本（`tools/export-hanzi-json.py`、`tools/build-hershey-fonts.py`），以及 5 款由 Hershey 数据生成的单线 TTF。
4. **功能调整**：新增参考点捕获、位置校准、刀路四角定位、抬笔/落笔、`Work Z` 校验；移除 Monitor 页（并入 Preview 页的 Printer Control）；移除自动开始打印选项（A1 固件限制，改为提示在打印机屏幕上手动启动）；切割功能整体屏蔽（`UIConfiguration.EnableCutting = False`）。
5. **品牌改名**：显示层、可执行文件名、配置目录由 PolyCut 改为 PlotWorks（`%LocalAppData%\PlotWorks`），并保留旧目录的自动迁移。内部命名空间与项目名保留 `PolyCut`（用户不可见）。
6. **切断上游网络依赖**：移除指向上游仓库的自动更新服务；语言加载改为只读本机文件，不再从上游拉取。
7. **构建**：新增 `tools/pack-release.ps1` 发布打包脚本，统一版本号校验与产物检查。

修改的详细记录见 `Docs/A1-README.md`（按日期）、`Docs/rename-debt.md`（改名遗留项）与 `Docs/trial-reports/`（真机试用记录）。

## 四、许可与源码

- 本作品整体按 GPL-3.0 发布，完整条款见 `LICENSE.txt`。
- 对应源码的获取方式见 `SOURCE.md`。
- 第三方组件与字体的许可见 `THIRD-PARTY-NOTICES.md` 与 `licenses/` 目录。

## 五、声明

本修改版按 GPL-3.0 第 5(a) 条「as a whole under this License」发布；上游作者不对本分支做出任何担保或背书。原始版权声明保留在上游文件的许可与出处说明中。
