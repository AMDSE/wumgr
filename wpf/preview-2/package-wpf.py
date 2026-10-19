#!/usr/bin/env python3
"""Package already-built net48 binaries together with complete corresponding source."""
import argparse
import hashlib
from pathlib import Path
import zipfile


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=Path(__file__).parent / 'artifacts')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    contents = {}

    def add(name, path):
        data = path.read_bytes()
        if name in contents and contents[name] != data:
            raise RuntimeError('Conflicting output: ' + name)
        contents[name] = data

    output_directories = {
        '程序': root / 'WuMgr.Wpf/bin/Release/net48',
        '验收工具': root / 'tests/WuMgr.Wpf.Smoke/bin/Release/net48',
    }
    for target, directory in output_directories.items():
        if not directory.is_dir():
            raise RuntimeError('Build the complete Release solution first: ' + str(directory))
        for path in sorted(directory.iterdir()):
            if path.is_file() and path.suffix.lower() in {'.exe', '.dll', '.config', '.pdb'}:
                add(target + '/' + path.name, path)
    bootstrapper = root / 'Bootstrapper/bin/WuMgr.exe'
    if not bootstrapper.is_file():
        raise RuntimeError('Build the native Bootstrapper first')
    add('程序/WuMgr.exe', bootstrapper)
    add('启动说明.txt', root / 'START-HERE.zh-CN.txt')
    add('程序/启动说明.txt', root / 'START-HERE.zh-CN.txt')
    add('RUNTIME-BOOTSTRAP.zh-CN.md', root / 'RUNTIME-BOOTSTRAP.zh-CN.md')
    logic = root / 'tests/WuMgr.Wpf.Logic/bin/Release/net48'
    for path in sorted(logic.iterdir()):
        if path.is_file() and path.suffix.lower() in {'.exe', '.dll', '.config', '.pdb'}:
            add('验收工具/' + path.name, path)

    excluded = {'bin', 'obj', '.git', '.vs', 'dist', 'artifacts', '__pycache__'}
    for path in sorted(root.rglob('*')):
        relative = path.relative_to(root)
        if not path.is_file() or any(part in excluded for part in relative.parts):
            continue
        if path.suffix.lower() in {'.user', '.suo', '.userprefs', '.zip', '.pfx', '.snk', '.o', '.obj'}:
            continue
        if path.name == '.test_policy':
            continue
        add('源码/' + relative.as_posix(), path)
    notices = ['LICENSE', 'Downloader-LICENSE.txt', 'THIRD-PARTY-NOTICES.md', '图标归属.txt']
    for name in notices:
        add('许可/' + name, root / name)
        add('程序/' + name, root / name)
    for name in ['README-WPF.zh-CN.md', 'README-WPF.en.md', 'ARCHITECTURE-WPF.md']:
        add(name, root / name)
    cmd = '@echo off\r\nchcp 65001 >nul\r\ncd /d "%~dp0"\r\nWuMgr.Wpf.Logic.exe > logic-results.txt 2>&1\r\nif errorlevel 1 (type logic-results.txt & pause & exit /b 1)\r\nWuMgr.Wpf.Smoke.exe "%~dp0验收结果"\r\nif errorlevel 1 (echo WPF smoke test failed. Review RESULT.txt. & pause & exit /b 1)\r\necho Safe demo tests completed. Review logic-results.txt and 验收结果.\r\npause\r\n'
    contents['验收工具/运行安全验收.cmd'] = cmd.encode('utf-8')
    hashes = '\n'.join(digest(data) + '  ' + name for name, data in sorted(contents.items())) + '\n'
    contents['SHA256FILES.txt'] = hashes.encode('utf-8')
    archive = output / 'WuMgr-WPF-preview-2.zip'
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as package:
        for name, data in sorted(contents.items()):
            package.writestr(name, data)
    checksum = output / 'SHA256SUMS-WPF-preview-2.txt'
    checksum.write_text(digest(archive.read_bytes()) + '  ' + archive.name + '\n', encoding='utf-8')
    with zipfile.ZipFile(archive) as package:
        if package.testzip() is not None:
            raise RuntimeError('ZIP integrity check failed')
        if any(part in excluded for name in package.namelist() for part in Path(name).parts):
            raise RuntimeError('Build intermediates leaked into archive')
    print(str(archive))
    print('Files:', len(contents), 'Bytes:', archive.stat().st_size)
    print(checksum.read_text().strip())


if __name__ == '__main__':
    main()
