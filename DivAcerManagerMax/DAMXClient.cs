using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DivAcerManagerMax;

/// <summary>
///     Client for communicating with the DAMX-Daemon over Unix socket
/// </summary>
public class DAMXClient : IDisposable
{
    private const string SocketPath = "/var/run/DAMX.sock";

    /// <summary>
    ///     Send a command to the DAMX-Daemon and receive response
    /// </summary>
    /// <param name="command">Command name</param>
    /// <param name="parameters">Optional parameters</param>
    /// <returns>Response from daemon as a JsonDocument</returns>
    private const int MaxRetryAttempts = 3;

    private const int RetryDelayMs = 500;
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    // Cache of available features
    private HashSet<string> _availableFeatures = new();

    private bool _disposed;
    private Socket _socket;

    public DAMXClient()
    {
        IsConnected = false;
    }

    public bool IsConnected { get; private set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Property to check if a feature is available
    public bool IsFeatureAvailable(string featureName)
    {
        return _availableFeatures.Contains(featureName);
    }

    /// <summary>
    ///     Connect to the DAMX-Daemon Unix socket
    /// </summary>
    /// <returns>True if connection successful, false otherwise</returns>
    private async Task<bool> ValidateConnection()
    {
        if (!IsConnected) return false;

        try
        {
            // Send a simple ping command to verify connection
            var response = await SendCommandAsync("ping");
            return response.RootElement.GetProperty("success").GetBoolean();
        }
        catch
        {
            IsConnected = false;
            return false;
        }
    }

// Modify ConnectAsync to include validation
    public async Task<bool> ConnectAsync()
    {
        try
        {
            if (IsConnected && await ValidateConnection()) return true;

            _socket?.Dispose();
            _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            var endpoint = new UnixDomainSocketEndPoint(SocketPath);

            await _socket.ConnectAsync(endpoint);
            IsConnected = true;

            // Get available features upon connection
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
    ///     Refresh the available features cache from the daemon
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
                foreach (var feature in features.EnumerateArray()) _availableFeatures.Add(feature.GetString());

                Console.WriteLine($"Available features: {string.Join(", ", _availableFeatures)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get available features: {ex.Message}");
        }
    }

    /// <summary>
    ///     Disconnect from the DAMX-Daemon Unix socket
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

    public async Task<JsonDocument> SendCommandAsync(string command, Dictionary<string, object> parameters = null)
    {
        var attempt = 0;

        while (attempt < MaxRetryAttempts)
        {
            if (!IsConnected)
            {
                await ConnectAsync();

                if (!IsConnected)
                    throw new InvalidOperationException("Not connected to daemon");
            }

            await _commandLock.WaitAsync();

            try
            {
                var request = new
                {
                    command,
                    @params = parameters ?? new Dictionary<string, object>()
                };

                var requestJson = JsonSerializer.Serialize(request);
                var requestBytes = Encoding.UTF8.GetBytes(requestJson);

                await _socket.SendAsync(requestBytes, SocketFlags.None);

                var buffer = new byte[4096];
                var received = await _socket.ReceiveAsync(buffer, SocketFlags.None);

                if (received <= 0)
                {
                    ResetConnection();
                    attempt++;
                    await Task.Delay(RetryDelayMs);
                    continue;
                }

                var responseJson = Encoding.UTF8.GetString(buffer, 0, received);
                return JsonDocument.Parse(responseJson);
            }
            catch (SocketException ex) when (
                ex.SocketErrorCode == SocketError.ConnectionReset ||
                ex.SocketErrorCode == SocketError.Shutdown ||
                ex.SocketErrorCode == SocketError.ConnectionAborted)
            {
                ResetConnection();
                attempt++;
                await Task.Delay(RetryDelayMs);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Invalid JSON response from daemon: {ex.Message}");

                ResetConnection();
                attempt++;
                await Task.Delay(RetryDelayMs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error communicating with daemon: {ex.Message}");
                ResetConnection();
                throw;
            }
            finally
            {
                _commandLock.Release();
            }
        }

        throw new IOException($"Failed to communicate with daemon after {MaxRetryAttempts} attempts");
    }


    /// <summary>
    ///     Get all settings from the DAMX-Daemon
    /// </summary>
    /// <returns>All settings as a JsonDocument</returns>
    public async Task<DAMXSettings> GetAllSettingsAsync()
    {
        var response = await SendCommandAsync("get_all_settings");
        var success = response.RootElement.GetProperty("success").GetBoolean();

        if (success)
        {
            var data = response.RootElement.GetProperty("data");
            var settings = JsonSerializer.Deserialize<DAMXSettings>(data.GetRawText());

            // Update available features cache
            if (settings.AvailableFeatures != null)
                _availableFeatures = new HashSet<string>(settings.AvailableFeatures);

            return settings;
        }

        var error = response.RootElement.GetProperty("error").GetString();
        throw new Exception($"Failed to get settings: {error}");
    }

    /// <summary>
    ///     Set thermal profile
    /// </summary>
    /// <param name="profile">Profile name</param>
    /// <returns>True if successful</returns>
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
    ///     Set fan speeds
    /// </summary>
    /// <param name="cpu">CPU fan speed (0-100)</param>
    /// <param name="gpu">GPU fan speed (0-100)</param>
    /// <returns>True if successful</returns>
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
    ///     Set backlight timeout
    /// </summary>
    /// <param name="enabled">Enable or disable timeout</param>
    /// <returns>True if successful</returns>
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
    ///     Set battery calibration
    /// </summary>
    /// <param name="enabled">Start or stop calibration</param>
    /// <returns>True if successful</returns>
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
    ///     Set battery limiter
    /// </summary>
    /// <param name="enabled">Enable or disable battery limit</param>
    /// <returns>True if successful</returns>
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
    ///     Set boot animation sound
    /// </summary>
    /// <param name="enabled">Enable or disable boot sound</param>
    /// <returns>True if successful</returns>
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
    ///     Set LCD override
    /// </summary>
    /// <param name="enabled">Enable or disable LCD override</param>
    /// <returns>True if successful</returns>
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
    ///     Set USB charging level
    /// </summary>
    /// <param name="level">USB charging level (0, 10, 20, or 30)</param>
    /// <returns>True if successful</returns>
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
    ///     Set keyboard per-zone mode colors
    /// </summary>
    /// <param name="zone1">Zone 1 color (hex RGB)</param>
    /// <param name="zone2">Zone 2 color (hex RGB)</param>
    /// <param name="zone3">Zone 3 color (hex RGB)</param>
    /// <param name="zone4">Zone 4 color (hex RGB)</param>
    /// <param name="brightness">Brightness (0-100)</param>
    /// <returns>True if successful</returns>
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
    ///     Set keyboard lighting effect
    /// </summary>
    /// <param name="mode">Effect mode (0-7)</param>
    /// <param name="speed">Effect speed (0-9)</param>
    /// <param name="brightness">Brightness (0-100)</param>
    /// <param name="direction">Direction (1=right to left, 2=left to right)</param>
    /// <param name="red">Red component (0-255)</param>
    /// <param name="green">Green component (0-255)</param>
    /// <param name="blue">Blue component (0-255)</param>
    /// <returns>True if successful</returns>
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
    ///     Set rear logo / back-lid lightbar color.
    /// </summary>
    /// <param name="color">RGB hex value in RRGGBB format.</param>
    /// <param name="brightness">Brightness 0-100.</param>
    /// <param name="enabled">Enable or disable logo/lightbar power.</param>
    /// <returns>True if successful</returns>
    public async Task<bool> SetBackLogoColorAsync(string color, int brightness, bool enabled)
    {
        if (!IsFeatureAvailable("back_logo"))
        {
            Console.WriteLine("Back logo/lightbar control is not available on this device");
            return false;
        }

        var parameters = new Dictionary<string, object>
        {
            { "color", color },
            { "brightness", brightness },
            { "enabled", enabled }
        };

        var response = await SendCommandAsync("set_back_logo_color", parameters);
        return response.RootElement.GetProperty("success").GetBoolean();
    }

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

    private void ResetConnection()
    {
        IsConnected = false;

        try
        {
            _socket?.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // Socket may already be closed.
        }

        try
        {
            _socket?.Dispose();
        }
        catch
        {
            // Ignore cleanup failure.
        }
    }
}

/// <summary>
///     Models for DAMX settings
/// </summary>
public class DAMXSettings
{
    [JsonPropertyName("laptop_type")] public string LaptopType { get; set; } = "UNKNOWN";

    [JsonPropertyName("has_four_zone_kb")] public bool HasFourZoneKb { get; set; }

    [JsonPropertyName("available_features")]
    public List<string> AvailableFeatures { get; set; } = new();

    [JsonPropertyName("version")] public string Version { get; set; } = "NOT CONNECTED PROPERLY";
    [JsonPropertyName("driver_version")] public string DriverVersion { get; set; } = "DRIVER VERSION NOT FOUND";


    [JsonPropertyName("thermal_profile")] public ThermalProfileSettings ThermalProfile { get; set; } = new();

    [JsonPropertyName("backlight_timeout")]
    public string BacklightTimeout { get; set; } = "0";

    [JsonPropertyName("battery_calibration")]
    public string BatteryCalibration { get; set; } = "0";

    [JsonPropertyName("battery_limiter")] public string BatteryLimiter { get; set; } = "0";

    [JsonPropertyName("boot_animation_sound")]
    public string BootAnimationSound { get; set; } = "0";

    [JsonPropertyName("fan_speed")] public FanSpeedSettings FanSpeed { get; set; } = new();

    [JsonPropertyName("lcd_override")] public string LcdOverride { get; set; } = "0";

    [JsonPropertyName("usb_charging")] public string UsbCharging { get; set; } = "0";

    [JsonPropertyName("per_zone_mode")] public string PerZoneMode { get; set; } = "";

    [JsonPropertyName("four_zone_mode")] public string FourZoneMode { get; set; } = "";

    [JsonPropertyName("back_logo_color")] public string BackLogoColor { get; set; } = "";

    [JsonPropertyName("modprobe_parameter")]
    public string ModprobeParameter { get; set; } = "";
}

public class ThermalProfileSettings
{
    [JsonPropertyName("current")] public string Current { get; set; } = "balanced";

    [JsonPropertyName("available")] public List<string> Available { get; set; } = new();
}

public class FanSpeedSettings
{
    [JsonPropertyName("cpu")] public string Cpu { get; set; } = "0";

    [JsonPropertyName("gpu")] public string Gpu { get; set; } = "0";
}