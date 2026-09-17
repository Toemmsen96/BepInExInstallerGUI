using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace BepInExInstaller.ProtonConfig;

public static class ProtonConfig
{
    /// <summary>
    /// Minimal protontricks implementation - focuses on winhttp override.
    /// Resolves the Wine prefix from the Steam compatdata of the given app.
    /// </summary>
    /// <param name="appId">Steam App ID</param>
    /// <param name="commands">Commands to execute (optional, defaults to "winecfg")</param>
    /// <returns>Exit code (0 for success, 1 for error)</returns>
    public static int Execute(string appId, params string[] commands)
    {
        if (string.IsNullOrEmpty(appId))
        {
            Console.WriteLine("Error: App ID is required");
            return 1;
        }

        string prefixPath = ResolvePrefixForAppId(appId);
        if (prefixPath == null)
            return 1;

        return ExecuteForPrefix(prefixPath, appId, commands);
    }

    /// <summary>
    /// Run the same configuration against an explicitly chosen Wine/Proton prefix.
    /// Works for prefixes that are not managed by Steam at all.
    /// </summary>
    /// <param name="prefixPath">Wine prefix directory, or a compatdata directory containing one</param>
    /// <param name="commands">Commands to execute (optional, defaults to "winecfg")</param>
    /// <returns>Exit code (0 for success, 1 for error)</returns>
    public static int ExecuteForPrefix(string prefixPath, params string[] commands)
    {
        return ExecuteForPrefix(prefixPath, null, commands);
    }

