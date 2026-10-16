using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OneFlowHub.Core;

namespace OneFlowHub.ViewModels;

public sealed class ReleaseTab : Bindable
{
    private bool _on;
    public string Tag { get; init; } = "";
    public ReleaseInfo Release { get; init; } = new();
    public bool IsSelected { get => _on; set => Set(ref _on, value); }
}

public sealed class AppVM : Bindable
{
    private static readonly (string a, string b)[] Palette =
    {
        ("#4F8BFF", "#22D3EE"), ("#A855F7", "#EC4899"), ("#6366F1", "#14B8A6"),
        ("#F97316", "#EF4444"), ("#10B981", "#84CC16"), ("#F59E0B", "#F43F5E"),
    };

    private readonly MainVM _host;
    public CatalogEntry Entry { get; }

    private List<ReleaseInfo> _releases = new();
    private string? _installedTag;
    private DateTimeOffset? _installedAt;
    private bool _busy, _loading = true;
    private double _progress;
    private string _busyText = "";
    private ImageSource? _icon;
    private bool _confirmRemove;

    public AppVM(CatalogEntry entry, int index, MainVM host)
    {
        Entry = entry; _host = host;
        var p = Palette[index % Palette.Length];
        AccentFrom = (Color)ColorConverter.ConvertFromString(p.a);
        AccentTo = (Color)ColorConverter.ConvertFromString(p.b);

        PrimaryCommand = new RelayCommand(_ => PrimaryAsync(), _ => !IsBusy && !_loading && PrimaryEnabled);
        OpenCommand = new RelayCommand(_ => _host.OpenDetails(this));
        RemoveCommand = new RelayCommand(_ => RemoveAsync(), _ => IsInstalled && !IsBusy);
        LocateCommand = new RelayCommand(_ => System.Diagnostics.Process.Start("explorer.exe", Installer.DirOf(Id)), _ => IsInstalled);
    }

    // ── identity
    public string Id => Entry.Id;

    // ── release stage (comes from the catalog)
    public bool IsSoon => string.Equals(Entry.Status, "soon", StringComparison.OrdinalIgnoreCase);
    public bool IsBeta => string.Equals(Entry.Status, "beta", StringComparison.OrdinalIgnoreCase);
    public bool IsReady => !IsSoon && !IsBeta;
    public string StageText => IsSoon ? "მალე დარელიზდება" : IsBeta ? "სატესტო რეჟიმში" : "გამოყენებისთვის მზა";
    public Brush StageBrush => Frozen(new SolidColorBrush(IsSoon ? Color.FromRgb(0x7F, 0xB0, 0xFF)
                                                          : IsBeta ? Color.FromRgb(0xD9, 0xA4, 0x41)
                                                          : Color.FromRgb(0x4C, 0xB9, 0x63)));
    public Brush StageBackground => Frozen(new SolidColorBrush(IsSoon ? Color.FromArgb(0x26, 0x7F, 0xB0, 0xFF)
                                                               : IsBeta ? Color.FromArgb(0x26, 0xD9, 0xA4, 0x41)
                                                               : Color.FromArgb(0x24, 0x4C, 0xB9, 0x63)));
    public string StageNote => IsSoon ? "ეს პროგრამა მალე დარელიზდება. ჩამოტვირთვა ჯერ გამორთულია."
                               : IsBeta ? "პროგრამა სატესტო რეჟიმშია — შესაძლოა ხარვეზები გამოჩნდეს. რჩევას ან შეცდომებს გვატყობინე."
                               : "";
    public bool HasStageNote => IsSoon || IsBeta;
    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string Glyph => string.IsNullOrWhiteSpace(Entry.IconText) ? Name[..1].ToUpperInvariant() : Entry.IconText!;
    public Color AccentFrom { get; }
    public Color AccentTo { get; }
    public ImageSource? Icon { get => _icon; private set { if (Set(ref _icon, value)) { Raise(nameof(HasIcon)); Raise(nameof(ShowGlyph)); } } }
    public bool HasIcon => _icon != null;
    public bool ShowGlyph => _icon == null;

