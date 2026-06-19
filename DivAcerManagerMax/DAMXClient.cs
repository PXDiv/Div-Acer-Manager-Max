using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DivAcerManagerMax;

/// <summary>
/// Client class responsible for communicating with the background system service daemon (DAMX-Daemon).
/// It handles connections over local Unix Domain Sockets ("/var/run/DAMX.sock"), manages retry loops
/// for commands, parses daemon states, caches feature flags, and updates hardware status values
/// (such as fan speeds, thermal profiles, keyboard lighting, and battery limits).
/// Implements IDisposable to release active socket connections and system resources.
/// </summary>
public class DAMXClient : IDisposable
{
    /// <summary>
    /// The local filesystem path where the daemon socket endpoint is located.
    /// </summary>
    private const string SocketPath = "/var/run/DAMX.sock";

    /// <summary>
    /// The maximum number of consecutive transmission retry attempts before marking the socket as failed.
    /// </summary>
    private const int MaxRetryAttempts = 3;

    /// <summary>
    /// The waiting delay in milliseconds between connection or transmission retry attempts.
    /// </summary>
    private const int RetryDelayMs = 500;

    /// <summary>
    /// Cache collection holding the list of features reported as supported by the daemon on this device model.
    /// </summary>
    private HashSet<string> _availableFeatures = new();

    /// <summary>
    /// Backing flag indicating whether the instance has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// The active Unix domain stream socket handle used to send and receive commands.
    /// </summary>
    private Socket _socket;

    /// <summary>
    /// Initializes a new instance of the DAMXClient class.
    /// Sets the initial connection flag to false.
    /// </summary>
    public DAMXClient()
    {
        IsConnected = false;
    }

    /// <summary>
    /// Gets a value indicating whether the client is currently connected to the daemon.
    /// </summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Queries the local feature flag cache to check if a specific hardware control feature
    /// (such as "fan_speed", "battery_limiter", "thermal_profile") is supported by this laptop model.
    /// </summary>
    /// <param name="featureName">The descriptive identifier name of the feature to check.</param>
    /// <returns>True if the feature is supported and active, false otherwise.</returns>
    public bool IsFeatureAvailable(string featureName)
    {
        return _availableFeatures.Contains(featureName);
    }

    /// <summary>
    /// Verifies the socket connection by sending a "ping" command to the daemon.
    /// Updates the connection state if the socket is closed or the command fails.
    /// </summary>
    /// <returns>True if the daemon responds successfully, false otherwise.</returns>
    private async Task<bool> ValidateConnection()
    {
        if (!IsConnected) return false;

        try
        {
            // Send a ping command and check the success property in the response
            var response = await SendCommandAsync("ping");
            return response.RootElement.GetProperty("success").GetBoolean();
        }
        catch
        {
            IsConnected = false;
            return false;
        }
    }

    /// <summary>
    /// Connects to the daemon Unix domain socket.
    /// If already connected, it validates the connection state first.
    /// Upon a successful connection, it queries and caches the supported features list.
    /// </summary>
    /// <returns>True if the connection was established successfully, false otherwise.</returns>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            // Return true if the active connection is valid
            if (IsConnected && await ValidateConnection()) return true;

            // Dispose any orphaned socket instances
            _socket?.Dispose();
            
            // Create a new stream socket using Unix address protocols
            _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            var endpoint = new UnixDomainSocketEndPoint(SocketPath);

            // Connect asynchronously to the socket path
            await _socket.ConnectAsync(endpoint);
            IsConnected = true;

