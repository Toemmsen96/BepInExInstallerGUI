using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BepInExInstaller.ProtonConfig;

namespace BepInExInstaller
{
    /// <summary>
    /// Backend installer logic adapted for Godot UI integration.
    /// Uses callbacks instead of Console.WriteLine for UI updates.
    /// </summary>
    public class InstallerBackend
    {
        public bool Verbose { get; set; } = false;
        public bool ConfigureConsole { get; set; } = false;
        
        // Callbacks for UI updates
        public Action<string> OnLog { get; set; }
        public Action<string, MessageType> OnVerboseLog { get; set; }
        public Action<string> OnError { get; set; }
        public Action<float> OnProgress { get; set; }
        public Action<string, Action<bool>> OnConfirmation { get; set; }
        
        public enum MessageType
        {
            Info,
            Warning,
            Error
        }

        public class GameInfo
        {
            public int AppId { get; set; }
            public string Name { get; set; }
            public string InstallPath { get; set; }
            
            public override string ToString() => Name;
        }

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }

        private void LogVerbose(string message, MessageType type = MessageType.Info)
        {
            if (Verbose)
            {
                OnVerboseLog?.Invoke(message, type);
            }
        }

        private void LogError(string message)
        {
            OnError?.Invoke(message);
        }

        /// <summary>
        /// Get all installed Steam games
        /// </summary>
        public async Task<List<GameInfo>> GetInstalledGamesAsync()
        {
            return await Task.Run(() => GetInstalledGames());
        }

