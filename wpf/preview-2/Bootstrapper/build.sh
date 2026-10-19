#!/bin/sh
set -eu
cd "$(dirname "$0")"
mkdir -p bin
: "${CC:=i686-w64-mingw32-gcc}"
"$CC" -m32 -std=c11 -Wall -Wextra -Werror -Wno-unknown-pragmas -Wno-missing-field-initializers -Os -municode -mwindows bootstrapper.c -o bin/WuMgr.exe -lwinhttp -lwintrust -lcrypt32 -lshell32 -lshlwapi -ladvapi32 -lole32 -luser32 -lgdi32
printf '%s\n' "built bin/WuMgr.exe"