            // Query and cache the supported features
            await RefreshAvailableFeaturesAsync();

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to daemon: {ex.Message}");
            IsConnected = false;
            return false;
        }
    }

    /// <summary>
    /// Queries the daemon for the list of supported hardware features on this laptop model,
    /// parses the JSON response, and populates the local _availableFeatures cache.
    /// </summary>
    private async Task RefreshAvailableFeaturesAsync()
    {
        try
        {
            var response = await SendCommandAsync("get_supported_features");
            var success = response.RootElement.GetProperty("success").GetBoolean();

            if (success)
            {
                var data = response.RootElement.GetProperty("data");
                var features = data.GetProperty("available_features");

                _availableFeatures.Clear();
                foreach (var feature in features.EnumerateArray()) 
                    _availableFeatures.Add(feature.GetString());

                Console.WriteLine($"Available features: {string.Join(", ", _availableFeatures)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get available features: {ex.Message}");
        }
    }

    /// <summary>
    /// Closes the active socket connection and updates the IsConnected status to false.
    /// </summary>
    public void Disconnect()
    {
        if (IsConnected)
            try
            {
                _socket?.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during disconnect: {ex.Message}");
            }
            finally
            {
                IsConnected = false;
            }
    }

    /// <summary>
    /// Sends a JSON command packet to the daemon and parses the returned JSON string into a JsonDocument.
    /// Automatically handles socket reconnections and retries up to MaxRetryAttempts if a connection reset occurs.
    /// </summary>
    /// <param name="command">The command string identifier (e.g. "set_fan_speed").</param>
    /// <param name="parameters">Optional dictionary containing parameters to attach to the request.</param>
    /// <returns>The deserialized JSON response document from the daemon.</returns>
    public async Task<JsonDocument> SendCommandAsync(string command, Dictionary<string, object> parameters = null)
    {
        var attempt = 0;
        while (attempt < MaxRetryAttempts)
            try
            {
                // Verify socket connection and attempt reconnection if necessary
                if (!IsConnected)
                {
                    await ConnectAsync();
                    if (!IsConnected) throw new InvalidOperationException("Not connected to daemon");
                }

                // Construct anonymous request payload
                var request = new
                {
                    command,
                    @params = parameters ?? new Dictionary<string, object>()
                };

                // Serialize payload to JSON and convert to UTF8 bytes
                var requestJson = JsonSerializer.Serialize(request);
                var requestBytes = Encoding.UTF8.GetBytes(requestJson);

                // Send request bytes across the socket stream
                await _socket.SendAsync(requestBytes, SocketFlags.None);

                // Wait and capture incoming response bytes
                var buffer = new byte[4096];
                var received = await _socket.ReceiveAsync(buffer, SocketFlags.None);

                if (received > 0)
                {
                    var responseJson = Encoding.UTF8.GetString(buffer, 0, received);
                    return JsonDocument.Parse(responseJson);
                }

                // Connection closed by remote host (0 bytes received) -> update state and retry
                IsConnected = false;
                attempt++;
                await Task.Delay(RetryDelayMs);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset ||
                                             ex.SocketErrorCode == SocketError.Shutdown ||
                                             ex.SocketErrorCode == SocketError.ConnectionAborted)
            {
                // Socket error -> update connection state and retry
                IsConnected = false;
                attempt++;
                await Task.Delay(RetryDelayMs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error communicating with daemon: {ex.Message}");
                IsConnected = false;
                throw;
            }

        throw new IOException($"Failed to communicate with daemon after {MaxRetryAttempts} attempts");
    }

    /// <summary>
    /// Queries the daemon for all active system settings, parses the JSON response,
    /// and returns a populated DAMXSettings instance.
    /// Also updates the local supported features cache during parsing.
    /// </summary>
    /// <returns>A populated DAMXSettings instance containing current hardware parameters.</returns>
    public async Task<DAMXSettings> GetAllSettingsAsync()
    {
        var response = await SendCommandAsync("get_all_settings");
        var success = response.RootElement.GetProperty("success").GetBoolean();

        if (success)
        {
            var data = response.RootElement.GetProperty("data");
            var settings = JsonSerializer.Deserialize<DAMXSettings>(data.GetRawText());

            // Sync the local features cache with the settings data
            if (settings.AvailableFeatures != null)
                _availableFeatures = new HashSet<string>(settings.AvailableFeatures);

            return settings;
        }

        var error = response.RootElement.GetProperty("error").GetString();
        throw new Exception($"Failed to get settings: {error}");
    }

    /// <summary>
    /// Transmits a request to set a thermal performance profile (e.g. "quiet", "balanced", "performance").
    /// Checks feature compatibility before sending.
    /// </summary>
    /// <param name="profile">The profile name to apply.</param>
    /// <returns>True if the profile was set successfully, false otherwise.</returns>
    public async Task<bool> SetThermalProfileAsync(string profile)
    {
        if (!IsFeatureAvailable("thermal_profile"))
        {
            Console.WriteLine("Thermal profile feature is not available on this device");
            return false;
        }

        var parameters = new Dictionary<string, object>
        {
            { "profile", profile }
        };

        var response = await SendCommandAsync("set_thermal_profile", parameters);
        return response.RootElement.GetProperty("success").GetBoolean();
    }

    /// <summary>
    /// Transmits a request to update CPU and GPU fan speeds.
    /// Speeds are integers ranging from 0 (automatic regulation) to 100 (maximum manual speed percentage).
    /// </summary>
    /// <param name="cpu">The target CPU fan speed percentage.</param>
    /// <param name="gpu">The target GPU fan speed percentage.</param>
    /// <returns>True if the speeds were updated successfully, false otherwise.</returns>
    public async Task<bool> SetFanSpeedAsync(int cpu, int gpu)
    {
        if (!IsFeatureAvailable("fan_speed"))
        {
            Console.WriteLine("Fan speed control is not available on this device");
            return false;
        }

        var parameters = new Dictionary<string, object>
        {
            { "cpu", cpu },
            { "gpu", gpu }
        };

        var response = await SendCommandAsync("set_fan_speed", parameters);
        return response.RootElement.GetProperty("success").GetBoolean();
    }

    /// <summary>
    /// Toggles the keyboard backlight timeout feature (automatically turning off lights after 30 seconds of inactivity).
    /// </summary>
    /// <param name="enabled">True to enable timeout, false to disable timeout.</param>
    /// <returns>True if the setting was applied successfully, false otherwise.</returns>
    public async Task<bool> SetBacklightTimeoutAsync(bool enabled)
    {
        if (!IsFeatureAvailable("backlight_timeout"))
        {
            Console.WriteLine("Backlight timeout feature is not available on this device");
            return false;
        }

        var parameters = new Dictionary<string, object>
        {
            { "enabled", enabled }
        };

        var response = await SendCommandAsync("set_backlight_timeout", parameters);
        return response.RootElement.GetProperty("success").GetBoolean();
    }

    /// <summary>
    /// Starts or stops the battery calibration procedure.
    /// Calibration performs complete charge/discharge cycles to restore capacity reporting accuracy.
    /// </summary>
    /// <param name="enabled">True to start calibration, false to abort calibration.</param>
    /// <returns>True if the request was sent successfully, false otherwise.</returns>
    public async Task<bool> SetBatteryCalibrationAsync(bool enabled)
    {
        if (!IsFeatureAvailable("battery_calibration"))
        {
            Console.WriteLine("Battery calibration feature is not available on this device");
            return false;
        }

        var parameters = new Dictionary<string, object>
        {
            { "enabled", enabled }
        };

        var response = await SendCommandAsync("set_battery_calibration", parameters);
        return response.RootElement.GetProperty("success").GetBoolean();
    }

    /// <summary>
    /// Configures the battery charge limiter, clamping the maximum charge level to 80% to extend battery lifespan.
    /// </summary>
    /// <param name="enabled">True to enable charge limits, false to disable limits (charge to 100%).</param>
    /// <returns>True if the limit was applied successfully, false otherwise.</returns>
    public async Task<bool> SetBatteryLimiterAsync(bool enabled)
    {
        if (!IsFeatureAvailable("battery_limiter"))
        {
            Console.WriteLine("Battery limiter feature is not available on this device");
            return false;
        }

        var parameters = new Dictionary<string, object>
        {
            { "enabled", enabled }
        };

        var response = await SendCommandAsync("set_battery_limiter", parameters);
        return response.RootElement.GetProperty("success").GetBoolean();
    }

    /// <summary>
    /// Configures the boot animation and sound.
    /// </summary>
    /// <param name="enabled">True to enable boot effects, false to disable them.</param>
    /// <returns>True if the setting was applied successfully, false otherwise.</returns>
    public async Task<bool> SetBootAnimationSoundAsync(bool enabled)
    {
        if (!IsFeatureAvailable("boot_animation_sound"))
        {
            Console.WriteLine("Boot animation sound feature is not available on this device");
            return false;
        }

        var parameters = new Dictionary<string, object>
        {
            { "enabled", enabled }
        };

        var response = await SendCommandAsync("set_boot_animation_sound", parameters);
        return response.RootElement.GetProperty("success").GetBoolean();
    }

    /// <summary>
    /// Configures LCD override mode, reducing display latency at the cost of power consumption.
    /// </summary>
    /// <param name="enabled">True to enable latency overrides, false to disable them.</param>
    /// <returns>True if the setting was applied successfully, false otherwise.</returns>
    public async Task<bool> SetLcdOverrideAsync(bool enabled)
    {
        if (!IsFeatureAvailable("lcd_override"))
        {
            Console.WriteLine("LCD override feature is not available on this device");
            return false;
        }

        var parameters = new Dictionary<string, object>
        {
            { "enabled", enabled }
        };

        var response = await SendCommandAsync("set_lcd_override", parameters);
        return response.RootElement.GetProperty("success").GetBoolean();
    }

    /// <summary>
    /// Configures USB power delivery, allowing connected external devices to charge from USB ports when the laptop is off.
    /// </summary>
    /// <param name="level">Charging threshold percentage limit (0, 10, 20, or 30%).</param>
    /// <returns>True if the threshold was set successfully, false otherwise.</returns>
    public async Task<bool> SetUsbChargingAsync(int level)
    {
        if (!IsFeatureAvailable("usb_charging"))
        {
            Console.WriteLine("USB charging control is not available on this device");
            return false;
        }

        var parameters = new Dictionary<string, object>
        {
            { "level", level }
        };

        var response = await SendCommandAsync("set_usb_charging", parameters);
        return response.RootElement.GetProperty("success").GetBoolean();
    }

    /// <summary>
    /// Updates colors across four keyboard backlight zones.
    /// </summary>
    /// <param name="zone1">Zone 1 color as a hex RGB string.</param>
    /// <param name="zone2">Zone 2 color as a hex RGB string.</param>
    /// <param name="zone3">Zone 3 color as a hex RGB string.</param>
    /// <param name="zone4">Zone 4 color as a hex RGB string.</param>
    /// <param name="brightness">Brightness level percentage (0 to 100).</param>
    /// <returns>True if the settings were applied successfully, false otherwise.</returns>
    public async Task<bool> SetPerZoneModeAsync(string zone1, string zone2, string zone3, string zone4, int brightness)
    {
        if (!IsFeatureAvailable("per_zone_mode"))
        {
            Console.WriteLine("Per-zone keyboard mode is not available on this device");
            return false;
        }

        var parameters = new Dictionary<string, object>
        {
            { "zone1", zone1 },
            { "zone2", zone2 },
            { "zone3", zone3 },
            { "zone4", zone4 },
            { "brightness", brightness }
        };

        var response = await SendCommandAsync("set_per_zone_mode", parameters);
        return response.RootElement.GetProperty("success").GetBoolean();
    }

    /// <summary>
    /// Applies dynamic lighting effect animations to the keyboard backlight zones.
    /// </summary>
    /// <param name="mode">The effect mode index (corresponding to RgbLightingMode).</param>
    /// <param name="speed">The animation transition speed (0 to 9).</param>
    /// <param name="brightness">The backlight brightness percentage (0 to 100).</param>
    /// <param name="direction">The animation flow direction indicator (1 for Right-to-Left, 2 for Left-to-Right).</param>
    /// <param name="red">Red color channel component value (0-255).</param>
    /// <param name="green">Green color channel component value (0-255).</param>
    /// <param name="blue">Blue color channel component value (0-255).</param>
    /// <returns>True if the effect was applied successfully, false otherwise.</returns>
    public async Task<bool> SetFourZoneModeAsync(int mode, int speed, int brightness, int direction, int red, int green,
        int blue)
    {
        if (!IsFeatureAvailable("four_zone_mode"))
        {
            Console.WriteLine("Four-zone keyboard mode is not available on this device");
            return false;
        }

        var parameters = new Dictionary<string, object>
        {
            { "mode", mode },
            { "speed", speed },
            { "brightness", brightness },
            { "direction", direction },
            { "red", red },
            { "green", green },
            { "blue", blue }
        };

        var response = await SendCommandAsync("set_four_zone_mode", parameters);
        return response.RootElement.GetProperty("success").GetBoolean();
    }

    /// <summary>
    /// Releases socket connections and system resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from a finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            Disconnect();
            _socket?.Dispose();
        }

        _disposed = true;
    }
}

