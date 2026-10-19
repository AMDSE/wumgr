using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace wumgr
{
    static class DriverProgram
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread]
        static int Main(string[] args)
        {
            bool demo = Array.IndexOf(args, "--demo") >= 0;
            UiLanguage lang = UiLanguageService.Load();
            int langIndex = Array.IndexOf(args, "--lang");
            if (langIndex >= 0)
            {
                if (langIndex + 1 >= args.Length || !(args[langIndex + 1].Equals("en-US", StringComparison.OrdinalIgnoreCase) || args[langIndex + 1].Equals("zh-CN", StringComparison.OrdinalIgnoreCase)))
                { Console.Error.WriteLine("--lang: use zh-CN or en-US."); return 2; }
                lang = args[langIndex + 1].Equals("en-US", StringComparison.OrdinalIgnoreCase) ? UiLanguage.EnUS : UiLanguage.ZhCN;
            }
            UiLanguageService.Set(lang, false);
            int previewIndex = Array.IndexOf(args, "--preview");
            if (previewIndex >= 0 && previewIndex + 1 >= args.Length) { Console.Error.WriteLine(UiLanguageService.Get("请在 --preview 后指定图片路径。")); return 2; }
            if (previewIndex >= 0) demo = true;
            bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(lang == UiLanguage.EnUS ? "en-US" : "zh-CN");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(lang == UiLanguage.EnUS ? "en-US" : "zh-CN");
            if (windows) { try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { } }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => MessageBox.Show(UiLanguageService.Get("程序遇到异常：\r\n") + UiLanguageService.Display(e.Exception.Message), UiLanguageService.Get("驱动下载中心"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (!windows && !demo) { MessageBox.Show(UiLanguageService.Get("驱动扫描需要 Windows 10 / 11 和 Windows 更新服务。\r\n当前系统仅支持 --demo 演示模式。"), UiLanguageService.Get("系统不受支持"), MessageBoxButtons.OK, MessageBoxIcon.Information); return 1; }
            using (var form = new DriverForm(demo))
            {
                if (previewIndex >= 0)
                {
                    string path = Path.GetFullPath(args[previewIndex + 1]); Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var timer = new System.Windows.Forms.Timer { Interval = 800 };
                    timer.Tick += (s, e) => { timer.Stop(); form.SavePreview(path); form.Close(); timer.Dispose(); };
                    form.Shown += (s, e) => timer.Start();
                }
                Application.Run(form);
            }
            return 0;
        }
    }
}
