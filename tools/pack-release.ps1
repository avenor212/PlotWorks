<#
.SYNOPSIS
    打一个可以直接分发的 PlotWorks 压缩包。

.DESCRIPTION
    一条命令走完：校验版本号 → dotnet publish → 检查发布目录完整性 → 打包 zip + SHA256。

    下面三处版本号必须一致，否则脚本直接中止（它们曾经分散在各处，改漏一处就会出现
    "界面显示 1.0.0、exe 属性还是 0.11.0"这种自相矛盾的包）：
      - PolyCut/Models/SettingsHandler.vb 的 SemanticVersion —— 界面与 G-code 头部用
      - PolyCut/PolyCut.vbproj 的 <Version>            —— exe 文件属性用
      - Data/version.json 的 LatestNonPreRelease       —— 将来更新服务用

    发布目录必须带 Resources/Fonts 下 5 个 ttf（BundledFontRegistry 按
    <exe 目录>\Resources\Fonts 找字体，缺了发布版就没有单线字体，且界面不报错）。
    脚本会把这个当成硬性检查。

.PARAMETER Flavor
    self-contained        自包含单文件，约 150 MB，目标机不用装 .NET 运行时（默认）
    framework-dependent   框架依赖，约 30 MB，目标机需要 .NET 10 Desktop Runtime

.PARAMETER OutDir
    输出目录，默认是仓库根下的 dist/。

.PARAMETER SkipPublish
    跳过 dotnet publish，直接拿现有的 PolyCut/bin/publish/FinalOutput 打包。
    只用于调试脚本本身；正常发版不要带这个参数。

.PARAMETER KeepStaging
    打完包保留中间目录（dist/PlotWorks-<版本>-win-x64/），方便检查包内容。

.EXAMPLE
    .\tools\pack-release.ps1

.EXAMPLE
    .\tools\pack-release.ps1 -Flavor framework-dependent

.EXAMPLE
    .\tools\pack-release.ps1 -KeepStaging
#>
param(
    [ValidateSet('self-contained', 'framework-dependent')]
    [string]$Flavor = 'self-contained',

    [string]$OutDir,

    [switch]$SkipPublish,

    [switch]$KeepStaging
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- 路径与前置检查

$RepoRoot = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $RepoRoot 'PolyCut.slnx'))) {
    throw "找不到仓库根（$RepoRoot 下没有 PolyCut.slnx），这个脚本要放在 tools/ 里运行"
}
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'dist' }

$ProjectFile = Join-Path $RepoRoot 'PolyCut\PolyCut.vbproj'
$SettingsFile = Join-Path $RepoRoot 'PolyCut\Models\SettingsHandler.vb'
$VersionFile = Join-Path $RepoRoot 'Data\version.json'
$FinalOutput = Join-Path $RepoRoot 'PolyCut\bin\publish\FinalOutput'

foreach ($f in @($ProjectFile, $SettingsFile, $VersionFile)) {
    if (-not (Test-Path $f)) { throw "缺少文件：$f" }
}

# ---------------------------------------------------------------- 版本号一致性

function Get-RequiredMatch {
    param([string]$Text, [string]$Pattern, [string]$What)
    $m = [regex]::Match($Text, $Pattern)
    if (-not $m.Success) { throw "无法从 $What 里解析出版本号（模式：$Pattern）" }
    return $m
}

# 1) 代码里的 SemanticVersion
$codeVersion = $(
    $m = Get-RequiredMatch ([IO.File]::ReadAllText($SettingsFile)) `
        'New\s+NuGet\.Versioning\.NuGetVersion\((\d+)\s*,\s*(\d+)\s*,\s*(\d+)\)' `
        'SettingsHandler.vb'
    "$($m.Groups[1].Value).$($m.Groups[2].Value).$($m.Groups[3].Value)"
)

# 2) vbproj 的 <Version>
$projVersion = $(
    $m = Get-RequiredMatch ([IO.File]::ReadAllText($ProjectFile)) '<Version>([^<]+)</Version>' 'PolyCut.vbproj'
    $m.Groups[1].Value.Trim()
)

# 3) version.json 的正式版号
$jsonVersion = (Get-Content $VersionFile -Raw | ConvertFrom-Json).LatestNonPreRelease

$versions = [ordered]@{
    'SettingsHandler.SemanticVersion' = $codeVersion
    'PolyCut.vbproj <Version>'        = $projVersion
    'Data/version.json'               = $jsonVersion
}

$distinct = @($versions.Values | Select-Object -Unique)
if ($distinct.Count -ne 1) {
    $detail = ($versions.GetEnumerator() | ForEach-Object { "  $($_.Key) = $($_.Value)" }) -join [Environment]::NewLine
    throw "版本号不一致，先统一再发版：" + [Environment]::NewLine + $detail
}

