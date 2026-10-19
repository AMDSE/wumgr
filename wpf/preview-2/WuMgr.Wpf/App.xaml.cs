using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WuMgr.Wpf.Services;
using WuMgr.Wpf.ViewModels;

namespace WuMgr.Wpf
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher));
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(args.Exception.Message, "WuMgr WPF", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            string preview = Argument(e.Args, "--preview");
            bool demo = e.Args.Contains("--demo") || preview != null;
            bool english = WpfInteraction.LoadEnglish();
            string language = Argument(e.Args, "--lang");
            if (language != null)
            {
                if (language != "en-US" && language != "zh-CN") { MessageBox.Show("Supported --lang values: zh-CN / en-US"); Shutdown(2); return; }
                english = language == "en-US";
            }
            var vm = new MainViewModel(new DriverBackend(), new WpfInteraction(), demo, english);
            var window = new MainWindow { DataContext = vm };
            MainWindow = window;
            window.Show();
            try
            {
                await vm.InitializeAsync();
                if (preview != null)
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    await Task.Delay(250);
                    window.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    string full = Path.GetFullPath(preview); Directory.CreateDirectory(Path.GetDirectoryName(full));
                    using (var stream = File.Create(full)) encoder.Save(stream);
                    window.Close();
                }
            }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "WuMgr WPF", MessageBoxButton.OK, MessageBoxImage.Error); if (preview != null) Shutdown(1); }
        }
        private static string Argument(string[] args, string name)
        {
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }
    }
}