        private List<GameInfo> GetInstalledGames()
        {
            var games = new List<GameInfo>();
            
            try
            {
                string steamPath = GetSteamPath();
                if (steamPath == null)
                {
                    LogError("Could not locate Steam installation.");
                    return games;
                }

                LogVerbose($"Found Steam at: {steamPath}");
                
                var steamLibraries = GetSteamLibraryPaths(steamPath);
                LogVerbose($"Searching {steamLibraries.Count} Steam library locations");

                foreach (string libraryPath in steamLibraries)
                {
                    string steamappsPath = Path.Combine(libraryPath, "steamapps");
                    if (!Directory.Exists(steamappsPath))
                        continue;

                    foreach (string manifestFile in Directory.GetFiles(steamappsPath, "appmanifest_*.acf"))
                    {
                        try
                        {
                            var (appId, gameName, installDir) = ParseAppManifest(manifestFile);
                            
                            if (appId > 0 && !string.IsNullOrEmpty(gameName) && !string.IsNullOrEmpty(installDir))
                            {
                                string fullPath = Path.Combine(steamappsPath, "common", installDir);
                                
                                if (Directory.Exists(fullPath))
                                {
                                    // Check if it's a Unity game (has .exe and _Data folder)
                                    if (IsUnityGame(fullPath))
                                    {
                                        games.Add(new GameInfo
                                        {
                                            AppId = appId,
                                            Name = gameName,
                                            InstallPath = fullPath
                                        });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogVerbose($"Failed to parse {Path.GetFileName(manifestFile)}: {ex.Message}", MessageType.Warning);
                        }
                    }
                }
                
                // Remove duplicates based on AppId
                games = games.GroupBy(g => g.AppId).Select(g => g.First()).ToList();
                
                Log($"Found {games.Count} Unity games");
            }
            catch (Exception ex)
            {
                LogError($"Error scanning for games: {ex.Message}");
            }

            return games.OrderBy(g => g.Name).ToList();
        }

        private bool IsUnityGame(string gamePath)
        {
            try
            {
                foreach (string file in Directory.GetFiles(gamePath, "*.exe"))
                {
                    string fileName = Path.GetFileName(file);
                    if (!fileName.StartsWith("BepInEx") && !fileName.StartsWith("UnityCrashHandler"))
                    {
                        string dataFolder = Path.Combine(gamePath, Path.GetFileNameWithoutExtension(file) + "_Data");
                        if (Directory.Exists(dataFolder))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
            
            return false;
        }

        /// <summary>
        /// Install BepInEx to the specified game path
        /// </summary>
        public async Task<bool> InstallBepInExAsync(string gamePath)
        {
            return await Task.Run(() => InstallBepInEx(gamePath));
        }

        private bool InstallBepInEx(string gamePath)
        {
            try
            {
                Log("Starting BepInEx installation...");

                if (!IsGameDirectoryValid(gamePath))
                {
                    LogError("Invalid game directory. Installation aborted.");
                    return false;
                }

                // Detect architecture
                bool x64 = true;
                foreach (string file in Directory.GetFiles(gamePath, "*.exe"))
                {
                    string fileName = Path.GetFileName(file);
                    if (!fileName.StartsWith("BepInEx") && Directory.Exists(Path.Combine(gamePath, Path.GetFileNameWithoutExtension(file) + "_Data")))
                    {
                        LogVerbose($"Basing architecture on {file}: {(GetAppCompiledMachineType(file) == MachineType.x86 ? "32-bit" : "64-bit")}");
                        x64 = GetAppCompiledMachineType(file) != MachineType.x86;
                    }
                }

                Log($"Game appears to be {(x64 ? "64-bit" : "32-bit")}...");

                // Check for IL2CPP
                if (IsIl2CppGame(gamePath))
                {
                    return InstallIL2Cpp(gamePath, x64);
                }

                // Check for existing zip
                string zipPath = FindExistingBepInExZip(x64);
                
                if (zipPath == null)
                {
                    Log("Downloading BepInEx from GitHub...");
                    zipPath = DownloadBepInEx(x64);
                    
                    if (zipPath == null)
                    {
                        LogError("Failed to download BepInEx.");
                        return false;
                    }
                }
                else
                {
                    Log($"Using existing archive: {Path.GetFileName(zipPath)}");
                }

                // Extract
                Log("Installing BepInEx...");
                OnProgress?.Invoke(0.3f);
                
                var archive = ZipFile.OpenRead(zipPath);
                int totalEntries = archive.Entries.Count;
                int current = 0;
                
                foreach (var entry in archive.Entries)
                {
                    string f = Path.Combine(gamePath, entry.FullName);
                    if (!Directory.Exists(Path.GetDirectoryName(f)))
                        Directory.CreateDirectory(Path.GetDirectoryName(f));
                    entry.ExtractToFile(Path.Combine(gamePath, entry.FullName), true);
                    LogVerbose($"Copying {entry.FullName}");
                    
                    current++;
                    OnProgress?.Invoke(0.3f + (0.5f * current / totalEntries));
                }
                archive.Dispose();

                if (!Directory.Exists(Path.Combine(gamePath, "BepInEx", "plugins")))
                    Directory.CreateDirectory(Path.Combine(gamePath, "BepInEx", "plugins"));

                OnProgress?.Invoke(0.9f);
                
                Log($"BepInEx installed to {gamePath}!");
                
                if (ConfigureConsole)
                {
                    ConfigureBepInExConsole(gamePath);
                }

                CheckAndConfigureProton(gamePath);
                
                OnProgress?.Invoke(1.0f);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Installation failed: {ex.Message}");
                return false;
            }
        }

        private bool InstallIL2Cpp(string gamePath, bool x64)
        {
            LogVerbose("IL2CPP game detected! Attempting to download BepInEx for IL2CPP...");
            
            try
            {
                LogVerbose("Finding latest IL2CPP build...");
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    string buildsPage = client.GetStringAsync("https://builds.bepinex.dev/projects/bepinex_be").Result;
                    
                    var artifactPattern = @"<span class=""artifact-id"">#(\d+)</span>\s*<a class=""hash-button"" href=""[^""]+"">([a-f0-9]+)</a>";
                    var artifactMatches = Regex.Matches(buildsPage, artifactPattern);
                    
                    if (artifactMatches.Count == 0)
                    {
                        throw new Exception("Could not find any build artifacts");
                    }
                    
                    Match latestArtifact = artifactMatches[0];
                    string buildNum = latestArtifact.Groups[1].Value;
                    string gitHash = latestArtifact.Groups[2].Value;
                    
                    string arch = x64 ? "x64" : "x86";
                    string fileName = $"BepInEx-Unity.IL2CPP-win-{arch}-6.0.0-be.{buildNum}+{gitHash}.zip";
                    string fileNameEncoded = fileName.Replace("+", "%2B");
                    
                    LogVerbose($"Found latest IL2CPP build: #{buildNum} ({gitHash})");
                    
                    string downloadUrl = $"https://builds.bepinex.dev/projects/bepinex_be/{buildNum}/{fileNameEncoded}";
                    
                    Log("Downloading IL2CPP build...");
                    string il2cppZipPath = Path.Combine(AppContext.BaseDirectory, fileName);
                    
                    var data = client.GetByteArrayAsync(downloadUrl).Result;
                    File.WriteAllBytes(il2cppZipPath, data);
                    Log("Downloaded IL2CPP build successfully!");
                    
                    // Extract
                    Log("Installing BepInEx IL2CPP...");
                    var il2cppArchive = ZipFile.OpenRead(il2cppZipPath);
                    foreach (var entry in il2cppArchive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;
                            
                        string f = Path.Combine(gamePath, entry.FullName);
                        string dir = Path.GetDirectoryName(f);
                        
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        
                        if (File.Exists(f))
                        {
                            try { File.Delete(f); } catch { }
                        }
                        
                        entry.ExtractToFile(f, true);
                        LogVerbose($"Copying {entry.FullName}");
                    }
                    il2cppArchive.Dispose();
                    
                    Log($"BepInEx IL2CPP installed to {gamePath}!");
                    
                    if (ConfigureConsole)
                    {
                        ConfigureBepInExConsole(gamePath);
                    }
                    
                    CheckAndConfigureProton(gamePath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to download IL2CPP build: {ex.Message}");
                Log("Please download BepInEx-Unity.IL2CPP manually from: https://builds.bepinex.dev/projects/bepinex_be");
                return false;
            }
        }

        private string FindExistingBepInExZip(bool x64)
        {
            string path = AppContext.BaseDirectory;
            foreach (string file in Directory.GetFiles(path, "*.zip"))
            {
                if ((x64 && Path.GetFileName(file).StartsWith("BepInEx_win_x64")) || 
                    (!x64 && Path.GetFileName(file).StartsWith("BepInEx_win_x86")))
                {
                    return file;
                }
            }
            return null;
        }

        private string DownloadBepInEx(bool x64)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "request");
                    string source = client.GetStringAsync("https://api.github.com/repos/BepInEx/BepInEx/releases/latest").Result;
                    var match = Regex.Match(source, "(https://github.com/BepInEx/BepInEx/releases/download/v[^/]+/BepInEx_win_[^\"]*" + (x64 ? "x64" : "x86") + "[^\"]+)\"");
                    if (!match.Success)
                    {
                        LogError("Couldn't find latest BepInEx file.");
                        return null;
                    }

                    string latest = match.Groups[1].Value;
                    Log($"Downloading {Path.GetFileName(latest)}");
                    string fileName = Path.GetFileName(latest);
                    string zipPath = Path.Combine(AppContext.BaseDirectory, fileName);
                    var data = client.GetByteArrayAsync(latest).Result;
                    File.WriteAllBytes(zipPath, data);
                    Log($"Download complete!");
                    return zipPath;
                }
            }
            catch (Exception ex)
            {
                LogError($"Download failed: {ex.Message}");
                return null;
            }
        }

        private void ConfigureBepInExConsole(string gamePath)
        {
            string configPath = Path.Combine(gamePath, "BepInEx", "config", "BepInEx.cfg");
            
            if (!File.Exists(configPath))
            {
                LogVerbose("BepInEx.cfg not found. The game needs to be run once to generate the config file.", MessageType.Warning);
                Log("Note: Console will be enabled on first game launch.");
                return;
            }
            
            try
            {
                LogVerbose("Configuring BepInEx console...");
                string[] lines = File.ReadAllLines(configPath);
                bool inConsoleSection = false;
                bool configUpdated = false;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Trim() == "[Logging.Console]")
                    {
                        inConsoleSection = true;
                    }
                    else if (inConsoleSection && lines[i].StartsWith("["))
                    {
                        inConsoleSection = false;
                    }
                    
                    if (inConsoleSection && lines[i].StartsWith("Enabled = "))
                    {
                        lines[i] = "Enabled = true";
                        configUpdated = true;
                        LogVerbose("Set [Logging.Console] Enabled = true");
                        break;
                    }
                }
                
                if (configUpdated)
                {
                    File.WriteAllLines(configPath, lines);
                    Log("BepInEx console enabled!");
                }
            }
            catch (Exception ex)
            {
                LogVerbose($"Failed to configure BepInEx console: {ex.Message}", MessageType.Warning);
            }
        }

        private void CheckAndConfigureProton(string gamePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;
                
            Log("Linux detected! Attempting to configure Proton...");
            
            string gameName = Path.GetFileName(gamePath);
            LogVerbose($"Attempting to find Steam App ID for '{gameName}'...");
            
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string steamPath = Path.Combine(home, ".steam", "steam");
            
            int appId = IDFinder.FindGameID(gameName, steamPath);
            
            if (appId > 0)
            {
                Log($"Found Steam App ID: {appId}");
                Log("Configuring Proton for this game...");
                int result = ProtonConfig.ProtonConfig.Execute(appId.ToString(), "winhttp");
                
                if (result == 0)
                {
                    Log("Proton configuration completed successfully!");
                }
                else
                {
                    LogError("Proton configuration failed. You may need to configure it manually.");
                }
            }
            else
            {
                LogVerbose($"Could not automatically find App ID for '{gameName}'.", MessageType.Warning);
            }
        }

        /// <summary>
        /// Install plugins from archive
        /// </summary>
        public async Task<bool> InstallPluginsAsync(string gamePath, string archivePath)
        {
            return await Task.Run(() => InstallPlugins(gamePath, archivePath));
        }

        private bool InstallPlugins(string gamePath, string archivePath)
        {
            try
            {
                if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
                {
                    LogError($"Archive file not found: {archivePath}");
                    return false;
                }

                string pluginsPath = Path.Combine(gamePath, "BepInEx", "plugins");
                
                if (!Directory.Exists(pluginsPath))
                {
                    LogVerbose($"BepInEx plugins directory not found. Creating: {pluginsPath}");
                    Directory.CreateDirectory(pluginsPath);
                }

                Log($"Installing plugins from {Path.GetFileName(archivePath)}...");
                
                var archive = ZipFile.OpenRead(archivePath);
                int filesExtracted = 0;
                
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;
                    
                    string destinationPath = Path.Combine(pluginsPath, entry.FullName);
                    string directory = Path.GetDirectoryName(destinationPath);
                    
                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);
                    
                    entry.ExtractToFile(destinationPath, true);
                    LogVerbose($"Extracted: {entry.FullName}");
                    filesExtracted++;
                }
                
                archive.Dispose();
                
                Log($"Successfully installed {filesExtracted} file(s) to BepInEx/plugins!");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to install plugins: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Uninstall BepInEx from game directory
        /// </summary>
        public async Task<bool> UninstallBepInExAsync(string gamePath)
        {
            return await Task.Run(() => UninstallBepInEx(gamePath));
        }

        private bool UninstallBepInEx(string gamePath)
        {
            try
            {
                if (!Directory.Exists(Path.Combine(gamePath, "BepInEx")))
                {
                    LogError("BepInEx is not installed in this directory.");
                    return false;
                }

                Log("Uninstalling BepInEx...");
                
                LogVerbose("Deleting BepInEx folder");
                Directory.Delete(Path.Combine(gamePath, "BepInEx"), true);
                
                string winhttpDll = Path.Combine(gamePath, "winhttp.dll");
                if (File.Exists(winhttpDll))
                {
                    LogVerbose("Deleting winhttp.dll");
                    File.Delete(winhttpDll);
                }
                
                string doorstopConfig = Path.Combine(gamePath, "doorstop_config.ini");
                if (File.Exists(doorstopConfig))
                {
                    LogVerbose("Deleting doorstop_config.ini");
                    File.Delete(doorstopConfig);
                }
                
                string changelog = Path.Combine(gamePath, "changelog.txt");
                if (File.Exists(changelog))
                {
                    LogVerbose("Deleting changelog.txt");
                    File.Delete(changelog);
                }
                
                Log("BepInEx uninstalled successfully!");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Uninstall failed: {ex.Message}");
                return false;
            }
        }

        // Helper methods
        
        private bool IsGameDirectoryValid(string gamePath)
        {
            if (!Directory.Exists(gamePath))
            {
                LogVerbose($"Error: Game directory {gamePath} does not exist!", MessageType.Error);
                return false;
            }
            
            bool foundValidGame = false;
            foreach (string file in Directory.GetFiles(gamePath, "*.exe"))
            {
                string fileName = Path.GetFileName(file);
                if (!fileName.StartsWith("BepInEx") && Directory.Exists(Path.Combine(gamePath, Path.GetFileNameWithoutExtension(file) + "_Data")))
                {
                    foundValidGame = true;
                    break;
                }
            }
            
            if (!foundValidGame)
            {
                LogVerbose($"Error: No valid Unity game found in {gamePath}.", MessageType.Error);
                return false;
            }
            
            return true;
        }

        private bool IsIl2CppGame(string gamePath)
        {
            if (Directory.Exists(Path.Combine(gamePath, "il2cpp_data")))
                return true;
            
            try
            {
                string[] subdirs = Directory.GetDirectories(gamePath, "il2cpp_data", SearchOption.AllDirectories);
                if (subdirs.Length > 0)
                    return true;
            }
            catch { }
            
            return false;
        }

        private MachineType GetAppCompiledMachineType(string fileName)
        {
            const int PE_POINTER_OFFSET = 60;
            const int MACHINE_OFFSET = 4;
            const int PE_SIGNATURE_SIZE = 4;
            
            byte[] data = new byte[4096];
            int bytesRead;
            
            using (Stream s = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            {
                bytesRead = s.Read(data, 0, 4096);
            }
            
            if (bytesRead < PE_POINTER_OFFSET + 4)
            {
                LogVerbose($"Warning: File {fileName} is too small to be a valid PE file. Assuming x64.");
                return MachineType.x64;
            }
            
            if (data[0] != 0x4D || data[1] != 0x5A)
            {
                LogVerbose($"Warning: File {fileName} does not have a valid DOS header. Assuming x64.");
                return MachineType.x64;
            }
            
            int PE_HEADER_ADDR = BitConverter.ToInt32(data, PE_POINTER_OFFSET);
            
            if (PE_HEADER_ADDR < 0 || PE_HEADER_ADDR + PE_SIGNATURE_SIZE + MACHINE_OFFSET + 2 > bytesRead)
            {
                LogVerbose($"Warning: PE header address is out of bounds. Assuming x64.");
                return MachineType.x64;
            }
            
            if (data[PE_HEADER_ADDR] != 0x50 || data[PE_HEADER_ADDR + 1] != 0x45)
            {
                LogVerbose($"Warning: Invalid PE signature. Assuming x64.");
                return MachineType.x64;
            }
            
            int machineUint = BitConverter.ToUInt16(data, PE_HEADER_ADDR + MACHINE_OFFSET);
            return (MachineType)machineUint;
        }

        private string GetSteamPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string[] commonPaths = new[]
                {
                    @"C:\Program Files (x86)\Steam",
                    @"C:\Program Files\Steam",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
                };

                foreach (string path in commonPaths)
                {
                    if (Directory.Exists(path) && File.Exists(Path.Combine(path, "steam.exe")))
                    {
                        LogVerbose($"Found Steam at: {path}");
                        return path;
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string steamPath = Path.Combine(home, ".steam", "steam");
                
                if (Directory.Exists(steamPath))
                {
                    LogVerbose($"Found Steam at: {steamPath}");
                    return steamPath;
                }
                
                string[] linuxPaths = new[]
                {
                    Path.Combine(home, ".local", "share", "Steam"),
                    "/usr/share/steam",
                    "/usr/local/share/steam"
                };
                
                foreach (string path in linuxPaths)
                {
                    if (Directory.Exists(path))
                    {
                        LogVerbose($"Found Steam at: {path}");
                        return path;
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string steamPath = Path.Combine(home, "Library", "Application Support", "Steam");
                
                if (Directory.Exists(steamPath))
                {
                    LogVerbose($"Found Steam at: {steamPath}");
                    return steamPath;
                }
            }

            return null;
        }

        private List<string> GetSteamLibraryPaths(string steamPath)
        {
            List<string> libraryPaths = new List<string> { steamPath };

            string libraryfoldersVdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryfoldersVdf))
            {
                try
                {
                    string content = File.ReadAllText(libraryfoldersVdf);
                    var pathMatches = Regex.Matches(content, @"""path""\s*""([^""]+)""", RegexOptions.IgnoreCase);

                    foreach (Match match in pathMatches)
                    {
                        string path = match.Groups[1].Value.Replace("\\\\", "/");
                        // Normalize path for comparison (handle symlinks and case sensitivity)
                        string normalizedPath = Path.GetFullPath(path);
                        string normalizedSteamPath = Path.GetFullPath(steamPath);
                        
                        if (Directory.Exists(path) && 
                            !libraryPaths.Any(p => Path.GetFullPath(p).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            libraryPaths.Add(path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogVerbose($"Warning: Could not parse libraryfolders.vdf: {ex.Message}", MessageType.Warning);
                }
            }

            return libraryPaths;
        }

        private (int appId, string name, string installDir) ParseAppManifest(string filePath)
        {
            string content = File.ReadAllText(filePath);

            var filenameMatch = Regex.Match(Path.GetFileName(filePath), @"appmanifest_(\d+)\.acf");
            int appId = filenameMatch.Success ? int.Parse(filenameMatch.Groups[1].Value) : -1;

            var nameMatch = Regex.Match(content, @"""name""\s*""([^""]+)""", RegexOptions.IgnoreCase);
            string name = nameMatch.Success ? nameMatch.Groups[1].Value : string.Empty;

            var installDirMatch = Regex.Match(content, @"""installdir""\s*""([^""]+)""", RegexOptions.IgnoreCase);
            string installDir = installDirMatch.Success ? installDirMatch.Groups[1].Value : string.Empty;

            return (appId, name, installDir);
        }
    }
}
