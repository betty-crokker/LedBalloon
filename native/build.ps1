# Builds WLED's effect engine into a DLL that LedBalloon can call.
#
#   pwsh native/build.ps1
#
# Produces native/wledfx.dll, which depends only on KERNEL32 and msvcrt - both of which ship with
# Windows - so it can travel beside the app as a single file.
#
# MSVC cannot compile this: bus_manager.h uses GCC's named-variadic macro extension
# (`#define DEBUGBUS_PRINTF(x...)`), which is not standard C++ and has no /Zc switch. g++ from MSYS2
# does, and nothing in the vendored source needed changing to make it.

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

$mingw = 'C:\msys64\mingw64\bin'
if (-not (Test-Path "$mingw\g++.exe")) {
    throw "g++ not found at $mingw. Install MSYS2 and the mingw-w64-x86_64-gcc package."
}
# g++ needs its own bin directory on PATH, not just an absolute path to the executable: without it
# the compile succeeds, emits nothing, and reports no error.
$env:PATH = "$mingw;$env:PATH"

$sources = @(
    'host.cpp', 'beats.cpp',
    'vendor/wled/FX.cpp', 'vendor/wled/FX_fcn.cpp', 'vendor/wled/colors.cpp',
    'vendor/wled/wled_math.cpp',
    'vendor/fastled/colorpalettes.cpp', 'vendor/fastled/colorutils.cpp',
    'vendor/fastled/hsv2rgb.cpp', 'vendor/fastled/lib8tion.cpp', 'vendor/fastled/noise.cpp'
) | ForEach-Object { Join-Path $here $_ }

$flags = @(
    '-std=c++17', '-O2', '-DWLED_DISABLE_2D',
    "-I$here", "-I$here/shim", "-I$here/vendor/wled", "-I$here/vendor/fastled",
    '-shared', '-o', (Join-Path $here 'wledfx.dll'),
    # -static so the result carries no MSYS2 runtime of its own.
    '-static', '-static-libgcc', '-static-libstdc++'
)

Write-Host "building native/wledfx.dll ..."
# FastLED and the shim both define LIB8STATIC; the redefinition is harmless and expected.
& g++ @flags @sources 2>&1 |
    Where-Object { $_ -notmatch 'LIB8STATIC|previous definition|^In file included from|^\s+from |^\s+\d+ \||^\s+\^|^\s+\|' } |
    ForEach-Object { Write-Host $_ }

if ($LASTEXITCODE -ne 0) { throw "g++ failed with $LASTEXITCODE" }

$dll = Get-Item (Join-Path $here 'wledfx.dll')
Write-Host ("built {0:N0} bytes" -f $dll.Length)