# 注意：Select-Object -Unique 在只有一个值时返回的是字符串而不是数组，
# 直接 $distinct[0] 会拿到首字符（"1.0.0" 变成 "1"）。上面用 @() 强制成数组。
$Version = $distinct[0]
Write-Host "版本号：$Version（三处一致）" -ForegroundColor Cyan

# ---------------------------------------------------------------- 发布

if (-not $SkipPublish) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "找不到 dotnet，无法发布" }

    # MovePublishedFiles 只做 Copy + SkipUnchangedFiles，不清目录；更要命的是，
    # 增量构建会漏掉 WPF 原生库（wpfgfx_cor3.dll / PresentationNative_cor3.dll …），
    # 打出来的包双击就崩。发版一律全量：先清 FinalOutput，再清各项目的 Release 中间产物。
    if (Test-Path $FinalOutput) {
        Write-Host "清空旧的发布目录…" -ForegroundColor DarkGray
        Remove-Item $FinalOutput -Recurse -Force
    }

    Write-Host "清 Release 中间产物（保证全量）…" -ForegroundColor DarkGray
    foreach ($project in @('PolyCut', 'PolyCut.Core', 'PolyCut.Shared', 'PolyCut.RichCanvas')) {
        foreach ($stage in @('bin', 'obj')) {
            $dir = Join-Path $RepoRoot (Join-Path $project "$stage\Release")
            if (Test-Path $dir) { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }

    $publishArgs = @('publish', $ProjectFile, '-c', 'Release', '--nologo', '-v', 'q')
    if ($Flavor -eq 'self-contained') {
        $publishArgs += @('-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true')
    }

    Write-Host "发布（$Flavor）…" -ForegroundColor Cyan
    Push-Location $RepoRoot
    try {
        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（exit $LASTEXITCODE）" }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path $FinalOutput)) { throw "发布目录不存在：$FinalOutput" }

# ---------------------------------------------------------------- 产物完整性检查

$exe = Join-Path $FinalOutput 'PlotWorks.exe'
if (-not (Test-Path $exe)) { throw "发布目录里没有 PlotWorks.exe" }

# 自包含单文件仍然依赖旁边这几个 WPF 原生库。增量构建最容易漏的就是它们，
# 而且漏了不会在打包时报错——只会让用户双击后当场闪退。
if ($Flavor -eq 'self-contained') {
    $missingNative = @()
    foreach ($native in @('wpfgfx_cor3.dll', 'PresentationNative_cor3.dll', 'D3DCompiler_47_cor3.dll')) {
        if (-not (Test-Path (Join-Path $FinalOutput $native))) { $missingNative += $native }
    }
    if ($missingNative.Count -gt 0) {
        throw "发布目录缺少 WPF 原生库：$($missingNative -join ', ')。这是增量构建的典型症状，包打出来会启动即崩；重跑一次脚本（它会清 Release 中间产物）即可。"
    }
}

# 字体是硬性要求：缺了发布版等于没有中文单线，而且界面不会有任何报错
$fontsDir = Join-Path $FinalOutput 'Resources\Fonts'
$fonts = @(Get-ChildItem $fontsDir -Filter *.ttf -ErrorAction SilentlyContinue)
if ($fonts.Count -ne 5) {
    throw "Resources\Fonts 下应是 5 个 ttf，实际 $($fonts.Count) 个。发布版会失去单线字体，先检查 vbproj 的 CopyToPublishDirectory。"
}

# exe 文件属性里的版本必须和代码版本一致（1.0.0.0 匹配 1.0.0）
$fileVersion = (Get-Item $exe).VersionInfo.FileVersion
if ($fileVersion -notlike "$Version.*" -and $fileVersion -ne $Version) {
    throw "exe 的 FileVersion 是 $fileVersion，与版本号 $Version 不符（检查 vbproj 的 <Version>）"
}

foreach ($required in @('LICENSE.txt', 'SOURCE.md', 'MODIFICATIONS.md', 'THIRD-PARTY-NOTICES.md')) {
    if (-not (Test-Path (Join-Path $RepoRoot $required))) { throw "仓库根缺少 $required，GPL 分发必须附上" }
}
if (-not (Test-Path (Join-Path $RepoRoot 'licenses'))) { throw "仓库根缺少 licenses\\ 目录，第三方许可文本必须随包" }

# 占位符没填就打包，等于发一个「源码在 <源码仓库URL>」的无效说明——只警告，不拦住出包，
# 但把话说到耳朵里。
$sourceText = Get-Content (Join-Path $RepoRoot 'SOURCE.md') -Raw
if ($sourceText -match '<源码仓库URL>') {
    Write-Host '警告：SOURCE.md 里仍有 <源码仓库URL> 等占位符，这份包还不能对外分发。' -ForegroundColor Yellow
}

Write-Host "产物检查通过：1 个 exe、5 个字体、合规文件齐全、文件版本 $fileVersion" -ForegroundColor DarkGray

# ---------------------------------------------------------------- 收拾不该进包的东西

# 只删确定无害的：库附带的 XML 文档与调试符号。运行时一律不需要。
$removed = 0
foreach ($name in @('IxMilia.Dxf.xml')) {
    $p = Join-Path $FinalOutput $name
    if (Test-Path $p) { Remove-Item $p -Force; $removed++ }
}
Get-ChildItem $FinalOutput -Recurse -Filter *.pdb -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item $_.FullName -Force; $script:removed++
}

# framework-dependent 形态不走 -r win-x64，NuGet 会把别的 RID 的原生资产一起塞进来
# （runtimes\osx-x64\native\*.dylib、runtime.osx.*.dll）。Windows 程序根本不会加载它们，
# 留着只是体积与第三方法务噪音（cairo/freetype/glib 各自的许可都要跟着致谢），直接删掉。
foreach ($rid in @('osx-x64', 'osx-arm64', 'linux-x64', 'linux-arm64')) {
    $ridDir = Join-Path $FinalOutput (Join-Path 'runtimes' $rid)
    if (Test-Path $ridDir) { Remove-Item $ridDir -Recurse -Force; $removed++ }
}
Get-ChildItem $FinalOutput -Filter 'runtime.*.dll' -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.Name -match 'osx|linux') { Remove-Item $_.FullName -Force; $script:removed++ }
}
if ($removed -gt 0) { Write-Host "清掉 $removed 个非运行时文件（XML 文档 / pdb / 异平台原生库）" -ForegroundColor DarkGray }

