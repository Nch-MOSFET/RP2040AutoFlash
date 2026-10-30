# RP2040 AutoFlash をビルドする（Windows 付属の .NET Framework の C# コンパイラだけで済む）
#   .\build.ps1          bin\RP2040AutoFlash.exe を作る
#   .\build.ps1 -Test    あわせてテストも走らせる
param([switch]$Test)

$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$src = Join-Path $PSScriptRoot 'src'
$bin = Join-Path $PSScriptRoot 'bin'
$obj = Join-Path $PSScriptRoot 'obj'
New-Item -ItemType Directory -Force $bin, $obj | Out-Null

$common = @('/nologo', '/codepage:65001', '/optimize+', '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
$sources = (Get-ChildItem (Join-Path $src '*.cs')).FullName

function Invoke-Csc([string[]]$arguments) {
    & $csc @common @arguments
    if ($LASTEXITCODE -ne 0) { throw "csc が失敗しました（終了コード $LASTEXITCODE）" }
}

# 1. アイコンを作る
Invoke-Csc @('/target:exe', "/out:$obj\MakeIcon.exe", "$src\IconFactory.cs", "$PSScriptRoot\tools\MakeIcon.cs")
& "$obj\MakeIcon.exe" "$obj\app.ico"
if ($LASTEXITCODE -ne 0) { throw 'アイコンを作れませんでした' }

# 2. 本体
Invoke-Csc (@('/target:winexe', "/out:$bin\RP2040AutoFlash.exe", "/win32icon:$obj\app.ico", "/win32manifest:$src\app.manifest") + $sources)
Write-Host "できました: $bin\RP2040AutoFlash.exe"

# 3. テスト
if ($Test) {
    $testSources = $sources | Where-Object { (Split-Path $_ -Leaf) -ne 'Program.cs' }
    Invoke-Csc (@('/target:exe', "/out:$obj\Tests.exe", "$PSScriptRoot\tests\Tests.cs") + $testSources)
    $work = Join-Path $obj ('testrun_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
    & "$obj\Tests.exe" $work
    if ($LASTEXITCODE -ne 0) { throw 'テストが失敗しました' }
}
