using System;
using System.IO;
using System.IO.Compression;
using static BepInExInstaller.Util;

namespace BepInExInstaller
{
    public static class PluginInstaller
    {
        internal static ConsoleKeyInfo key;
        
        public static void InstallTo(string gamePath, string archivePath)
        {
            if (string.IsNullOrEmpty(archivePath))
            {
                PrintError("No archive path specified.");
                return;
            }

            if (!File.Exists(archivePath))
            {
                PrintError($"Archive file not found: {archivePath}");
                Console.ReadKey();
                return;
            }

            string pluginsPath = Path.Combine(gamePath, "BepInEx", "plugins");
            
            if (!Directory.Exists(pluginsPath))
            {
                PrintVerbose($"BepInEx plugins directory not found. Creating: {pluginsPath}");
                Directory.CreateDirectory(pluginsPath);
            }

            try
            {
                Console.WriteLine($"Installing plugins from {Path.GetFileName(archivePath)}...");
                
                var archive = ZipFile.OpenRead(archivePath);
                int filesExtracted = 0;
                
                foreach (var entry in archive.Entries)
                {
                    // Skip directory entries
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;
                    
                    string destinationPath = Path.Combine(pluginsPath, entry.FullName);
                    string directory = Path.GetDirectoryName(destinationPath);
                    
                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);
                    
                    // Extract and overwrite existing files
                    entry.ExtractToFile(destinationPath, true);
                    PrintVerbose($"Extracted: {entry.FullName}");
                    filesExtracted++;
                }
                
                archive.Dispose();
                
                Console.WriteLine($"Successfully installed {filesExtracted} file(s) to BepInEx/plugins!");
            }
            catch (Exception ex)
            {
                PrintError($"Failed to install plugins: {ex.Message}");
                Console.WriteLine(ex.ToString());
            }
        }
    }
}