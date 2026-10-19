# WPF implementation contract

Use namespace `WuMgr.Wpf.ViewModels`, C# 7.3 syntax, target .NET Framework 4.8. Main app links existing UI-free `wumgr` business source files (not WinForms exe). Do not modify original `wumgr` business sources.

## MainViewModel : INotifyPropertyChanged, IDisposable
Constructor `MainViewModel(IBackend backend, IInteraction interaction, bool demo, bool english)`; interfaces under `WuMgr.Wpf.Services` (VM implementer defines them; UI author does not need to implement).
Properties:
- `ObservableCollection<DeviceRowViewModel> Devices` (filtered, keep row instances and selected versions), `ObservableCollection<CategoryItem> Categories`, `CategoryItem SelectedCategory`.
- `string SearchText`, `bool IncludeHistory`, `bool InstallAfterDownload`, `string DownloadFolder`.
- `bool IsBusy`, `bool IsIdle`, `bool IsDemo`, `bool IsEnglish` (read/write language, all labels notify), `bool CancelRequested`.
- `string Title`, `Subtitle`, `Summary`, `StatusText`, `PolicyText`, `ProgressText`, `LogText`; `double ProgressValue`; `int SelectedCount`, `DeviceCount`, `CandidateCount`.
- indexer `string this[string key]` for labels: AppName, Workspace, SearchHint, Scan, RefreshDevices, Cancel, Download, AutoSelect, ClearSelection, Browse, OpenFolder, History, Install, PolicyEnable, PolicyRestore, Details, Versions, Installed, Candidate, NoCandidates, EmptyTitle, EmptyBody, DemoNotice, DownloadFolderLabel, SelectionLabel, Log, Protection, Language, About, DeviceSearch, AllDevices, CommonDevices.
- ICommand properties `ScanCommand`, `RefreshDevicesCommand`, `CancelCommand`, `DownloadCommand`, `AutoSelectCommand`, `ClearSelectionCommand`, `BrowseCommand`, `OpenFolderCommand`, `EnablePolicyCommand`, `RestorePolicyCommand`, `ToggleLanguageCommand`.
- `Task InitializeAsync()`, `Task WaitForIdleAsync()` (window closing cancel then await), `void Cancel()`.

## DeviceRowViewModel : INotifyPropertyChanged
Properties: `string Name`, `Category`, `Manufacturer`, `InstalledVersion`, `InstalledProvider`, `InstalledDate`, `HardwareIds`, `InstanceId`, `Status`, `VersionSummary`; `bool IsSelected` (write), `bool CanSelect`, `bool HasCandidates`, `bool IsExpanded` (write); `ObservableCollection<VersionItem> Versions`; `VersionItem SelectedVersion` (write); `ICommand DetailsCommand`.
VersionItem properties `string Display`, `string Title`, `string Version`, `string Date`, `string Provider`, `string UpdateId`, `string Status`; `wumgr.DriverInfo Driver`.
CategoryItem: `string Key`, `string Name`.
Selection must be disabled on rows with no downloadable candidate. One version per device. AutoSelect uses original conservative DeviceDiscovery.SelectAutomatic. Raw device metadata remains unchanged across languages. All commands recheck busy/demo/admin in execution, not just CanExecute.

## Services responsibility
VM implementer owns `ViewModels/*.cs`, `Services/Contracts.cs`, `Services/DriverBackend.cs`, `Services/Localization.cs`, `Services/DemoData.cs` and pure VM tests. Parent owns WPF-specific `Services/WpfInteraction.cs`, `App.xaml(.cs)`, project files, build/package scripts. UI author owns `MainWindow.xaml(.cs)`, `Themes/*.xaml`, `Views/DeviceDetailsWindow.xaml(.cs)`.
`IInteraction` must be UI-neutral; methods expected `bool Confirm(string title,string message)`, `void ShowMessage(string title,string message)`, `string ChooseFolder(string current)`, `void OpenFolder(string path)`, `void ShowDetails(DeviceRowViewModel row)`, `void SaveLanguage(bool english)`. If implementer needs a dispatcher: use captured SynchronizationContext; no WPF references in VM/services except parent adapter.

UI author: use DynamicResource only theme resources; bind labels as `{Binding [Scan]}` etc. Window DataContext assigned by App. Native WPF, no web/WinForms interop, no generated mockup. MainWindow closing handler async uses Cancel(), WaitForIdleAsync(), dispose only after worker ends. Keyboard Ctrl+F focus SearchBox, Escape clears SearchText only when SearchBox focused, F5 runs ScanCommand only when executable. Details can use a single self-contained WPF dialog constructed in code instead of separate XAML, showing raw metadata/versions. UI author must implement details public ctor `(DeviceRowViewModel row)`.
