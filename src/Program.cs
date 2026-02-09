using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInExInstaller.ProtonConfig;
using static BepInExInstaller.Installer;
using static BepInExInstaller.Util;

namespace BepInExInstaller
{
    class Program
    {
        public static bool verbose { get; private set; } = false;
        public static bool configureConsole { get; private set; } = false;
        private static string gameName = null;
        private static string filesToInstallArchive = null;
        static void Main(string[] args)
        {
            try
            {
                // Parse command-line arguments
                gameName = null;
                filesToInstallArchive = null;
                ParseArguments(args);

                // If -n flag was provided, find the game directory
                if (!string.IsNullOrEmpty(gameName))
                {
                    Console.WriteLine($"Searching for game: {gameName}");
                    string gameDir = FindGameDirectory(gameName);
                    
                    if (gameDir != null)
                    {
                        Console.WriteLine($"Found game at: {gameDir}");
                        InstallTo(gameDir);
                        
                        // Install plugins if archive was specified
                        if (!string.IsNullOrEmpty(filesToInstallArchive))
                        {
                            PluginInstaller.InstallTo(gameDir, filesToInstallArchive);
                        }
                        
                        Console.WriteLine("\nPress any key to exit...");
                        Console.ReadKey();
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"Could not find game '{gameName}' in Steam libraries.");
                        Console.WriteLine("Please provide the game path manually:");
                        string manualPath = Console.ReadLine();
                        InstallTo(manualPath);
                        
                        // Install plugins if archive was specified
                        if (!string.IsNullOrEmpty(filesToInstallArchive))
                        {
                            PluginInstaller.InstallTo(manualPath, filesToInstallArchive);
                        }
                        
                        Console.WriteLine("Press any key to exit...");
                        Console.ReadKey();
                        return;
                    }
                }

                if (File.Exists(Path.Combine(AppContext.BaseDirectory, "UnityPlayer.dll")))
                {
                    Console.WriteLine("Installer is in game folder.");
                    if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "BepInEx")))
                    {
                        Console.WriteLine("BepInEx folder already exists!");
                        Console.WriteLine("Press U to uninstall or Y to install anyway:");
                        ConsoleKeyInfo key = Console.ReadKey();
                        if (key.Key == ConsoleKey.U)
                        {
                            Console.WriteLine("This will remove all existing BepInEx data and any plugins already installed! Press Y if you're sure:");
                            key = Console.ReadKey();
                            if (key.Key == ConsoleKey.Y || key.Key == ConsoleKey.Enter)
                            {
                                PrintVerbose("Deleting BepInEx folder");
                                Directory.Delete(Path.Combine(AppContext.BaseDirectory, "BepInEx"), true);
                                PrintVerbose("Deleting winhttp.dll");
                                File.Delete(Path.Combine(AppContext.BaseDirectory, "winhttp.dll"));
                                PrintVerbose("Deleting doorstop_config.ini");
                                File.Delete(Path.Combine(AppContext.BaseDirectory, "doorstop_config.ini"));
                                PrintVerbose("Deleting changelog.txt");
                                File.Delete(Path.Combine(AppContext.BaseDirectory, "changelog.txt"));
                                Console.WriteLine("\nBepInEx uninstalled! Press any key to exit...");
                            }
                            else
                            {
                                Console.WriteLine("Uninstall aborted! Press any key to exit...");
                            }
                        }
                        else if (key.Key == ConsoleKey.Y || key.Key == ConsoleKey.Enter)
                        {
                            InstallTo(AppContext.BaseDirectory);
                            
                            // Install plugins if archive was specified
                            if (!string.IsNullOrEmpty(filesToInstallArchive))
                            {
                                PluginInstaller.InstallTo(AppContext.BaseDirectory, filesToInstallArchive);
                            }
                            
                            Console.WriteLine("\nPress any key to exit...");
                        }
                        Console.ReadKey();
                        return;
                    }

                    InstallTo(AppContext.BaseDirectory);
                    
                    // Install plugins if archive was specified
                    if (!string.IsNullOrEmpty(filesToInstallArchive))
                    {
                        PluginInstaller.InstallTo(AppContext.BaseDirectory, filesToInstallArchive);
                    }
                    
