using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using wumgr;
class CatalogLiveProbe
{
    static async Task Probe()
    {
        using(var cts=new CancellationTokenSource(TimeSpan.FromMinutes(3)))
        using(var client=new MicrosoftUpdateCatalogClient())
        {
            var result=await client.SearchAsync("PCI\\VEN_8086&DEV_15F3",2,cts.Token);
            Console.WriteLine("search pages="+result.PagesRead+" entries="+result.Entries.Count+" distinct="+result.Entries.Select(x=>x.Id).Distinct().Count()+" truncated="+result.Truncated);
            if(result.PagesRead!=2||result.Entries.Count!=50)throw new Exception("two-page live search did not produce 50 distinct entries");
            Console.WriteLine("page1-first="+result.Entries[0].Id+" page2-first="+result.Entries[25].Id);
            var detail=await client.GetDetailsAsync(result.Entries[0],cts.Token);
            Console.WriteLine("detail id="+detail.Id+" hardware="+detail.HardwareIds.Count+" architecture="+detail.Architecture+" version="+detail.Version+" provider="+detail.Provider+" products="+detail.Products);
            if(detail.HardwareIds.Count==0||String.IsNullOrWhiteSpace(detail.Architecture))throw new Exception("live detail incomplete");
            var urls=await client.GetDownloadUrlsAsync(detail.Id,cts.Token);
            Console.WriteLine("download-dialog packages="+urls.Count+" hosts="+String.Join(",",urls.Select(u=>new Uri(u).Host)));
            if(urls.Count==0||urls.Any(u=>!MicrosoftUpdateCatalogClient.IsTrustedDownloadUrl(u)))throw new Exception("live download policy failure");
            Console.WriteLine("Linux/Mono live HTTP protocol passed. No driver package executed; no Windows WUA installation tested.");
        }
    }
    public static int Main(){try{Probe().GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
