using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WuMgr.Wpf;
using WuMgr.Wpf.Services;
using WuMgr.Wpf.ViewModels;
using WuMgr.Wpf.Views;

sealed class BindingErrors : TraceListener
{
    public readonly List<string> Errors = new List<string>();
    public override void Write(string message) { }
    public override void WriteLine(string message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
}
static class Program
{
    static int assertions;
    static string directory;
    static BindingErrors bindingErrors;
    static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        assertions++;
        Console.WriteLine("PASS " + description);
    }
    [STAThread]
    public static int Main(string[] args)
    {
        directory = Path.GetFullPath(args.Length == 0 ? "wpf-smoke-results" : args[0]);
        Directory.CreateDirectory(directory);
        var app = new Application();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        bindingErrors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        var vm = new MainViewModel(new DriverBackend(), new SafeInteraction(), true, false);
        var window = new MainWindow { DataContext = vm };
        int exit = 1;
        window.Loaded += async (s, e) =>
        {
            try
            {
                await vm.InitializeAsync();
                await RenderReady(window);
                Check(vm.IsDemo && !vm.IsBusy, "demo initialization is safe and completes");
                Check(vm.DeviceCount > 0, "default common hardware is visible");
                Check(!vm.ScanCommand.CanExecute(null), "real update scan disabled in demo");
                var search = (TextBox)window.FindName("SearchBox");
                search.Text = "__no_such_device__";
                await Task.Delay(300);
                Check(vm.DeviceCount == 0, "actual TextBox updates the filtered inventory");
                search.Clear();
                await Task.Delay(300);
                Check(vm.DeviceCount > 0, "clearing actual TextBox restores inventory");
                var row = vm.Devices.First(x => x.CanSelect);
                var checkBox = Descendants<CheckBox>(window).Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == row.Name);
                checkBox.IsChecked = true;
                row.IsExpanded = true;
                await RenderReady(window);
                Check(vm.SelectedCount == 1, "device selection updates the VM count");
                var versions = Descendants<ListBox>(window).Single(x => ReferenceEquals(x.ItemsSource, row.Versions));
                versions.SelectedItem = row.Versions.Last();
                await RenderReady(window);
                Check(ReferenceEquals(row.SelectedVersion, row.Versions.Last()), "actual version ListBox updates selected candidate");
                Check(!vm.DownloadCommand.CanExecute(null), "demo download stays disabled after selecting a version");
                foreach (bool english in new[] { false, true })
                {
                    vm.IsEnglish = english;
                    await RenderReady(window);
                    Check(window.Title == vm.Title, "window title responds to language change");
                    foreach (bool compact in new[] { false, true })
                    {
                        window.Width = compact ? 1024 : 1280;
                        window.Height = compact ? 680 : 860;
                        await RenderReady(window);
                        CheckFooter(window, vm);
                        string name = (english ? "en-US" : "zh-CN") + (compact ? "-compact" : "-wide");
                        Capture(window, name + ".png");
                    }
                }
                var details = new DeviceDetailsWindow(row, true) { Owner = window };
                details.Show();
                await RenderReady(details);
                Capture(details, "details-en-US.png");
                details.Close();
                Check(bindingErrors.Errors.Count == 0, "no runtime WPF data binding errors");
                File.WriteAllText(Path.Combine(directory, "RESULT.txt"), "PASS\nAssertions: " + assertions + "\nSafe demo only. No device/WUA/download/install/policy operations performed.\nReal device and DPI acceptance still required.\n");
                exit = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                File.WriteAllText(Path.Combine(directory, "RESULT.txt"), "FAIL\n" + ex + "\n" + string.Join("\n", bindingErrors.Errors));
            }
            finally
            {
                vm.Dispose();
                window.Close();
            }
        };
        app.Run(window);
        return exit;
    }
    static async Task RenderReady(Window window)
    {
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        await Task.Delay(120);
    }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            T match = child as T;
            if (match != null) yield return match;
            foreach (T nested in Descendants<T>(child)) yield return nested;
        }
    }
    static void CheckFooter(Window window, MainViewModel vm)
    {
        foreach (string caption in new[] { vm["Download"], vm["Cancel"], vm["Browse"], vm["OpenFolder"] })
        {
            Button button = Descendants<Button>(window).Single(b => (b.Content as string) == caption);
            Point point = button.TranslatePoint(new Point(0, 0), window);
            Check(button.ActualWidth > 0 && point.X >= 0 && point.Y >= 0 && point.X + button.ActualWidth <= window.ActualWidth && point.Y + button.ActualHeight <= window.ActualHeight, "footer button in window bounds: " + caption);
        }
    }
    static void Capture(Window window, string name)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(Path.Combine(directory, name))) png.Save(file);
    }
    sealed class SafeInteraction : IInteraction
    {
        public bool Confirm(string title, string message) { throw new InvalidOperationException("Unexpected confirmation in demo"); }
        public void ShowMessage(string title, string message) { throw new InvalidOperationException(message); }
        public string ChooseFolder(string path) { throw new InvalidOperationException("Unexpected folder chooser in demo"); }
        public void OpenFolder(string path) { throw new InvalidOperationException("Unexpected shell action in demo"); }
        public void ShowDetails(DeviceRowViewModel row) { }
        public void SaveLanguage(bool english) { }
    }
}
