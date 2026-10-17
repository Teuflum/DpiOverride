#!/usr/bin/env bash
# Builds bin/DpiOverride.exe with Mono's C# compiler (runs on .NET Framework 4.x on Windows).
# Only use APIs that exist in .NET Framework 4.x - Mono's corlib has extra overloads that crash on Windows.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p bin
mcs -langversion:5 -target:winexe -platform:anycpu -optimize+ \
    -win32icon:assets/app.ico \
    -resource:assets/app.ico,app.ico -resource:assets/logo.png,logo.png \
    -r:System.Windows.Forms.dll -r:System.Drawing.dll \
    -out:bin/DpiOverride.exe src/*.cs
mono bin/DpiOverride.exe --selftest
