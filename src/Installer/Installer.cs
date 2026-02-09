using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInExInstaller.ProtonConfig;
using static BepInExInstaller.Util;
namespace BepInExInstaller
{
    public static class Installer
    {
        internal static ConsoleKeyInfo key;
        public static void InstallTo(string gamePath)
        {
            ConsoleKeyInfo key;

            Console.WriteLine("Looking for BepInEx archive...");

            string path = AppContext.BaseDirectory;

            bool x64 = true;
            if (!IsGameDirectoryValid(gamePath))
            {
                PrintError("Invalid game directory. Installation aborted.");
                return;
            }
            foreach (string file in Directory.GetFiles(gamePath, "*.exe"))
            {
                string fileName = Path.GetFileName(file);
                if (!fileName.StartsWith("BepInEx") && Directory.Exists(Path.Combine(gamePath, Path.GetFileNameWithoutExtension(file) + "_Data")))
                {
                   PrintVerbose($"Basing architecture on {file}: {(GetAppCompiledMachineType(file) == MachineType.x86 ? "32-bit" : "64-bit")}");
                    x64 = GetAppCompiledMachineType(file) != MachineType.x86;
                }
            }

            Console.WriteLine($"Game appears to be {(x64 ? "64-bit" : "32-bit")}...");

            if (IsIl2CppGame(gamePath))
            {
                InstallIL2Cpp(gamePath, x64);
                
                CheckAndConfigureProton(gamePath);
                return;
            }

            string zipPath = null;
            foreach (string file in Directory.GetFiles(path, "*.zip"))
            {
                if ((x64 && Path.GetFileName(file).StartsWith("BepInEx_win_x64")) || (!x64 && Path.GetFileName(file).StartsWith("BepInEx_win_x86")))
                {
                    zipPath = file;
                    break;
                }
            }
            if (zipPath != null)
            {
                Console.WriteLine($"Existing archive found at {zipPath}");
                Console.WriteLine($"Use this archive? Y/n");
                key = Console.ReadKey();
                if (key.Key == ConsoleKey.N)
                {
                    zipPath = null;
                }
            }
            else
            {
                Console.WriteLine("BepInEx zip file not found...");
            }
            if (zipPath == null)
            {
                Console.WriteLine("Downloading BepInEx from GitHub...");
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "request");
                    string source = client.GetStringAsync("https://api.github.com/repos/BepInEx/BepInEx/releases/latest").Result;
                    var match = Regex.Match(source, "(https://github.com/BepInEx/BepInEx/releases/download/v[^/]+/BepInEx_win_[^\"]*" + (x64 ? "x64" : "x86") + "[^\"]+)\"");
                    if (!match.Success)
                    {
                        Console.WriteLine("Couldn't find latest BepInEx file, please visit https://github.com/BepInEx/BepInEx/releases/ to download the latest release.");
                        Console.ReadKey();
                        return;
                    }

                    string latest = match.Groups[1].Value;
                    Console.WriteLine($"Downloading {latest}");
                    string fileName = Path.GetFileName(latest);
                    zipPath = Path.Combine(path, fileName);
                    var data = client.GetByteArrayAsync(latest).Result;
                    File.WriteAllBytes(zipPath, data);
                    Console.WriteLine($"Downloaded {latest}");

                }
            }

            if (!File.Exists(zipPath))
            {
                PrintVerbose($"Zip file {zipPath} does not exist!", MessageType.Error);
                Console.ReadKey();
                return;
            }

            Console.WriteLine("Installing BepInEx...");

