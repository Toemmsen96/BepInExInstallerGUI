using System;
using System.Diagnostics;
using System.IO;

public sealed class ModTemplateService
{
	private const string MonoTemplateShortName = "bepinex5plugin";
	private const string Il2CppTemplateShortName = "bep6plugin_unity_il2cpp";
	private const string CtDynTemplateShortName = "ctdynmmplugin";
	private const string CtDynTemplateRepoUrl = "https://github.com/Toemmsen96/CTDynMMTemplate";

	public string LastCommandOutput { get; private set; } = string.Empty;

	public string GetTemplateShortName(ModTemplateKind kind)
	{
		return kind switch
		{
			ModTemplateKind.IL2CppBepInEx6 => Il2CppTemplateShortName,
			_ => MonoTemplateShortName
		};
	}

	public string GetCtDynTemplateShortName()
	{
		return CtDynTemplateShortName;
	}

	public bool EnsureCtDynTemplateAvailable(out string error)
	{
		error = string.Empty;

		if (TemplateExists(CtDynTemplateShortName, out _))
		{
			return true;
		}

		if (!RunDotnet($"new install \"{CtDynTemplateRepoUrl}\"", out string directInstallOutput))
		{
			string tempPath = Path.Combine(Path.GetTempPath(), "ctdynmmtemplate_" + Guid.NewGuid().ToString("N"));
			try
			{
				if (!RunCommand("git", $"clone --depth 1 \"{CtDynTemplateRepoUrl}\" \"{tempPath}\"", out string cloneOutput))
				{
					error =
						"Failed to install CTDynMM template from GitHub.\n\n" +
						"Direct install output:\n" + directInstallOutput + "\n\n" +
						"Git clone output:\n" + cloneOutput;
					return false;
				}

				if (!RunDotnet($"new install \"{tempPath}\"", out string localInstallOutput))
				{
					error = "Failed to install CTDynMM template from local clone.\n\n" + localInstallOutput;
					return false;
				}
			}
			finally
			{
				try
				{
					if (Directory.Exists(tempPath))
					{
						Directory.Delete(tempPath, true);
					}
				}
				catch
				{
				}
			}
		}

		if (!TemplateExists(CtDynTemplateShortName, out string templateError))
		{
			error = "CTDynMM template installation did not produce the expected template.\n\n" + templateError;
			return false;
		}

		return true;
	}

	public bool EnsureDotnetSdkAvailable(out string error)
	{
		error = string.Empty;
		bool ok = RunDotnet("--list-sdks", out string output);
		if (!ok)
		{
			error = "The .NET SDK is not available on PATH. Install .NET and restart the app.\n\n" + output;
			return false;
		}

		if (string.IsNullOrWhiteSpace(output))
		{
			error = "No .NET SDK was found. Install the .NET SDK to create templates.";
			return false;
		}

		return true;
	}

	public bool TemplateExists(string templateShortName, out string error)
	{
		error = string.Empty;
		bool ok = RunDotnet($"new {templateShortName} --help", out string output);
		if (ok)
		{
			return true;
		}

		error =
			$"Required template '{templateShortName}' is not installed.\n\n" +
			$"Install it and try again.\n" +
			$"dotnet new install BepInEx.Templates\n\n" +
			$"dotnet output:\n{output}";
		return false;
	}

	public bool CreateFromTemplate(string templateShortName, string projectName, string outputDir, out string output)
	{
		bool ok = RunDotnet($"new {templateShortName} --name \"{projectName}\" --output \"{outputDir}\"", out output);
		LastCommandOutput = output;
		return ok;
	}



	private bool RunDotnet(string arguments, out string output)
	{
		return RunCommand("dotnet", arguments, out output);
	}

	private bool RunCommand(string fileName, string arguments, out string output)
	{
		output = string.Empty;

		try
		{
			using Process process = new Process();
			process.StartInfo = new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};

			process.Start();
			string stdout = process.StandardOutput.ReadToEnd();
			string stderr = process.StandardError.ReadToEnd();
			process.WaitForExit();

			output = (stdout + "\n" + stderr).Trim();
			return process.ExitCode == 0;
		}
		catch (Exception ex)
		{
			output = ex.Message;
			return false;
		}
	}
}
