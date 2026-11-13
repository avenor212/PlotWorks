# 从源码构建前的准备：子模块代码生成
#
# 背景：PolyCut.vbproj 直接引用 Extern/IxMilia.Converters 下的 Dwg / Dxf 子项目，而这两个
# 子项目的一部分源码（Generated/）由代码生成器产出，不在 git 里。生成器项目自带
# global.json，把 SDK 锁在 8.0.100；机器上只装了别的 .NET SDK（例如 10.x）时，
# dotnet run 会直接报 "SDK not found"，随后构建出现大量 CS0115（找不到基类成员）。
#
# 这个脚本做三件事：
#   1. 把子模块里 global.json 的 rollForward 放宽到 latestMajor（只改工作区文件，不进 git）；
#   2. 运行 Dwg / Dxf 的代码生成器；
#   3. 构建 PolyCut.vbproj（加 -SkipBuild 可跳过）。
#
# 用法：
#   git clone --recurse-submodules <仓库地址> PlotWorks
#   cd PlotWorks
#   .\tools\prepare-build.ps1
#
# 需要：.NET SDK（10.0.1xx 或更高）与 .NET 8 运行时；生成器本身是 net8.0 项目。

param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path $PSScriptRoot -Parent
$sub = Join-Path $RepoRoot 'Extern\IxMilia.Converters'

if (-not (Test-Path $sub)) {
    throw "子模块没有检出：$sub。请用 git clone --recurse-submodules，或补跑 git submodule update --init --recursive"
}

# 1) 放宽子模块的 SDK 锁定
$relaxed = 0
Get-ChildItem $sub -Recurse -Filter 'global.json' | ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    if ($text -match '"rollForward"\s*:\s*"latestMinor"') {
        $text = $text -replace '"rollForward"\s*:\s*"latestMinor"', '"rollForward": "latestMajor"'
        # 用 WriteAllText 写无 BOM 的 UTF-8：dotnet 读 global.json 时不容忍 BOM
        [System.IO.File]::WriteAllText($_.FullName, $text, (New-Object System.Text.UTF8Encoding($false)))
        $relaxed++
        Write-Host "放宽 SDK 锁定：$($_.FullName.Replace($RepoRoot, ''))" -ForegroundColor DarkGray
    }
}
Write-Host "global.json 处理完成（改动 $relaxed 个）"

# 2) 生成 Dwg / Dxf 的 Generated/ 目录
Push-Location (Join-Path $sub 'src\IxMilia.Dwg')
try {
    Write-Host '生成 Dwg 代码…'
    & .\generate-code.ps1
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw "Dwg 代码生成失败（exit $LASTEXITCODE）" }
} finally { Pop-Location }

Write-Host '生成 Dxf 代码…'
Push-Location (Join-Path $sub 'src\IxMilia.Dxf')
try {
    cmd /c .\generate-code.cmd
    if ($LASTEXITCODE -ne 0) { throw "Dxf 代码生成失败（exit $LASTEXITCODE）" }
} finally { Pop-Location }

$generated = Join-Path $sub 'src\IxMilia.Dwg\src\IxMilia.Dwg\Generated'
$count = (Get-ChildItem $generated -File -ErrorAction SilentlyContinue | Measure-Object).Count
if ($count -eq 0) { throw "Dwg 的 Generated/ 仍为空，代码生成没有真正跑通" }
Write-Host "Dwg Generated/ 共 $count 个文件"

# 3) 构建
if (-not $SkipBuild) {
    Push-Location $RepoRoot
    try {
        Write-Host '构建 PolyCut.vbproj（Debug）…'
        dotnet build PolyCut/PolyCut.vbproj -c Debug
        if ($LASTEXITCODE -ne 0) { throw "构建失败（exit $LASTEXITCODE）" }
    } finally { Pop-Location }
}

Write-Host '准备完成。' -ForegroundColor Green
