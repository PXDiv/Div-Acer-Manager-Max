using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DivAcerManagerMax;

public static class FanProfileStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string ProfilesPath
    {
        get
        {
            var configDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(configDir))
                configDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var appDir = Path.Combine(configDir, "DivAcerManagerMax");
            Directory.CreateDirectory(appDir);
            return Path.Combine(appDir, "fan-profiles.json");
        }
    }

    public static async Task<ObservableCollection<FanProfile>> LoadAsync()
    {
        if (!File.Exists(ProfilesPath))
            return DefaultProfiles();

        await using var stream = File.OpenRead(ProfilesPath);
        var profiles = await JsonSerializer.DeserializeAsync<ObservableCollection<FanProfile>>(stream, JsonOptions);
        return profiles is { Count: > 0 } ? profiles : DefaultProfiles();
    }

    public static async Task SaveAsync(ObservableCollection<FanProfile> profiles)
    {
        await using var stream = File.Create(ProfilesPath);
        await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions);
    }

    private static ObservableCollection<FanProfile> DefaultProfiles()
    {
        return
        [
            new FanProfile(),
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
