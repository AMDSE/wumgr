using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace wumgr
{
    public sealed class CatalogEntry
    {
        public string Id, Title, Products, Classification, Version, Manufacturer, Provider, DriverClass, Architecture, Model;
        public DateTime DriverDate;
        public decimal Size; // bytes, never rounded display MB
        public List<string> HardwareIds = new List<string>();
        public string DetailUrl;
    }
    public sealed class CatalogSearchResult
    {
        public List<CatalogEntry> Entries = new List<CatalogEntry>();
        public List<string> Diagnostics = new List<string>();
        public bool Truncated;
        public int PagesRead;
    }
    /// <summary>Read-only Microsoft Catalog adapter. HTML changes fail explicitly; no scripts execute.</summary>
    public sealed class MicrosoftUpdateCatalogClient : IDisposable
    {
        public const string CatalogHost = "https://www.catalog.update.microsoft.com";
        const int MaxBytes = 8 * 1024 * 1024;
        static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
        readonly HttpClient client;
        public MicrosoftUpdateCatalogClient() : this(new HttpClientHandler { UseCookies=true, CookieContainer=new CookieContainer(), AllowAutoRedirect=false }) { }
        public MicrosoftUpdateCatalogClient(HttpMessageHandler handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            var http = handler as HttpClientHandler; if (http != null) http.AllowAutoRedirect=false;
            client=new HttpClient(handler, true) { Timeout=Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WuMgr-Catalog/1.1");
        }
        public async Task<CatalogSearchResult> SearchAsync(string query, int maxPages, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (String.IsNullOrWhiteSpace(query) || query.Length>300) throw new ArgumentException("Invalid Catalog query.","query");
            if(maxPages<1) throw new ArgumentOutOfRangeException("maxPages");
            maxPages=Math.Min(maxPages,40); // Catalog exposes at most 1,000 results at 25/page.
            var result=new CatalogSearchResult(); var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var pages=new HashSet<string>();
            for(int page=0;page<maxPages;page++)
            {
                token.ThrowIfCancellationRequested();
                // Current first-party handlePageChange uses QueryStringPageIndex (p), zero-based.
                string url=CatalogHost+"/Search.aspx?q="+Uri.EscapeDataString(query)+(page==0?"":"&p="+page.ToString(CultureInfo.InvariantCulture));
                try
                {
                    string html=await RequestTextAsync(HttpMethod.Get,new Uri(url),null,token).ConfigureAwait(false);
                    bool next; var entries=ParseSearchPage(html,out next);
                    string fingerprint=String.Join("|",entries.Select(x=>x.Id));
                    if(!pages.Add(fingerprint)) { result.Truncated=true; result.Diagnostics.Add("目录重复返回同一页，已停止翻页。"); return result; }
                    result.PagesRead++;
                    foreach(var entry in entries) if(ids.Add(entry.Id)) result.Entries.Add(entry);
                    bool capped=RxMatch(Clean(html),@"(?:over|more than|first|exceed\w*)\s+(?:1,?000|1000)\b").Success;
                    if(capped) { result.Truncated=true; if(!result.Diagnostics.Contains("目录服务最多展示1,000条匹配结果。")) result.Diagnostics.Add("目录服务最多展示1,000条匹配结果。"); }
                    if(!next) return result;
                    if(page+1>=maxPages) { result.Truncated=true; return result; }
                }
                catch(OperationCanceledException) { throw; }
                catch(Exception ex)
                {
                    if(result.PagesRead==0) throw;
                    result.Truncated=true; result.Diagnostics.Add("目录后续页失败，已保留前页："+ex.Message); return result;
                }
            }
            return result;
        }
        public async Task<CatalogEntry> GetDetailsAsync(CatalogEntry entry,CancellationToken token)
        {
            Guid id; if(entry==null||!Guid.TryParse(entry.Id,out id)) throw new ArgumentException("Invalid Catalog update ID.","entry");
            string url=CatalogHost+"/ScopedViewInline.aspx?updateid="+id.ToString("D");
            var detail=ParseDetails(await RequestTextAsync(HttpMethod.Get,new Uri(url),null,token).ConfigureAwait(false),entry);
            detail.DetailUrl=url; return detail;
        }
        public async Task<List<string>> GetDownloadUrlsAsync(string updateId,CancellationToken token)
        {
            Guid id; if(!Guid.TryParse(updateId,out id)) throw new ArgumentException("Invalid Catalog update ID.","updateId");
            string canonical=id.ToString("D");
            var fields=new Dictionary<string,string>{{"updateIDs","[{\"size\":0,\"updateID\":\""+canonical+"\",\"uidInfo\":\""+canonical+"\"}]"}};
            return ParseDownloadUrls(await RequestTextAsync(HttpMethod.Post,new Uri(CatalogHost+"/DownloadDialog.aspx"),fields,token).ConfigureAwait(false));
        }
        async Task<string> RequestTextAsync(HttpMethod method,Uri uri,IDictionary<string,string> fields,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    for(int redirects=0;redirects<=3;redirects++)
                    {
                        ValidateCatalogUri(uri);
                        using(var request=new HttpRequestMessage(method,uri))
                        {
                            if(fields!=null) request.Content=new FormUrlEncodedContent(fields);
                            using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false))
                            {
                                if(response.RequestMessage!=null) ValidateCatalogUri(response.RequestMessage.RequestUri);
                                int status=(int)response.StatusCode;
                                if(status>=300&&status<400)
                                {
                                    if(response.Headers.Location==null||redirects==3) throw new InvalidDataException("Unexpected Catalog redirect.");
                                    uri=new Uri(uri,response.Headers.Location); ValidateCatalogUri(uri);
                                    if(status==303 || ((status==301||status==302)&&method==HttpMethod.Post)) { method=HttpMethod.Get;fields=null; }
                                    continue;
                                }
                                response.EnsureSuccessStatusCode();
                                if(response.Content.Headers.ContentLength>MaxBytes) throw new InvalidDataException("Catalog response exceeds 8 MiB.");
                                using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                using(var abort=deadline.Token.Register(()=>stream.Dispose()))
                                using(var buffer=new MemoryStream())
                                {
                                    byte[] bytes=new byte[32768]; int read;
                                    while((read=await stream.ReadAsync(bytes,0,bytes.Length,deadline.Token).ConfigureAwait(false))>0)
                                    {
                                        if(buffer.Length+read>MaxBytes) throw new InvalidDataException("Catalog response exceeds 8 MiB.");
                                        buffer.Write(bytes,0,read);
                                    }
                                    deadline.Token.ThrowIfCancellationRequested();
                                    return Encoding.UTF8.GetString(buffer.ToArray());
                                }
                            }
                        }
                    }
                    throw new InvalidDataException("Catalog redirect limit exceeded.");
                }
                catch(Exception) when(deadline.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested(); throw new TimeoutException("Microsoft Update Catalog request exceeded 30 seconds.");
                }
            }
        }
        static void ValidateCatalogUri(Uri uri)
        {
            if(uri==null||uri.Scheme!="https"||!String.Equals(uri.Host,"www.catalog.update.microsoft.com",StringComparison.OrdinalIgnoreCase)||uri.Port!=443||!String.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("Only the official Microsoft Update Catalog HTTPS origin is allowed.");
        }
        public static List<CatalogEntry> ParseSearchPage(string html,out bool hasNext)
        {
            if(String.IsNullOrWhiteSpace(html)) throw new InvalidDataException("Empty Catalog search page.");
            hasNext=false; var list=new List<CatalogEntry>();
            foreach(Match row in RxMatches(html,"<tr\\b[^>]*\\bid\\s*=\\s*['\"](?<id>[0-9a-f-]{36})_R\\d+['\"][^>]*>(?<v>.*?)</tr>"))
            {
                Guid id; if(!Guid.TryParse(row.Groups["id"].Value,out id)) continue;
                var cells=RxMatches(row.Groups["v"].Value,"<td\\b[^>]*>(?<v>.*?)</td>");
                if(cells.Count<7) throw new InvalidDataException("Catalog result row changed its column structure.");
                string sizeCell=cells[6].Groups["v"].Value;
                string original=ById(sizeCell,id.ToString("D")+"_originalSize"); decimal bytes;
                if(!Decimal.TryParse(original,NumberStyles.Integer,CultureInfo.InvariantCulture,out bytes)) bytes=ParseSize(Clean(sizeCell));
                list.Add(new CatalogEntry{Id=id.ToString("D"),Title=Clean(cells[1].Groups["v"].Value),Products=Clean(cells[2].Groups["v"].Value),Classification=Clean(cells[3].Groups["v"].Value),DriverDate=ParseDate(Clean(cells[4].Groups["v"].Value)),Version=Clean(cells[5].Groups["v"].Value),Size=bytes,DetailUrl=CatalogHost+"/ScopedViewInline.aspx?updateid="+id.ToString("D")});
            }
            if(list.Count==0)
            {
                string text=Clean(html);
                if(!RxMatch(text,@"(?:did not find any results|no (?:matching )?(?:results|updates)(?: were found)?|no results found)").Success)
                    throw new InvalidDataException("Catalog search structure is unrecognized; this is not a no-update result.");
                return list;
            }
            foreach(Match link in RxMatches(html,@"<a\b[^>]*>"))
            {
                var a=Attributes(link.Value); string id; if(!a.TryGetValue("id",out id)||id!="ctl00_catalogBody_nextPageLinkText") continue;
                string href,disabled; hasNext=a.TryGetValue("href",out href)&&!String.IsNullOrEmpty(href)&&!a.ContainsKey("disabled")&&(!a.TryGetValue("aria-disabled",out disabled)||disabled!="true");
            }
            return list;
        }
        public static CatalogEntry ParseDetails(string html,CatalogEntry seed=null)
        {
            if(String.IsNullOrWhiteSpace(html)) throw new InvalidDataException("Empty Catalog detail page.");
            string pageId=ById(html,"ScopedViewHandler_UpdateID"); Guid id,expected;
            if(!Guid.TryParse(pageId,out id)) throw new InvalidDataException("Catalog detail page has no valid update identity.");
            if(seed!=null&&(!Guid.TryParse(seed.Id,out expected)||expected!=id)) throw new InvalidDataException("Catalog detail identity differs from requested update.");
            var entry=new CatalogEntry{Id=id.ToString("D"),Title=ById(html,"ScopedViewHandler_titleText")??(seed==null?null:seed.Title),Products=seed==null?null:seed.Products,Classification=seed==null?null:seed.Classification,Version=seed==null?null:seed.Version,DriverDate=seed==null?DateTime.MinValue:seed.DriverDate,Size=seed==null?0:seed.Size,DetailUrl=seed==null?null:seed.DetailUrl};
            entry.Manufacturer=ById(html,"ScopedViewHandler_manufacturer"); entry.Provider=ById(html,"ScopedViewHandler_driverProvider"); entry.DriverClass=ById(html,"ScopedViewHandler_driverClass"); entry.Model=ById(html,"ScopedViewHandler_driverModel");
            entry.Version=ById(html,"ScopedViewHandler_version")??entry.Version;
            DateTime date=ParseDate(ById(html,"ScopedViewHandler_versionDate")); if(date!=DateTime.MinValue) entry.DriverDate=date;
            entry.Architecture=RemoveLabel(ById(html,"archDiv"),"Architecture:");
            entry.Products=RemoveLabel(ById(html,"productsDiv"),"Supported products:")??entry.Products;
            string hardware=ById(html,"driverhwIDs"); if(hardware==null) throw new InvalidDataException("Catalog detail hardware-ID container is unrecognized.");
            entry.HardwareIds=RxMatches(hardware,@"\b[A-Z][A-Z0-9_]*\\[A-Z0-9_&.*{}\-]+") .Cast<Match>().Select(m=>m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return entry;
        }
        public static List<string> ParseDownloadUrls(string html)
        {
            if(String.IsNullOrWhiteSpace(html)) throw new InvalidDataException("Empty Catalog download dialog.");
            var urls=new List<string>();
            foreach(Match m in RxMatches(html,"files\\s*\\[\\s*\\d+\\s*\\]\\s*\\.url\\s*=\\s*(?<q>['\"])(?<v>.*?)\\k<q>"))
            {
                string decoded=DecodeJs(m.Groups["v"].Value);
                if(!IsTrustedDownloadUrl(decoded)) throw new InvalidDataException("Catalog dialog contains an untrusted package URL; refusing incomplete download.");
                string url=new Uri(decoded).AbsoluteUri; if(!urls.Contains(url,StringComparer.OrdinalIgnoreCase)) urls.Add(url);
            }
            if(urls.Count==0) throw new InvalidDataException("Catalog dialog has no recognizable official package URLs.");
            return urls;
        }
        public static bool IsTrustedDownloadUrl(string url)
        {
            Uri u; if(!Uri.TryCreate(url,UriKind.Absolute,out u)) return false;
            string h=u.Host; bool host=String.Equals(h,"download.windowsupdate.com",StringComparison.OrdinalIgnoreCase)||h.EndsWith(".download.windowsupdate.com",StringComparison.OrdinalIgnoreCase)||String.Equals(h,"download.microsoft.com",StringComparison.OrdinalIgnoreCase)||h.EndsWith(".download.microsoft.com",StringComparison.OrdinalIgnoreCase);
            string ext=Path.GetExtension(u.AbsolutePath).ToLowerInvariant();
            return u.Scheme=="https"&&host&&u.Port==443&&String.IsNullOrEmpty(u.UserInfo)&&(ext==".cab"||ext==".msu"||ext==".exe"||ext==".zip");
        }
        static string DecodeJs(string s)
        {
            return WebUtility.HtmlDecode(Regex.Replace(s,@"\\(?:u(?<u>[0-9a-f]{4})|x(?<x>[0-9a-f]{2})|(?<slash>/))",m=>m.Groups["u"].Success?((char)Convert.ToInt32(m.Groups["u"].Value,16)).ToString():m.Groups["x"].Success?((char)Convert.ToInt32(m.Groups["x"].Value,16)).ToString():"/",RegexOptions.IgnoreCase,RegexTimeout));
        }
        static string RemoveLabel(string text,string label){return text==null?null:(text.StartsWith(label,StringComparison.OrdinalIgnoreCase)?text.Substring(label.Length).Trim():text);}
        static string ById(string html,string id)
        {
            // Balance same-tag nesting; hardware/product containers contain child DIV/SPAN elements.
            var opening=RxMatch(html,"<(?<tag>[a-z][a-z0-9]*)\\b(?=[^>]*\\bid\\s*=\\s*['\"]"+Regex.Escape(id)+"['\"])[^>]*>");
            if(!opening.Success) return null;
            string tag=opening.Groups["tag"].Value; int depth=1,start=opening.Index+opening.Length;
            foreach(Match match in RxMatches(html.Substring(start),"<(?<close>/?)"+Regex.Escape(tag)+"\\b[^>]*>"))
            {
                depth+=match.Groups["close"].Length>0?-1:1;
                if(depth==0) return Clean(html.Substring(start,match.Index));
            }
            throw new InvalidDataException("Catalog HTML element is not balanced: "+id);
        }
        static string Clean(string html)
        {
            string s=Regex.Replace(html??"",@"<(script|style)\b[^>]*>.*?</\1>"," ",RegexOptions.IgnoreCase|RegexOptions.Singleline,RegexTimeout);
            s=Regex.Replace(s,"<[^>]+>"," ",RegexOptions.Singleline,RegexTimeout);
            return Regex.Replace(WebUtility.HtmlDecode(s),@"\s+"," ",RegexOptions.None,RegexTimeout).Trim();
        }
        static Dictionary<string,string> Attributes(string html)
        {
            var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(Match m in RxMatches(html,"(?<n>[a-z_:][-a-z0-9_:.]*)\\s*=\\s*(?<q>['\"])(?<v>.*?)\\k<q>")) result[m.Groups["n"].Value]=WebUtility.HtmlDecode(m.Groups["v"].Value);
            return result;
        }
        static Match RxMatch(string s,string pattern){return Regex.Match(s??"",pattern,RegexOptions.IgnoreCase|RegexOptions.Singleline,RegexTimeout);}
        static MatchCollection RxMatches(string s,string pattern){return Regex.Matches(s??"",pattern,RegexOptions.IgnoreCase|RegexOptions.Singleline,RegexTimeout);}
        static DateTime ParseDate(string s){DateTime d;return DateTime.TryParse(s,CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out d)?d:DateTime.MinValue;}
        static decimal ParseSize(string s)
        {
            Match m=RxMatch(s,@"([0-9]+(?:[.,][0-9]+)?)\s*(KB|MB|GB|B)\b"); decimal n;
            if(!m.Success||!Decimal.TryParse(m.Groups[1].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out n)) return 0;
            switch(m.Groups[2].Value.ToUpperInvariant()){case "KB":return n*1024;case "MB":return n*1048576;case "GB":return n*1073741824;default:return n;}
        }
        public void Dispose(){client.Dispose();}
    }
}
