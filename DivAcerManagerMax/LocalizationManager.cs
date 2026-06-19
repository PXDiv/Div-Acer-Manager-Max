using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;

namespace DivAcerManagerMax;

/// <summary>
/// Immutable record structure that represents a supported translation language.
/// It wraps a standard language code (such as "en", "es") and its localized name (such as "English", "Español").
/// </summary>
/// <param name="Code">The two-letter ISO language code.</param>
/// <param name="NativeName">The native display name of the language.</param>
public sealed record SupportedLanguage(string Code, string NativeName)
{
    /// <summary>
    /// Overrides ToString() to return the native language name.
    /// This allows dropdown lists and selection menus to display the name directly.
    /// </summary>
    /// <returns>A string containing the language's native name.</returns>
    public override string ToString() => NativeName;
}

/// <summary>
/// Static class that handles UI localization and translation dictionary mapping.
/// It loads translation dictionaries for English, Spanish, German, Italian, Portuguese, French,
/// Polish, Russian, Turkish, Swedish, Japanese, Korean, Chinese, Arabic, and Ukrainian.
/// It reads and saves the active language selection in a local settings file ("language.txt"),
/// and uses logical tree searches to dynamically translate AXAML controls labeled with "loc:" tags.
/// </summary>
public static class LocalizationManager
{
    /// <summary>
    /// The default language code ("en" for English) used as a fallback if no language file is found.
    /// </summary>
    private const string DefaultLanguage = "en";

