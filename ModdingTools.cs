using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInExInstaller;

public partial class ModdingTools : PanelContainer
{
	private InstallerBackend _installer;
	private ModTemplateService _templateService;
	private ModGameAnalyzer _gameAnalyzer;
	private ModTemplateArtifactsWriter _artifactsWriter;
	private List<InstallerBackend.GameInfo> _games = new();

	private OptionButton _gameOptionButton;
	private Button _recheckGamesButton;
	private Button _pickManualButton;
	private FileDialog _fileDialog;
	private Button _pickLocationButton;
	private FileDialog _templateDialog;
	private CheckBox _ctDynCheck;
	private LineEdit _nameInput;
	private Button _createTemplateButton;
	private AcceptDialog _errorDialog;
	private CheckBox _logCheck;
	private RichTextLabel _logOutput;
	private ProgressBar _progressBar;
	private AcceptDialog _resultDialog;

	private string _selectedGamePath;
	private string _selectedTemplateLocation;

	public override void _Ready()
	{
		_installer = new InstallerBackend();
		_templateService = new ModTemplateService();
		_gameAnalyzer = new ModGameAnalyzer();
		_artifactsWriter = new ModTemplateArtifactsWriter();
		GetUIReferences();
		ConnectSignals();
		LoadGamesAsync();

		_selectedTemplateLocation = GetDefaultTemplateLocation();
		AppendLog($"[color=gray]Template output: {_selectedTemplateLocation}[/color]");
	}

	private void GetUIReferences()
	{
		_gameOptionButton = GetNode<OptionButton>("MarginContainer2/VBoxContainer/OptionButton");
		_recheckGamesButton = GetNode<Button>("MarginContainer2/VBoxContainer/RecheckGames");
		_pickManualButton = GetNode<Button>("MarginContainer2/VBoxContainer/pick");
		_fileDialog = GetNode<FileDialog>("MarginContainer2/VBoxContainer/FileDialog");
		_pickLocationButton = GetNode<Button>("MarginContainer2/VBoxContainer/pickloc");
		_templateDialog = GetNode<FileDialog>("MarginContainer2/VBoxContainer/TemplateDialog");
		_ctDynCheck = GetNode<CheckBox>("MarginContainer2/VBoxContainer/CTDynCheck");
		_nameInput = GetNode<LineEdit>("MarginContainer2/VBoxContainer/NameInput");
		_createTemplateButton = GetNode<Button>("MarginContainer2/VBoxContainer/CreateTemplate");
		_errorDialog = GetNode<AcceptDialog>("MarginContainer2/VBoxContainer/ErrorDialog");
		_logCheck = GetNode<CheckBox>("MarginContainer2/VBoxContainer/LogCheck");
		_logOutput = GetNode<RichTextLabel>("MarginContainer2/VBoxContainer/ScrollContainer/LogOutput");
		_progressBar = GetNode<ProgressBar>("MarginContainer2/VBoxContainer/ProgressBar");
		_resultDialog = GetNode<AcceptDialog>("MarginContainer2/VBoxContainer/ResultDialog");

		_fileDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
		_fileDialog.Access = FileDialog.AccessEnum.Filesystem;
		_fileDialog.Title = "Select Game Directory";

		_templateDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
		_templateDialog.Access = FileDialog.AccessEnum.Filesystem;
		_templateDialog.Title = "Select Template Output Folder";

		_progressBar.MinValue = 0;
		_progressBar.MaxValue = 100;
		_progressBar.Value = 0;

		_gameOptionButton.Clear();
		_gameOptionButton.AddItem("Loading games...");
		_gameOptionButton.Disabled = true;
	}

	private void ConnectSignals()
	{
		_gameOptionButton.ItemSelected += OnGameSelected;
		_recheckGamesButton.Pressed += OnRecheckGamesPressed;
		_pickManualButton.Pressed += OnPickManualPressed;
		_fileDialog.DirSelected += OnDirectorySelected;
		_pickLocationButton.Pressed += OnPickLocationPressed;
		_templateDialog.DirSelected += OnTemplateLocationSelected;
		_createTemplateButton.Pressed += OnCreateTemplatePressed;
		_logCheck.Toggled += OnLogCheckToggled;
	}

	private async void LoadGamesAsync()
	{
		AppendLog("[color=cyan]Searching for installed Unity games...[/color]");
		_recheckGamesButton.Disabled = true;
		_createTemplateButton.Disabled = true;

		_games = await _installer.GetInstalledGamesAsync();

		_gameOptionButton.Clear();
		if (_games.Count == 0)
		{
			_gameOptionButton.AddItem("No Unity games found");
			_gameOptionButton.Disabled = true;
			AppendLog("[color=yellow]No Unity games found. Use manual folder selection.[/color]");
		}
		else
		{
			_gameOptionButton.AddItem("-- Select a game --");
			foreach (InstallerBackend.GameInfo game in _games)
			{
				_gameOptionButton.AddItem(game.Name);
			}

			_gameOptionButton.Selected = 0;
			_gameOptionButton.Disabled = false;
			AppendLog($"[color=green]Found {_games.Count} Unity games.[/color]");
		}

		_recheckGamesButton.Disabled = false;
		_createTemplateButton.Disabled = false;
	}

