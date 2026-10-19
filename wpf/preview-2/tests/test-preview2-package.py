#!/usr/bin/env python3
"""Validate Preview 2 delivery contents without executing Windows code."""
import hashlib
from pathlib import Path
import sys
import zipfile


def main():
    archive = Path(sys.argv[1]).resolve()
    with zipfile.ZipFile(archive) as package:
        assert package.testzip() is None, 'ZIP CRC failure'
        names = package.namelist()
        assert len(names) == len(set(names)), 'Duplicate ZIP entries'
        required = {
            '程序/WuMgr.exe', '程序/WuMgr.Wpf.exe', '程序/WuMgr.Wpf.exe.config',
            '程序/WuMgr.Core.dll', '程序/Downloader.dll', '启动说明.txt',
            'RUNTIME-BOOTSTRAP.zh-CN.md', '源码/Bootstrapper/bootstrapper.c',
            '源码/Bootstrapper/bootstrap_policy.h', '源码/Bootstrapper/test_policy.c',
            '源码/Bootstrapper/build.sh', '源码/Bootstrapper/build.ps1',
            '源码/WuMgr.Wpf.sln', '源码/WuMgr.Wpf/WuMgr.Wpf.csproj',
            '源码/validation/RESULTS.md', 'SHA256FILES.txt',
        }
        assert required <= set(names), 'Missing: ' + repr(required - set(names))
        forbidden = {'bin', 'obj', '.git', '__pycache__', '.vs'}
        for name in names:
            assert not (forbidden & set(Path(name).parts)), 'Build debris: ' + name
            assert not name.endswith(('.o', '.obj', '.zip', '.test_policy')), name
        manifest = package.read('SHA256FILES.txt').decode('utf-8').splitlines()
        hashed = set()
        for line in manifest:
            digest, name = line.split('  ', 1)
            assert name not in hashed, 'Duplicate hash: ' + name
            assert hashlib.sha256(package.read(name)).hexdigest() == digest, name
            hashed.add(name)
        assert hashed == set(names) - {'SHA256FILES.txt'}, 'Incomplete hash manifest'
        for name in ('程序/WuMgr.exe', '程序/WuMgr.Wpf.exe'):
            assert package.read(name).startswith(b'MZ'), 'Not a PE: ' + name
        launcher_size = len(package.read('程序/WuMgr.exe'))
        payload_size = sum(len(package.read(n)) for n in names if n.startswith('程序/'))
        assert payload_size < 5_000_000, 'Program folder exceeds 5 MB'
        assert b'WuMgr.exe' in package.read('启动说明.txt'), 'Wrong entry point'
        print('PASS ZIP integrity, required source/binaries, no build debris, complete SHA-256 manifest, PE headers, program size, startup instructions')
        print('Files:', len(names), 'Launcher bytes:', launcher_size, 'Program folder bytes:', payload_size, 'Archive bytes:', archive.stat().st_size)


if __name__ == '__main__':
    main()
