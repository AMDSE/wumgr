# Microsoft Update Catalog 最终验证

最终客户端使用微软当前网页内联脚本的 `p` 零基页码 GET 翻页，不使用会读错结果的旧式 ASP.NET POST 分页。页码参数来自第一方 `SiteConstants.aspx?6` 中 `QueryStringPageIndex = 'p'`。下载对话框 POST 采用包含 `size`、`updateID`、`uidInfo` 的对象数组。

真实 Linux/Mono HTTP 端到端验证成功，完整输出见 `live-validation.log`：

- 查询 `PCI\VEN_8086&DEV_15F3`，两页、50 个互不重复的更新；第一页首 ID `027e8668-e62d-411f-a4a8-7ad2a44d8d09`，第二页首 ID `83bcb1d8-7909-4b2e-8221-5c7ff028398f`。两页后仍有更多结果，因此明确 `Truncated=True`。
- 首项详情：硬件 ID 4 条；架构 AMD64；版本 2.1.5.10；提供商 Intel；产品 Windows 11 Client 24H2 及后续版本相关驱动。
- 下载对话框解析 1 个真实包链接，主机 `catalog.s.download.windowsupdate.com`，符合微软 HTTPS 白名单。没有执行包。

新增目录协议回归 18 项通过，覆盖真实搜索、原始精确字节数、嵌套 div 全部硬件 ID、产品/架构字段、身份错配、空结果与结构变化区分、真实下载对话框、多包与 JS 转义、非法链接、两页 GET、下载 POST 格式、后页失败保留、重定向、大小上限和预取消。完整源码位于两版 `tests/CatalogClientTests.cs`；网络探测位于 `tests/CatalogLiveProbe.cs`（不纳入默认离线 runner）。

这不是 Windows 实机验收，未验证真实 WUA COM/SetupAPI/管理员安装、Windows 代理策略或高 DPI。目录硬件匹配不等于操作系统适用性；目录来源仅允许下载，不能冒充 WUA 更新安装。
