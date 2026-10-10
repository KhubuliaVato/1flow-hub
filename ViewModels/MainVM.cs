using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OneFlowHub.Core;

namespace OneFlowHub.ViewModels;

public sealed class FeedItem
{
    public string AppName { get; init; } = "";
    public string Tag { get; init; } = "";
    public string DateText { get; init; } = "";
    public Color AccentFrom { get; init; }
    public Color AccentTo { get; init; }
    public string Glyph { get; init; } = "";
    public List<NoteLine> Lines { get; init; } = new();
    public AppVM App { get; init; } = null!;
    public ReleaseTab Tab { get; init; } = null!;
    public ICommand OpenCommand { get; init; } = null!;
}

public sealed class MainVM : Bindable
{
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.6) };
    private (ReleaseInfo rel, AssetInfo asset)? _hubUpdate;
    private int _page;
    private bool _loading = true;
    private string _toast = "";
    private bool _toastOn;
    private AppVM? _details;
    private bool _hubBusy;
    private double _hubProgress;

    public AppSettings Settings { get; }
    public ObservableCollection<AppVM> Apps { get; } = new();
    public ObservableCollection<FeedItem> Feed { get; } = new();

    public MainVM(AppSettings settings)
    {
        Settings = settings;
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastOn = false; };

        RefreshCommand = new RelayCommand(_ => LoadAsync(force: true));
        PageCommand = new RelayCommand(p => Page = int.Parse((string)p!));
        CloseDetailsCommand = new RelayCommand(_ => Details = null);
        SelectTabCommand = new RelayCommand(p => { if (p is ReleaseTab t && Details != null) Details.SelectTab(t); });
        HubUpdateCommand = new RelayCommand(_ => ApplyHubUpdateAsync(), _ => _hubUpdate != null && !_hubBusy);
        CheckHubCommand = new RelayCommand(async _ =>
        {
            await CheckHubAsync(force: true);
            if (_hubUpdate == null) Toast("გაქვს ბოლო ვერსია ✓");
        });
        OpenDataFolderCommand = new RelayCommand(_ =>
        {
            Paths.EnsureAll();
            System.Diagnostics.Process.Start("explorer.exe", Paths.Root);
        });
    }

    // ── navigation / state
    public int Page { get => _page; set { if (Set(ref _page, value)) Raise(nameof(PageTitle)); } }
    public string PageTitle => Page switch { 0 => "აპლიკაციები", 1 => "განახლებები", _ => "პარამეტრები" };
    public bool IsLoading { get => _loading; private set => Set(ref _loading, value); }
    public AppVM? Details { get => _details; set { if (Set(ref _details, value)) Raise(nameof(HasDetails)); } }
    public bool HasDetails => _details != null;
    public string ToastText { get => _toast; private set => Set(ref _toast, value); }
    public bool ToastOn { get => _toastOn; private set => Set(ref _toastOn, value); }

    public string HubVersion => "v" + SelfUpdater.CurrentVersion;
    public bool HasHubUpdate => _hubUpdate != null;
    public string HubUpdateText => _hubUpdate == null ? "" : $"ახალი ვერსია v{_hubUpdate.Value.rel.Version}";
    public bool HubBusy { get => _hubBusy; private set => Set(ref _hubBusy, value); }
    public double HubProgress { get => _hubProgress; private set => Set(ref _hubProgress, value); }

    public string Summary
    {
        get
        {
            if (IsLoading && Apps.Count == 0) return "იტვირთება…";
            int inst = Apps.Count(a => a.IsInstalled), upd = Apps.Count(a => a.HasUpdate);
            var s = $"{Apps.Count} პროგრამა · {inst} დაინსტალირებული";
            return upd > 0 ? s + $" · {upd} განახლება" : s;
        }
    }

    // ── settings proxies
    public bool CloseToTray { get => Settings.CloseToTray; set { Settings.CloseToTray = value; Settings.Save(); Raise(); } }
    public bool CheckOnStart { get => Settings.CheckUpdatesOnStart; set { Settings.CheckUpdatesOnStart = value; Settings.Save(); Raise(); } }
    public bool StartWithWindows
    {
        get => Settings.StartWithWindows;
        set { Settings.StartWithWindows = value; Settings.Save(); AppSettings.ApplyAutostart(value); Raise(); }
    }
    public string DataFolder => Paths.Root;

    // ── commands
    public ICommand RefreshCommand { get; }
    public ICommand PageCommand { get; }
    public ICommand CloseDetailsCommand { get; }
    public ICommand SelectTabCommand { get; }
    public ICommand HubUpdateCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand CheckHubCommand { get; }

    public void OpenDetails(AppVM app) { app.ConfirmRemove = false; Details = app; }
    public void NotifyAppsChanged() { Raise(nameof(Summary)); BuildFeed(); }

    public void Toast(string text)
    {
        _ui.Invoke(() =>
        {
            ToastText = text; ToastOn = true;
            _toastTimer.Stop(); _toastTimer.Start();
        });
    }

    // ── loading
    public async Task LoadAsync(bool force = false)
    {
        IsLoading = true; Raise(nameof(Summary));
        try
        {
            var entries = await Catalog.LoadAsync(Settings.CatalogUrl);
            if (Apps.Count != entries.Count || !Apps.Select(a => a.Id).SequenceEqual(entries.Select(e => e.Id)))
            {
                Apps.Clear();
                int i = 0;
                foreach (var e in entries) Apps.Add(new AppVM(e, i++, this));
            }
            await Task.WhenAll(Apps.Select(a => a.RefreshAsync(force)));
            BuildFeed();

            if (Settings.CheckUpdatesOnStart || force) await CheckHubAsync(force);
            if (force) Toast("განახლებულია");
        }
        catch (Exception ex) { Log.Error(ex); Toast("ჩატვირთვა ვერ მოხერხდა"); }
        finally { IsLoading = false; Raise(nameof(Summary)); }
    }

    public async Task CheckHubAsync(bool force = false)
    {
        _hubUpdate = await SelfUpdater.CheckAsync(Settings, force);
        Raise(nameof(HasHubUpdate)); Raise(nameof(HubUpdateText));
        CommandManager.InvalidateRequerySuggested();
        if (_hubUpdate != null) Toast($"HUB-ის ახალი ვერსია: v{_hubUpdate.Value.rel.Version}");
    }

    private void BuildFeed()
    {
        Feed.Clear();
        var items = new List<FeedItem>();
        foreach (var app in Apps)
            foreach (var tab in app.Tabs.Take(3))
            {
                var tabCopy = tab; var appCopy = app;
                items.Add(new FeedItem
                {
                    AppName = app.Name,
                    Tag = tab.Release.Tag,
                    DateText = $"{Ka.Date(tab.Release.Published)} · {Ka.Ago(tab.Release.Published)}",
                    AccentFrom = app.AccentFrom, AccentTo = app.AccentTo, Glyph = app.Glyph,
                    Lines = Notes.Parse(tab.Release.Body, 4).Where(l => l.Kind != NoteKind.Heading).ToList(),
                    App = app, Tab = tab,
                    OpenCommand = new RelayCommand(_ => { appCopy.SelectTab(tabCopy); OpenDetails(appCopy); }),
                });
            }
        foreach (var f in items.OrderByDescending(f => f.Tab.Release.Published).Take(14)) Feed.Add(f);
        Raise(nameof(HasFeed));
    }

    public bool HasFeed => Feed.Count > 0;

    private async Task ApplyHubUpdateAsync()
    {
        if (_hubUpdate == null) return;
        HubBusy = true; HubProgress = 0;
        try
        {
            var prog = new Progress<double>(p => HubProgress = p);
            await SelfUpdater.ApplyAsync(_hubUpdate.Value.rel, _hubUpdate.Value.asset, prog);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            HubBusy = false;
            Toast("HUB-ის განახლება ვერ მოხერხდა. სცადე მოგვიანებით.");
        }
    }
}
