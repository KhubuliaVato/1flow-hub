using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace OneFlowHub.Core;

// ───────────────────────── release notes → lines ─────────────────────────
public enum NoteKind { Heading, Bullet, Text }

public sealed record NoteLine(NoteKind Kind, string Text, string? Tag = null)
{
    public bool IsHeading => Kind == NoteKind.Heading;
    public bool IsBullet => Kind == NoteKind.Bullet;
    public bool IsText => Kind == NoteKind.Text;
    public bool HasTag => !string.IsNullOrEmpty(Tag);
}

public static partial class Notes
{
    private static readonly Dictionary<string, string> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["feat"] = "ახალი", ["fix"] = "გასწორება", ["style"] = "დიზაინი", ["perf"] = "სიჩქარე",
        ["refactor"] = "გაუმჯობესება", ["docs"] = "დოკუმენტაცია", ["chore"] = "სხვა",
    };

    [GeneratedRegex(@"^([-*•·]|\d+[.)])\s+")] private static partial Regex BulletRx();
    [GeneratedRegex(@"^(feat|fix|style|perf|refactor|docs|chore)\s*:\s*", RegexOptions.IgnoreCase)] private static partial Regex PrefixRx();
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")] private static partial Regex LinkRx();
    [GeneratedRegex(@"\*\*|__|`")] private static partial Regex MarkRx();
    [GeneratedRegex(@"^[\p{So}\p{Cs}️\s]+")] private static partial Regex EmojiRx();

    public static List<NoteLine> Parse(string body, int max = 0)
    {
        var res = new List<NoteLine>();
        foreach (var raw in (body ?? "").Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0) continue;
            bool bullet = BulletRx().IsMatch(l);
            l = BulletRx().Replace(l, "");
            if (l.StartsWith('#')) l = l.TrimStart('#').Trim();
            l = LinkRx().Replace(l, "$1");
            l = MarkRx().Replace(l, "").Trim();

            string? tag = null;
            var m = PrefixRx().Match(l);
            if (m.Success) { tag = Tags[m.Groups[1].Value]; l = l[m.Length..]; }
            l = EmojiRx().Replace(l, "").Trim();
            if (l.Length < 3) continue;

            bool shortLabel = !bullet && tag == null && l.Length < 45;
            res.Add(shortLabel
                ? new NoteLine(NoteKind.Heading, l.TrimEnd(':'))
                : bullet || tag != null
                    ? new NoteLine(NoteKind.Bullet, l, tag)
                    : new NoteLine(NoteKind.Text, l));
            if (max > 0 && res.Count(x => x.Kind != NoteKind.Heading) >= max) break;
        }
        return res;
    }

    /// <summary>First readable change as a one-line summary.</summary>
    public static string Summary(string body)
    {
        var first = Parse(body).FirstOrDefault(x => x.Kind != NoteKind.Heading);
        if (first == null) return "";
        var t = first.Text;
        int dot = t.IndexOfAny(new[] { '.', '!', '?' });
        if (dot is >= 12 and < 110) t = t[..(dot + 1)];
        return t.Length > 130 ? t[..127].TrimEnd() + "…" : t;
    }
}

// ───────────────────────── window effects (Mica, dark title, round corners) ─────────────────────────
public static class Dwm
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            var src = HwndSource.FromHwnd(hwnd);
            if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
            int on = 1, mica = 2, round = 2;
            DwmSetWindowAttribute(hwnd, 20, ref on, 4);     // immersive dark mode
            DwmSetWindowAttribute(hwnd, 38, ref mica, 4);   // system backdrop: Mica (Win11 22H2+, ignored elsewhere)
            DwmSetWindowAttribute(hwnd, 33, ref round, 4);  // rounded corners
        }
        catch { /* older Windows: just stay dark */ }
    }
}

// ───────────────────────── tray ─────────────────────────
public sealed class Tray : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;

    public Tray(Action show, Action checkUpdates, Action exit)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("გახსნა", null, (_, _) => show());
        menu.Items.Add("განახლებების შემოწმება", null, (_, _) => checkUpdates());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("გასვლა", null, (_, _) => exit());

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "1Flow HUB",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = LoadIcon(),
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) show();
        };
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/assets/app.ico");
            var s = Application.GetResourceStream(uri)?.Stream;
            if (s != null) return new System.Drawing.Icon(s);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    public void Balloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(3500);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
