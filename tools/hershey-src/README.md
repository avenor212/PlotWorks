# 字形原始数据（两套许可，别搞混）

这个目录同时存放**两种来源完全不同**的字形数据。它们的许可不一样，再分发时不能合并处理。

| 文件 | 来源 | 许可 | 用途 |
|---|---|---|---|
| `scripts.jhf`、`scriptc.jhf`、`cursive.jhf`、`gothgbt.jhf`、`futural.jhf` | Hershey 矢量字体（Dr. A. V. Hershey / U.S. National Bureau of Standards，1967）；`.jhf` 格式由 James Hurt（Cognition, Inc.）定义 | **公有领域字形数据 + 须保留致谢** | `tools/build-hershey-fonts.py` 生成 5 个 TTF；`tools/export-hershey-json.py` 生成 `hershey-glyphs.json` |
| `graphics.txt` | [Make Me a Hanzi](https://github.com/skishore/makemeahanzi)，字形源自 Arphic PL KaitiM GB / UKai | **Arphic Public License（Arphic-1999）**，全文见 `licenses/ARPHICPL.TXT` | `tools/export-hanzi-json.py` 生成 `hanzi-strokes.json` |

## 为什么放在一起

历史上只有 Hershey 的 `.jhf`，后来汉字数据也放进同一目录，目录名就留着了。**不要因为目录叫 `hershey-src` 就以为里面的东西都是公有领域**——`graphics.txt` 是 Arphic 字体的衍生数据，必须按 Arphic PL 处理：

- 分发时保留 `licenses/ARPHICPL.TXT` 原样全文；
- 修改声明（何时、如何修改）见 `Docs/fonts.md` 的「汉字数据的修改声明」一节；
- 修改后的产物 `hanzi-strokes.json` 整体继续以 Arphic PL 分发。

## 重新生成

```bash
pip install fonttools shapely

python tools/build-hershey-fonts.py tools/hershey-src PolyCut/Resources/Fonts
python tools/export-hershey-json.py tools/hershey-src PolyCut.Shared/Resources/hershey-glyphs.json
python tools/export-hanzi-json.py
```

`graphics.txt` 约 30 MB，仓库直接收录（而不是下载脚本），是为了让字形生成链可以离线复现。