# ---------------------------------------------------------------- 打包

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
$OutDir = (Resolve-Path $OutDir).Path

# 两种形态必须用不同的文件名，否则先打自包含再打框架依赖会静默覆盖前一个包，
# 而且光看文件名分不清手里这个是哪一个。
$stageName = "PlotWorks-$Version-win-x64-$Flavor"
$stage = Join-Path $OutDir $stageName
$zip = Join-Path $OutDir "$stageName.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item (Join-Path $FinalOutput '*') $stage -Recurse -Force

# 分发二进制必须附许可与出处说明（GPL-3.0）
Copy-Item (Join-Path $RepoRoot 'LICENSE.txt') $stage -Force
Copy-Item (Join-Path $RepoRoot 'README.md') $stage -Force

# GPL-3.0 §6：目标码旁边必须给出「对应源码在哪拿」的清晰指引，所以 SOURCE.md 必须随包，
# 不能只放在网站上。MODIFICATIONS.md 是 §5(a) 的修改声明，THIRD-PARTY-NOTICES.md 与
# licenses/ 覆盖第三方组件与 Arphic 字体数据的独立义务。
foreach ($doc in @('SOURCE.md', 'MODIFICATIONS.md', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item (Join-Path $RepoRoot $doc) $stage -Force
}
Copy-Item (Join-Path $RepoRoot 'licenses') (Join-Path $stage 'licenses') -Recurse -Force

# 用户手册与主界面截图一起进包：解压后第一眼就能看到怎么用。
# 手册里指向 A1-README.md 之类的链接在包内会失效，正文本身是自洽的。
Copy-Item (Join-Path $RepoRoot 'Docs\user-guide.md') (Join-Path $stage '用户手册.md') -Force
Copy-Item (Join-Path $RepoRoot 'Docs\screenshot-main.png') $stage -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $zip) { Remove-Item $zip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
$hashFile = "$zip.sha256.txt"
"$hash  $(Split-Path $zip -Leaf)" | Set-Content $hashFile -Encoding ascii -NoNewline

if (-not $KeepStaging) { Remove-Item $stage -Recurse -Force }

# ---------------------------------------------------------------- 摘要

$zipItem = Get-Item $zip
$fileCount = (Get-ChildItem $FinalOutput -Recurse -File | Measure-Object).Count
$runtime = if ($Flavor -eq 'self-contained') { '自包含，目标机无需 .NET 运行时' } else { '框架依赖，目标机需 .NET 10 Desktop Runtime' }

Write-Host ''
Write-Host '发布包已生成' -ForegroundColor Green
Write-Host ("  {0}" -f $zip)
Write-Host ("  版本      {0}" -f $Version)
Write-Host ("  形态      {0}" -f $runtime)
Write-Host ("  内容      {0} 个文件（另附 LICENSE.txt、README.md、用户手册.md、screenshot-main.png）" -f $fileCount)
Write-Host ("  大小      {0:N1} MB" -f ($zipItem.Length / 1MB))
Write-Host ("  SHA256    {0}" -f $hash)
Write-Host ("  校验文件  {0}" -f $hashFile)
