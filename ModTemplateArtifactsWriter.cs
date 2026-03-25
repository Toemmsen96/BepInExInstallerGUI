using System.IO;
using System.Text;
using System.Xml.Linq;

public sealed class ModTemplateArtifactsWriter
{
	public bool AddAssemblyCSharpReferenceToProject(string projectCsprojPath, string dependencyDllPath, out string error)
	{
		error = string.Empty;

		try
		{
			if (!File.Exists(projectCsprojPath))
			{
				error = "Project file not found: " + projectCsprojPath;
				return false;
			}

			XDocument doc = XDocument.Load(projectCsprojPath);
			
			var project = doc.Root;
			if (project == null)
			{
				error = "Invalid project file structure.";
				return false;
			}

			XNamespace ns = project.Name.Namespace;
			string dependencyFileName = Path.GetFileName(dependencyDllPath);
			if (string.IsNullOrWhiteSpace(dependencyFileName))
			{
				dependencyFileName = "Assembly-CSharp.dll";
			}

			string relativePath = Path.Combine("Dependencies", dependencyFileName);

			foreach (var existingRef in project.Descendants(ns + "Reference"))
			{
				XAttribute includeAttr = existingRef.Attribute("Include");
				if (includeAttr != null && string.Equals(includeAttr.Value, "Assembly-CSharp", System.StringComparison.OrdinalIgnoreCase))
				{
					var existingHintPath = existingRef.Element(ns + "HintPath");
					if (existingHintPath == null)
					{
						existingRef.Add(new XElement(ns + "HintPath", relativePath));
					}
					else
					{
						existingHintPath.Value = relativePath;
					}

					doc.Save(projectCsprojPath);
					return true;
				}
			}
			
			var itemGroup = project.Element(ns + "ItemGroup");
			if (itemGroup == null)
			{
				itemGroup = new XElement(ns + "ItemGroup");
				project.Add(itemGroup);
			}

			var refElement = new XElement(ns + "Reference");
			refElement.SetAttributeValue("Include", "Assembly-CSharp");
			var hintPath = new XElement(ns + "HintPath", relativePath);
			refElement.Add(hintPath);
			itemGroup.Add(refElement);

			doc.Save(projectCsprojPath);
			return true;
		}
		catch (System.Exception ex)
		{
			error = "Failed to add assembly reference: " + ex.Message;
			return false;
		}
	}

	public bool CopyAssemblyCSharpToDependencies(string sourceAssemblyPath, string outputDir, out string destinationPath, out string error)
	{
		destinationPath = string.Empty;
		error = string.Empty;

		try
		{
			if (!File.Exists(sourceAssemblyPath))
			{
				error = "Assembly-CSharp.dll source file does not exist.";
				return false;
			}

			string dependenciesDir = Path.Combine(outputDir, "Dependencies");
			Directory.CreateDirectory(dependenciesDir);

			destinationPath = Path.Combine(dependenciesDir, "Assembly-CSharp.dll");
			File.Copy(sourceAssemblyPath, destinationPath, true);
			return true;
		}
		catch (System.Exception ex)
		{
			error = "Failed to copy Assembly-CSharp.dll: " + ex.Message;
			return false;
		}
	}

	public void WriteReadme(string outputDir, string gameName, string gamePath, bool isIl2Cpp, bool includeCtDyn)
	{
		string type = isIl2Cpp ? "IL2CPP" : "Mono";
		string ctDynText = includeCtDyn
			? "Enabled: project created from https://github.com/Toemmsen96/CTDynMMTemplate."
			: "Disabled: standard BepInEx template was used.";

		string readme =
			"# BepInEx Template\n\n" +
			$"- Game: {gameName}\n" +
			$"- Type: {type}\n" +
			$"- Source game folder: {gamePath}\n" +
			$"- CTDynamicModMenu: {ctDynText}\n\n" +
			"## Next steps\n\n" +
			"1. Open this folder in your IDE.\n" +
			"2. Build with `dotnet build`.\n" +
			"3. Copy the generated dll into `BepInEx/plugins`.\n";

		WriteAllTextSafe(Path.Combine(outputDir, "README.md"), readme);
	}

	public void WriteCtDynamicScaffold(string outputDir, string projectName)
	{
		string content =
			$"namespace {projectName};\n\n" +
			"public static class CTDynamicIntegration\n" +
			"{\n" +
			"\tpublic const bool Enabled = true;\n\n" +
			"\t// Add your CTDynamicModMenu setup and API calls here.\n" +
			"\tpublic static void RegisterMenuEntries()\n" +
			"\t{\n" +
			"\t}\n" +
			"}\n";

		WriteAllTextSafe(Path.Combine(outputDir, "CTDynamicIntegration.cs"), content);
	}

	private static void WriteAllTextSafe(string path, string content)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
		File.WriteAllText(path, content, Encoding.UTF8);
	}
}
