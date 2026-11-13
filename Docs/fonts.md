# 内置单线（手写）字体

PolyCut 内置 5 款单线字体，用于切割、刻字和钢笔描绘。它们出现在字体选择器的最前面（其余是系统字体）。

| 字体名 | 风格 | 源文件 | 适合 |
|---|---|---|---|
| PolyCut Hershey Script Simplex | 手写连笔，简洁 | `scripts.jhf` | 贺卡、标签、手写感文字 |
| PolyCut Hershey Script Complex | 手写连笔，花体 | `scriptc.jhf` | 请柬、装饰性文字 |
| PolyCut Hershey Cursive | 草书，斜体感 | `cursive.jhf` | 签名、装饰 |
| PolyCut Hershey Gothic English | 哥特手写 | `gothgbt.jhf` | 风格化标题 |
| PolyCut Hershey Roman Simplex | 技术单线（非手写） | `futural.jhf` | 工程标注、尺寸文字 |

## 为什么需要单线字体

普通 TrueType 字体（Calibri、Arial 等）的字形是**闭合轮廓**，用它切割会得到"空心字"——每笔画要切两遍内外两条线。单线字体每个笔画就是一条开放路径，刀/笔画一遍即可。

## 来源与许可

字形数据来自 **Hershey 矢量字体**：

- 原始作者：Dr. A. V. Hershey，美国国家标准局（U.S. National Bureau of Standards），1967 年
- `.jhf` 文件格式：James Hurt，Cognition, Inc.
- 数据分发许可：**允许任何人以任何目的使用（含商业用途）**，条件是保留上述致谢，且不得转成 U.S. NTIS 的专有格式

