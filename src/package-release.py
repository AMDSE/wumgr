from pathlib import Path
import hashlib
import shutil
import zipfile

repo = Path(__file__).resolve().parent
out = repo.parent / "wumgr-delivery"
stage = out / "WuMgr-驱动下载中心-v8"
if stage.exists():
    shutil.rmtree(stage)
program = stage / "程序"
source = stage / "源码"
program.mkdir(parents=True)
source.mkdir(parents=True)

release = repo / "wumgr" / "bin" / "Release"
for name in ("wumgr.exe", "wumgr.exe.config", "Downloader.dll"):
    path = release / name
    if not path.is_file():
        raise FileNotFoundError(f"缺少构建输出: {path}")
    shutil.copy2(path, program / name)

for name in ("LICENSE", "README.md", "README.zh-CN.md", "README.en.md", "PRIVACY_POLICY.md", "更新说明-v8.md"):
    shutil.copy2(repo / name, stage / name)
shutil.copy2(repo / "LICENSE", program / "LICENSE")
shutil.copy2(repo / "wumgr" / "res" / "icons8.txt", program / "图标归属.txt")
shutil.copy2(repo / "third-party" / "Downloader" / "LICENSE", program / "Downloader-LICENSE.txt")
shutil.copy2(repo / "third-party" / "Downloader" / "README.md", program / "Downloader-归属说明.md")

# Keep source complete, but never ship package caches or generated build/test output.
excluded_dirs = {".git", "bin", "obj", "packages", "artifacts", ".vs", "__pycache__"}
for path in repo.rglob("*"):
    rel = path.relative_to(repo)
    if any(part in excluded_dirs for part in rel.parts):
        continue
    if not path.is_file() or path.suffix.lower() in (".exe", ".zip", ".log", ".user", ".suo", ".pyc"):
        continue
    target = source / rel
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, target)

images = ("driver-center-zh-CN.png", "driver-center-en-US.png", "device-inventory-zh-CN.png", "device-inventory-en-US.png")
for name in images:
    preview = repo / "docs" / "images" / name
    if not preview.is_file():
        raise FileNotFoundError(f"缺少双语运行预览: {preview}")
    target = stage / "docs" / "images" / name
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(preview, target)

log = repo / "artifacts" / "最终测试日志.txt"
if not log.exists():
    raise FileNotFoundError(f"缺少最新测试日志: {log}")
shutil.copy2(log, stage / "最终测试日志-v8.txt")
(stage / "先读我.txt").write_text(
    "WuMgr 驱动下载中心 / Driver Download Center v8\n\n"
    "运行 / Run: Windows 10 / 11 + .NET Framework 4.8, 程序\\wumgr.exe\n"
    "设备：原生 SetupAPI 全类别读取，主界面只显示常见硬件及已安装驱动版本/提供商/日期；无更新也保留。\n"
    "过滤虚拟设备、音频端点、PCI桥/ACPI杂项及重复逻辑核心；完整清单仍可在设备搜索查看。\n"
    "语言 / Language: 顶栏简体中文 / English 即时切换；首次默认中文，手动切换保存偏好。\n"
    "Command-line: wumgr.exe --lang en-US / --lang zh-CN\n"
    "下载：Downloader 3.1.2，8 块、4 并发；小文件或无 Range 时单流。满足校验条件可恢复。\n"
    "安装：默认仅下载。可选 WUA 安装需管理员权限和确认，可能另行下载，不自动重启。\n"
    "Default: download only. Optional WUA installation needs administrator rights and confirmation. No automatic reboot.\n"
    "源码 / Source: 源码\\，不含 packages 缓存、bin/obj 或测试 exe。\n"
    "Preview: docs/images/ 下为 Linux/Mono 真实程序的演示数据截图，不是 Windows 实测。\n"
    "Linux/Mono automated tests only. Real Windows WMI/WUA/CDN/policy acceptance remains pending.\n"
    "许可 / Licenses: GPLv3 + Downloader MIT + original icon attribution included.\n",
    encoding="utf-8")

files = sorted(p for p in stage.rglob("*") if p.is_file())
manifest = [hashlib.sha256(p.read_bytes()).hexdigest() + "  " + p.relative_to(stage).as_posix() for p in files]
(stage / "SHA256SUMS.txt").write_text("\n".join(manifest) + "\n", encoding="utf-8")

archive = out / "WuMgr-驱动下载中心-v8.zip"
out.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as z:
    for path in sorted(stage.rglob("*")):
        if path.is_file():
            z.write(path, path.relative_to(out))
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    names = z.namelist()
    for suffix in ("/程序/wumgr.exe", "/程序/Downloader.dll", "/源码/tests/ServiceTests.cs", "/README.en.md", "/docs/images/driver-center-en-US.png", "/最终测试日志-v8.txt"):
        assert any(n.endswith(suffix) for n in names), suffix
    assert not any(n.endswith(".exe") and "/源码/" in n for n in names)
    assert not any("/packages/" in n or "/bin/" in n or "/obj/" in n for n in names)
    prefix = stage.name + "/"
    for entry in z.read(prefix + "SHA256SUMS.txt").decode("utf-8").splitlines():
        digest, rel = entry.split("  ", 1)
        assert hashlib.sha256(z.read(prefix + rel)).hexdigest() == digest, rel
print(str(archive))
print("SHA256:", hashlib.sha256(archive.read_bytes()).hexdigest())
