using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace DivAcerManagerMax;

/// <summary>
/// The App class represents the main Avalonia Application class.
/// It acts as the central hub for the application life cycle, loading global resources,
/// parsing application-wide XAML styles, defining accent colors, and initializing
/// the main window on desktop platforms.
/// </summary>
public class App : Application
{
    /// <summary>
    /// The Initialize method is called when the application starts.
    /// It uses the AvaloniaXamlLoader to load and compile the App.axaml markup file,
    /// binding all styles, templates, and resource dictionaries defined in it.
    /// In addition, it sets up custom system accent colors and brushes (specifically, the custom blue color `#1B89D8`)
    /// dynamically in the application resources to ensure a consistent theme style.
    /// </summary>
    public override void Initialize()
    {
        // Load the associated XAML structure for the App control
        AvaloniaXamlLoader.Load(this);

        // Define a custom accent color for the user interface styling
        var accentColor = Color.Parse("#1B89D8");
        
        // Register the parsed accent color in the application's global resource dictionary
        Application.Current.Resources["SystemAccentColor"] = accentColor;
        
        // Register a brush using the accent color in the application's global resource dictionary for paint styling
        Application.Current.Resources["SystemAccentBrush"] = new SolidColorBrush(accentColor);
    }

    /// <summary>
    /// The OnFrameworkInitializationCompleted method is called by the Avalonia framework
    /// once the application initialization steps are fully finished.
    /// It checks if the current application lifetime matches a classic desktop application (such as standard Windows/Linux GUI).
    /// If so, it creates a new instance of the MainWindow class and assigns it as the primary desktop main window
    /// to handle OS-level window rendering and interaction events.
    /// Finally, it calls the base framework implementation to finalize the initialization sequence.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        // Check if our application is running on a desktop environment
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Instantiates and displays the main window of the DivAcerManagerMax tool
            desktop.MainWindow = new MainWindow();
        }

        // Call base class initialization code to complete native window lifecycle integration
        base.OnFrameworkInitializationCompleted();
    }
}