    private static int ExecuteForPrefix(string prefixPath, string appId, string[] commands)
    {
        string resolvedPrefix = NormalizePrefixPath(prefixPath);
        if (resolvedPrefix == null)
        {
            Console.WriteLine($"Error: '{prefixPath}' is not a valid Wine prefix");
            Console.WriteLine("Expected a directory containing 'drive_c'/'system.reg', or a compatdata directory containing 'pfx'");
            return 1;
        }

        Console.WriteLine($"Using Wine prefix: {resolvedPrefix}");

        // Find a Wine binary: prefer Proton (Steam), fall back to a system Wine install
        string wineBinary = FindWineBinary(appId);
        if (wineBinary == null)
        {
            Console.WriteLine("Error: Could not find a Wine binary (no Proton installation and no system Wine)");
            return 1;
        }

        // Handle the command
        string[] command;
        if (commands != null && commands.Length > 0)
        {
            command = commands;
        }
        else
        {
            command = ["winecfg"];
        }

        // Special handling for winhttp override
        if (command.Length == 1 && command[0] == "winhttp")
        {
            Console.WriteLine("Setting winhttp override...");
            return SetWinhttpOverride(wineBinary, resolvedPrefix);
        }

        // Execute other commands via Wine
        try
        {
            List<string> cmd = [wineBinary, .. command];
            Console.WriteLine($"Executing: {string.Join(" ", cmd)}");

            var processInfo = new ProcessStartInfo
            {
                FileName = wineBinary,
                Arguments = string.Join(" ", command)
            };
            processInfo.Environment["WINEPREFIX"] = resolvedPrefix;

            var process = Process.Start(processInfo);
            if (process != null)
            {
                process.WaitForExit();
                return process.ExitCode;
            }
            return 1;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error executing command: {e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Locate the Steam compatdata Wine prefix belonging to an App ID.
    /// </summary>
    /// <returns>Path to the 'pfx' directory, or null if it could not be found</returns>
    public static string ResolvePrefixForAppId(string appId)
    {
        if (string.IsNullOrEmpty(appId))
            return null;

        string steamPath = SteamPathResolver.ResolveSteamPath();
        if (steamPath == null)
        {
            Console.WriteLine("Error: Could not locate Steam installation");
            return null;
        }

        List<string> steamLibraries = GetSteamLibraryPaths(steamPath);

        string compatdataPath = null;
        foreach (string libraryPath in steamLibraries)
        {
            string testCompatdataPath = Path.Combine(libraryPath, "steamapps", "compatdata", appId);
            if (Directory.Exists(testCompatdataPath))
            {
                compatdataPath = testCompatdataPath;
                Console.WriteLine($"Found compatdata for app {appId} at: {compatdataPath}");
                break;
            }
        }

        if (compatdataPath == null)
        {
            Console.WriteLine($"Error: Could not find compatdata for app {appId}");
            Console.WriteLine("Searched in Steam libraries:");
            foreach (string lib in steamLibraries)
            {
                Console.WriteLine($"  - {Path.Combine(lib, "steamapps", "compatdata", appId)}");
            }
            return null;
        }

        string prefixPath = Path.Combine(compatdataPath, "pfx");
        if (!Directory.Exists(prefixPath))
        {
            Console.WriteLine($"Error: Wine prefix not found at {prefixPath}");
            return null;
        }

        return prefixPath;
    }

    /// <summary>
    /// Accept either a Wine prefix directory or a compatdata directory holding one,
    /// and return the actual prefix path. Returns null when neither applies.
    /// </summary>
    public static string NormalizePrefixPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string trimmed = path.Trim();
        if (!Directory.Exists(trimmed))
            return null;

        if (IsValidWinePrefix(trimmed))
            return trimmed;

        // A compatdata directory (or a Steam app id directory) contains the prefix in "pfx"
        string nestedPfx = Path.Combine(trimmed, "pfx");
        if (IsValidWinePrefix(nestedPfx))
            return nestedPfx;

        return null;
    }

    /// <summary>
    /// Check whether a directory looks like a Wine prefix
    /// </summary>
    public static bool IsValidWinePrefix(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        return File.Exists(Path.Combine(path, "system.reg")) ||
               Directory.Exists(Path.Combine(path, "drive_c"));
    }

    /// <summary>
    /// Find a usable Wine binary: a Proton build when Steam is available,
    /// otherwise a system Wine installation.
    /// </summary>
    /// <param name="appId">Optional Steam App ID, used to prefer the Proton build that app runs with</param>
    public static string FindWineBinary(string appId = null)
    {
        string steamPath = SteamPathResolver.ResolveSteamPath();
        if (steamPath != null)
        {
            string protonWine = FindProtonWineBinary(steamPath, appId);
            if (protonWine != null)
                return protonWine;
        }

        return FindSystemWineBinary();
    }

    /// <summary>
    /// Find a Wine binary installed system-wide (for non-Steam prefixes)
    /// </summary>
    private static string FindSystemWineBinary()
    {
        var candidates = new List<string>();

        string pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;

                candidates.Add(Path.Combine(dir, "wine64"));
                candidates.Add(Path.Combine(dir, "wine"));
            }
        }

        // Common locations in case PATH is not inherited (e.g. Flatpak/AppImage launches)
        candidates.Add("/usr/bin/wine64");
        candidates.Add("/usr/bin/wine");
        candidates.Add("/usr/local/bin/wine64");
        candidates.Add("/usr/local/bin/wine");

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate) && IsExecutable(candidate))
            {
                Console.WriteLine($"✔ Found system Wine binary: {candidate}");
                return candidate;
            }
        }

        Console.WriteLine("✘ No system Wine binary found");
        return null;
    }

    /// <summary>
    /// Get all Steam library paths by reading libraryfolders.vdf
    /// </summary>
    private static List<string> GetSteamLibraryPaths(string steamPath)
    {
        List<string> libraryPaths = new List<string> { steamPath }; // Add default Steam library

        string libraryfoldersVdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryfoldersVdf))
        {
            try
            {
                string content = File.ReadAllText(libraryfoldersVdf);

                // Parse VDF format to extract library paths
                var pathMatches = Regex.Matches(content, @"""path""\s*""([^""]+)""", RegexOptions.IgnoreCase);

                foreach (Match match in pathMatches)
                {
                    // Handle escaped backslashes in Windows-style paths
                    string path = match.Groups[1].Value.Replace("\\\\", "/");

                    if (Directory.Exists(path) && !libraryPaths.Contains(path))
                    {
                        libraryPaths.Add(path);
                        Console.WriteLine($"Found Steam library: {path}");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Warning: Could not parse libraryfolders.vdf: {e.Message}");
            }
        }

        Console.WriteLine($"Searching in {libraryPaths.Count} Steam library location(s)");
        return libraryPaths;
    }

    /// <summary>
    /// Find Wine binary in any available Proton installation
    /// </summary>
    /// <param name="appId">Optional Steam App ID whose configured Proton build is preferred</param>
    private static string FindProtonWineBinary(string steamPath, string appId = null)
    {
        // First, try to find the specific Proton version the app is configured to use
        string usedProtonPath = FindUsedProtonInstallation(steamPath, appId);
        if (usedProtonPath != null)
        {
            string winePath = Path.Combine(usedProtonPath, "dist", "bin", "wine64");
            if (File.Exists(winePath) && IsExecutable(winePath))
            {
                string protonName = Path.GetFileName(usedProtonPath);
                Console.WriteLine($"Found Wine binary in used Proton version {protonName}: {winePath}");
                return winePath;
            }
        }

        // Fallback to searching all Proton installations in all Steam libraries
        List<string> steamLibraries = GetSteamLibraryPaths(steamPath);

        List<(string name, string path, string library)> allProtonDirs = new();
        foreach (string libraryPath in steamLibraries)
        {
            string steamappsCommon = Path.Combine(libraryPath, "steamapps", "common");

            if (!Directory.Exists(steamappsCommon))
                continue;

            // Get all directories in steamapps/common that contain "proton" (case insensitive)
            try
            {
                foreach (string dirName in Directory.GetDirectories(steamappsCommon))
                {
                    string baseName = Path.GetFileName(dirName);
                    if (baseName.Contains("proton", StringComparison.OrdinalIgnoreCase))
                    {
                        string fullPath = Path.Combine(steamappsCommon, baseName);
                        if (Directory.Exists(fullPath))
                        {
                            allProtonDirs.Add((baseName, fullPath, libraryPath));
                        }
                    }
                }
            }
            catch
            {
                continue;
            }
        }

        if (allProtonDirs.Count == 0)
        {
            Console.WriteLine("No Proton installations found in any Steam library");
            return null;
        }

        // Sort by version (try to prefer newer versions)
        allProtonDirs.Sort((a, b) =>
        {
            var aKey = GetVersionSortKey(a.name);
            var bKey = GetVersionSortKey(b.name);
            return bKey.CompareTo(aKey);
        });

        // Try to find wine binary in each Proton installation across all libraries
        foreach (var (dirName, protonPath, libraryPath) in allProtonDirs)
        {
            // Try multiple possible wine binary locations
            string[] winePaths = new[]
            {
                Path.Combine(protonPath, "dist", "bin", "wine64"),  // Try wine64 first
                Path.Combine(protonPath, "files", "bin", "wine64"),
                Path.Combine(protonPath, "dist", "bin", "wine"),
                Path.Combine(protonPath, "files", "bin", "wine"),
                Path.Combine(protonPath, "bin", "wine64"),
                Path.Combine(protonPath, "bin", "wine")
            };

            foreach (string winePath in winePaths)
            {
                if (File.Exists(winePath) && IsExecutable(winePath))
                {
                    Console.WriteLine($"✔ Found Wine binary in {dirName} (library: {libraryPath}): {winePath}");
                    return winePath;
                }
            }
        }

        Console.WriteLine("✘ No Wine binary found in any Proton installation across all Steam libraries");
        Console.WriteLine("Checked Proton directories:");
        foreach (var (dirName, protonPath, _) in allProtonDirs)
        {
            Console.WriteLine($"  - {dirName} at {protonPath}");
        }
        return null;
    }

    /// <summary>
    /// Get version sort key from directory name
    /// </summary>
    private static int GetVersionSortKey(string name)
    {
        string nameLower = name.ToLower();
        
        // Experimental is usually the newest
        if (nameLower.Contains("experimental"))
            return 9999;

        // Extract numbers from the name
        var numbers = Regex.Matches(name, @"\d+");
        if (numbers.Count > 0)
        {
            // Convert to integer for proper numeric sorting
            if (int.TryParse(numbers[0].Value, out int firstNum))
                return firstNum;
        }

        return 0;
    }

    /// <summary>
    /// Find the specific Proton installation being used for the given app
    /// </summary>
    private static string FindUsedProtonInstallation(string steamPath, string appId)
    {
        // Without an App ID there is no compatdata to read the version from
        if (string.IsNullOrEmpty(appId))
            return null;

        try
        {
            // Get all Steam library paths to find the compatdata
            List<string> steamLibraries = GetSteamLibraryPaths(steamPath);

            foreach (string libraryPath in steamLibraries)
            {
                string compatdataPath = Path.Combine(libraryPath, "steamapps", "compatdata", appId);
                string versionFile = Path.Combine(compatdataPath, "version");

                if (File.Exists(versionFile))
                {
                    try
                    {
                        string versionContent = File.ReadAllText(versionFile).Trim();
                        Console.WriteLine($"🔍 Found Proton version file: {versionContent}");

                        // Map version to actual Proton directory name
                        string protonDirName = MapVersionToProtonDirectory(steamPath, versionContent);

                        if (protonDirName != null)
                        {
                            // Search for this Proton directory in all Steam libraries
                            foreach (string searchLibraryPath in steamLibraries)
                            {
                                string protonPath = Path.Combine(searchLibraryPath, "steamapps", "common", protonDirName);
                                if (Directory.Exists(protonPath))
                                {
                                    Console.WriteLine($"✔ Found used Proton installation: {protonPath}");
                                    return protonPath;
                                }
                            }

                            Console.WriteLine($"Warning: Proton directory '{protonDirName}' not found in any Steam library");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error reading version file: {e.Message}");
                    }
                }
            }

            Console.WriteLine("No Proton version file found in any Steam library");
            return null;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error finding used Proton installation: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Map version string to actual Proton directory name
    /// </summary>
    private static string MapVersionToProtonDirectory(string steamPath, string version)
    {
        try
        {
            // Get all Steam library paths to search for Proton directories
            List<string> steamLibraries = GetSteamLibraryPaths(steamPath);

            HashSet<string> uniqueProtonDirs = new();
            foreach (string libraryPath in steamLibraries)
            {
                string steamappsCommon = Path.Combine(libraryPath, "steamapps", "common");
                if (!Directory.Exists(steamappsCommon))
                    continue;

                try
                {
                    foreach (string dirPath in Directory.GetDirectories(steamappsCommon))
                    {
                        string dirName = Path.GetFileName(dirPath);
                        if (dirName.Contains("proton", StringComparison.OrdinalIgnoreCase))
                        {
                            uniqueProtonDirs.Add(dirName);
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            if (uniqueProtonDirs.Count == 0)
            {
                Console.WriteLine("No Proton directories found in any Steam library");
                return null;
            }

            Console.WriteLine($"Available Proton directories across all libraries: {string.Join(", ", uniqueProtonDirs)}");

            // First, try to find exact version matches by checking version files
            foreach (string protonDir in uniqueProtonDirs)
            {
                string exactMatch = CheckProtonVersionFile(protonDir, version, steamLibraries);
                if (exactMatch != null)
                {
                    Console.WriteLine($"✔ Exact version match found: {version} -> {exactMatch}");
                    return exactMatch;
                }
            }

            // Fallback: Try direct directory name matches
            foreach (string protonDir in uniqueProtonDirs)
            {
                if (protonDir.Contains(version, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"✔ Direct name match found: {version} -> {protonDir}");
                    return protonDir;
                }
            }

            // Handle common version patterns
            if (version.Contains("experimental", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string protonDir in uniqueProtonDirs)
                {
                    if (protonDir.Contains("experimental", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"✔ Experimental match: {version} -> {protonDir}");
                        return protonDir;
                    }
                }
            }

            // Extract version numbers (e.g., "9.0-1" -> "9.0")
            var versionMatch = Regex.Match(version, @"(\d+)\.(\d+)");
            if (versionMatch.Success)
            {
                string majorMinor = $"{versionMatch.Groups[1].Value}.{versionMatch.Groups[2].Value}";
                foreach (string protonDir in uniqueProtonDirs)
                {
                    if (protonDir.Contains(majorMinor))
                    {
                        Console.WriteLine($"✔ Version match: {version} -> {protonDir}");
                        return protonDir;
                    }
                }
            }

            Console.WriteLine($"✘ Could not map version '{version}' to any Proton directory");
            return null;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error mapping version to directory: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check Proton version file for exact version matching
    /// </summary>
    private static string CheckProtonVersionFile(string protonDirName, string targetVersion, List<string> steamLibraries)
    {
        try
        {
            // Search all Steam libraries for this Proton directory
            foreach (string libraryPath in steamLibraries)
            {
                string protonPath = Path.Combine(libraryPath, "steamapps", "common", protonDirName);
                string versionFile = Path.Combine(protonPath, "version");

                if (File.Exists(versionFile))
                {
                    try
                    {
                        string protonVersion = File.ReadAllText(versionFile).Trim();
                        Console.WriteLine($"Checking {protonDirName}: version file contains '{protonVersion}'");

                        // Direct match
                        if (protonVersion.Equals(targetVersion, StringComparison.OrdinalIgnoreCase))
                        {
                            return protonDirName;
                        }

                        // Check if versions are compatible (same major.minor)
                        if (AreVersionsCompatible(protonVersion, targetVersion))
                        {
                            Console.WriteLine($"Compatible version found: {targetVersion} is compatible with {protonVersion}");
                            return protonDirName;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error reading version file for {protonDirName}: {e.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: No version file found for {protonDirName} at {versionFile}");
                }
            }

            return null;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error checking Proton version file for {protonDirName}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check if two Proton versions are compatible
    /// </summary>
    private static bool AreVersionsCompatible(string protonVersion, string targetVersion)
    {
        try
        {
            // Extract major.minor from both versions
            var protonMatch = Regex.Match(protonVersion, @"(\d+)\.(\d+)");
            var targetMatch = Regex.Match(targetVersion, @"(\d+)\.(\d+)");

            if (protonMatch.Success && targetMatch.Success)
            {
                string protonMajor = protonMatch.Groups[1].Value;
                string protonMinor = protonMatch.Groups[2].Value;
                string targetMajor = targetMatch.Groups[1].Value;
                string targetMinor = targetMatch.Groups[2].Value;

                // Same major.minor versions are considered compatible
                return protonMajor == targetMajor && protonMinor == targetMinor;
            }

            // Handle experimental versions
            if (protonVersion.Contains("experimental", StringComparison.OrdinalIgnoreCase) &&
                targetVersion.Contains("experimental", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error comparing versions: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Set winhttp to native,builtin override using Wine registry
    /// </summary>
    private static int SetWinhttpOverride(string wineBinary, string prefixPath)
    {
        try
        {
            // Verify wine binary exists
            if (!File.Exists(wineBinary))
            {
                Console.WriteLine($"✘ Wine binary not found at: {wineBinary}");
                return 1;
            }

            // Create temporary registry file
            string regFilePath = Path.GetTempFileName();
            string regContent = @"Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Wine\DllOverrides]
""winhttp""=""native,builtin""
";
            File.WriteAllText(regFilePath, regContent);

            // Import registry file using Wine regedit
            Console.WriteLine($"Importing registry with command: {wineBinary} regedit /S {regFilePath}");
            Console.WriteLine($"Wine binary exists: {File.Exists(wineBinary)}");
            Console.WriteLine($"Registry file exists: {File.Exists(regFilePath)}");

            var processInfo = new ProcessStartInfo
            {
                FileName = wineBinary,
                Arguments = $"regedit /S \"{regFilePath}\"",
                WorkingDirectory = Path.GetDirectoryName(wineBinary),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            processInfo.Environment["WINEPREFIX"] = prefixPath;
            processInfo.Environment["WINEDLLOVERRIDES"] = "mscoree,mshtml="; // Disable Wine Gecko/Mono prompts
            processInfo.Environment["WINEARCH"] = "win64"; // Set architecture
            processInfo.Environment["WINEDEBUG"] = "-all"; // Disable debug output

            var process = Process.Start(processInfo);
            if (process != null)
            {
                // Start async reading to prevent buffer deadlock
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                
                // Wait with timeout (Wine can take longer on some systems, especially during prefix initialization)
                bool exited = process.WaitForExit(15000);
                
                // Get output
                string stdout = stdoutTask.IsCompleted ? stdoutTask.Result : "";
                string stderr = stderrTask.IsCompleted ? stderrTask.Result : "";

                // Clean up temporary file
                try
                {
                    File.Delete(regFilePath);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                if (!exited)
                {
                    Console.WriteLine("✘ Wine regedit timed out");
                    try { process.Kill(); } catch { }
                    return 1;
                }

                if (process.ExitCode == 0)
                {
                    Console.WriteLine("✔ winhttp override set to native,builtin successfully");
                    return 0;
                }
                else
                {
                    Console.WriteLine($"✘ Failed to set winhttp override (exit code: {process.ExitCode})");
                    if (!string.IsNullOrEmpty(stderr))
                        Console.WriteLine($"stderr: {stderr}");
                    if (!string.IsNullOrEmpty(stdout))
                        Console.WriteLine($"stdout: {stdout}");
                    return 1;
                }
            }

            return 1;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error setting winhttp override: {e.Message}");
            Console.WriteLine(e.StackTrace);
            return 1;
        }
    }

    /// <summary>
    /// Check if a file is executable (Unix-like systems)
    /// </summary>
    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
            return false;

        // On Windows there is no execute bit, so existing files count as executable
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true;

        try
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            return mode.HasFlag(UnixFileMode.UserExecute) ||
                   mode.HasFlag(UnixFileMode.GroupExecute) ||
                   mode.HasFlag(UnixFileMode.OtherExecute);
        }
        catch
        {
            // If the mode cannot be read, fall back to accepting the file
            return true;
        }
    }
}
