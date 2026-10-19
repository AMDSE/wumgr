using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using wumgr;

class ServiceTests
{
    static int passed;
    sealed class ImmediateProgress : IProgress<DriverDownloadProgress>
    {
        public Action<DriverDownloadProgress> Action;
        public void Report(DriverDownloadProgress value) { Action(value); }
    }
    static void Check(bool ok, string name) { if (!ok) throw new Exception("失败：" + name); passed++; Console.WriteLine("通过：" + name); }
    static DriverInfo Item(string id, params string[] urls) { return new DriverInfo { Id = id, Title = "测试驱动 - 1.2.3.4", Version = "1.2.3.4", DeviceModel = "测试设备型号", DownloadUrls = urls.ToList() }; }
    static async Task Respond(HttpListenerContext c)
    {
        try
        {
            string p = c.Request.Url.AbsolutePath;
            if (p == "/one.bin" || p == "/two.bin")
            {
                var b = Encoding.UTF8.GetBytes(p == "/one.bin" ? "one-new" : "two"); c.Response.ContentLength64 = b.Length;
                await c.Response.OutputStream.WriteAsync(b, 0, b.Length);
            }
            else if (p == "/empty") c.Response.ContentLength64 = 0;
            else if (p == "/short")
            {
                c.Response.ContentLength64 = 1024; await c.Response.OutputStream.WriteAsync(new byte[8], 0, 8);
            }
            else if (p == "/slow")
            {
                c.Response.ContentLength64 = 1024 * 1024;
                var first = new byte[65536]; await c.Response.OutputStream.WriteAsync(first, 0, first.Length); await c.Response.OutputStream.FlushAsync();
                await Task.Delay(800); await c.Response.OutputStream.WriteAsync(first, 0, first.Length);
            }
            else c.Response.StatusCode = 404;
        }
        catch (Exception) { /* 测试取消会主动关闭客户端连接。 */ }
        finally { try { c.Response.Close(); } catch { } }
    }
    static async Task ExpectFailure(DriverService svc, DriverInfo item, string root)
    {
        bool failed = false;
        try { await svc.DownloadAsync(new[] { item }, root, CancellationToken.None, null); } catch (AggregateException) { failed = true; }
        Check(failed && item.Status.StartsWith("失败："), item.Id);
    }
    static string SafeName(string id) { foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_'); using (var h = System.Security.Cryptography.SHA256.Create()) return id.Trim().Substring(0, Math.Min(80, id.Trim().Length)) + "_" + BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(id))).Replace("-", "").Substring(0, 12); }
    static async Task MainTask()
    {
        string root = Path.Combine(Path.GetTempPath(), "wumgr-service-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var allocator = new TcpListener(IPAddress.Loopback, 0); allocator.Start(); int port = ((IPEndPoint)allocator.LocalEndpoint).Port; allocator.Stop();
        var listener = new HttpListener(); listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start();
        var requests = new List<Task>();
        var server = Task.Run(async () => { while (listener.IsListening) { HttpListenerContext c; try { c = await listener.GetContextAsync(); } catch { break; } lock (requests) requests.Add(Respond(c)); } });
        try
        {
            var svc = new DriverService(); string url = "http://127.0.0.1:" + port;
            var d = Item("..\\escape/" + new string('x', 150), url + "/one.bin", url + "/two.bin");
            d.Superseded = true;
            int maxProgress = 0; int lastProgress = -1;
            var progress = new ImmediateProgress { Action = p => { CheckRange(p.Percent); maxProgress = Math.Max(maxProgress, p.Percent); lastProgress = p.Percent; } };
            await svc.DownloadAsync(new[] { d }, root, CancellationToken.None, progress); string dir = d.LocalPath;
            Check(d.Status == "已下载" && File.ReadAllText(Path.Combine(dir, "driver_01.bin")) == "one-new" && File.ReadAllText(Path.Combine(dir, "driver_02.bin")) == "two", "多文件下载内容完整且状态正确");
            string metadata = File.ReadAllText(Path.Combine(dir, "说明.txt"));
            Check(metadata.Contains("版本：1.2.3.4") && metadata.Contains("型号：测试设备型号") && metadata.Contains(d.Id), "说明文件保留版本、型号和原始标识");
            Check(metadata.Contains("WUA 已被取代：是"), "下载说明保留WUA历史候选标记");
            Check(maxProgress == 100 && lastProgress == 100, "进度不超过100且完成后为100");
            File.WriteAllText(Path.Combine(dir, "driver_01.bin"), "old"); await svc.DownloadAsync(new[] { d }, root, CancellationToken.None, null);
            Check(File.ReadAllText(Path.Combine(dir, "driver_01.bin")) == "one-new", "重下原子覆盖已完成文件");
            Check(Path.GetDirectoryName(dir) == root && Path.GetFileName(dir).Length < 100, "路径穿越和超长标识被隔离");
            var bad = Item("失败项", url + "/missing"); var good = Item("后续项", url + "/one.bin"); bool aggregated = false;
            try { await svc.DownloadAsync(new[] { bad, good }, root, CancellationToken.None, null); } catch (AggregateException) { aggregated = true; }
            Check(aggregated && bad.Status.StartsWith("失败：") && good.Status == "已下载", "HTTP404会汇总失败但继续后续驱动");
            await ExpectFailure(svc, Item("空响应失败", url + "/empty"), root);
            await ExpectFailure(svc, Item("截断响应失败", url + "/short"), root);
            await ExpectFailure(svc, Item("无地址失败"), root);
            await ExpectFailure(svc, Item("非HTTP地址失败", "file:///tmp/x"), root);
            await ExpectFailure(svc, Item("嵌入凭据地址失败", "http://user:pass@127.0.0.1/x"), root);
            var cancel = Item("取消项", url + "/slow"); bool sawPart = false, canceled = false;
            using (var cts = new CancellationTokenSource())
            {
                var during = new ImmediateProgress { Action = p => { if (p.Percent > 0) { sawPart = Directory.GetFiles(root, "*.part", SearchOption.AllDirectories).Length > 0; cts.Cancel(); } } };
                try { await svc.DownloadAsync(new[] { cancel }, root, cts.Token, during); } catch (OperationCanceledException) { canceled = true; }
            }
            string cancelDir = Path.Combine(root, SafeName(cancel.Id));
            Check(canceled && sawPart && cancel.Status == "已取消" && Directory.GetFiles(cancelDir, "*.part", SearchOption.TopDirectoryOnly).Length > 0 && Directory.GetFiles(cancelDir, "*.resume.xml", SearchOption.TopDirectoryOnly).Length == 0 && !File.Exists(Path.Combine(cancelDir, "driver_01.bin")), "无Range无验证器取消保留part且不创建正式文件");
            using (var between = new CancellationTokenSource())
            {
                var first = Item("批量取消首项", url + "/one.bin"); var next = Item("批量取消未开始项", url + "/two.bin"); bool batchCanceled = false;
                var atBoundary = new ImmediateProgress { Action = p => { if (p.Message.StartsWith("已下载驱动：")) between.Cancel(); } };
                try { await svc.DownloadAsync(new[] { first, next }, root, between.Token, atBoundary); } catch (OperationCanceledException) { batchCanceled = true; }
                Check(batchCanceled && first.Status == "已下载" && next.Status == "已取消" && String.IsNullOrEmpty(next.LocalPath), "批量项间取消保留已完成项并标记未开始项");
            }
            // 已存在的完整文件在重试失败时必须保留。
            d.DownloadUrls[0] = url + "/empty"; await ExpectFailure(svc, d, root);
            Check(File.ReadAllText(Path.Combine(dir, "driver_01.bin")) == "one-new", "失败重试不破坏原有完整文件");
            Check(DriverService.ExtractVersion("Intel - Net - 23.60.1.2") == "23.60.1.2" && DriverService.ExtractVersion("驱动版本 6.0.9700.1") == "6.0.9700.1", "从明确版本位置提取驱动号");
            Check(DriverService.ExtractVersion("Intel - 2024.01.02") == "未提供" && DriverService.ExtractVersion("Intel AX211 160MHz") == "未提供" && DriverService.ExtractVersion("KB5034441") == "未提供", "日期、型号和KB不冒充版本");
            string[] branches = DriverService.DriverSearchCriteria.Split(new[] { " or " }, StringSplitOptions.None);
            Check(branches.Length == 2 && branches.All(b => b.StartsWith("(Type='Driver'") && b.EndsWith(")") && b.Contains("IsInstalled=0") && b.Contains("IsHidden=0")), "WUA查询只在顶层OR且每支均限制为驱动");
            using (var cts = new CancellationTokenSource()) { cts.Cancel(); bool earlyCanceled = false; try { await svc.ScanAsync(cts.Token, null); } catch (OperationCanceledException) { earlyCanceled = true; } Check(earlyCanceled, "预取消扫描不创建Windows COM对象"); }
            using (var cts = new CancellationTokenSource()) { cts.Cancel(); bool canceledHistory = false; try { await svc.ScanAsync(true, cts.Token, null); } catch (OperationCanceledException) { canceledHistory = true; } Check(canceledHistory, "预取消历史版本扫描不创建Windows COM对象"); }
        }
        finally
        {
            listener.Stop(); listener.Close(); await server;
            Task[] pending; lock (requests) pending = requests.ToArray(); await Task.WhenAll(pending);
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine("服务测试完成：" + passed + " 项全部通过。");
    }
    static void CheckRange(int value) { if (value < 0 || value > 100) throw new Exception("进度超出范围"); }
    public static int Main() { try { MainTask().GetAwaiter().GetResult(); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; } }
}