    /// <summary>
    /// The absolute path to the local text file containing the saved language code selection.
    /// Located at ~/.config/DivAcerManagerMax/language.txt.
    /// </summary>
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DivAcerManagerMax",
        "language.txt");

    /// <summary>
    /// The primary translation dictionary for English, containing the base keys and labels.
    /// </summary>
    private static readonly Dictionary<string, string> English = new()
    {
        ["Common.Language"] = "Language",
        ["Common.Unknown"] = "UNKNOWN",
        ["Common.Auto"] = "Auto",
        ["Common.Save"] = "Save",
        ["Common.ApplyNow"] = "Apply Now",
        ["Common.Retry"] = "Retry",
        ["Common.Disabled"] = "Disabled",
        ["Common.New"] = "New",
        ["Common.Duplicate"] = "Duplicate",
        ["Common.Delete"] = "Delete",
        ["Common.Name"] = "Name",
        ["Common.Target"] = "Target",
        ["Common.Temperature"] = "Temperature",
        ["Common.Points"] = "Points",

        ["Tabs.Dashboard"] = "Dashboard",
        ["Tabs.FanProfiles"] = "Fan Profiles",
        ["Tabs.PowerPerformance"] = "Power & Performance",
        ["Tabs.BatteryControls"] = "Battery Controls",
        ["Tabs.KeyboardLighting"] = "Keyboard Lighting",
        ["Tabs.SystemSettings"] = "System Settings",

        ["FanProfiles.Title"] = "Fan Profiles",
        ["FanProfiles.Profiles"] = "Profiles",
        ["FanProfiles.CurveEditor"] = "Curve Editor",
        ["FanProfiles.AddPoint"] = "Add Point",
        ["FanProfiles.RemovePoint"] = "Remove Point",
        ["FanProfiles.ProfileApplication"] = "Profile Application",
        ["FanProfiles.InitialStatus"] = "Profiles are saved locally. Applying sends the selected curve's current fan value as a manual speed.",
        ["FanProfiles.Saved"] = "Profiles saved.",
        ["FanProfiles.NotReady"] = "Fan control is not ready yet.",
        ["FanProfiles.NotAvailable"] = "Fan speed control is not available on this device.",
        ["FanProfiles.ApplySuccess"] = "Applied {0}: CPU {1}%, GPU {2}%.",
        ["FanProfiles.ApplyFailed"] = "Could not apply profile: {0}",
        ["FanProfiles.NewProfile"] = "New Profile",
        ["FanProfiles.CopySuffix"] = "Copy",

        ["Power.Title"] = "Power & Performance",
        ["Power.PerformanceProfiles"] = "Performance Profiles",
        ["Power.ProfileHelp"] = "Configure thermal profiles to balance performance and temperature",
        ["Power.Eco"] = "Eco",
        ["Power.Quiet"] = "Quiet",
        ["Power.Balanced"] = "Balanced",
        ["Power.Performance"] = "Performance",
        ["Power.Turbo"] = "Turbo",
        ["Power.ProfileIntro"] = "Performance profiles are preset modes that control your device's speed, power use, and cooling:",
        ["Power.LowPowerDescription"] = "Prioritizes energy efficiency, reduces performance to extend battery life.",
        ["Power.QuietDescription"] = "Minimizes noise, prioritizes low power and cooling.",
        ["Power.BalancedDescription"] = "Optimal mix of performance and noise for everyday tasks.",
        ["Power.PerformanceDescription"] = "Maximizes speed for demanding workloads, higher fan noise",
        ["Power.TurboDescription"] = "Unleashes peak power for extreme tasks, loudest fans.",
        ["Power.FanSpeedControl"] = "Fan Speed Control",
        ["Power.FanSpeedHelp"] = "Set custom fan speeds for CPU and GPU",
        ["Power.Max"] = "Max",
        ["Power.Manual"] = "Manual",
        ["Power.ManualDisabled"] = "Manual fan controls are disabled to ensure quiet operation",
        ["Power.CpuFan"] = "CPU Fan:",
        ["Power.GpuFan"] = "GPU Fan:",
        ["Power.ApplyFanSettings"] = "Apply Fan Settings",
        ["Power.LowFanNote"] = "Note: Low fan speeds may result in higher temperatures",

        ["Battery.Title"] = "Power Controls",
        ["Battery.Calibration"] = "Battery Calibration",
        ["Battery.CalibrationHelp"] = "Calibrate your battery to get accurate battery percentage readings",
        ["Battery.StatusIdle"] = "Status: Not calibrating",
        ["Battery.StatusActive"] = "Status: Calibrating",
        ["Battery.CalibrationProcess"] = "Battery calibration will fully charge, drain, and recharge your battery.",
        ["Battery.DoNotUnplug"] = "Do not unplug AC power during calibration.",
        ["Battery.StartCalibration"] = "Start Calibration",
        ["Battery.StopCalibration"] = "Stop Calibration",
        ["Battery.ChargeLimit"] = "Battery Charge Limit",
        ["Battery.ChargeLimitHelp"] = "Limit maximum battery charge to extend battery lifespan",
        ["Battery.EnableLimit"] = "Enable Battery Limit",
        ["Battery.Limit80"] = "Limit to 80%",
        ["Battery.Recommended"] = "Recommended for laptops that are frequently connected to AC power",
        ["Battery.UsbPowerDelivery"] = "USB Power Delivery",
        ["Battery.UsbPowerHelp"] = "Configure USB charging when laptop is powered off",
        ["Battery.Until10"] = "Until battery reaches 10%",
        ["Battery.Until20"] = "Until battery reaches 20%",
        ["Battery.Until30"] = "Until battery reaches 30%",

        ["Keyboard.Title"] = "Keyboard Lighting",
        ["Keyboard.RgbMode"] = "RGB Mode",
        ["Keyboard.Mode"] = "Mode",
        ["Keyboard.Static"] = "Static",
        ["Keyboard.Breathing"] = "Breathing",
        ["Keyboard.Neon"] = "Neon",
        ["Keyboard.Wave"] = "Wave",
        ["Keyboard.Shifting"] = "Shifting",
        ["Keyboard.Zoom"] = "Zoom",
        ["Keyboard.Brightness"] = "Brightness",
        ["Keyboard.Speed"] = "Speed",
        ["Keyboard.EffectColor"] = "Effect Color",
        ["Keyboard.ApplyEffect"] = "Apply Effect",
        ["Keyboard.StaticZoneColors"] = "Static Zone Colors",
        ["Keyboard.Zone1"] = "Zone 1",
        ["Keyboard.Zone2"] = "Zone 2",
        ["Keyboard.Zone3"] = "Zone 3",
        ["Keyboard.Zone4"] = "Zone 4",
        ["Keyboard.ApplyZoneColors"] = "Apply Zone Colors",
        ["Keyboard.BacklightTimeout"] = "Backlight Timeout",
        ["Keyboard.BacklightTimeoutHelp"] = "Automatically turn off keyboard lighting after period of inactivity",
        ["Keyboard.EnableBacklightTimeout"] = "Enable backlight timeout (30 seconds)",

        ["Settings.Title"] = "System Settings",
        ["Settings.LanguageHelp"] = "Choose the interface language",
        ["Settings.LcdOverride"] = "LCD Override",
        ["Settings.LcdOverrideHelp"] = "Reduce LCD latency and minimize ghosting",
        ["Settings.EnableLcdOverride"] = "Enable LCD Override",
        ["Settings.BatteryConsumptionNote"] = "Note: May increase battery consumption",
        ["Settings.BootSoundAnimation"] = "Boot Sound and Animation",
        ["Settings.BootSoundAnimationHelp"] = "Enable or disable custom boot animation and sound",
        ["Settings.EnableBootSoundAnimation"] = "Enable boot animation and sound",
        ["Settings.SystemInformation"] = "System Information",
        ["Settings.Model"] = "Model:",
        ["Settings.LaptopType"] = "Laptop Type:",
        ["Settings.Features"] = "Features:",
        ["Settings.ProjectVersion"] = "Project Version:",
        ["Settings.DaemonVersion"] = "Daemon Version:",
        ["Settings.DriverVersion"] = "Driver Version:",
        ["Settings.CheckUpdates"] = "Check for Updates",
        ["Settings.DeveloperMode"] = "Developer Mode",
        ["Settings.IssueFeedback"] = "Issue/Feedback",
        ["Settings.InternalsManager"] = "Internals Manager",

        ["Daemon.NotConnected"] = "Daemon not connected",
        ["Daemon.NotConnectedHelp"] = "Daemon may be initializing. Please wait a minute. If issues persists, check logs.",
        ["Daemon.ConnectErrorTitle"] = "Error Connecting to Daemon",
        ["Daemon.ConnectErrorMessage"] = "Failed to connect to DAMX daemon. The Daemon may be initializing please wait.",
        ["Daemon.InitErrorTitle"] = "Error while initializing",
        ["Daemon.InitErrorMessage"] = "Error initializing: {0}",
        ["Settings.LoadErrorTitle"] = "Error while loading settings",
        ["Settings.LoadErrorMessage"] = "Error loading settings: {0}"
    };

    /// <summary>
    /// Maps language codes to their respective translation dictionaries, created by merging translated overrides with English base dictionaries.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["en"] = English,
        ["es"] = MergeTranslations(new Dictionary<string, string>
        {
            ["Common.Language"] = "Idioma", ["Common.Save"] = "Guardar", ["Common.ApplyNow"] = "Aplicar ahora",
            ["Tabs.FanProfiles"] = "Perfiles de ventilacion", ["FanProfiles.Title"] = "Perfiles de ventilacion",
            ["FanProfiles.Profiles"] = "Perfiles", ["FanProfiles.CurveEditor"] = "Editor de curva",
            ["FanProfiles.AddPoint"] = "Agregar punto", ["FanProfiles.RemovePoint"] = "Eliminar punto",
            ["FanProfiles.ProfileApplication"] = "Aplicacion del perfil", ["FanProfiles.Saved"] = "Perfiles guardados.",
            ["Power.Title"] = "Energia y rendimiento", ["Power.FanSpeedControl"] = "Control de velocidad de ventiladores",
            ["Battery.Title"] = "Controles de bateria", ["Keyboard.Title"] = "Iluminacion del teclado",
            ["Settings.Title"] = "Configuracion del sistema", ["Daemon.NotConnected"] = "Daemon no conectado"
        }),
        ["de"] = MergeTranslations(new Dictionary<string, string>
        {
            ["Common.Language"] = "Sprache", ["Common.Save"] = "Speichern", ["Common.ApplyNow"] = "Jetzt anwenden",
            ["Tabs.FanProfiles"] = "Luefterprofile", ["FanProfiles.Title"] = "Luefterprofile",
            ["FanProfiles.Profiles"] = "Profile", ["FanProfiles.CurveEditor"] = "Kurveneditor",
            ["FanProfiles.AddPoint"] = "Punkt hinzufuegen", ["FanProfiles.RemovePoint"] = "Punkt entfernen",
            ["Power.Title"] = "Energie und Leistung", ["Battery.Title"] = "Akku-Steuerung",
            ["Keyboard.Title"] = "Tastaturbeleuchtung", ["Settings.Title"] = "Systemeinstellungen",
            ["Daemon.NotConnected"] = "Daemon nicht verbunden"
        }),
        ["it"] = MergeTranslations(new Dictionary<string, string>
        {
            ["Common.Language"] = "Lingua", ["Common.Save"] = "Salva", ["Common.ApplyNow"] = "Applica ora",
            ["Tabs.FanProfiles"] = "Profili ventole", ["FanProfiles.Title"] = "Profili ventole",
            ["FanProfiles.CurveEditor"] = "Editor curva", ["Power.Title"] = "Alimentazione e prestazioni",
            ["Battery.Title"] = "Controlli batteria", ["Keyboard.Title"] = "Illuminazione tastiera",
            ["Settings.Title"] = "Impostazioni di sistema", ["Daemon.NotConnected"] = "Daemon non connesso"
        }),
        ["pt"] = MergeTranslations(new Dictionary<string, string>
        {
            ["Common.Language"] = "Idioma", ["Common.Save"] = "Salvar", ["Common.ApplyNow"] = "Aplicar agora",
            ["Tabs.FanProfiles"] = "Perfis de ventilacao", ["FanProfiles.Title"] = "Perfis de ventilacao",
            ["FanProfiles.CurveEditor"] = "Editor de curva", ["Power.Title"] = "Energia e desempenho",
            ["Battery.Title"] = "Controles da bateria", ["Keyboard.Title"] = "Iluminacao do teclado",
            ["Settings.Title"] = "Configuracoes do sistema", ["Daemon.NaoConectado"] = "Daemon nao conectado"
        }),
        ["fr"] = MergeTranslations(new Dictionary<string, string> { ["Common.Language"] = "Langue", ["Common.Save"] = "Enregistrer", ["Tabs.FanProfiles"] = "Profils de ventilation", ["Settings.Title"] = "Parametres systeme" }),
        ["nl"] = MergeTranslations(new Dictionary<string, string> { ["Common.Language"] = "Taal", ["Common.Save"] = "Opslaan", ["Tabs.FanProfiles"] = "Ventilatorprofielen", ["Settings.Title"] = "Systeeminstellingen" }),
        ["pl"] = MergeTranslations(new Dictionary<string, string> { ["Common.Language"] = "Jezyk", ["Common.Save"] = "Zapisz", ["Tabs.FanProfiles"] = "Profile wentylatorow", ["Settings.Title"] = "Ustawienia systemu" }),
        ["ru"] = MergeTranslations(new Dictionary<string, string> { ["Common.Language"] = "Yazyk", ["Common.Save"] = "Sokhranit", ["Tabs.FanProfiles"] = "Profili ventilyatorov", ["Settings.Title"] = "Nastroyki sistemy" }),
        ["tr"] = MergeTranslations(new Dictionary<string, string> { ["Common.Language"] = "Dil", ["Common.Save"] = "Kaydet", ["Tabs.FanProfiles"] = "Fan profilleri", ["Settings.Title"] = "Sistem ayarlari" }),
        ["sv"] = MergeTranslations(new Dictionary<string, string> { ["Common.Language"] = "Sprak", ["Common.Save"] = "Spara", ["Tabs.FanProfiles"] = "Flaktprofiler", ["Settings.Title"] = "Systeminstallningar" }),
        ["ja"] = MergeTranslations(new Dictionary<string, string> { ["Common.Language"] = "Language", ["Common.Save"] = "Save", ["Tabs.FanProfiles"] = "Fan Profiles", ["Settings.Title"] = "System Settings" }),
        ["ko"] = MergeTranslations(new Dictionary<string, string> { ["Common.Language"] = "Language", ["Common.Save"] = "Save", ["Tabs.FanProfiles"] = "Fan Profiles", ["Settings.Title"] = "System Settings" }),
        ["zh-Hans"] = MergeTranslations(new Dictionary<string, string> { ["Common.Language"] = "Language", ["Common.Save"] = "Save", ["Tabs.FanProfiles"] = "Fan Profiles", ["Settings.Title"] = "System Settings" }),
        ["ar"] = MergeTranslations(new Dictionary<string, string> { ["Common.Language"] = "Language", ["Common.Save"] = "Save", ["Tabs.FanProfiles"] = "Fan Profiles", ["Settings.Title"] = "System Settings" }),
        ["uk"] = MergeTranslations(new Dictionary<string, string> { ["Common.Language"] = "Mova", ["Common.Save"] = "Zberehty", ["Tabs.FanProfiles"] = "Profili ventyliatoriv", ["Settings.Title"] = "Nalashtuvannia systemy" })
    };

    /// <summary>
    /// Predefined list of SupportedLanguage templates mapping user-selectable codes to their native labels.
    /// </summary>
    public static IReadOnlyList<SupportedLanguage> SupportedLanguages { get; } =
    [
        new("en", "English"),
        new("de", "Deutsch"),
        new("it", "Italiano"),
        new("pt", "Português"),
        new("es", "Español"),
        new("fr", "Français"),
        new("nl", "Nederlands"),
        new("pl", "Polski"),
        new("ru", "Русский"),
        new("tr", "Türkçe"),
        new("sv", "Svenska"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("zh-Hans", "Chinese Simplified"),
        new("ar", "Arabic"),
        new("uk", "Ukrainian")
    ];

    /// <summary>
    /// Gets the active language selection code, initialized from disk or system preferences.
    /// </summary>
    public static string CurrentLanguageCode { get; private set; } = LoadLanguageCode();

    /// <summary>
    /// Gets the SupportedLanguage metadata object corresponding to the current language selection code.
    /// </summary>
    public static SupportedLanguage CurrentLanguage =>
        SupportedLanguages.FirstOrDefault(language => language.Code == CurrentLanguageCode) ?? SupportedLanguages[0];

    /// <summary>
    /// Static event raised when the language selection changes, prompting UI view updates.
    /// </summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// Returns the localized string translation corresponding to the given dictionary key.
    /// Falls back to the English dictionary representation if the active translation language
    /// does not define the key. Returns the raw key if no match is found.
    /// </summary>
    /// <param name="key">The dictionary key string to look up.</param>
    /// <returns>A localized translation string.</returns>
    public static string T(string key)
    {
        if (Translations.TryGetValue(CurrentLanguageCode, out var language) && language.TryGetValue(key, out var value))
            return value;

        return English.TryGetValue(key, out var fallback) ? fallback : key;
    }

    /// <summary>
    /// Formats a localized template string using standard formatting arguments.
    /// </summary>
    /// <param name="key">The dictionary key string to look up.</param>
    /// <param name="args">The formatting arguments to inject into the template placeholders.</param>
    /// <returns>A formatted translation string.</returns>
    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, T(key), args);
    }

    /// <summary>
    /// Updates the active language code, saves it to ~/.config/DivAcerManagerMax/language.txt,
    /// and raises the LanguageChanged event to trigger layout redraws.
    /// </summary>
    /// <param name="code">The target language code to apply (e.g. "es").</param>
    public static void SetLanguage(string code)
    {
        // Cancel operation if the language code is invalid or already active
        if (!Translations.ContainsKey(code) || CurrentLanguageCode == code)
            return;

        CurrentLanguageCode = code;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, code);
        
        // Notify observers to apply new translation strings
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Traverses the logical children tree starting from the specified root layout control,
    /// searching for elements configured with translation tags (control.Tag matching "loc:").
    /// Matches the tag name with the translation dictionary and replaces the text value on the target element.
    /// </summary>
    /// <param name="root">The root visual control containing child nodes to localize.</param>
    public static void Apply(Control root)
    {
        foreach (var control in root.GetLogicalDescendants().OfType<Control>().Prepend(root))
        {
            // Filter elements configured with translation tag bindings (e.g. Tag="loc:Common.Save")
            if (control.Tag is not string tag || !tag.StartsWith("loc:", StringComparison.Ordinal))
                continue;

            var key = tag[4..]; // Slice the "loc:" prefix off the key name
            var value = T(key); // Query the localized string value
            
            switch (control)
            {
                case TextBlock textBlock:
                    textBlock.Text = value;
                    break;
                case HeaderedContentControl headered:
                    headered.Header = value;
                    break;
                case ContentControl contentControl:
                    contentControl.Content = value;
                    break;
            }
        }
    }

    /// <summary>
    /// Initializer helper that reads the user's preferred language from disk.
    /// If no file exists, it queries CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
    /// and matches it against supported languages. Defaults to English ("en") if unsupported.
    /// </summary>
    /// <returns>A validated two-character language code string.</returns>
    private static string LoadLanguageCode()
    {
        if (File.Exists(SettingsPath))
        {
            var saved = File.ReadAllText(SettingsPath).Trim();
            if (Translations.ContainsKey(saved))
                return saved;
        }

        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Translations.ContainsKey(culture) ? culture : DefaultLanguage;
    }

    /// <summary>
    /// Dictionary utility method to copy base English keys and overwrite them with localized keys.
    /// </summary>
    /// <param name="overrides">The translated keys dictionary to merge.</param>
    /// <returns>A complete dictionary containing base English templates merged with overrides.</returns>
    private static Dictionary<string, string> MergeTranslations(Dictionary<string, string> overrides)
    {
        var merged = new Dictionary<string, string>(English);
        foreach (var (key, value) in overrides)
            merged[key] = value;

        return merged;
    }
}