/// <summary>
/// Data model wrapping settings returned from the DAMX daemon.
/// </summary>
public class DAMXSettings
{
    /// <summary>The identified laptop model/chassis group (e.g. Predator, Nitro).</summary>
    [JsonPropertyName("laptop_type")] public string LaptopType { get; set; } = "UNKNOWN";

    /// <summary>Indicates if the system possesses a 4-zone customizable RGB backlight.</summary>
    [JsonPropertyName("has_four_zone_kb")] public bool HasFourZoneKb { get; set; }

    /// <summary>Cached list of supported features returned from the daemon.</summary>
    [JsonPropertyName("available_features")]
    public List<string> AvailableFeatures { get; set; } = new();

    /// <summary>Active version tag of the background daemon binary.</summary>
    [JsonPropertyName("version")] public string Version { get; set; } = "NOT CONNECTED PROPERLY";
    
    /// <summary>Version identifier for the active kernel module driver.</summary>
    [JsonPropertyName("driver_version")] public string DriverVersion { get; set; } = "DRIVER VERSION NOT FOUND";

    /// <summary>Settings details for active thermal profiles.</summary>
    [JsonPropertyName("thermal_profile")] public ThermalProfileSettings ThermalProfile { get; set; } = new();

    /// <summary>Current timeout setting for the backlight (e.g. "1" for active, "0" for inactive).</summary>
    [JsonPropertyName("backlight_timeout")]
    public string BacklightTimeout { get; set; } = "0";

