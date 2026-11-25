using System.Globalization;
using System.Security;
using MessagePack;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace KitveiHakodesh.Core.Reminders;

/// <summary>A one-off reminder the user set in the calendar.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class Reminder
{
    /// <summary>Its id in Windows' schedule; empty for a new one.</summary>
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>A second line under the title (optional).</summary>
    public string Note { get; set; } = "";
    /// <summary>Local date and time it fires: yyyy-MM-ddTHH:mm.</summary>
    public string Time { get; set; } = "";
    /// <summary>One of <see cref="WindowsReminders.Sounds"/>: "none", "default", "reminder", ...</summary>
    public string Sound { get; set; } = "default";
    /// <summary>"brief" (slides away), "reminder" (stays until closed), "alarm" (stays, sound repeats).</summary>
    public string Style { get; set; } = "reminder";
    /// <summary>A snooze button (5/10/30/60 minutes) beside "close".</summary>
    public bool Snooze { get; set; }
}

/// <summary>Reply of every reminders op: the reminders asked for, or why it failed.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class RemindersResult
{
    public List<Reminder> Reminders { get; set; } = [];
    public string? Error { get; set; }
}

/// <summary>
/// One-off reminders kept by Windows itself: each is a scheduled notification, which Windows
/// shows at its time even when the app and the service are closed. There is no file of ours -
/// Windows' schedule is the list, read back here to show and edit them. A reminder leaves the
/// list once it has fired. No repeating reminders.
///
/// Windows ties the notifications to the app by an app id (AppUserModelID), registered per user
/// under HKCU\Software\Classes\AppUserModelId\ (the installer does it too; done again here on
/// first use so the portable app and a removed key still work).
/// </summary>
public static class WindowsReminders
{
    /// <summary>The app's id in Windows' notifications (the installer registers the same one).</summary>
    public const string AppId = "KleiKodesh.KitveiHakodesh";
    private const string AppIdKey = @"Software\Classes\AppUserModelId\" + AppId;
    private const string TimeFormat = "yyyy-MM-ddTHH:mm";
    private static readonly object Gate = new();

    /// <summary>The sounds offered: our name -> Windows' sound. The "looping" ones repeat, alarm style only.</summary>
    public static readonly IReadOnlyDictionary<string, string> Sounds = new Dictionary<string, string>
    {
        ["default"] = "ms-winsoundevent:Notification.Default",
        ["reminder"] = "ms-winsoundevent:Notification.Reminder",
        ["sms"] = "ms-winsoundevent:Notification.SMS",
        ["mail"] = "ms-winsoundevent:Notification.Mail",
        ["im"] = "ms-winsoundevent:Notification.IM",
        ["alarm"] = "ms-winsoundevent:Notification.Looping.Alarm",
        ["alarm2"] = "ms-winsoundevent:Notification.Looping.Alarm2",
        ["call"] = "ms-winsoundevent:Notification.Looping.Call",
    };
    private static readonly HashSet<string> LoopingSounds = ["alarm", "alarm2", "call"];
    private static bool _registered;

