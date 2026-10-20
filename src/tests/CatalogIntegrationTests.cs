using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using wumgr;

public static class CatalogIntegrationTests
{
    static int passed;
    const string Id = "027e8668-e62d-411f-a4a8-7ad2a44d8d09";
    const string Hardware = "PCI\\VEN_8086&DEV_15F3&REV_01";
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("通过：" + message); passed++; }
    static LocalDeviceInfo Device(string hw = Hardware) { return new LocalDeviceInfo { Name="Intel Ethernet", Class="Net", InstanceId=hw+"\\1", HardwareIds=new List<string>{hw}, InstalledVersion="1.0.0.0" }; }
    static DriverInfo Wua(string id = Id, int revision = 5) { return new DriverInfo { Id=id, RevisionNumber=revision, HardwareId=Hardware, Version="2.0.0.0", DeviceModel="Intel Ethernet", DriverClass="Net", DownloadUrls=new List<string>() }; }
    static CatalogEntry Entry() { return new CatalogEntry { Id=Id, HardwareIds=new List<string>{Hardware}, Version="2.1.5.10", Title="Intel Net Driver Update", Architecture="AMD64", Products="Windows 11" }; }
    sealed class Replay : HttpMessageHandler
    {
        public int Requests, Details;
        public bool FailDetails;
        readonly string fixtures;
        public Replay(string path) { fixtures=path; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests++;
            string name;
            if (request.RequestUri.AbsolutePath.EndsWith("ScopedViewInline.aspx"))
            {
                Details++; if (FailDetails) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){RequestMessage=request}); name="live-details.html";
            }
            else if(request.RequestUri.AbsolutePath.EndsWith("DownloadDialog.aspx")) name="live-download-objects.html";
            else name="search.html";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request, Content=new StringContent(File.ReadAllText(Path.Combine(fixtures,name)).Replace("ctl00_catalogBody_nextPageLinkText", "disabled_next"))});
        }
    }
    static async Task Run(string fixtures)
    {
        var queryDevice=Device(); queryDevice.HardwareIds.Insert(0, Hardware+"&SUBSYS_00011111"); queryDevice.HardwareIds.Add("PCI\\VEN_8086&DEV_15F3");
        Check(DriverSearchCoordinator.HardwareQueries(queryDevice).First()=="PCI\\VEN_8086&DEV_15F3"&&DriverSearchCoordinator.HardwareQueries(queryDevice).Count==2,"优先查真实通用硬件ID并保留具体ID，避免仅查SUBSYS漏候选");
        var wua=Wua(); Check(wua.CanDownload,"有效 WUA UpdateID 无 URL 可延迟解析目录地址");
        Check(!Wua("not-a-guid").CanDownload,"无 URL 且无有效 UpdateID 时不能下载");
        var raw=DriverSearchCoordinator.ReadCatalog(Entry()); Check(raw!=null&&!raw.CatalogHardwareVerified&&!raw.CanDownload,"仅解析目录详情不能冒充本机硬件验证");
        var catalog=DriverSearchCoordinator.ReadCatalog(Entry(),new[]{Device()});
        Check(catalog.CatalogHardwareVerified&&catalog.CanDownload&&catalog.DownloadUrls.Count==0,"完整硬件匹配的目录项允许延迟解析真实下载地址");
        Check(!DriverSearchCoordinator.ReadCatalog(Entry(),new[]{Device("PCI\\VEN_8086&DEV_15F3")}).CatalogHardwareVerified,"完整 ID 不相等时不能靠前缀匹配");
        Check(DriverSearchCoordinator.ReadCatalog(new CatalogEntry{Id=Id})==null,"目录详情缺硬件 ID 时不创建候选");
        catalog.DownloadUrls.Add("https://download.windowsupdate.com.evil.test/a.cab"); Check(!catalog.CanDownload,"目录项的预填伪微软 URL 不能绕过门禁");
        catalog.DownloadUrls.Clear(); catalog.DownloadUrls.Add("https://download.windowsupdate.com/a.cab");
        Check(!DeviceDiscovery.SelectAutomatic(new[]{Device()},new[]{catalog}).Any(),"目录候选即使版本较新也不进入自动选择");
        Check(DeviceDiscovery.Match(Device(),new[]{catalog}).Count==1,"目录候选经完整硬件 ID 匹配可人工查看");
        var merged=DriverCatalog.NormalizeCandidates(new[]{catalog,wua});
        Check(merged.Count==1&&Object.ReferenceEquals(merged[0],wua)&&wua.RevisionNumber==5&&wua.DownloadUrls.Count==0,"跨来源同 UpdateID 保留 WUA 原对象与修订且不混入目录 URL");
        var newerCatalog=DriverSearchCoordinator.ReadCatalog(Entry(),new[]{Device()}); newerCatalog.RevisionNumber=100;
        Check(Object.ReferenceEquals(DriverCatalog.NormalizeCandidates(new[]{newerCatalog,wua})[0],wua),"目录修订不能替换已获系统验证的 WUA 身份");
        Check(DriverInstaller.ValidateDriver(raw)!=null,"未经硬件核验的目录项被安装入口拒绝");
        catalog.Status="已下载"; catalog.LocalPath=Path.GetTempPath();
        Check(DriverInstaller.ValidateDriver(catalog)!=null,"即使目录项已硬件核验并下载，也不能冒充WUA适用更新安装");
        Check(MicrosoftUpdateCatalogClient.IsTrustedDownloadUrl("https://download.windowsupdate.com/a.cab"),"微软 HTTPS 包域可用");
        foreach(var url in new[]{"http://download.windowsupdate.com/a.cab","https://download.windowsupdate.com.evil.test/a.cab","https://evil.test@download.windowsupdate.com/a.cab","https://download.windowsupdate.com:444/a.cab","https://download.windowsupdate.com/a.ps1"})
            Check(!MicrosoftUpdateCatalogClient.IsTrustedDownloadUrl(url),"拒绝不可信目录 URL："+url);
        var handler=new Replay(fixtures); var coordinator=new DriverSearchCoordinator((h,t,p)=>Task.FromResult(new List<DriverInfo>{wua}),()=>new MicrosoftUpdateCatalogClient(handler));
        var result=await coordinator.ScanWithCatalogAsync(new[]{Device()},false,CancellationToken.None,null);
        Check(result.Count==1&&handler.Requests==0,"WUA 已有候选且未开历史时不额外查目录");
        var failedHandler=new Replay(fixtures); var fallback=new DriverSearchCoordinator((h,t,p)=>{throw new IOException("WUA offline");},()=>new MicrosoftUpdateCatalogClient(failedHandler));
        var recovered=await fallback.ScanWithCatalogAsync(new[]{Device()},false,CancellationToken.None,null);
        Check(recovered.Any(x=>x.Source=="Microsoft Update Catalog"&&x.CatalogHardwareVerified),"WUA 失败后真实搜索/详情夹具恢复目录候选");
        Check(fallback.Partial&&fallback.LastDiagnostics.Any(x=>x.Contains("WUA offline")),"回退保留 WUA 失败诊断而非伪报完整成功");
        var partialHandler=new Replay(fixtures){FailDetails=true}; var partial=new DriverSearchCoordinator((h,t,p)=>Task.FromResult(new List<DriverInfo>{wua}),()=>new MicrosoftUpdateCatalogClient(partialHandler));
        var retained=await partial.ScanWithCatalogAsync(new[]{Device()},true,CancellationToken.None,null);
        Check(retained.Contains(wua)&&partial.Partial&&partial.LastDiagnostics.Any(x=>x.Contains("详情失败")),"目录详情失败保留已成功 WUA 结果并标记部分失败");
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel(); int calls=0; var canceled=new DriverSearchCoordinator((h,t,p)=>{calls++; return Task.FromResult(new List<DriverInfo>());},()=>new MicrosoftUpdateCatalogClient(new Replay(fixtures)));
            bool stopped=false;try{await canceled.ScanWithCatalogAsync(new[]{Device()},true,cancel.Token,null);}catch(OperationCanceledException){stopped=true;}
            Check(stopped&&calls==0,"预取消编排不创建 WUA 查询或网络请求");
        }
        var noHardware=new DriverSearchCoordinator((h,t,p)=>Task.FromResult(new List<DriverInfo>()),()=>{throw new Exception("should not connect");});
        Check((await noHardware.ScanWithCatalogAsync(new LocalDeviceInfo[0],true,CancellationToken.None,null)).Count==0&&noHardware.Partial,"没有具体硬件 ID 时不发网络并解释原因");
        Console.WriteLine("目录集成测试完成："+passed+" 项全部通过。");
    }
    public static int Main(string[] args){try{Run(args.Length>0?args[0]:"tests/fixtures/catalog").GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
