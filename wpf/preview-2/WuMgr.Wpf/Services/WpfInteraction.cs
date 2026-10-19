using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WuMgr.Wpf.ViewModels;
using WuMgr.Wpf.Views;

namespace WuMgr.Wpf.Services
{
    public sealed class WpfInteraction : IInteraction
    {
        private Window Owner { get { return Application.Current.MainWindow; } }
        public bool Confirm(string title, string message) { return MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes; }
        public void ShowMessage(string title, string message) { MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information); }
        public string ChooseFolder(string current) { return NativeFolderPicker.Pick(Owner, current); }
        public void OpenFolder(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new DirectoryNotFoundException(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetFullPath(path) + "\"") { UseShellExecute = true });
        }
        public void ShowDetails(DeviceRowViewModel row) { var vm = Owner.DataContext as MainViewModel; new DeviceDetailsWindow(row, vm != null && vm.IsEnglish) { Owner = Owner }.ShowDialog(); }
        public void SaveLanguage(bool english)
        {
            var path = LanguagePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, english ? "en-US" : "zh-CN");
        }
        public static string LanguagePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WuMgr", "language.txt"); } }
        public static bool LoadEnglish() { try { return File.Exists(LanguagePath) && File.ReadAllText(LanguagePath).Trim().Equals("en-US", StringComparison.OrdinalIgnoreCase); } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } }
    }

    // Native Windows Common Item Dialog. No Windows Forms dependency or interop host.
    internal static class NativeFolderPicker
    {
        public static string Pick(Window owner, string current)
        {
            object instance = null; IShellItem item = null, initial = null;
            try
            {
                instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")));
                var dialog = (IFileDialog)instance;
                dialog.SetOptions(0x20 | 0x40 | 0x800 | 0x02000000); // folders, filesystem, existing path, don't add recent
                dialog.SetTitle("选择驱动下载目录 / Choose driver download folder");
                if (Directory.Exists(current))
                {
                    Guid iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(Path.GetFullPath(current), IntPtr.Zero, ref iid, out initial) == 0) dialog.SetFolder(initial);
                }
                int hr = dialog.Show(owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle);
                if (hr == unchecked((int)0x800704C7)) return null;
                Marshal.ThrowExceptionForHR(hr);
                dialog.GetResult(out item); IntPtr name;
                item.GetDisplayName(0x80058000, out name);
                try { return Marshal.PtrToStringUni(name); } finally { Marshal.FreeCoTaskMem(name); }
            }
            finally { if (initial != null) Marshal.ReleaseComObject(initial); if (item != null) Marshal.ReleaseComObject(item); if (instance != null) Marshal.ReleaseComObject(instance); }
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bind, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bind, ref Guid handler, ref Guid iid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdn, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
        [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr owner);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, uint placement);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
        }
    }
}
