using System.IO;
using System.Runtime.InteropServices;
using Godot;

namespace BepInExInstaller.ProtonConfig;

public static class SteamPathResolver
{
    private const string SettingsPath = "user://settings.cfg";
    private const string SteamSection = "steam";
    private const string SteamPathKey = "steam_path";

    public static string ResolveSteamPath()
    {
        string savedSteamPath = LoadSavedSteamPath();
        if (IsValidSteamPath(savedSteamPath))
        {
            return savedSteamPath;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] commonPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "Steam")
            };

            foreach (string path in commonPaths)
            {
                if (IsValidSteamPath(path))
                {
                    return path;
                }
            }

            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string steamPath = key.GetValue("SteamPath") as string;
                        if (IsValidSteamPath(steamPath))
                        {
                            return steamPath;
                        }

                        string steamExe = key.GetValue("SteamExe") as string;
                        if (!string.IsNullOrEmpty(steamExe))
                        {
                            string steamDir = Path.GetDirectoryName(steamExe);
                            if (IsValidSteamPath(steamDir))
                            {
                                return steamDir;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore registry failures and fall back to other probes.
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            string xdgDataHome = System.Environment.GetEnvironmentVariable("XDG_DATA_HOME");

            string[] linuxPaths = new[]
            {
                Path.Combine(home, ".steam", "steam"),
                !string.IsNullOrWhiteSpace(xdgDataHome)
                    ? Path.Combine(xdgDataHome, "Steam")
                    : Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"),
                "/usr/share/steam",
                "/usr/local/share/steam"
            };

            foreach (string path in linuxPaths)
            {
                if (IsValidSteamPath(path))
                {
                    return path;
                }
                else
                {
                    // TODO: Add warning message 
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            string steamPath = Path.Combine(home, "Library", "Application Support", "Steam");

            if (IsValidSteamPath(steamPath))
            {
                return steamPath;
            }
            else
            {
                // TODO: Add waring message & better fallback for macOS
            }
        }

        return null;
    }

    private static string LoadSavedSteamPath()
    {
        try
        {
            string settingsFile = ProjectSettings.GlobalizePath(SettingsPath);
            var config = new ConfigFile();
            if (config.Load(settingsFile) != Error.Ok)
            {
                return null;
            }

            if (!config.HasSectionKey(SteamSection, SteamPathKey))
            {
                return null;
            }

            return (string)config.GetValue(SteamSection, SteamPathKey, string.Empty);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValidSteamPath(string steamPath)
    {
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
        {
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return File.Exists(Path.Combine(steamPath, "steam.exe"));
        }

        return true;
    }
}