    /// <summary>Current battery calibration state.</summary>
    [JsonPropertyName("battery_calibration")]
    public string BatteryCalibration { get; set; } = "0";

    /// <summary>Current battery limiter state.</summary>
    [JsonPropertyName("battery_limiter")] public string BatteryLimiter { get; set; } = "0";

    /// <summary>Current boot animation sound state.</summary>
    [JsonPropertyName("boot_animation_sound")]
    public string BootAnimationSound { get; set; } = "0";

    /// <summary>Backing details for manual fan speed settings.</summary>
    [JsonPropertyName("fan_speed")] public FanSpeedSettings FanSpeed { get; set; } = new();

    /// <summary>Current LCD latency override state.</summary>
    [JsonPropertyName("lcd_override")] public string LcdOverride { get; set; } = "0";

    /// <summary>Current USB power delivery charging limit threshold.</summary>
    [JsonPropertyName("usb_charging")] public string UsbCharging { get; set; } = "0";

    /// <summary>Raw parameters describing active static zones colors.</summary>
    [JsonPropertyName("per_zone_mode")] public string PerZoneMode { get; set; } = "";

    /// <summary>Raw parameters describing active dynamic color animations.</summary>
    [JsonPropertyName("four_zone_mode")] public string FourZoneMode { get; set; } = "";

    /// <summary>Cached modprobe driver parameter (e.g. predator_v4, nitro_v4, enable_all).</summary>
    [JsonPropertyName("modprobe_parameter")]
    public string ModprobeParameter { get; set; } = "";
}

/// <summary>
/// Data model describing thermal profiles.
/// </summary>
public class ThermalProfileSettings
{
    /// <summary>The name of the currently active thermal profile.</summary>
    [JsonPropertyName("current")] public string Current { get; set; } = "balanced";

    /// <summary>The list of all profiles supported by the daemon on this hardware.</summary>
    [JsonPropertyName("available")] public List<string> Available { get; set; } = new();
}

/// <summary>
/// Data model describing manual fan speed metrics.
/// </summary>
public class FanSpeedSettings
{
    /// <summary>Current CPU fan speed percentage.</summary>
    [JsonPropertyName("cpu")] public string Cpu { get; set; } = "0";

    /// <summary>Current GPU fan speed percentage.</summary>
    [JsonPropertyName("gpu")] public string Gpu { get; set; } = "0";
}