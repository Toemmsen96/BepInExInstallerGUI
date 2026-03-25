using System.IO;
using System;

public sealed class ModGameAnalyzer
{
	public bool IsIl2CppGame(string gamePath)
	{
		if (Directory.Exists(Path.Combine(gamePath, "il2cpp_data")))
		{
			return true;
		}

		try
		{
			return Directory.GetDirectories(gamePath, "il2cpp_data", SearchOption.AllDirectories).Length > 0;
		}
		catch
		{
			return false;
		}
	}

	public bool TryGetAssemblyCSharpPath(string gamePath, out string assemblyPath, out string error)
	{
		assemblyPath = string.Empty;
		error = string.Empty;

		if (!TryGetManagedFolderPath(gamePath, out string managedFolder, out error))
		{
			return false;
		}

		string candidate = Path.Combine(managedFolder, "Assembly-CSharp.dll");
		if (!File.Exists(candidate))
		{
			error = $"Assembly-CSharp.dll was not found in managed folder: {managedFolder}";
			return false;
		}

		assemblyPath = candidate;
		return true;
	}

	private bool TryGetManagedFolderPath(string gamePath, out string managedFolder, out string error)
	{
		managedFolder = string.Empty;
		error = string.Empty;

		try
		{
			foreach (string exePath in Directory.GetFiles(gamePath, "*.exe", SearchOption.TopDirectoryOnly))
			{
				string exeName = Path.GetFileName(exePath);
				if (exeName.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase) ||
					exeName.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				string managed = Path.Combine(gamePath, Path.GetFileNameWithoutExtension(exeName) + "_Data", "Managed");
				if (Directory.Exists(managed))
				{
					managedFolder = managed;
					return true;
				}
			}

			string[] managedDirs = Directory.GetDirectories(gamePath, "Managed", SearchOption.AllDirectories);
			foreach (string dir in managedDirs)
			{
				if (dir.Contains("_Data" + Path.DirectorySeparatorChar + "Managed", StringComparison.OrdinalIgnoreCase) ||
					dir.Contains("_Data" + Path.AltDirectorySeparatorChar + "Managed", StringComparison.OrdinalIgnoreCase))
				{
					managedFolder = dir;
					return true;
				}
			}

			error = "Could not locate the game's _Data/Managed folder.";
			return false;
		}
		catch (Exception ex)
		{
			error = "Failed to locate Managed folder: " + ex.Message;
			return false;
		}
	}
}
