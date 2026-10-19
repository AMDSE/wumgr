using System;
using System.Collections.Generic;
using System.Linq;
using wumgr;

class CatalogTests
{
    static int passed;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("失败：" + name); passed++; Console.WriteLine("通过：" + name); }
    static DriverInfo D(string id, string hardware, string version, string model = "Model", string provider = "Provider", string driverClass = "Net", DateTime date = default(DateTime), string manufacturer = null)
    {
        return new DriverInfo {
            Id = id, HardwareId = hardware, Version = version, DeviceModel = model,
            Manufacturer = manufacturer ?? provider, Provider = provider, DriverClass = driverClass,
            DriverDate = date == default(DateTime) ? new DateTime(2024, 1, 1) : date,
            DownloadUrls = new List<string> { "https://example.invalid/" + id }
        };
    }
    public static int Main()
    {
        try
        {
            var samePayloadA = D("payload-a", "PCI\\VEN_PAY", "3.0.0.0");
            var samePayloadB = D("payload-b", "pci\\ven_pay", "3.0.0.0");
            samePayloadB.DownloadUrls = new List<string> { " HTTPS://EXAMPLE.invalid/payload-a " };
            var differentPackage = D("payload-c", "PCI\\VEN_PAY", "3.0.0.0");
            differentPackage.DownloadUrls = new List<string> { "https://example.invalid/other-package" };
            var oldRevision = D("revision-id", "PCI\\VEN_REV", "4.0.0.0");
            oldRevision.RevisionNumber = 2;
            oldRevision.DownloadUrls = new List<string> { "https://example.invalid/old.cab" };
            var newRevision = D("REVISION-ID", "pci\\ven_rev", "4.0.0.0");
            newRevision.RevisionNumber = 5;
            newRevision.DownloadUrls = new List<string> { "https://example.invalid/new.cab" };
            var historical = D("history", "PCI\\VEN_HIST", "2.0.0.0", date: new DateTime(2022, 1, 1));
            var current = D("current", "PCI\\VEN_HIST", "3.0.0.0", date: new DateTime(2024, 1, 1));
            var noIdA = D(null, "PCI\\VEN_NOID", "1.0.0.0");
            var noIdB = D(null, "PCI\\VEN_NOID", "1.0.0.0");
            var normalizedCandidates = DriverCatalog.NormalizeCandidates(new[] { samePayloadA, samePayloadB, differentPackage, oldRevision, newRevision, historical, current, noIdA, noIdB });
            Check(normalizedCandidates.Contains(samePayloadA) && !normalizedCandidates.Contains(samePayloadB), "相同规范URL载荷可跨更新ID合并且保留原实例");
            Check(normalizedCandidates.Contains(differentPackage), "同版本不同下载包必须保留");
            Check(normalizedCandidates.Contains(newRevision) && !normalizedCandidates.Contains(oldRevision) && !newRevision.DownloadUrls.Contains("https://example.invalid/old.cab"), "同更新ID选择最高revision且不混入旧revision载荷");
            Check(normalizedCandidates.Contains(historical) && normalizedCandidates.Contains(current), "不同历史版本必须保留");
            Check(normalizedCandidates.Contains(noIdA) && normalizedCandidates.Contains(noIdB), "无ID候选保守保留");

            var repeated = Enumerable.Range(0, 60).Select(i => D("duplicate-" + i, "PCI\\VEN_REPEAT", "2.0.0.0")).ToList();
            foreach (var r in repeated) r.DownloadUrls = new List<string> { "https://example.invalid/same.cab" };
            Check(DriverCatalog.NormalizeCandidates(repeated).Count == 1, "六十个不同更新ID的同一驱动载荷只显示一次");
            var emptyUrls = D("empty-urls", "PCI\\VEN_NULL", "1.0"); emptyUrls.DownloadUrls = null;
            Check(DriverCatalog.NormalizeCandidates(new[] { emptyUrls }).Single().DownloadUrls != null, "空下载地址列表规范为空集合避免界面崩溃");
            var noUrlNewRevision = D("REVISION-ID", "PCI\\VEN_NEW", "5.0"); noUrlNewRevision.RevisionNumber = 6; noUrlNewRevision.DownloadUrls.Clear();
            Check(DriverCatalog.NormalizeCandidates(new[] { oldRevision, newRevision, noUrlNewRevision }).Single() == noUrlNewRevision && noUrlNewRevision.DownloadUrls.Count == 0, "新修订即使硬件版本变化也替换旧修订，不能下载旧包冒充");
            var dated = D("other-date", "PCI\\VEN_REPEAT", "2.0.0.0", date: new DateTime(2025, 1, 1)); dated.DownloadUrls = repeated[0].DownloadUrls.ToList();
            Check(DriverCatalog.NormalizeCandidates(new[] { repeated[0], dated }).Count == 2, "同载荷但不同驱动日期不错误合并");

            var equivalentA = D("equiv-a", "PCI\\VEN_ABCD&DEV_0001; PCI\\VEN_EF01&DEV_0002; PCI\\VEN_ABCD&DEV_0001", "1.0.0.0");
            var equivalentB = D("equiv-b", " pci\\ven_ef01&dev_0002 ; pci\\ven_abcd&dev_0001 ", "2.0.0.0");
            var numeric10 = D("v10", equivalentA.HardwareId, "10.0.0.0");
            var numeric2 = D("v2", equivalentA.HardwareId, "2.0.0.0");
            var numeric19 = D("v19", equivalentA.HardwareId, "1.9.0.0");
            var missingVersion = D("missing-version", equivalentA.HardwareId, "未提供", date: new DateTime(2020, 1, 1));
            var duplicate = D("EQUIV-B", equivalentA.HardwareId, "9.0.0.0");
            var differentModel = D("different-model", equivalentA.HardwareId, "8.0.0.0", "Other Model");
            var differentProvider = D("different-provider", equivalentA.HardwareId, "7.0.0.0", provider: "Other Provider");
            var differentClass = D("different-class", equivalentA.HardwareId, "6.0.0.0", driverClass: "Display");
            var differentManufacturer = D("different-manufacturer", equivalentA.HardwareId, "5.0.0.0", manufacturer: "Other Manufacturer");
            var noHardware1 = D("no-hardware-1", "未提供", "3.0.0.0");
            var noHardware2 = D("no-hardware-2", "", "4.0.0.0");
            var noHardware3 = D("no-hardware-3", null, "5.0.0.0");
            var groups = DriverCatalog.Group(new[] { equivalentA, equivalentB, numeric10, numeric2, numeric19, missingVersion, duplicate, differentModel, differentProvider, differentClass, differentManufacturer, noHardware1, noHardware2, noHardware3 });

            Check(groups.Count == 8, "等价硬件集合归并且精确元数据差异拆分组");
            Check(groups.Select(g => g.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == groups.Count, "每个设备组Key唯一");
            var normalized = groups.Single(g => g.Versions.Any(v => v.Id == "equiv-a"));
            Check(normalized.Versions.Any(v => v.Id == "equiv-b"), "硬件集合顺序、大小写、重复项规范化后归并");
            Check(normalized.Versions.Count(v => String.Equals(v.Id, "EQUIV-B", StringComparison.OrdinalIgnoreCase)) == 1, "同更新ID即使元数据冲突也只显示一次");
            Check(normalized.Versions[0].Id == "v10" && normalized.Versions.Any(v => v.Id == "v2") && normalized.Versions.Any(v => v.Id == "v19") && normalized.Versions.Last().Id == "missing-version" && normalized.Versions.IndexOf(normalized.Versions.Single(v => v.Id == "v2")) < normalized.Versions.IndexOf(normalized.Versions.Single(v => v.Id == "v19")), "版本按数字语义降序且缺失版本置后");
            Check(groups.Count(g => g.Versions.Any(v => v.Id == "different-model")) == 1 && groups.Count(g => g.Versions.Any(v => v.Id == "different-provider")) == 1 && groups.Count(g => g.Versions.Any(v => v.Id == "different-class")) == 1 && groups.Count(g => g.Versions.Any(v => v.Id == "different-manufacturer")) == 1, "型号、厂商、提供商、类别任一差异不会误合并");
            Check(groups.Count(g => g.Versions.Count == 1 && g.Versions[0].Id.StartsWith("no-hardware-")) == 3, "缺失硬件ID的候选各自独立且不会丢失");
            Check(groups.All(g => g.Latest == g.Versions[0]), "Latest始终对应排序后的首个版本");
            Console.WriteLine("目录测试完成：" + passed + " 项全部通过。"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
