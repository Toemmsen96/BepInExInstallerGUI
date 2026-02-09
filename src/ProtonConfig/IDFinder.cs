using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BepInExInstaller.ProtonConfig;

public static class IDFinder
{
    private static readonly string CacheFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".bepinex_steam_cache.json"
    );

    /// <summary>
    /// Find a Steam game's App ID by its name
    /// </summary>
    /// <param name="name">The game name to search for (case-insensitive)</param>
    /// <param name="steamPath">Path to Steam installation</param>
    /// <returns>App ID as integer, or -1 if not found</returns>
    public static int FindGameID(string name, string steamPath)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Error: Game name is required");
            return -1;
        }

        // Try to load from cache first
        var cache = LoadCache();
        string nameLower = name.ToLower().Trim();
        
        if (cache.TryGetValue(nameLower, out int cachedId))
        {
            Console.WriteLine($"Found '{name}' in cache with App ID: {cachedId}");
            return cachedId;
        }

        // Get all Steam library paths
        List<string> steamLibraries = GetSteamLibraryPaths(steamPath);

        // Search through all appmanifest files
        foreach (string libraryPath in steamLibraries)
        {
            string steamappsPath = Path.Combine(libraryPath, "steamapps");
            if (!Directory.Exists(steamappsPath))
                continue;

            foreach (string manifestFile in Directory.GetFiles(steamappsPath, "appmanifest_*.acf"))
            {
                try
                {
                    var (appId, gameName) = ParseAppManifest(manifestFile);
                    
                    if (appId > 0 && !string.IsNullOrEmpty(gameName))
                    {
                        // Add to cache
                        string gameNameLower = gameName.ToLower().Trim();
                        cache[gameNameLower] = appId;

                        // Check if this is the game we're looking for
                        if (gameNameLower.Contains(nameLower) || nameLower.Contains(gameNameLower))
                        {
                            Util.PrintVerbose($"Found match: '{gameName}' (App ID: {appId})");
                            SaveCache(cache);
                            return appId;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to parse {Path.GetFileName(manifestFile)}: {ex.Message}");
                }
            }
        }

        // Save cache even if we didn't find the game
        SaveCache(cache);

        Console.WriteLine($"Game '{name}' not found in any Steam library");
        return -1;
    }

    /// <summary>
    /// Find a Steam game's installation directory by its App ID
    /// </summary>
    /// <param name="appId">The Steam App ID</param>
    /// <param name="steamPath">Path to Steam installation</param>
    /// <returns>Full path to game installation directory, or null if not found</returns>
    public static string FindGameInstallDirectory(int appId, string steamPath)
    {
        if (appId <= 0)
        {
            Console.WriteLine("Error: Invalid App ID");
            return null;
        }

        // Get all Steam library paths
        List<string> steamLibraries = GetSteamLibraryPaths(steamPath);

        // Search for the appmanifest file in all libraries
        foreach (string libraryPath in steamLibraries)
        {
            string steamappsPath = Path.Combine(libraryPath, "steamapps");
            string manifestFile = Path.Combine(steamappsPath, $"appmanifest_{appId}.acf");

            if (File.Exists(manifestFile))
            {
                try
                {
                    string content = File.ReadAllText(manifestFile);
                    
                    // Extract install directory from manifest
                    var installDirMatch = Regex.Match(content, @"""installdir""\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    
                    if (installDirMatch.Success)
                    {
                        string installDir = installDirMatch.Groups[1].Value;
                        string fullPath = Path.Combine(steamappsPath, "common", installDir);
                        
                        if (Directory.Exists(fullPath))
                        {
                            Console.WriteLine($"Found game installation at: {fullPath}");
                            return fullPath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading manifest for App ID {appId}: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"Could not find installation directory for App ID {appId}");
        return null;
    }

    /// <summary>
    /// Get all Steam library paths by reading libraryfolders.vdf
    /// </summary>
    private static List<string> GetSteamLibraryPaths(string steamPath)
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
                    if (Directory.Exists(path) && !libraryPaths.Contains(path))
                    {
                        libraryPaths.Add(path);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not parse libraryfolders.vdf: {ex.Message}");
            }
        }

        return libraryPaths;
    }

    /// <summary>
    /// Parse an appmanifest file to extract App ID and game name
    /// </summary>
    private static (int appId, string name) ParseAppManifest(string filePath)
    {
        string content = File.ReadAllText(filePath);

        // Extract App ID from filename (appmanifest_XXXXX.acf)
        var filenameMatch = Regex.Match(Path.GetFileName(filePath), @"appmanifest_(\d+)\.acf");
        int appId = filenameMatch.Success ? int.Parse(filenameMatch.Groups[1].Value) : -1;

        // Extract game name from content
        var nameMatch = Regex.Match(content, @"""name""\s*""([^""]+)""", RegexOptions.IgnoreCase);
        string name = nameMatch.Success ? nameMatch.Groups[1].Value : string.Empty;

        return (appId, name);
    }

    /// <summary>
    /// Load the cache from disk
    /// </summary>
    private static Dictionary<string, int> LoadCache()
    {
        if (!File.Exists(CacheFilePath))
            return new Dictionary<string, int>();

        try
        {
            string json = File.ReadAllText(CacheFilePath);
            var cache = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            return cache ?? new Dictionary<string, int>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to load cache: {ex.Message}");
            return new Dictionary<string, int>();
        }
    }

    /// <summary>
    /// Save the cache to disk
    /// </summary>
    private static void SaveCache(Dictionary<string, int> cache)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(cache, options);
            File.WriteAllText(CacheFilePath, json);
            Console.WriteLine($"Cache updated with {cache.Count} games");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to save cache: {ex.Message}");
        }
    }
}