    // ── state
    public IReadOnlyList<ReleaseInfo> Releases => _releases;
    public ReleaseInfo? Latest => _releases.FirstOrDefault();
    public bool IsLoading => _loading;
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) RaiseState(); } }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
    public bool ConfirmRemove { get => _confirmRemove; set { if (Set(ref _confirmRemove, value)) Raise(nameof(RemoveText)); } }
    public string RemoveText => _confirmRemove ? "დარწმუნებული ხარ?" : "წაშლა";

    public bool IsInstalled => _installedTag != null;
    public string? InstalledTag => _installedTag;
    public bool HasUpdate => IsInstalled && Latest != null && VersionUtil.IsNewer(Latest.Version, _installedTag);
    public bool Unavailable => !_loading && !IsInstalled && Latest == null;
    private bool PrimaryEnabled => IsInstalled || Latest != null;
    public bool PrimaryEnabledUi => !IsBusy && PrimaryEnabled;
    public bool CanUpdate => HasUpdate && !IsBusy;

    public string VersionText =>
        IsSoon && !IsInstalled ? "ჯერ არ გამოსულა"
        : IsInstalled ? (HasUpdate ? $"v{VersionUtil.Clean(_installedTag)} → v{Latest!.Version}" : $"v{VersionUtil.Clean(_installedTag)}")
        : Latest != null ? $"v{Latest.Version}" : "—";

    public string StatusText =>
        _busy ? BusyText
        : _loading ? "იტვირთება…"
        : IsSoon && !IsInstalled ? "ჯერ არ არის ხელმისაწვდომი"
        : HasUpdate ? "ახალი ვერსია ხელმისაწვდომია"
        : IsInstalled ? "დაინსტალირებულია · განახლებულია"
        : Latest != null ? "არ არის დაინსტალირებული"
        : "ამჟამად მიუწვდომელია";

    public string PrimaryText =>
        _busy ? "მიმდინარეობს…"
        : IsSoon && !IsInstalled ? "მალე"
        : HasUpdate ? "განახლება"
        : IsInstalled ? "გახსნა"
        : Latest != null ? "დაინსტალირება"
        : "მიუწვდომელია";

    public Brush PrimaryBrush =>
        _busy || Unavailable ? Frozen(new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x30)))
        : IsInstalled && !HasUpdate ? Frozen(new SolidColorBrush(Color.FromRgb(0x2F, 0x2F, 0x35)))
        : Frozen(new SolidColorBrush(Color.FromRgb(0x4F, 0x8E, 0xF7)));

    public Brush PrimaryForeground =>
        _busy || Unavailable ? Frozen(new SolidColorBrush(Color.FromRgb(0x9C, 0x9C, 0xA6)))
        : IsInstalled && !HasUpdate ? Frozen(new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xEF)))
        : Frozen(new SolidColorBrush(Colors.White));

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }


    // ── presentation (library cards, list rows, drawer)
    public string CategoryText => string.IsNullOrWhiteSpace(Entry.Category) ? "პროგრამა" : Entry.Category!;
    public string SizeText
    {
        get
        {
            var a = Latest == null ? null : Installer.PickAsset(Latest, Entry.AssetMatch);
            return a == null ? (IsSoon ? "" : "—") : Ka.Size(a.Size);
        }
    }
    public string UpdatedText => Latest == null ? "—" : Ka.Date(Latest.Published);
    public string UpdatedAgo => Latest == null ? "" : Ka.Ago(Latest.Published);
    public string InstalledText => _installedAt == null ? (IsInstalled ? "დაინსტალირებულია" : "—") : Ka.Date(_installedAt.Value);
    public string LatestSummary => Latest == null ? Description : (Notes.Summary(Latest.Body) is { Length: > 0 } s ? s : Description);
    public string LatestVersionText => Latest == null ? "—" : "v" + Latest.Version;
    public string InstalledVersionText => IsInstalled && _installedTag != "0" ? "v" + VersionUtil.Clean(_installedTag) : "—";

    public string BadgeText =>
        _busy ? "მიმდინარეობს"
        : _loading ? "…"
        : IsSoon && !IsInstalled ? "მალე"
        : HasUpdate ? "განახლება"
        : IsInstalled ? "დაინსტალირებულია"
        : Latest != null ? "ხელმისაწვდომია"
        : "მიუწვდომელია";

    public Brush BadgeBackground => Frozen(new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)));

    public Brush BadgeForeground =>
        _busy ? Frozen(new SolidColorBrush(Color.FromRgb(0x7F, 0xB0, 0xFF)))
        : HasUpdate ? Frozen(new SolidColorBrush(Color.FromRgb(0xD9, 0xA4, 0x41)))
        : IsInstalled ? Frozen(new SolidColorBrush(Color.FromRgb(0x4C, 0xB9, 0x63)))
        : Frozen(new SolidColorBrush(Color.FromRgb(0x9C, 0x9C, 0xA6)));

    public bool Matches(string q) =>
        string.IsNullOrWhiteSpace(q)
        || Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
        || Description.Contains(q, StringComparison.CurrentCultureIgnoreCase)
        || CategoryText.Contains(q, StringComparison.CurrentCultureIgnoreCase);

    // ── details panel
    public List<ReleaseTab> Tabs { get; private set; } = new();
    private ReleaseTab? _selected;
    public ReleaseTab? SelectedTab => _selected;
    public List<NoteLine> SelectedNotes { get; private set; } = new();
    public string SelectedTitle => _selected == null ? "" : _selected.Release.Name;
    public string SelectedDate => _selected == null ? "" : $"{Ka.Date(_selected.Release.Published)} · {Ka.Ago(_selected.Release.Published)}";

    public void SelectTab(ReleaseTab tab)
    {
        foreach (var t in Tabs) t.IsSelected = ReferenceEquals(t, tab);
        _selected = tab;
        SelectedNotes = Notes.Parse(tab.Release.Body);
        if (SelectedNotes.Count == 0) SelectedNotes = new() { new NoteLine(NoteKind.Text, "ამ ვერსიისთვის დეტალური აღწერა არ არის დამატებული.") };
        Raise(nameof(SelectedNotes)); Raise(nameof(SelectedTab)); Raise(nameof(SelectedTitle)); Raise(nameof(SelectedDate));
    }

    // ── commands
    public ICommand PrimaryCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand LocateCommand { get; }

    // ── loading
    public async Task RefreshAsync(bool force = false)
    {
        _loading = true; RaiseState();
        try
        {
            var rels = IsSoon ? new List<ReleaseInfo>() : await GitHub.GetReleasesAsync(Entry.RepoOwner, Entry.RepoName, 5, force);
            _releases = rels ?? new();
            var rec = Installer.GetInstalled(Id);
            _installedAt = rec?.InstalledAt;
            _installedTag = rec?.Tag
                            ?? (Installer.FindExe(Id, Entry.ExecutableName) != null ? "0" : null);
            Tabs = _releases.Select(r => new ReleaseTab { Tag = r.Tag, Release = r }).ToList();
            if (Tabs.Count > 0) SelectTab(Tabs[0]);
            Raise(nameof(Tabs)); Raise(nameof(Releases)); Raise(nameof(Latest));
        }
        finally { _loading = false; RaiseState(); }
        _ = LoadIconAsync();
    }

    private async Task LoadIconAsync()
    {
        if (_icon != null || string.IsNullOrWhiteSpace(Entry.IconUrl)) return;
        try
        {
            var key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes(Entry.IconUrl!)))[..10];
            var path = Path.Combine(Paths.Cache, $"icon_{Id}_{key}.png");
            byte[] bytes;
            if (File.Exists(path) && DateTime.Now - File.GetLastWriteTime(path) < TimeSpan.FromDays(3))
                bytes = await File.ReadAllBytesAsync(path);
            else
            {
                bytes = await GitHub.Http.GetByteArrayAsync(Entry.IconUrl);
                await File.WriteAllBytesAsync(path, bytes);
            }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.DecodePixelWidth = 128;
            bmp.EndInit();
            bmp.Freeze();
            Icon = bmp;
        }
        catch { /* glyph fallback stays */ }
    }

    private void RaiseState()
    {
        foreach (var n in new[]
        {
            nameof(IsLoading), nameof(IsInstalled), nameof(InstalledTag), nameof(HasUpdate), nameof(Unavailable),
            nameof(VersionText), nameof(StatusText), nameof(PrimaryText), nameof(PrimaryBrush), nameof(PrimaryForeground),
            nameof(BadgeText), nameof(BadgeBackground), nameof(BadgeForeground), nameof(SizeText), nameof(UpdatedText),
            nameof(UpdatedAgo), nameof(InstalledText), nameof(LatestSummary), nameof(LatestVersionText), nameof(InstalledVersionText),
            nameof(PrimaryEnabledUi),
        }) Raise(n);
        CommandManager.InvalidateRequerySuggested();
    }

    // ── actions
    private async Task PrimaryAsync()
    {
        if (IsBusy) return;
        if (IsInstalled && !HasUpdate)
        {
            if (!Installer.Launch(Id, Entry.ExecutableName))
                _host.Toast("პროგრამის გაშვება ვერ მოხერხდა. სცადე თავიდან დაინსტალირება.");
            return;
        }
        await InstallAsync();
    }

    public async Task InstallAsync()
    {
        var rel = Latest;
        if (rel == null || (IsSoon && !IsInstalled)) return;
        IsBusy = true; Progress = 0; BusyText = "მზადდება…";
        try
        {
            var prog = new Progress<(double pct, string text)>(p => { Progress = p.pct; BusyText = p.text; Raise(nameof(StatusText)); });
            bool wasUpdate = HasUpdate;
            await Installer.InstallAsync(Id, rel, Entry.AssetMatch, prog);
            _installedTag = rel.Tag;
            _installedAt = DateTimeOffset.Now;
            _host.Toast(wasUpdate ? $"{Name} განახლდა — v{rel.Version}" : $"{Name} დაინსტალირდა");
            _host.NotifyAppsChanged();
        }
        catch (IOException)
        {
            _host.Toast($"{Name} ახლა გახსნილია. დახურე და სცადე ისევ.");
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            _host.Toast("ინსტალაცია ვერ მოხერხდა — შეამოწმე ინტერნეტი და სცადე ისევ.");
        }
        finally { IsBusy = false; RaiseState(); }
    }

    private async Task RemoveAsync()
    {
        if (!ConfirmRemove) { ConfirmRemove = true; await Task.Delay(3500); ConfirmRemove = false; return; }
        try
        {
            await Task.Run(() => Installer.Uninstall(Id));
            _installedTag = null;
            ConfirmRemove = false;
            _host.Toast($"{Name} წაიშალა");
            _host.NotifyAppsChanged();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            _host.Toast("წაშლა ვერ მოხერხდა — დახურე პროგრამა და სცადე ისევ.");
        }
        RaiseState();
    }
}
