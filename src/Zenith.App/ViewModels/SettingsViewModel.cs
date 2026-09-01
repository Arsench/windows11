using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenith.App.Localization;
using Zenith.App.Services;
using Zenith.Core.Abstractions;
using Zenith.Core.Licensing;
using Zenith.Core.Safety;
using Zenith.Core.Settings;

namespace Zenith.App.ViewModels;

public sealed record ChoiceOption<T>(T Value, string Label, string Description);

public sealed partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private const string NoticesFileName = "THIRD-PARTY-NOTICES.md";

    private readonly ISettingsStore _settings;
    private readonly ThemeService _theme;
    private readonly LicenseService _license;
    private readonly PathSafetyGuard _safety;
    private readonly IShellService _shell;
    private readonly IDialogService _dialogs;

    private bool _loading;

    [ObservableProperty] private ChoiceOption<ThemePreference>? _selectedTheme;
    [ObservableProperty] private ChoiceOption<AppLanguage>? _selectedLanguage;
    [ObservableProperty] private ChoiceOption<int>? _selectedInterval;
    [ObservableProperty] private ChoiceOption<DeletionBehavior>? _selectedDeletion;
    [ObservableProperty] private string _defaultMoveFolderText = string.Empty;

    [ObservableProperty] private string _licenseKeyInput = string.Empty;
    [ObservableProperty] private string _licenseStateText = string.Empty;
    [ObservableProperty] private string _licenseHintText = string.Empty;
    [ObservableProperty] private bool _hasLicenseKey;

    private static Loc L => Loc.Instance;

    public SettingsViewModel(
        ISettingsStore settings,
        ThemeService theme,
        LicenseService license,
        PathSafetyGuard safety,
        IShellService shell,
        IDialogService dialogs)
    {
        _settings = settings;
        _theme = theme;
        _license = license;
        _safety = safety;
        _shell = shell;
        _dialogs = dialogs;

        RebuildOptions();
        L.LanguageChanged += (_, _) => RebuildOptions();
    }

    public ObservableCollection<ChoiceOption<ThemePreference>> ThemeOptions { get; } = [];

    public ObservableCollection<ChoiceOption<AppLanguage>> LanguageOptions { get; } = [];

    public ObservableCollection<ChoiceOption<int>> IntervalOptions { get; } = [];

    public ObservableCollection<ChoiceOption<DeletionBehavior>> DeletionOptions { get; } = [];

    public ObservableCollection<string> ExcludedPaths { get; } = [];

    public string VersionText
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "Zenith" : $"Zenith {version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public string LogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zenith", "logs");

    /// <summary>
    /// Rehace las listas de opciones con los textos del idioma actual y
    /// restaura la selección, que se pierde al sustituir los elementos.
    /// </summary>
    private void RebuildOptions()
    {
        _loading = true;
        try
        {
            var current = _settings.Current;

            Fill(ThemeOptions,
            [
                new(ThemePreference.System, L["ThemeSystem"], L["ThemeSystemHint"]),
                new(ThemePreference.Light, L["ThemeLight"], L["ThemeLightHint"]),
                new(ThemePreference.Dark, L["ThemeDark"], L["ThemeDarkHint"])
            ]);

            Fill(LanguageOptions,
            [
                new(AppLanguage.System, L["LanguageSystem"], L["LanguageSystemHint"]),
                new(AppLanguage.Spanish, L["LanguageSpanish"], L["LanguageSpanishHint"]),
                new(AppLanguage.English, L["LanguageEnglish"], L["LanguageEnglishHint"])
            ]);

            Fill(IntervalOptions,
            [
                new(500, L["IntervalFast"], L["IntervalFastHint"]),
                new(1000, L["IntervalNormal"], L["IntervalNormalHint"]),
                new(2000, L["IntervalSaver"], L["IntervalSaverHint"])
            ]);

            Fill(DeletionOptions,
            [
                new(DeletionBehavior.RecycleBin, L["DeletionRecycle"], L["DeletionRecycleHint"]),
                new(DeletionBehavior.Permanent, L["DeletionPermanent"], L["DeletionPermanentHint"])
            ]);

            SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == current.Theme) ?? ThemeOptions[0];
            SelectedLanguage = LanguageOptions.FirstOrDefault(o => o.Value == current.Language) ?? LanguageOptions[0];
            SelectedInterval = IntervalOptions.FirstOrDefault(o => o.Value == current.MonitorIntervalMs) ?? IntervalOptions[1];
            SelectedDeletion = DeletionOptions.FirstOrDefault(o => o.Value == current.DeletionBehavior) ?? DeletionOptions[0];

            DefaultMoveFolderText = string.IsNullOrWhiteSpace(current.DefaultMoveFolder)
                ? L["SettingsMoveFolderUnset"]
                : current.DefaultMoveFolder;

            RefreshLicense();
        }
        finally
        {
            _loading = false;
        }
    }

    private static void Fill<T>(ObservableCollection<ChoiceOption<T>> target, IEnumerable<ChoiceOption<T>> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    public void OnNavigatedTo()
    {
        RebuildOptions();

        _loading = true;
        try
        {
            ExcludedPaths.Clear();
            foreach (var path in _settings.Current.ExcludedPaths) ExcludedPaths.Add(path);
        }
        finally
        {
            _loading = false;
        }
    }

    public void OnNavigatedFrom() { }

    partial void OnSelectedThemeChanged(ChoiceOption<ThemePreference>? value)
    {
        if (_loading || value is null) return;

        _theme.Apply(value.Value);
        _ = _settings.UpdateAsync(s => s.Theme = value.Value);
    }

    partial void OnSelectedLanguageChanged(ChoiceOption<AppLanguage>? value)
    {
        if (_loading || value is null) return;

        // Apply dispara LanguageChanged, que a su vez rehace estas listas.
        L.Apply(value.Value);
        _ = _settings.UpdateAsync(s => s.Language = value.Value);
    }

    partial void OnSelectedIntervalChanged(ChoiceOption<int>? value)
    {
        if (_loading || value is null) return;
        _ = _settings.UpdateAsync(s => s.MonitorIntervalMs = value.Value);
    }

    partial void OnSelectedDeletionChanged(ChoiceOption<DeletionBehavior>? value)
    {
        if (_loading || value is null) return;
        _ = _settings.UpdateAsync(s => s.DeletionBehavior = value.Value);
    }

    // ---------------------------------------------------------------- carpetas

    [RelayCommand]
    private async Task ChooseMoveFolderAsync()
    {
        var folder = _dialogs.PickFolder(L["SettingsMoveFolderPick"], _settings.Current.DefaultMoveFolder);
        if (folder is null) return;

        await _settings.UpdateAsync(s => s.DefaultMoveFolder = folder).ConfigureAwait(true);
        DefaultMoveFolderText = folder;
    }

    [RelayCommand]
    private async Task AddExclusionAsync()
    {
        var folder = _dialogs.PickFolder(L["SettingsExclusionPick"]);
        if (folder is null) return;

        if (ExcludedPaths.Any(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase))) return;

        ExcludedPaths.Add(folder);
        await PersistExclusionsAsync().ConfigureAwait(true);
        _dialogs.Notify(L["SettingsExclusionAdded"], ToastKind.Success);
    }

    [RelayCommand]
    private async Task RemoveExclusionAsync(string? path)
    {
        if (path is null) return;

        ExcludedPaths.Remove(path);
        await PersistExclusionsAsync().ConfigureAwait(true);
    }

    private async Task PersistExclusionsAsync()
    {
        var paths = ExcludedPaths.ToList();
        await _settings.UpdateAsync(s => s.ExcludedPaths = paths).ConfigureAwait(true);
        _safety.SetUserExclusions(paths);
    }

    // ---------------------------------------------------------------- licencia

    private void RefreshLicense()
    {
        _license.Refresh();

        var status = _license.Current;
        LicenseStateText = Present.LicenseStatus(status.State);
        LicenseHintText = Present.LicenseStatusHint(status.State);
        HasLicenseKey = status.Key is not null;
        LicenseKeyInput = status.Key ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveLicenseAsync()
    {
        var validation = await _license.ActivateAsync(LicenseKeyInput).ConfigureAwait(true);

        if (validation != LicenseKeyValidation.Ok)
        {
            _dialogs.Notify(Present.LicenseValidation(validation), ToastKind.Warning);
            return;
        }

        RefreshLicense();
        _dialogs.Notify(L["LicenseSaved"], ToastKind.Success);
    }

    [RelayCommand]
    private async Task RemoveLicenseAsync()
    {
        await _license.ClearAsync().ConfigureAwait(true);
        RefreshLicense();
        _dialogs.Notify(L["LicenseRemoved"], ToastKind.Info);
    }

    /// <summary>
    /// Abre los avisos de licencias de terceros que se copian junto al ejecutable.
    /// Es lo que hay que conservar si algún día esto se distribuye o se vende.
    /// </summary>
    [RelayCommand]
    private void OpenThirdPartyNotices()
    {
        var path = Path.Combine(AppContext.BaseDirectory, NoticesFileName);
        if (File.Exists(path)) _shell.OpenFile(path);
        else _dialogs.Notify(L["CommonNotAvailable"], ToastKind.Warning);
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(LogFolder);
        _shell.OpenFolder(LogFolder);
    }
}