            var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                string f = Path.Combine(gamePath, entry.FullName);
                if (!Directory.Exists(Path.GetDirectoryName(f)))
                    Directory.CreateDirectory(Path.GetDirectoryName(f));
                entry.ExtractToFile(Path.Combine(gamePath, entry.FullName), true);
                PrintVerbose($"Copying {entry.FullName}");
            }
            archive.Dispose();

            if (!Directory.Exists(Path.Combine(gamePath, "BepInEx", "plugins")))
                Directory.CreateDirectory(Path.Combine(gamePath, "BepInEx", "plugins"));

            Console.WriteLine($"BepInEx installed to {gamePath}!");
            Console.WriteLine("Delete downloaded zip archive? Y/n");
            key = Console.ReadKey();
            if (key.Key != ConsoleKey.N)
            {
                File.Delete(zipPath);
            }
            Console.WriteLine($"");
            Console.WriteLine($"Installation Complete!");
            
            ConfigureBepInExConsole(gamePath);
            CheckAndConfigureProton(gamePath);
            }

        private static void ConfigureBepInExConsole(string gamePath)
        {
            if (!Program.configureConsole)
            {
                return;
            }

            string configPath = Path.Combine(gamePath, "BepInEx", "config", "BepInEx.cfg");
            
            // Wait a moment for the file to be created if it doesn't exist yet
            if (!File.Exists(configPath))
            {
                PrintVerbose("BepInEx.cfg not found. The game needs to be run once to generate the config file.", MessageType.Warning);
                Console.WriteLine("Note: Console will be enabled on first game launch.");
                return;
            }
            
            try
            {
                PrintVerbose("Configuring BepInEx console...");
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
                        PrintVerbose("Set [Logging.Console] Enabled = true");
                        break;
                    }
                }
                
                if (configUpdated)
                {
                    File.WriteAllLines(configPath, lines);
                    Console.WriteLine("BepInEx console enabled!");
                }
                else
                {
                    PrintVerbose("Could not find Enabled setting in [Logging.Console] section.", MessageType.Warning);
                }
            }
            catch (Exception ex)
            {
                PrintVerbose($"Failed to configure BepInEx console: {ex.Message}", MessageType.Warning);
            }
        }

        private static void CheckAndConfigureProton(string gamePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Console.WriteLine("");
                Console.WriteLine("Linux detected! Would you like to configure Proton for this game? Y/n");
                Console.WriteLine("This will set the winhttp override required for BepInEx to work.");
                key = Console.ReadKey();
                Console.WriteLine("");
                
                if (key.Key == ConsoleKey.Y || key.Key == ConsoleKey.Enter)
                {
                    // Use directory name (from Steam install dir) as it matches the appmanifest game name
                    string gameName = Path.GetFileName(gamePath);
                    
                    PrintVerbose($"Attempting to find Steam App ID for '{gameName}'...");
                    
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string steamPath = Path.Combine(home, ".steam", "steam");
                    
                    int appId = IDFinder.FindGameID(gameName, steamPath);
                    
                    if (appId > 0)
                    {
                        Console.WriteLine($"Found Steam App ID: {appId}");
                        Console.WriteLine($"Use this App ID? Y to confirm, N to enter manually:");
                        key = Console.ReadKey();
                        Console.WriteLine("");
                        
                        if (key.Key == ConsoleKey.N)
                        {
                            Console.WriteLine("Please enter the Steam App ID for this game:");
                            string manualAppId = Console.ReadLine();
                            if (!string.IsNullOrWhiteSpace(manualAppId) && int.TryParse(manualAppId, out int parsedId))
                            {
                                appId = parsedId;
                            }
                            else
                            {
                                Console.WriteLine("Invalid App ID. Skipping Proton configuration.");
                                appId = -1;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Could not automatically find App ID for '{gameName}'.");
                        Console.WriteLine("Please enter the Steam App ID manually (or press Enter to skip):");
                        string manualAppId = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(manualAppId) && int.TryParse(manualAppId, out int parsedId))
                        {
                            appId = parsedId;
                        }
                    }
                    
                    if (appId > 0)
                    {
                        Console.WriteLine($"Configuring Proton for Steam App ID {appId}...");
                        int result = ProtonConfig.ProtonConfig.Execute(appId.ToString(), "winhttp");
                        
                        if (result == 0)
                        {
                            Console.WriteLine("Proton configuration completed successfully!");
                        }
                        else
                        {
                            Console.WriteLine("Proton configuration failed. You may need to configure it manually.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid App ID. Skipping Proton configuration.");
                    }
                }
        }
        }

        public static bool IsIl2CppGame(string gamePath)
        {
            if (Directory.Exists(Path.Combine(gamePath, "il2cpp_data")))
                return true;
            
            // Search in subfolders
            try
            {
                string[] subdirs = Directory.GetDirectories(gamePath, "il2cpp_data", SearchOption.AllDirectories);
                if (subdirs.Length > 0)
                    return true;
            }
            catch
            {
                // Ignore exceptions (e.g., permission issues)
            }
            
            return false;
        }

        public static void InstallIL2Cpp(string gamePath, bool x64)
        {
            PrintVerbose("IL2CPP game detected! Attempting to download BepInEx for IL2CPP...");
                
                string il2cppZipPath = null;
                try
                {
                    PrintVerbose("Finding latest IL2CPP build...");
                    using (HttpClient client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                        string buildsPage = client.GetStringAsync("https://builds.bepinex.dev/projects/bepinex_be").Result;
                        
                        // Parse artifact-details to find build number and hash
                        // Pattern: <span class="artifact-id">#752</span>....<a class="hash-button" href="...">dd0655f</a>
                        var artifactPattern = @"<span class=""artifact-id"">#(\d+)</span>\s*<a class=""hash-button"" href=""[^""]+"">([a-f0-9]+)</a>";
                        var artifactMatches = Regex.Matches(buildsPage, artifactPattern);
                        
                        if (artifactMatches.Count == 0)
                        {
                            throw new Exception("Could not find any build artifacts");
                        }
                        
                        // Get the first match (latest build)
                        Match latestArtifact = artifactMatches[0];
                        string buildNum = latestArtifact.Groups[1].Value;
                        string gitHash = latestArtifact.Groups[2].Value;
                        
                        // Construct the expected filename
                        string arch = x64 ? "x64" : "x86";
                        string fileName = $"BepInEx-Unity.IL2CPP-win-{arch}-6.0.0-be.{buildNum}+{gitHash}.zip";
                        string fileNameEncoded = fileName.Replace("+", "%2B"); // URL encode the +
                        
                        PrintVerbose($"Found latest IL2CPP build: #{buildNum} ({gitHash})");
                        PrintVerbose($"Artifact: {fileName}");
                        
                        string downloadUrl = $"https://builds.bepinex.dev/projects/bepinex_be/{buildNum}/{fileNameEncoded}";
                        
                        PrintVerbose($"Downloading...");
                        il2cppZipPath = Path.Combine(AppContext.BaseDirectory, fileName);
                        
                        var data = client.GetByteArrayAsync(downloadUrl).Result;
                        File.WriteAllBytes(il2cppZipPath, data);
                        Console.WriteLine($"Downloaded IL2CPP build successfully!");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to download IL2CPP build: {ex.Message}");
                    if (x64)
                        Console.WriteLine("Please download BepInEx-Unity.IL2CPP-win-x64...zip manually from: https://builds.bepinex.dev/projects/bepinex_be");
                    else
                        Console.WriteLine("Please download BepInEx-Unity.IL2CPP-win-x86...zip manually from: https://builds.bepinex.dev/projects/bepinex_be");
                    
                    Console.WriteLine("You can still Check and Configure Proton for this game. Do you want to continue? Y/n");
                    key = Console.ReadKey();
                    if (!(key.Key == ConsoleKey.Y || key.Key == ConsoleKey.Enter))
                    {
                        return;
                    }
                    CheckAndConfigureProton(gamePath);
                    return;
                }
                
                // If we successfully downloaded, install it
                if (il2cppZipPath != null && File.Exists(il2cppZipPath))
                {
                    Console.WriteLine("Installing BepInEx IL2CPP...");
                    var il2cppArchive = ZipFile.OpenRead(il2cppZipPath);
                    foreach (var entry in il2cppArchive.Entries)
                    {
                        // Skip directory entries (they end with /)
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;
                            
                        string f = Path.Combine(gamePath, entry.FullName);
                        string dir = Path.GetDirectoryName(f);
                        
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        
                        // Delete existing file first if it exists to avoid permission issues
                        if (File.Exists(f))
                        {
                            try
                            {
                                File.Delete(f);
                            }
                            catch (Exception)
                            {
                                // If we can't delete, try to continue anyway
                            }
                        }
                        
                        entry.ExtractToFile(f, true);
                        PrintVerbose($"Copying {entry.FullName}");
                    }
                    il2cppArchive.Dispose();
                    
                    Console.WriteLine($"BepInEx IL2CPP installed to {gamePath}!");
                    Console.WriteLine("Delete downloaded zip archive? Y/n");
                    key = Console.ReadKey();
                    if (key.Key != ConsoleKey.N)
                    {
                        File.Delete(il2cppZipPath);
                    }
                    Console.WriteLine("");
                    Console.WriteLine("Installation Complete!");
                    
                    ConfigureBepInExConsole(gamePath);
                }
        }


        public static bool IsGameDirectoryValid(string gamePath)
        {
            if (!Directory.Exists(gamePath))
            {
                PrintVerbose($"Error: Game directory {gamePath} does not exist!", MessageType.Error);
                Console.ReadKey();
                return false;
            }
            
            // Check if there's a valid Unity game structure (GameName.exe + GameName_Data folder)
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
                PrintVerbose($"Error: No valid Unity game found in {gamePath}. Expected to find GameName.exe with corresponding GameName_Data folder.", MessageType.Error);
                Console.ReadKey();
                return false;
            }
            
            return true;
        }
        public static MachineType GetAppCompiledMachineType(string fileName)
        {
            const int PE_POINTER_OFFSET = 60;
            const int MACHINE_OFFSET = 4;
            const int PE_SIGNATURE_SIZE = 4; // "PE\0\0"
            
            byte[] data = new byte[4096];
            int bytesRead;
            
            using (Stream s = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            {
                bytesRead = s.Read(data, 0, 4096);
            }
            
            // Validate we read enough for DOS header
            if (bytesRead < PE_POINTER_OFFSET + 4)
            {
                PrintVerbose($"Warning: File {fileName} is too small to be a valid PE file. Assuming x64.");
                return MachineType.x64; // Default to x64
            }
            
            // Check DOS signature "MZ" (0x5A4D)
            if (data[0] != 0x4D || data[1] != 0x5A)
            {
                PrintVerbose($"Warning: File {fileName} does not have a valid DOS header (MZ). Assuming x64.");
                return MachineType.x64; // Default to x64
            }
            
            // dos header is 64 bytes, last element, long (4 bytes) is the address of the PE header
            int PE_HEADER_ADDR = BitConverter.ToInt32(data, PE_POINTER_OFFSET);
            
            // Validate PE header address is within bounds
            if (PE_HEADER_ADDR < 0 || PE_HEADER_ADDR + PE_SIGNATURE_SIZE + MACHINE_OFFSET + 2 > bytesRead)
            {
                PrintVerbose($"Warning: PE header address is out of bounds in {fileName}. Assuming x64.");
                return MachineType.x64; // Default to x64
            }
            
            // Verify PE signature "PE\0\0" (0x50 0x45 0x00 0x00)
            if (data[PE_HEADER_ADDR] != 0x50 || data[PE_HEADER_ADDR + 1] != 0x45 || 
                data[PE_HEADER_ADDR + 2] != 0x00 || data[PE_HEADER_ADDR + 3] != 0x00)
            {
                PrintVerbose($"Warning: Invalid PE signature in {fileName}. Assuming x64.");
                return MachineType.x64; // Default to x64
            }
            
            int machineUint = BitConverter.ToUInt16(data, PE_HEADER_ADDR + MACHINE_OFFSET);
            return (MachineType)machineUint;
        }

    }
}
