using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PaqetTunnel.Services;

/// <summary>
/// Orchestrates first-run setup: downloads paqet binary, checks Npcap, creates default config.
/// Also migrates files from old %USERPROFILE%\paqet location if present.
/// </summary>
public sealed class SetupService
{
    private readonly PaqetService _paqetService;
    private readonly TunService _tunService;

    public SetupService(PaqetService paqetService, TunService tunService)
    {
        _paqetService = paqetService;
        _tunService = tunService;
    }

    /// <summary>Check if Npcap is installed (required for paqet raw packets).</summary>
    public static bool IsNpcapInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Npcap")
                         ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Npcap");
            if (key != null) return true;

            return File.Exists(@"C:\Windows\System32\Npcap\wpcap.dll");
        }
        catch { return false; }
    }

    /// <summary>Download and install Npcap silently (requires admin).</summary>
    public async Task<(bool Success, string Message)> InstallNpcapAsync(IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report("Downloading Npcap installer...");

            const string npcapUrl = "https://npcap.com/dist/npcap-1.80.exe";
            var tempPath = Path.Combine(Path.GetTempPath(), "npcap-installer.exe");

            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PaqetTunnel/1.0");
            var bytes = await http.GetByteArrayAsync(npcapUrl);
            await File.WriteAllBytesAsync(tempPath, bytes);

            progress?.Report("Installing Npcap (requires admin)...");

            PaqetService.RunElevated(tempPath, "/S /winpcap_mode=yes");

            await Task.Delay(5000);
            if (IsNpcapInstalled())
            {
                try { File.Delete(tempPath); } catch { }
                return (true, "Npcap installed successfully.");
            }

            return (false, "Npcap installation may have failed. Check UAC prompt.");
        }
        catch (Exception ex)
        {
            return (false, $"Npcap install failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Migrate existing paqet files from old locations to new AppPaths location.
    /// Checks both %USERPROFILE%\paqet and %LOCALAPPDATA%\PaqetManager (pre-rename).
    /// </summary>
    public static void MigrateFromOldLocation()
    {
        AppPaths.EnsureDirectories();

        // Migrate from old PaqetManager data dir (pre-rename)
        var oldAppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaqetManager");
        if (Directory.Exists(oldAppDir))
        {
            MigrateDir(Path.Combine(oldAppDir, "bin"), AppPaths.BinDir);
            MigrateDir(Path.Combine(oldAppDir, "config"), AppPaths.ConfigDir);
            MigrateDir(Path.Combine(oldAppDir, "logs"), AppPaths.LogDir);
            var oldSettings = Path.Combine(oldAppDir, "settings.json");
            if (File.Exists(oldSettings) && !File.Exists(AppPaths.SettingsPath))
                try { File.Copy(oldSettings, AppPaths.SettingsPath, overwrite: false); } catch { }
            Logger.Info($"Migrated data from old PaqetManager dir: {oldAppDir}");
        }

        // Migrate from ancient %USERPROFILE%\paqet
        var oldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "paqet");
        if (!Directory.Exists(oldDir)) return;

        var oldBinary = Path.Combine(oldDir, AppPaths.BINARY_NAME);
        if (File.Exists(oldBinary) && !File.Exists(AppPaths.BinaryPath))
            try { File.Copy(oldBinary, AppPaths.BinaryPath, overwrite: false); } catch { }

        var oldConfig = Path.Combine(oldDir, "client.yaml");
        if (File.Exists(oldConfig) && !File.Exists(AppPaths.PaqetConfigPath))
            try { File.Copy(oldConfig, AppPaths.PaqetConfigPath, overwrite: false); } catch { }
    }

    private static void MigrateDir(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
        {
            var destFile = Path.Combine(dst, Path.GetFileName(file));
            if (!File.Exists(destFile))
                try { File.Copy(file, destFile, overwrite: false); } catch { }
        }
    }

    /// <summary>Run full automated setup: migrate + binary + Npcap + config.</summary>
    public async Task<(bool Success, string Message)> RunFullSetupAsync(IProgress<string>? progress = null)
    {
        var messages = new System.Text.StringBuilder();

        // Step 0: Migrate from old location
        progress?.Report("Checking for existing installation...");
        MigrateFromOldLocation();

        // Step 1: Download paqet binary
        if (!_paqetService.BinaryExists())
        {
            progress?.Report("Downloading paqet binary...");
            var (ok, msg) = await _paqetService.DownloadLatestAsync(progress);
            messages.AppendLine(msg);
            if (!ok) return (false, messages.ToString());
        }
        else
        {
            messages.AppendLine("Paqet binary: OK");
        }

        // Step 2: Ensure config
        _paqetService.EnsureConfigExists();
        messages.AppendLine("Config file: OK");

        // Step 3: Check/install Npcap
        if (!IsNpcapInstalled())
        {
            progress?.Report("Installing Npcap...");
            var (ok, msg) = await InstallNpcapAsync(progress);
            messages.AppendLine(msg);
            if (!ok) messages.AppendLine("Warning: Paqet requires Npcap to work.");
        }
        else
        {
            messages.AppendLine("Npcap: OK");
        }

        // Step 4: Download TUN binaries (tun2socks + wintun)
        if (!_tunService.AllBinariesExist())
        {
            progress?.Report("Downloading TUN binaries...");
            var (ok, msg) = await _tunService.DownloadBinariesAsync(progress);
            messages.AppendLine(msg);
        }
        else
        {
            messages.AppendLine("TUN binaries: OK");
        }

        progress?.Report("Setup complete.");
        return (true, messages.ToString());
    }
}