	private void OnGameSelected(long index)
	{
		if (index <= 0 || _games == null || index > _games.Count)
		{
			_selectedGamePath = null;
			_pickManualButton.Visible = true;
			return;
		}

		InstallerBackend.GameInfo game = _games[(int)index - 1];
		_selectedGamePath = game.InstallPath;
		_pickManualButton.Visible = false;
		AppendLog($"[color=cyan]Selected game: {game.Name}[/color]");
		AppendLog($"[color=gray]{_selectedGamePath}[/color]");
	}

	private void OnRecheckGamesPressed()
	{
		_selectedGamePath = null;
		_pickManualButton.Visible = true;
		LoadGamesAsync();
	}

	private void OnPickManualPressed()
	{
		_fileDialog.PopupCentered(new Vector2I(800, 600));
	}

	private void OnDirectorySelected(string dir)
	{
		_selectedGamePath = dir;
		_pickManualButton.Visible = false;
		AppendLog("[color=cyan]Manually selected game directory:[/color]");
		AppendLog($"[color=gray]{dir}[/color]");
	}

	private void OnPickLocationPressed()
	{
		_templateDialog.PopupCentered(new Vector2I(800, 600));
	}

	private void OnTemplateLocationSelected(string dir)
	{
		_selectedTemplateLocation = dir;
		AppendLog("[color=cyan]Template output location selected:[/color]");
		AppendLog($"[color=gray]{dir}[/color]");
	}