    /// <summary>The reminders still to come between two dates (yyyy-MM-dd, both included), by time.</summary>
    public static RemindersResult List(string fromDate, string toDate) => Run(result =>
    {
        if (!DateTime.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !DateTime.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
        {
            result.Error = "bad date";
            return;
        }
        var end = to.AddDays(1);
        foreach (var toast in Notifier().GetScheduledToastNotifications())
        {
            var at = toast.DeliveryTime.LocalDateTime;
            if (at < from || at >= end) continue;
            result.Reminders.Add(Read(toast));
        }
        result.Reminders.Sort((a, b) => string.CompareOrdinal(a.Time, b.Time));
    });

    /// <summary>Adds a reminder, or replaces the one with its id. Returns it with its id.</summary>
    public static RemindersResult Save(Reminder reminder) => Run(result =>
    {
        if (!DateTime.TryParseExact(reminder.Time, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            result.Error = "bad time";
            return;
        }
        if (at <= DateTime.Now)
        {
            result.Error = "past";
            return;
        }
        var notifier = Notifier();
        if (reminder.Id.Length > 0) Remove(notifier, reminder.Id);
        string id = reminder.Id.Length > 0 ? reminder.Id : Guid.NewGuid().ToString("N")[..12] /* Windows refuses longer ids (0x803E0120) */;
        var saved = Normalized(reminder, id, at);
        var toast = new ScheduledToastNotification(Content(saved, at), new DateTimeOffset(at)) { Id = id, Tag = id };
        // Windows delivers scheduled notifications only for an app that has shown one before
        // (it then keeps per-app notification settings for it): show one now, the first time.
        if (!HasShownNotification()) notifier.Show(new ToastNotification(Confirmation(reminder.Title, at)));
        notifier.AddToSchedule(toast);
        result.Reminders.Add(saved);
    });

    public static RemindersResult Delete(string id) => Run(_ => Remove(Notifier(), id));

    private static void Remove(ToastNotifier notifier, string id)
    {
        foreach (var toast in notifier.GetScheduledToastNotifications())
            if (toast.Id == id) notifier.RemoveFromSchedule(toast);
    }

    /// <summary>Options checked: an unknown sound or style falls back; a repeating sound only in alarm style.</summary>
    private static Reminder Normalized(Reminder r, string id, DateTime at)
    {
        string style = r.Style is "brief" or "reminder" or "alarm" ? r.Style : "reminder";
        string sound = r.Sound == "none" || Sounds.ContainsKey(r.Sound) ? r.Sound : "default";
        if (LoopingSounds.Contains(sound) && style != "alarm") sound = "default";
        return new Reminder
        {
            Id = id, Title = r.Title.Trim(), Note = r.Note.Trim(), Sound = sound, Style = style, Snooze = r.Snooze,
            Time = at.ToString(TimeFormat, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>A scheduled notification back as a reminder. Its options ride in the toast's
    /// launch attribute (kk-reminder|sound|style|snooze); title and note are its text lines.</summary>
    private static Reminder Read(ScheduledToastNotification toast)
    {
        var content = toast.Content;
        var texts = content.GetElementsByTagName("text");
        var root = content.DocumentElement;
        string[] options = (root?.GetAttribute("launch") ?? "").Split('|');
        string Option(int i, string fallback) => options.Length > i && options[i].Length > 0 ? options[i] : fallback;
        return new Reminder
        {
            Id = toast.Id,
            Title = texts.Count > 0 ? texts[0].InnerText : "",
            Note = texts.Count > 2 ? texts[2].InnerText : "",
            Time = toast.DeliveryTime.LocalDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
            Sound = Option(1, "default"),
            Style = Option(2, "reminder"),
            Snooze = Option(3, "0") == "1",
        };
    }

    /// <summary>"Open calendar" and "open Kitvei Hakodesh": links of the app's registered
    /// kitveihakodeshapp:// scheme, so they work with the app closed (Program.CalendarLink).</summary>
    private const string OpenButtons =
        "<action activationType=\"protocol\" arguments=\"kitveihakodeshapp://calendar\" content=\"פתח לוח שנה\"/>" +
        "<action activationType=\"protocol\" arguments=\"kitveihakodeshapp://open\" content=\"פתח כתבי הקודש\"/>";

    /// <summary>The notification: title, time, note; sound, style and buttons as chosen.</summary>
    private static XmlDocument Content(Reminder r, DateTime at)
    {
        static string X(string s) => SecurityElement.Escape(s) ?? "";
        string title = X(r.Title.Length > 0 ? r.Title : "תזכורת");
        string scenario = r.Style == "brief" ? "" : $" scenario=\"{r.Style}\"";
        string launch = $"kk-reminder|{r.Sound}|{r.Style}|{(r.Snooze ? 1 : 0)}";
        var xml = new System.Text.StringBuilder();
        xml.Append($"<toast launch=\"{X(launch)}\"{scenario}><visual><binding template=\"ToastGeneric\">");
        xml.Append($"<text>{title}</text><text>{at.ToString("HH:mm", CultureInfo.InvariantCulture)}</text>");
        if (r.Note.Length > 0) xml.Append($"<text>{X(r.Note)}</text>");
        xml.Append("</binding></visual>");
        xml.Append(r.Sound == "none"
            ? "<audio silent=\"true\"/>"
            : $"<audio src=\"{Sounds[r.Sound]}\"{(LoopingSounds.Contains(r.Sound) ? " loop=\"true\"" : "")}/>");
        if (r.Snooze)
        {
            xml.Append("<actions><input id=\"snoozeTime\" type=\"selection\" defaultInput=\"10\">");
            foreach (var (minutes, label) in new[] { ("5", "5 דקות"), ("10", "10 דקות"), ("30", "חצי שעה"), ("60", "שעה") })
                xml.Append($"<selection id=\"{minutes}\" content=\"{label}\"/>");
            xml.Append("</input><action activationType=\"system\" arguments=\"snooze\" hint-inputId=\"snoozeTime\" content=\"נודניק\"/>");
            xml.Append(OpenButtons);
            xml.Append("<action activationType=\"system\" arguments=\"dismiss\" content=\"סגור\"/></actions>");
        }
        else
            // Open buttons always; a reminder or alarm also needs a button to be closed with.
            xml.Append("<actions>" + OpenButtons +
                (r.Style == "brief" ? "" : "<action activationType=\"system\" arguments=\"dismiss\" content=\"סגור\"/>") + "</actions>");
        xml.Append("</toast>");
        var doc = new XmlDocument();
        doc.LoadXml(xml.ToString());
        return doc;
    }

    /// <summary>Windows keeps notification settings for an app once it has shown a notification.</summary>
    private static bool HasShownNotification()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + AppId);
        return key != null;
    }

    /// <summary>"Reminder set": the title and when, shown once at once (see Save).</summary>
    private static XmlDocument Confirmation(string title, DateTime at)
    {
        string text = SecurityElement.Escape(title.Trim()) ?? "";
        string when = at.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
        var doc = new XmlDocument();
        doc.LoadXml("<toast><visual><binding template=\"ToastGeneric\">" +
            $"<text>התזכורת נקבעה</text><text>{text} · {when}</text></binding></visual><audio silent=\"true\"/></toast>");
        return doc;
    }

    private static ToastNotifier Notifier()
    {
        EnsureRegistered();
        return ToastNotificationManager.CreateToastNotifier(AppId);
    }

    /// <summary>The app id Windows needs to show our notifications (name and icon), per user.</summary>
    private static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered) return;
            using var key = Registry.CurrentUser.CreateSubKey(AppIdKey);
            if (key.GetValue("DisplayName") is not string)
                key.SetValue("DisplayName", "כתבי הקודש");
            _registered = true;
        }
    }

    private static RemindersResult Run(Action<RemindersResult> work)
    {
        var result = new RemindersResult();
        try { work(result); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException)
        {
            result.Error = $"{ex.Message} (0x{ex.HResult:X8})".Trim();
        }
        return result;
    }
}