数据取自 [kamalmostafa/hershey-fonts](https://github.com/kamalmostafa/hershey-fonts)（`hershey-fonts/` 目录）。

> 相关参考：[Hershey 字体格式说明](https://raw.githubusercontent.com/tinkerator/hershey/refs/heads/main/jhfdata/README.md)、[AVHershey-OTF](https://github.com/scruss/AVHershey-OTF)（CC0，另一个转换实现）

## 字形是怎么生成的

`.jhf` 里的字形是**开放折线**，而 TrueType 只能存闭合轮廓。这里有两套并行的数据：

1. **TTF（显示用）**：把每条折线缓冲成极细的条带（半径 4 font units），用于字体选择器预览和画布上的 TextBox。
2. **原始折线（出刀路用）**：`hershey-glyphs.json` 直接存 `.jhf` 的开放折线，供 G-code 生成使用。

> 只用 TTF 是不行的：它的每条笔画是闭合条带，切割/描绘时会沿两侧各走一遍，看起来是双线。

## 单线渲染（关键实现）

`HersheyTextRenderer` 在 `DrawableGeometry.GetLocalGeometry` 里接管内置字体：

- 用 `PathGeometry` + 多个**开放** `PathFigure`（`IsClosed = False`）表达每条笔画
- 字符位置按字体自身 advance 累加，行高/基线取自 `FormattedText`，自动换行按文本框宽度计算
- **`PathFigure.IsFilled` 必须为 `True`**：WPF 的 `GetFlattenedPathGeometry` 会**丢弃** `IsFilled = False` 的 figure，导致"画布上没有路径"的错误。这也是这个功能最初失效的原因。

## 中文（单线汉字）

内置字体同时覆盖：

- **GB2312 全集 6763 字**（一级 3755 + 二级 3008）。二级字里大量是人名字（睿、曦、宸、晟、梓、萱、琪、怡、婷、鑫、淼），只取一级会让整个字从牌子上消失。
- **全角标点**：，。！？、；：（）「」《》与 ♥ ♡ —— 源数据里没有任何标点，这 16 个字形是手写的骨架。
- **带声调的拉丁字母**（è ǎ ō ǘ ǐ ǔ …）：字符先按 FormD 分解成「基础字母 + 组合符」，基字母走 Hershey 字形，声调作为一条短笔画画在字母上方。生字卡的拼音才不会丢掉声调。

数据在 `PolyCut.Shared/Resources/hanzi-strokes.json`（嵌入资源，约 6 MB），每个字存**笔画中位线**（hand-written centre line），也就是一笔下去笔尖走的轨迹。

- 来源：[Make Me a Hanzi](https://github.com/skishore/makemeahanzi) 的 `graphics.txt`，派生自 Arphic PL KaitiM GB / UKai，**Arphic Public License**
- 生成脚本：[tools/export-hanzi-json.py](../tools/export-hanzi-json.py)（RDP 简化，容差 2/1024 em；标点与心形字形直接写在脚本里）

### 汉字数据的修改声明（Arphic PL §2(a)）

Arphic Public License 第 2(a) 条要求：对字体的修改必须附一份醒目声明，说明**如何修改**与**何时修改**。本项目对原始数据的修改如下：

| 项 | 内容 |
|---|---|
| 原始数据 | Make Me a Hanzi `graphics.txt`（字段 `character` / `strokes` / `medians`），源自 Arphic PL KaitiM GB / UKai |
| 修改时间 | 2026-11 首次生成；此后随 `tools/export-hanzi-json.py` 的改动重新生成 |
| 修改方式 | ① 只保留 `medians`（笔画中位线），丢弃 `strokes`（轮廓）字段；② 对中位线做 RDP 简化，容差 2/1024 em；③ 字符集收窄为 GB2312 全集（6763 字），其余字符不收录；④ 另手写 16 个全角标点与 ♥/♡ 骨架，追加进同一数据文件；⑤ 输出为 `PolyCut.Shared/Resources/hanzi-strokes.json`（JSON，无加密、无压缩、无专有格式） |
| 修改后的获取方式 | 本仓库公开提供脚本与产物，见 `licenses/ARPHICPL.TXT`；满足 §2(b) 的 "Freely Available" |
| 许可 | 该数据整体继续以 Arphic Public License 分发，不并入本项目的 GPL-3.0（两套义务并存） |

渲染规则（`HersheyTextRenderer.TryBuildGeometry`）：

- ASCII 字符走字体自带字形（Hershey 折线）
- 码位 > 127 的字符按**全角 1 em** 前进；有笔画数据就画
- **只要有 CJK 字符查不到笔画，整段文字退回 TrueType 轮廓**。这是刻意的取舍：轮廓是双线、更粗更难看，但字还在；而跳过笔画会让一个字凭空消失，用户看到的是一块少字的牌子。
- 汉字坐标是 1024 单位 em、基线 y=0、y 向上，与 Hershey 的 30 单位制分开缩放，共用同一个基线
- **字体必须是内置单线体**：选 Calibri 等系统字体时，中文仍走轮廓（双线）

> 为什么不能只用系统字体的汉字：TrueType 汉字是填充轮廓，没有"骨架"信息。轮廓描边 = 每笔两遍。中位线才是单线。

### 一个必须知道的坑：闭合

单线笔画是**开放路径**，但 `GeometryExtractor.NormalizeFiguresForCut` 在 `IsFilled` 为真时会给每条路径补一条回到起点的线段（这是给闭合轮廓用的）。对一笔画来说等于走两遍。

修复：`GeometrySnapshot.SingleLine` 记录该元素是否用内置单线字体绘制，此时不再强制闭合。实测「岳阳楼记」32pt：闭合版 78.20 cm，修复后 **42.78 cm**，落刀 32 次（= 32 画）。

## 工程实现要点（字体加载）

- **行高度量必须是 1.35 em**（`build-hershey-fonts.py` 里 `ascent=1000, descent=-350`）。原先设成 750/-250，行高只有 1.0 em —— 汉字占满字身，多行文字会一行压一行，屏幕上和刀路上都糊成一团。WPF 的 `FontFamily.LineSpacing` 直接来自 hhea 的 ascent/descent，`HersheyTextRenderer` 的单线行高也取自同一个 `FormattedText`，所以改这一处两边同时生效。

- 字体文件作为 **Content** 随程序复制到 `<输出目录>\Resources\Fonts\`
- WPF 只有 `"目录\#字体族名"` 这种**字符串形式**能解析出字形；`New FontFamily(Uri, String)` 构造函数和 `"./相对路径/#名字"` 都会静默失败（`TryGetGlyphTypeface` 返回 False）
- 加载逻辑在 [BundledFontRegistry.vb](../PolyCut/Services/BundledFontRegistry.vb)
- 项目文件里存的是 `bundled:PolyCut Hershey Script Simplex` 这样的稳定标记，而不是绝对路径——否则换目录/换机器就丢字体

## 重新生成

```bash
pip install fonttools shapely

# 1) TTF（预览用）
python tools/build-hershey-fonts.py tools/hershey-src PolyCut/Resources/Fonts

# 2) 开放折线数据（出刀路用）
python tools/export-hershey-json.py tools/hershey-src PolyCut.Shared/Resources/hershey-glyphs.json

# 3) 汉字笔画中位线（需要 tools/hershey-src/graphics.txt，约 30 MB）
python tools/export-hanzi-json.py
```

原始 `.jhf` 在 [tools/hershey-src/](../tools/hershey-src/)（5 个文件，公有领域）；同目录的 `graphics.txt` 是汉字数据的输入，**适用 Arphic PL 而非 Hershey 公有领域许可**，见该目录的 `README.md`。

## 已知限制

- 拉丁字母只覆盖 **ASCII 32–126** 加上带声调字母（由基础字母 + 声调笔画合成）。其余拉丁扩展（如 ø、ñ、ß）不在此列。
- 汉字覆盖 GB2312 全集 6763 字。**GBK / GB18030 里的字不在内**（珺、玥、犇这类名字用字），遇到时整段回退轮廓——字不会丢，但会变成双线。
- 字形是折线近似，没有曲线指令。字号很大时（200pt 以上）能看到多边形边，属于源数据特性。
- 画布上的 TextBox 仍用 TTF 渲染，中文靠系统字体 fallback，**屏幕上看到的字形和刀路不完全一致**（刀路是楷体骨架）。
- **切割模式**下开放路径仍会经过 `ShiftAlongBlade` 平移（刀具安装偏移），文字会整体偏移半个刀径。
