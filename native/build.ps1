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

param(
    # The directory holding g++.exe, when it is somewhere this cannot work out for itself.
    [string]$Mingw
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

# Candidates in order, and the first one that can actually do the job wins. Which g++ is found
# matters as much as whether one is: a machine with Strawberry Perl on PATH offers GCC 4.8 from
# 2014, which does not know -std=c++17 and fails forty lines into the compile with an error about a
# command line option rather than about the compiler being twelve years old.
#
# Hard-coding the MSYS2 path alone was wrong the other way: it fails for anybody whose MSYS2 is
# elsewhere, and it failed on CI, where the runner installs it under its own temp directory.
# A directory named on the command line is the only candidate. Searching on past it would mean
# asking for one compiler, getting another, and being told neither - which is how this was found:
# -Mingw pointed at the 2014 one and the build quietly succeeded with a different toolchain.
$candidates = if ($Mingw) { @($Mingw) } else {
    $found = @('C:\msys64\mingw64\bin')
    if ($onPath = Get-Command g++ -ErrorAction SilentlyContinue) { $found += Split-Path $onPath.Source }
    $found
}

$mingw = $null
$rejected = @()

foreach ($candidate in $candidates) {
    $exe = Join-Path $candidate 'g++.exe'
    if (-not (Test-Path $exe)) { continue }

    $version = (& $exe -dumpversion 2>$null)
    $major = 0
    [int]::TryParse(($version -split '\.')[0], [ref]$major) | Out-Null

    # GCC 7 is where C++17 became complete enough for the vendored source to compile.
    if ($major -ge 7) { $mingw = $candidate; break }

    $rejected += "  $exe is GCC $version, too old for -std=c++17"
}

if (-not $mingw) {
    throw @"
No g++ that can build this was found. Tried:
$($candidates -join "`n  ")
$(if ($rejected) { "`n" + ($rejected -join "`n") })

Install MSYS2 and its mingw-w64-x86_64-gcc package, or pass -Mingw with the directory holding a
g++.exe of GCC 7 or newer. MSVC cannot build this at all - see native/README.md for why.
"@
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
    # 2D is off, which costs the 44 matrix-only effects - they register as RSVD and the engine
    # offers 143 of the firmware's 187. That is close to no loss here: the house is one strip and the
    # app filters matrix-only effects out anyway. Turning it on needs vendor/wled/FX_2Dfcn.cpp
    # vendored for the drawing primitives (setPixelColorXY, blur2D, drawCircle and the rest) and
    # crc16 extracted from util.cpp; FX.cpp and FX_fcn.cpp themselves already compile with it.
    # The target is an ESP32 - both controllers are - and saying so matters beyond tidiness.
    # MIN_FRAME_DELAY is 2 on an ESP32 and 8 on the 8266 that FX.h falls back to, and in unlimited
    # frame rate mode FRAMETIME *is* MIN_FRAME_DELAY. Every effect that reads FRAMETIME was running
    # on the wrong constant: Blink at full speed went dark 44% of frames where the strip never
    # goes dark at all.
    '-std=c++17', '-O2', '-DARDUINO_ARCH_ESP32', '-DWLED_DISABLE_2D',
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
