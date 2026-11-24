# Rebuild ScreenVeil.exe using the csc that ships with .NET Framework (no SDK needed).
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$fw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $fw "csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

Push-Location $PSScriptRoot
try {
    $ico = Join-Path $PSScriptRoot 'screenveil.ico'
    $iconArg = @()
    if (Test-Path $ico) { $iconArg = @("/win32icon:$ico") }

    # Optional: WinRT media sessions (pause videos that are playing when you switch).
    # Needs the Windows SDK union metadata; if it isn't installed we compile without it
    # and the feature silently does nothing.
    $winrtArgs = @()
    $winmd = $null
    $sdkRoot = "C:\Program Files (x86)\Windows Kits\10\UnionMetadata"
    if (Test-Path $sdkRoot) {
        $winmd = Get-ChildItem $sdkRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+\.\d+' } |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'Windows.winmd' } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
    }
    $wr = Join-Path $fw 'System.Runtime.WindowsRuntime.dll'
    $srf = Join-Path $fw 'System.Runtime.dll'
    if ($winmd -and (Test-Path $wr) -and (Test-Path $srf)) {
        $winrtArgs = @('/define:WINRT', ('/r:"' + $winmd + '"'), ('/r:"' + $wr + '"'), ('/r:"' + $srf + '"'))
        "  + media sessions: enabled"
    }
    else {
        "  + media sessions: skipped (no Windows SDK metadata)"
    }

    & $csc /nologo /target:winexe /platform:anycpu /optimize+ /out:ScreenVeil.exe @iconArg @winrtArgs `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
        ScreenVeil.cs
    if ($LASTEXITCODE -ne 0) { throw "build failed, exit=$LASTEXITCODE" }

    # 这个工作区目录带 Low 完整性标签，生成的 exe 会继承它；那样进程就是低权限，
    # 系统会拒绝它移动任何窗口（err=5 拒绝访问）。所以这里显式改成 Medium。
    try { & icacls (Join-Path $PSScriptRoot 'ScreenVeil.exe') /setintegritylevel Medium | Out-Null } catch { }

    # 同步到安装目录（真正运行的那份）
    $install = Join-Path $env:LOCALAPPDATA 'ScreenVeil'
    if (Test-Path $install) {
        try {
            Copy-Item (Join-Path $PSScriptRoot 'ScreenVeil.exe') $install -Force
            if (Test-Path $ico) { Copy-Item $ico $install -Force }
            try { & icacls (Join-Path $install 'ScreenVeil.exe') /setintegritylevel Medium | Out-Null } catch { }
            "OK -> " + (Join-Path $install 'ScreenVeil.exe') + "  (已同步)"
        }
        catch {
            "OK -> " + (Join-Path $PSScriptRoot 'ScreenVeil.exe')
            "(!) 安装目录那份正在运行，没能覆盖；退出托盘里的 ScreenVeil 后再跑一次 build.ps1"
        }
    }
    else {
        "OK -> " + (Join-Path $PSScriptRoot 'ScreenVeil.exe')
    }
}
finally {
    Pop-Location
}