                    Console.WriteLine("\nPress any key to exit...");
                    Console.ReadKey();
                    return;
                }
                Console.WriteLine("Game folder not found! Install here anyway? (Y to confirm)");
                var keyinfo = Console.ReadKey();
                if (keyinfo.Key == ConsoleKey.Y || keyinfo.Key == ConsoleKey.Enter  )
                {
                    InstallTo(AppContext.BaseDirectory);
                    
                    // Install plugins if archive was specified
                    if (!string.IsNullOrEmpty(filesToInstallArchive))
                    {
                        PluginInstaller.InstallTo(AppContext.BaseDirectory, filesToInstallArchive);
                    }
                    
                    Console.WriteLine("\nPress any key to exit...");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.WriteLine("\n\nPress any key to exit...");
                Console.ReadKey();
            }
        }


        private static void ParseArguments(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-n":
                        if (i + 1 < args.Length)
                        {
                            i++;
                            gameName = args[i];
                            Environment.SetEnvironmentVariable("GAME_NAME", gameName);
                        }
                        else
                        {
                            PrintError("No game name specified after -n");
                            PrintHelp();
                            Environment.Exit(1);
                        }
                        break;
                    case "-i":
                        if (i + 1 < args.Length)
                        {
                            i++;
                            filesToInstallArchive = args[i];
                            Environment.SetEnvironmentVariable("FILES_NAME", filesToInstallArchive);
                        }
                        else
                        {
                            PrintError("No files archive specified after -i");
                            PrintHelp();
                            Environment.Exit(1);
                        }
                        break;
                    case "-c":
                    case "--console":
                        configureConsole = true;
                        break;
                    case "-v":
                    case "--verbose":
                        verbose = true;
                        break;
                    case "-h":
                    case "--help":
                        PrintHelp();
                        Environment.Exit(0);
                        break;
                    default:
                        PrintError($"Unknown argument: {args[i]}");
                        PrintHelp();
                        Environment.Exit(1);
                        break;
                }
            }
        }

        private static string FindGameDirectory(string gameName)
        {
            string steamPath = GetSteamPath();
            if (steamPath == null)
            {
                Console.WriteLine("Could not locate Steam installation.");
                return null;
            }

            // First, find the App ID
            int appId = IDFinder.FindGameID(gameName, steamPath);
            if (appId <= 0)
            {
                Console.WriteLine($"Could not find App ID for '{gameName}'");
                return null;
            }

            Console.WriteLine($"Found App ID: {appId}");

            // Get the install directory from appmanifest
            return IDFinder.FindGameInstallDirectory(appId, steamPath);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("BepInEx Installer Help");
            Console.WriteLine("Usage: BepInExInstaller [-n <game_name>] [-i <files_archive>] [-c|--console] [-v|--verbose] [-h|--help]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -n <game_name>   Specify the name of the game to install BepInEx for.");
            Console.WriteLine("                   The installer will attempt to locate the game in Steam libraries.");
            Console.WriteLine("  -i <files_archive> Specify the archive of .dll files to install.");
            Console.WriteLine("  -c, --console    Enable BepInEx console logging by setting Enabled=true in BepInEx.cfg.");
            Console.WriteLine("  -v, --verbose    Enable verbose output during installation.");
            Console.WriteLine("  -h, --help       Display this help message.");
            Console.WriteLine();
            Console.WriteLine("If no options are provided, the installer will check if it is located in a game directory.");
        }

        private static string GetSteamPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Try common Windows locations
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
                        PrintVerbose($"Found Steam at: {path}");
                        return path;
                    }
                }

                // Try reading from registry
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    {
                        if (key != null)
                        {
                            string steamPath = key.GetValue("SteamPath") as string;
                            if (!string.IsNullOrEmpty(steamPath) && Directory.Exists(steamPath))
                            {
                                PrintVerbose($"Found Steam path from registry: {steamPath}");
                                return steamPath;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    PrintVerbose($"Could not read Steam path from registry: {ex.Message}", MessageType.Warning);
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux default location
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string steamPath = Path.Combine(home, ".steam", "steam");
                
                if (Directory.Exists(steamPath))
                {
                    PrintVerbose($"Found Steam at: {steamPath}");
                    return steamPath;
                }
                
                // Try alternative Linux locations
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
                        PrintVerbose($"Found Steam at: {path}");
                        return path;
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS location
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string steamPath = Path.Combine(home, "Library", "Application Support", "Steam");
                
                if (Directory.Exists(steamPath))
                {
                    PrintVerbose($"Found Steam at: {steamPath}");
                    return steamPath;
                }
            }

            return null;
        }

    }
}
