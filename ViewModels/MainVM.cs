using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
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
    public string Summary { get; init; } = "";
    public AppVM App { get; init; } = null!;
    public ReleaseTab Tab { get; init; } = null!;
    public ICommand OpenCommand { get; init; } = null!;
}

public enum Pg { Home = 0, Library = 1, Updates = 2, Settings = 3 }

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
    private int _filter;
    private bool _list;
    private string _search = "";
    private bool _updatingAll;
    private AppVM? _hero;

    public AppSettings Settings { get; }
    public ObservableCollection<AppVM> Apps { get; } = new();
    public ObservableCollection<FeedItem> Feed { get; } = new();
    public ObservableCollection<AppVM> Installed { get; } = new();
    public ObservableCollection<AppVM> Pending { get; } = new();
    public ObservableCollection<FeedItem> Recent { get; } = new();
    public ICollectionView Library { get; }

    public MainVM(AppSettings settings)
    {
        Settings = settings;
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastOn = false; };

        Library = CollectionViewSource.GetDefaultView(Apps);
        Library.Filter = o => o is AppVM a && a.Matches(_search) && _filter switch
        {
            1 => a.IsInstalled,
            2 => a.HasUpdate,
            _ => true,
        };

        RefreshCommand = new RelayCommand(_ => LoadAsync(force: true));
        PageCommand = new RelayCommand(p => Page = int.Parse((string)p!));
        FilterCommand = new RelayCommand(p => Filter = int.Parse((string)p!));
        ViewCommand = new RelayCommand(p => ViewList = (string)p! == "list");
        CloseDetailsCommand = new RelayCommand(_ => Details = null);
        SelectTabCommand = new RelayCommand(p => { if (p is ReleaseTab t && Details != null) Details.SelectTab(t); });
        HubUpdateCommand = new RelayCommand(_ => ApplyHubUpdateAsync(), _ => _hubUpdate != null && !_hubBusy);
        UpdateAllCommand = new RelayCommand(_ => UpdateAllAsync(), _ => Apps.Any(a => a.CanUpdate) && !_updatingAll);
        OpenHeroCommand = new RelayCommand(_ => { if (Hero != null) OpenDetails(Hero); });
        ClearSearchCommand = new RelayCommand(_ => SearchText = "");
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
    public int Page
    {
        get => _page;
        set { if (Set(ref _page, value)) { Raise(nameof(PageTitle)); Raise(nameof(IsHome)); Raise(nameof(IsLibrary)); Raise(nameof(IsUpdates)); Raise(nameof(IsSettings)); } }
    }
    public bool IsHome => Page == 0;
    public bool IsLibrary => Page == 1;
    public bool IsUpdates => Page == 2;
    public bool IsSettings => Page == 3;
    public string PageTitle => Page switch { 0 => "მიმოხილვა", 1 => "აპლიკაციები", 2 => "განახლებები", _ => "პარამეტრები" };

    public bool IsLoading { get => _loading; private set { if (Set(ref _loading, value)) Raise(nameof(StatusLine)); } }
    public AppVM? Details { get => _details; set { if (Set(ref _details, value)) Raise(nameof(HasDetails)); } }
    public bool HasDetails => _details != null;
    public string ToastText { get => _toast; private set => Set(ref _toast, value); }
    public bool ToastOn { get => _toastOn; private set => Set(ref _toastOn, value); }

    public int Filter { get => _filter; set { if (Set(ref _filter, value)) { Library.Refresh(); Raise(nameof(ShownCount)); } } }
    public bool ViewList { get => _list; set { if (Set(ref _list, value)) Raise(nameof(ViewGrid)); } }
    public bool ViewGrid => !_list;
    public string SearchText
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value ?? "")) return;
            Library.Refresh();
            Raise(nameof(HasSearch)); Raise(nameof(ShownCount));
            if (!string.IsNullOrWhiteSpace(_search) && Page != 1) Page = 1;
        }
    }
    public bool HasSearch => !string.IsNullOrEmpty(_search);
    public int ShownCount => Library.Cast<object>().Count();
    public bool NothingShown => ShownCount == 0 && !IsLoading;

    // hero (featured = most recently published release)
    public AppVM? Hero { get => _hero; private set { if (Set(ref _hero, value)) Raise(nameof(HasHero)); } }
    public bool HasHero => _hero != null;

    // status
    public int UpdateCount => Apps.Count(a => a.HasUpdate);
    public bool HasUpdates => UpdateCount > 0;
    public bool NoUpdates => !HasUpdates;
    public int InstalledCount => Apps.Count(a => a.IsInstalled);
    public bool HasInstalled => InstalledCount > 0;
    public bool NoInstalled => !HasInstalled;
    public bool UpdatingAll { get => _updatingAll; private set => Set(ref _updatingAll, value); }
    public string SidebarUpdateText => UpdateCount switch { 0 => "ყველაფერი განახლებულია", 1 => "1 განახლება ხელმისაწვდომია", _ => $"{UpdateCount} განახლება ხელმისაწვდომია" };
    public bool IsOnline => GitHub.Online;
    public string ConnectionText => GitHub.Online ? "ონლაინ" : "ოფლაინ — ნაჩვენებია შენახული მონაცემები";
    public string SyncText => GitHub.LastSync is { } t ? $"სინქრონიზაცია: {t:HH:mm}" : "სინქრონიზაცია: —";
    public string StatusLine => IsLoading ? "იტვირთება…" : $"{Apps.Count} პროგრამა · {InstalledCount} დაინსტალირებული";

    public string HubVersion => "v" + SelfUpdater.CurrentVersion;
    public bool HasHubUpdate => _hubUpdate != null;
    public string HubUpdateText => _hubUpdate == null ? "" : $"HUB v{_hubUpdate.Value.rel.Version}";
    public bool HubBusy { get => _hubBusy; private set => Set(ref _hubBusy, value); }
    public double HubProgress { get => _hubProgress; private set => Set(ref _hubProgress, value); }

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
    public ICommand FilterCommand { get; }
    public ICommand ViewCommand { get; }
    public ICommand CloseDetailsCommand { get; }
    public ICommand SelectTabCommand { get; }
    public ICommand HubUpdateCommand { get; }
    public ICommand UpdateAllCommand { get; }
    public ICommand OpenHeroCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand CheckHubCommand { get; }
    public ICommand OpenDataFolderCommand { get; }

    public void OpenDetails(AppVM app) { app.ConfirmRemove = false; Details = app; }

    public void NotifyAppsChanged()
    {
        RebuildDerived();
        CommandManager.InvalidateRequerySuggested();
    }

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
        IsLoading = true;
        try
        {
            var entries = await Catalog.LoadAsync(Settings.CatalogUrl);
            if (Apps.Count != entries.Count || !Apps.Select(a => a.Id).SequenceEqual(entries.Select(e => e.Id)))
            {
                Apps.Clear();
                int i = 0;
                foreach (var e in entries) Apps.Add(new AppVM(e, i++, this));
            }
            foreach (var a in Apps) a.PropertyChanged -= OnAppChanged;
            foreach (var a in Apps) a.PropertyChanged += OnAppChanged;

            await Task.WhenAll(Apps.Select(a => a.RefreshAsync(force)));
            RebuildDerived();

            if (Settings.CheckUpdatesOnStart || force) await CheckHubAsync(force);
            if (force) Toast(GitHub.Online ? "განახლებულია" : "ინტერნეტი მიუწვდომელია — ნაჩვენებია შენახული მონაცემები");
        }
        catch (Exception ex) { Log.Error(ex); Toast("ჩატვირთვა ვერ მოხერხდა"); }
        finally
        {
            IsLoading = false;
            RaiseStatus();
            Raise(nameof(NothingShown));
        }
    }

    private void OnAppChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppVM.HasUpdate) or nameof(AppVM.IsInstalled) or nameof(AppVM.IsBusy))
        {
            _ui.BeginInvoke(() => { RaiseStatus(); CommandManager.InvalidateRequerySuggested(); });
        }
    }

    public async Task CheckHubAsync(bool force = false)
    {
        _hubUpdate = await SelfUpdater.CheckAsync(Settings, force);
        Raise(nameof(HasHubUpdate)); Raise(nameof(HubUpdateText));
        RaiseStatus();
        CommandManager.InvalidateRequerySuggested();
        if (_hubUpdate != null) Toast($"HUB-ის ახალი ვერსია: v{_hubUpdate.Value.rel.Version}");
    }

    private void RaiseStatus()
    {
        foreach (var n in new[]
        {
            nameof(UpdateCount), nameof(HasUpdates), nameof(NoUpdates), nameof(InstalledCount), nameof(HasInstalled), nameof(NoInstalled),
            nameof(SidebarUpdateText), nameof(IsOnline), nameof(ConnectionText), nameof(SyncText), nameof(StatusLine),
            nameof(ShownCount), nameof(NothingShown),
        }) Raise(n);
        Library.Refresh();
    }

    private void RebuildDerived()
    {
        // quick-launch + pending updates
        Installed.Clear();
        foreach (var a in Apps.Where(a => a.IsInstalled)) Installed.Add(a);
        Pending.Clear();
        foreach (var a in Apps.Where(a => a.HasUpdate)) Pending.Add(a);

        // activity feed
        Feed.Clear();
        var items = new List<FeedItem>();
        foreach (var app in Apps)
            foreach (var tab in app.Tabs.Take(3))
            {
                var tabCopy = tab; var appCopy = app;
                var lines = Notes.Parse(tab.Release.Body, 4).Where(l => l.Kind != NoteKind.Heading).ToList();
                items.Add(new FeedItem
                {
                    AppName = app.Name,
                    Tag = tab.Release.Tag,
                    DateText = $"{Ka.Date(tab.Release.Published)} · {Ka.Ago(tab.Release.Published)}",
                    AccentFrom = app.AccentFrom, AccentTo = app.AccentTo, Glyph = app.Glyph,
                    Lines = lines,
                    Summary = Notes.Summary(tab.Release.Body),
                    App = app, Tab = tab,
                    OpenCommand = new RelayCommand(_ => { appCopy.SelectTab(tabCopy); OpenDetails(appCopy); }),
                });
            }
        foreach (var f in items.OrderByDescending(f => f.Tab.Release.Published).Take(14)) Feed.Add(f);
        Recent.Clear();
        foreach (var f in Feed.Take(3)) Recent.Add(f);
        Raise(nameof(HasFeed));

        Hero = Apps.Where(a => a.Latest != null).OrderByDescending(a => a.Latest!.Published).FirstOrDefault() ?? Apps.FirstOrDefault();
        RaiseStatus();
    }

    public bool HasFeed => Feed.Count > 0;

    private async Task UpdateAllAsync()
    {
        UpdatingAll = true;
        try
        {
            foreach (var a in Apps.Where(a => a.CanUpdate).ToList())
                await a.InstallAsync();
        }
        finally { UpdatingAll = false; NotifyAppsChanged(); }
    }

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