	private async void OnCreateTemplatePressed()
	{
		if (string.IsNullOrWhiteSpace(_selectedGamePath))
		{
			ShowError("Template Creation Failed", "Please select a game first.");
			return;
		}

		if (string.IsNullOrWhiteSpace(_selectedTemplateLocation))
		{
			ShowError("Template Creation Failed", "Please select a template output location.");
			return;
		}

		if (!Directory.Exists(_selectedGamePath))
		{
			ShowError("Template Creation Failed", "The selected game directory does not exist.");
			return;
		}

		if (!_templateService.EnsureDotnetSdkAvailable(out string dotnetError))
		{
			ShowError(".NET SDK Missing", dotnetError);
			AppendLog($"[color=red]{dotnetError}[/color]");
			return;
		}

		_createTemplateButton.Disabled = true;
		_createTemplateButton.Text = "Creating...";
		_progressBar.Value = 0;

		try
		{
			bool isIl2Cpp = _gameAnalyzer.IsIl2CppGame(_selectedGamePath);
			bool includeCtDyn = _ctDynCheck.ButtonPressed;
			string requestedName = _nameInput?.Text?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(requestedName))
			{
				ShowError("Template Creation Failed", "Please enter a project name in NameInput.");
				AppendLog("[color=red]Project name is required (NameInput).[/color]");
				return;
			}

			if (includeCtDyn && isIl2Cpp)
			{
				ShowError("Template Creation Failed", "CTDynMMTemplate currently supports Mono only. Disable CTDynamicModMenu for IL2CPP games.");
				AppendLog("[color=red]CTDynMMTemplate is Mono-only and cannot be used for IL2CPP templates.[/color]");
				return;
			}

			string gameName = ResolveSelectedGameName();
			string projectName = BuildProjectName(requestedName);
			string outputDir = Path.Combine(_selectedTemplateLocation, projectName);
			ModTemplateKind templateKind = isIl2Cpp ? ModTemplateKind.IL2CppBepInEx6 : ModTemplateKind.MonoBepInEx5;
			string templateShortName = includeCtDyn
				? _templateService.GetCtDynTemplateShortName()
				: _templateService.GetTemplateShortName(templateKind);

			if (Directory.Exists(outputDir))
			{
				ShowError("Template Creation Failed", $"The folder already exists:\n{outputDir}");
				AppendLog($"[color=red]Template folder already exists: {outputDir}[/color]");
				return;
			}

			Directory.CreateDirectory(_selectedTemplateLocation);

			AppendLog($"[color=cyan]Creating {(isIl2Cpp ? "IL2CPP" : "Mono")} template...[/color]");
			AppendLog($"[color=gray]Using dotnet template: {templateShortName}[/color]");
			_progressBar.Value = 10;

			string ctDynTemplateError = string.Empty;
			string templateMissingReason = string.Empty;
			bool templateReady = includeCtDyn
				? _templateService.EnsureCtDynTemplateAvailable(out ctDynTemplateError)
				: _templateService.TemplateExists(templateShortName, out templateMissingReason);

			if (!templateReady)
			{
				string errorMessage = includeCtDyn ? ctDynTemplateError : templateMissingReason;
				ShowError("Template Not Installed", errorMessage);
				AppendLog($"[color=red]{errorMessage}[/color]");
				return;
			}

			bool created = await System.Threading.Tasks.Task.Run(() =>
				_templateService.CreateFromTemplate(templateShortName, projectName, outputDir, out _)
			);

			if (!created)
			{
				ShowError("Template Creation Failed", "dotnet template creation failed. Check log output for details.");
				if (_templateService.LastCommandOutput?.Length > 0)
				{
					AppendLog($"[color=red]{_templateService.LastCommandOutput}[/color]");
				}
				return;
			}

			_artifactsWriter.WriteReadme(outputDir, gameName, _selectedGamePath, isIl2Cpp, includeCtDyn);

			if (!_gameAnalyzer.TryGetAssemblyCSharpPath(_selectedGamePath, out string assemblyPath, out string assemblyLookupError))
			{
				ShowError("Dependency Copy Failed", assemblyLookupError);
				AppendLog($"[color=red]{assemblyLookupError}[/color]");
				return;
			}

			if (!_artifactsWriter.CopyAssemblyCSharpToDependencies(assemblyPath, outputDir, out string dependencyPath, out string copyError))
			{
				ShowError("Dependency Copy Failed", copyError);
				AppendLog($"[color=red]{copyError}[/color]");
				return;
			}

			AppendLog($"[color=gray]Copied Assembly-CSharp.dll to: {dependencyPath}[/color]");

			string mainProjectPath = Path.Combine(outputDir, projectName + ".csproj");
			if (!File.Exists(mainProjectPath))
			{
				ShowError("Dependency Setup Failed", "Main template .csproj could not be found.");
				AppendLog($"[color=red]Main project file missing: {mainProjectPath}[/color]");
				return;
			}

			if (!_artifactsWriter.AddAssemblyCSharpReferenceToProject(mainProjectPath, dependencyPath, out string refError))
			{
				ShowError("Dependency Setup Failed", refError);
				AppendLog($"[color=red]{refError}[/color]");
				return;
			}

			AppendLog($"[color=gray]Added Assembly-CSharp reference to project file.[/color]");

			if (includeCtDyn)
			{
				AppendLog("[color=gray]Used CTDynMMTemplate from github.com/Toemmsen96/CTDynMMTemplate.[/color]");
			}

			_progressBar.Value = 100;
			AppendLog($"[color=lime]Template created: {outputDir}[/color]");
			_resultDialog.Title = "Template Created";
			_resultDialog.DialogText =
				$"Template created successfully.\n\nGame: {gameName}\nType: {(isIl2Cpp ? "IL2CPP" : "Mono")}\nOutput: {outputDir}\nDependencies: {Path.Combine(outputDir, "Dependencies")}";
			_resultDialog.PopupCentered(new Vector2I(620, 280));
		}
		catch (Exception ex)
		{
			ShowError("Template Creation Failed", ex.Message);
			AppendLog($"[color=red]Template creation failed: {ex.Message}[/color]");
		}
		finally
		{
			_createTemplateButton.Disabled = false;
			_createTemplateButton.Text = "Create Template";
		}
	}

	private string ResolveSelectedGameName()
	{
		int selectedIndex = _gameOptionButton.Selected;
		if (selectedIndex > 0 && _games != null && selectedIndex <= _games.Count)
		{
			return _games[selectedIndex - 1].Name;
		}

		return Path.GetFileName(_selectedGamePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
	}

	private string BuildProjectName(string requestedName)
	{
		return SanitizeIdentifier(requestedName);
	}

	private static string SanitizeIdentifier(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "BepInExMod";
		}

		var builder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			builder.Append(char.IsLetterOrDigit(c) ? c : '_');
		}

		string result = builder.ToString().Trim('_');
		if (string.IsNullOrWhiteSpace(result))
		{
			result = "BepInExMod";
		}

		if (char.IsDigit(result[0]))
		{
			result = "Mod_" + result;
		}

		return result;
	}

	private static string GetDefaultTemplateLocation()
	{
		string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
		if (!string.IsNullOrWhiteSpace(home))
		{
			return Path.Combine(home, "Documents", "BepInExMods");
		}

		string docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
		if (!string.IsNullOrWhiteSpace(docs))
		{
			return Path.Combine(docs, "BepInExMods");
		}

		return Path.Combine(AppContext.BaseDirectory, "BepInExMods");
	}

	private void ShowError(string title, string message)
	{
		_errorDialog.Title = title;
		_errorDialog.DialogText = message;
		_errorDialog.PopupCentered(new Vector2I(620, 280));
	}

	private void OnLogCheckToggled(bool enabled)
	{
		_logOutput.Visible = enabled;
	}

	private void AppendLog(string message)
	{
		if (_logOutput == null)
		{
			GD.Print(message);
			return;
		}

		_logOutput.AppendText(message + "\n");
	}

	public override void _Process(double delta)
	{
	}
}
