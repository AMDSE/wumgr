# Downloader 3.1.2 归属与许可

本项目随 v5 使用 **Downloader 3.1.2** 的 `net462/Downloader.dll`，用于多线程 HTTP 下载。该库不是本项目原创，来源如下：

- 上游源码：<https://github.com/bezzad/Downloader/tree/v3.1.2>
- NuGet：<https://www.nuget.org/packages/Downloader/3.1.2>
- 许可证：MIT，见本目录的 `LICENSE`（Copyright (c) 2021 Behzad Khosravifar）。
- 本项目使用的 DLL：`packages/Downloader.3.1.2/lib/net462/Downloader.dll`

固定包完整性（SHA-256）：

```text
Downloader.3.1.2.nupkg  886bc8ef5556719af3cf7c37b7e0937d58d3c3b00153ad26d1ee0533069d0d8d
Downloader.dll          c5d961ecaade6078a4024ad6c1ce4d463f686d1807ab0d2d5214ca762d8386ca
```

3.1.2 的 NuGet 包不带外部依赖。本项目仅引用其 `net462` 构建；Downloader 3.2 及更高版本不兼容本项目旧目标框架，不能无审查升级。3.1.2 默认会改变 TLS 证书策略；本项目适配层恢复系统 TLS 1.2 与证书校验，并限制并发。请同时分发本说明与 `LICENSE`。
