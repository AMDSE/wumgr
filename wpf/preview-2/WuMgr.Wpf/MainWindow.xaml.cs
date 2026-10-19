using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WuMgr.Wpf.ViewModels;

namespace WuMgr.Wpf
{
    public partial class MainWindow : Window
    {
        bool waitingForClose;
        bool allowClose;
        bool disposed;

        public MainWindow()
        {
            InitializeComponent();
            PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var model = DataContext as MainViewModel;
            if (model == null) return;
            if (e.Key == Key.F5)
            {
                if (model.ScanCommand.CanExecute(null)) model.ScanCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin)
            {
                model.SearchText = string.Empty;
                e.Handled = true;
            }
        }

        async void Window_Closing(object sender, CancelEventArgs e)
        {
            if (allowClose || disposed) return;
            if (waitingForClose) { e.Cancel = true; return; }
            var model = DataContext as MainViewModel;
            if (model == null) return;
            if (!model.IsBusy)
            {
                model.Dispose();
                disposed = true;
                return; // Never recursively call Close from a synchronous Closing event.
            }
            e.Cancel = true;
            string message = model.IsEnglish
                ? "A task is still running. Cancel and close when it finishes? COM/WUA calls cannot be interrupted immediately. Completed installations will not be rolled back."
                : "任务仍在运行。请求取消，并在操作真正结束后关闭吗？\nCOM/WUA 调用不能立即中断，已完成的安装不会回滚。";
            if (MessageBox.Show(this, message, "WuMgr", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            waitingForClose = true;
            IsEnabled = false;
            try
            {
                model.Cancel();
                await model.WaitForIdleAsync();
                model.Dispose();
                disposed = true;
                allowClose = true;
                // A completed await may be synchronous: enqueue the second close after this event.
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
            catch (Exception ex)
            {
                waitingForClose = false;
                IsEnabled = true;
                MessageBox.Show(this, ex.Message, "WuMgr", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
