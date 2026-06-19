using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DivAcerManagerMax;

/// <summary>
/// The FanProfileStorage class provides utility methods to load and save user-configured fan profiles
/// to and from the local filesystem.
/// Profiles are serialized into JSON format and stored in the application's configuration directory
/// under the user's Application Data folder (typically ~/.config/DivAcerManagerMax on Linux).
/// If no profile storage file is present, this class returns a pre-configured set of default profiles:
/// Balanced, Quiet, and Performance curves.
/// </summary>
public static class FanProfileStorage
{
    /// <summary>
    /// Static read-only serialization options configuration.
    /// Configured with WriteIndented set to true to ensure that the saved JSON file is nicely formatted
    /// and readable for users who might inspect the configuration manually.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Gets the absolute filesystem path pointing to the JSON file where fan profiles are saved.
    /// It queries the SpecialFolder.ApplicationData configuration path, fallbacks to the UserProfile path
    /// if ApplicationData is empty or unavailable, creates the "DivAcerManagerMax" subdirectory if it doesn't
    /// exist, and returns the path to the "fan-profiles.json" configuration file.
    /// </summary>
    private static string ProfilesPath
    {
        get
        {
            // Retrieve system-defined location for application data configuration files
            var configDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(configDir))
            {
                // Fallback to primary home directory path if ApplicationData returns empty (common in sandboxed setups)
                configDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            // Combine base configuration path with our application directory name
            var appDir = Path.Combine(configDir, "DivAcerManagerMax");
            
            // Ensure the directory exists on disk; creates it recursively if necessary
            Directory.CreateDirectory(appDir);
            
            // Return the full file destination path for fan profile parameters
            return Path.Combine(appDir, "fan-profiles.json");
        }
    }

    /// <summary>
    /// Asynchronously reads the JSON profile storage file from disk and deserializes it into an ObservableCollection
    /// of FanProfile instances.
    /// If the configuration file does not exist, or contains an empty list, this method automatically
    /// returns the pre-populated default profiles list to initialize the editor.
    /// </summary>
    /// <returns>A task representing the asynchronous load operation, yielding the loaded collection of FanProfiles.</returns>
    public static async Task<ObservableCollection<FanProfile>> LoadAsync()
    {
        // Return default settings if no profiles have been saved on this machine yet
        if (!File.Exists(ProfilesPath))
            return DefaultProfiles();

        // Open a stream to read the file asynchronously
        await using var stream = File.OpenRead(ProfilesPath);
        
        // Deserialize JSON array into profile collection
        var profiles = await JsonSerializer.DeserializeAsync<ObservableCollection<FanProfile>>(stream, JsonOptions);
        
        // Return the deserialized collection if it contains items, otherwise fallback to factory defaults
        return profiles is { Count: > 0 } ? profiles : DefaultProfiles();
    }

    /// <summary>
    /// Asynchronously serializes the given collection of FanProfiles into JSON format and saves it
    /// to the application's configuration file on disk, overwriting any existing settings.
    /// </summary>
    /// <param name="profiles">The ObservableCollection of FanProfile objects to save.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public static async Task SaveAsync(ObservableCollection<FanProfile> profiles)
    {
        // Open or create the target JSON file, overwriting the previous contents
        await using var stream = File.Create(ProfilesPath);
        
        // Serialize the active profile list asynchronously to the stream using JSON settings
        await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions);
    }

    /// <summary>
    /// Creates and returns a hardcoded default list of fan profiles.
    /// This list contains:
    /// 1. A default "Balanced Curve" profile (using the base constructor configurations).
    /// 2. A "Quiet" profile which regulates fan speed conservatively to minimize operational noise.
    /// 3. A "Performance" profile which runs fans aggressively at lower temperature thresholds to prevent thermal throttling.
    /// </summary>
    /// <returns>An ObservableCollection containing default FanProfile templates.</returns>
    private static ObservableCollection<FanProfile> DefaultProfiles()
    {
        return
        [
            // Default Balanced profile
            new FanProfile(),
            
            // Custom Quiet profile
            new FanProfile
            {
                Name = "Quiet",
                Target = "All Fans",
                Points =
                [
                    new() { Temperature = 35, FanPercent = 18 },
                    new() { Temperature = 55, FanPercent = 30 },
                    new() { Temperature = 75, FanPercent = 55 },
                    new() { Temperature = 92, FanPercent = 90 }
                ]
            },
            
            // Custom Performance profile
            new FanProfile
            {
                Name = "Performance",
                Target = "All Fans",
                Points =
                [
                    new() { Temperature = 30, FanPercent = 35 },
                    new() { Temperature = 50, FanPercent = 55 },
                    new() { Temperature = 70, FanPercent = 78 },
                    new() { Temperature = 85, FanPercent = 100 }
                ]
            }
        ];
    }
}
