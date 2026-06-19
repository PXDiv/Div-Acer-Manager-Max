using Avalonia;
using System;
using SkiaSharp;
using Avalonia.Skia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;

namespace DivAcerManagerMax;

/// <summary>
/// The Program class serves as the main entry point for the DivAcerManagerMax application.
/// It is responsible for configuring and initializing the Avalonia framework, setting up
/// the application runtime context, and starting the main event loop with a classic desktop lifetime.
/// This class configures logging, font loading, platform detection, and delegates the lifetime
/// management to the Avalonia framework.
/// </summary>
class Program
{
    /// <summary>
    /// The Main method is the standard entry point of the executable.
    /// It is marked with the [STAThread] attribute, which specifies that the COM threading model
    /// for the application is single-threaded apartment (STA). This is required for many Windows
    /// and GUI APIs to function correctly.
    /// It builds the Avalonia application instance and runs the classic desktop lifetime loop,
    /// passing down the command-line arguments.
    /// </summary>
    /// <param name="args">An array of command-line arguments passed to the application upon startup.</param>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// The BuildAvaloniaApp method configures the Avalonia application instance.
    /// It registers the App class as the main application control logic, detects the current platform
    /// (such as Linux, Windows, or macOS) to configure appropriate rendering backends (e.g. X11, Wayland, Win32),
    /// initializes the Inter Font family for consistent cross-platform UI typography, and routes
    /// internal Avalonia framework diagnostic messages to the trace listener system for easier debugging.
    /// </summary>
    /// <returns>An AppBuilder object initialized and configured for the Avalonia application lifetime.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
