using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskKit.Core.Models;

namespace DeskKit.Widgets.Clock;

/// <summary>
/// Shows the time and date. Refreshes from the shell's shared tick rather than a
/// timer of its own.
/// </summary>
public sealed partial class ClockViewModel : WidgetViewModel, ITickAware
{
    private const string KeyUse24Hour = "use24Hour";
    private const string KeyShowSeconds = "showSeconds";
    private const string KeyShowDate = "showDate";
    private const string KeyShowWeekday = "showWeekday";
    private const string KeyTimeFontSize = "timeFontSize";

    private bool _loading = true;

    [ObservableProperty]
    public partial string TimeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Use24Hour { get; set; }

    [ObservableProperty]
    public partial bool ShowSeconds { get; set; }

    [ObservableProperty]
    public partial bool ShowDate { get; set; }
    [ObservableProperty]
    public partial bool ShowWeekday { get; set; }

    [ObservableProperty]
    public partial double TimeFontSize { get; set; }

    public ClockViewModel(WidgetContext context)
        : base(context)
    {
        Use24Hour = Settings.Get(KeyUse24Hour, true);
        ShowSeconds = Settings.Get(KeyShowSeconds, false);
        ShowDate = Settings.Get(KeyShowDate, true);
        ShowWeekday = Settings.Get(KeyShowWeekday, true);
        TimeFontSize = Math.Clamp(Settings.Get(KeyTimeFontSize, 38d), 18, 96);

        _loading = false;

        OnTick(DateTimeOffset.Now);
    }

    /// <summary>Font size of the secondary date line, derived from the time size.</summary>
    public double DateFontSize => Math.Clamp(TimeFontSize * 0.32, 10, 28);

    public override Control CreateView() => new ClockView { DataContext = this };

    public override Control CreateSettingsView() => new ClockSettingsView { DataContext = this };

    public void OnTick(DateTimeOffset now)
    {
        var timeFormat = (Use24Hour, ShowSeconds) switch
        {
            (true, true) => "HH:mm:ss",
            (true, false) => "HH:mm",
            (false, true) => "h:mm:ss tt",
            (false, false) => "h:mm tt",
        };

        // An invariant-ish fallback keeps the widget readable if the current
        // culture has an unexpected pattern.
        var culture = CultureInfo.CurrentCulture;

        TimeText = now.ToString(timeFormat, culture);

        var date = now.ToString("D", culture);
        DateText = ShowWeekday ? $"{date} {now.ToString("dddd", culture)}" : date;
    }

    partial void OnUse24HourChanged(bool value) => Persist(KeyUse24Hour, value);

    partial void OnShowSecondsChanged(bool value) => Persist(KeyShowSeconds, value);

    partial void OnShowWeekdayChanged(bool value) => Persist(KeyShowWeekday, value);

    partial void OnShowDateChanged(bool value) => Persist(KeyShowDate, value);

    partial void OnTimeFontSizeChanged(double value)
    {
        OnPropertyChanged(nameof(DateFontSize));
        Persist(KeyTimeFontSize, Math.Round(value));
    }

    private void Persist<T>(string key, T value)
    {
        if (_loading)
            return;

        Settings.Set(key, value);
        Host.RequestSave();

        // Re-render immediately so the change is visible while the slider moves.
        OnTick(DateTimeOffset.Now);
    }
}
