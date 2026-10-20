using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using wumgr;
public static class CatalogClientTests
{
    const string Id="027e8668-e62d-411f-a4a8-7ad2a44d8d09";
    static int count;
    static void Check(bool value,string message){if(!value)throw new Exception(message);count++;Console.WriteLine("通过："+message);}
    static bool Throws(Action action){try{action();return false;}catch(InvalidDataException){return true;}}
    sealed class Handler : HttpMessageHandler
    {
        public readonly List<string> Urls=new List<string>();public string Body;public bool Redirect,BrokenPage,Large;
        readonly string root;
        public Handler(string path){root=path;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Urls.Add(req.RequestUri.AbsoluteUri);
            if(Redirect){var r=new HttpResponseMessage(HttpStatusCode.Found){RequestMessage=req};r.Headers.Location=new Uri("https://example.invalid/steal");return r;}
            if(Large){var c=new StringContent("x");c.Headers.ContentLength=9*1024*1024;return new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=req,Content=c};}
            string file;
            if(req.RequestUri.AbsolutePath.EndsWith("DownloadDialog.aspx")){Body=await req.Content.ReadAsStringAsync();file="live-download-objects.html";}
            else if(req.RequestUri.Query.Contains("p=1")){if(BrokenPage)throw new IOException("page2 disconnected");file="search-page2.html";}
            else file="search.html";
            return new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=req,Content=new StringContent(File.ReadAllText(Path.Combine(root,file)))};
        }
    }
    static async Task Run(string root)
    {
        bool next;var rows=MicrosoftUpdateCatalogClient.ParseSearchPage(File.ReadAllText(Path.Combine(root,"search.html")),out next);
        Check(rows.Count==25&&next&&rows[0].Id==Id,"真实第一页解析25条及下一页");
        Check(rows[0].Size==386103,"使用原始精确386103字节而不是377KB四舍五入值");
        var detail=MicrosoftUpdateCatalogClient.ParseDetails(File.ReadAllText(Path.Combine(root,"live-details.html")),rows[0]);
        Check(detail.HardwareIds.Count==4&&detail.HardwareIds.Contains("pci\\ven_8086&dev_125c"),"平衡嵌套div完整读取四条真实硬件ID");
        Check(detail.Architecture=="AMD64"&&detail.Provider=="Intel"&&detail.Version=="2.1.5.10"&&detail.Products.Contains("Windows 11"),"真实详情的架构供应商版本产品不串字段");
        Check(detail.Title=="Intel Net Driver Update (2.1.5.10)","详情标题与更新身份对应");
        Check(Throws(()=>MicrosoftUpdateCatalogClient.ParseDetails(File.ReadAllText(Path.Combine(root,"live-details.html")),new CatalogEntry{Id=Guid.NewGuid().ToString()})),"详情身份错配拒绝");
        Check(Throws(()=>{bool n;MicrosoftUpdateCatalogClient.ParseSearchPage("<html><table><tr><td>Service unavailable</td></tr></table></html>",out n);}),"有HTML表格但无真实目录行也不能伪报空结果");
        Check(MicrosoftUpdateCatalogClient.ParseSearchPage(File.ReadAllText(Path.Combine(root,"search-empty.html")),out next).Count==0&&!next,"真实微软空结果页被正确区分");
        var urls=MicrosoftUpdateCatalogClient.ParseDownloadUrls(File.ReadAllText(Path.Combine(root,"live-download-objects.html")));
        Check(urls.Count==1&&MicrosoftUpdateCatalogClient.IsTrustedDownloadUrl(urls[0]),"真实DownloadDialog仅提取files.url包赋值");
        var escaped=MicrosoftUpdateCatalogClient.ParseDownloadUrls("x.files[0].url=\"https:\\/\\/download.windowsupdate.com\\/a.cab?x=1\\u0026y=2\"; x.files[1].url='https://download.microsoft.com/b.cab';");
        Check(escaped.Count==2&&escaped[0].Contains("x=1&y=2"),"双引号、多包、JS斜杠与Unicode转义正确解析");
        Check(Throws(()=>MicrosoftUpdateCatalogClient.ParseDownloadUrls("x.files[0].url='https://download.microsoft.com/a.cab'; x.files[1].url='https://evil.invalid/b.cab';")),"任一非法包链接导致明确失败而非悄悄漏包");
        var handler=new Handler(root);using(var client=new MicrosoftUpdateCatalogClient(handler))
        {
            var result=await client.SearchAsync("PCI\\VEN_8086&DEV_15F3",2,CancellationToken.None);
            Check(result.PagesRead==2&&result.Entries.Count==50&&result.Entries.Select(x=>x.Id).Distinct().Count()==50,"两页GET翻页解析50条互不重复更新");
            Check(handler.Urls[1].Contains("p=1")&&handler.Urls.All(x=>x.Contains("q=PCI")),"翻页保持硬件查询且使用真实零基页码参数");
            await client.GetDownloadUrlsAsync(Id,CancellationToken.None);
            var body=Uri.UnescapeDataString(handler.Body.Replace('+',' '));
            Check(body.Contains("\"updateID\":\""+Id+"\"")&&body.Contains("\"uidInfo\""),"下载POST采用真实对象数组而不是GUID字符串数组");
        }
        using(var client=new MicrosoftUpdateCatalogClient(new Handler(root){BrokenPage=true}))
        {
            var result=await client.SearchAsync("hardware",2,CancellationToken.None);
            Check(result.Entries.Count==25&&result.Truncated&&result.Diagnostics.Any(x=>x.Contains("page2 disconnected")),"后页断网保留前页结果并明确报告不完整");
        }
        var redir=new Handler(root){Redirect=true};using(var client=new MicrosoftUpdateCatalogClient(redir))
        {
            bool blocked=false;try{await client.SearchAsync("test",1,CancellationToken.None);}catch(InvalidOperationException){blocked=true;}
            Check(blocked&&redir.Urls.Count==1,"重定向到非微软目录源之前阻断");
        }
        using(var client=new MicrosoftUpdateCatalogClient(new Handler(root){Large=true}))
        {
            bool blocked=false;try{await client.SearchAsync("test",1,CancellationToken.None);}catch(InvalidDataException){blocked=true;}Check(blocked,"超大响应头立即拒绝");
        }
        using(var cts=new CancellationTokenSource())
        {
            cts.Cancel();var mock=new Handler(root);using(var client=new MicrosoftUpdateCatalogClient(mock))
            {bool canceled=false;try{await client.SearchAsync("test",1,cts.Token);}catch(OperationCanceledException){canceled=true;}Check(canceled&&mock.Urls.Count==0,"预取消不发HTTP请求");}
        }
        Console.WriteLine("目录协议测试完成："+count+" 项全部通过。");
    }
    public static int Main(string[] args){try{Run(args.Length>0?args[0]:"tests/fixtures/catalog").GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
