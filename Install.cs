using Godot;
using System;
using System.Collections.Generic;
using BepInExInstaller;

public partial class Install : PanelContainer
{
	[Signal]
	public delegate void InstallCompletedEventHandler();

	private InstallerBackend _installer;
	private List<InstallerBackend.GameInfo> _games;

	private OptionButton _gameOptionButton;
	private Button _recheckGamesButton;
	private Button _pickManualButton;
	private FileDialog _fileDialog;
	private CheckBox _logCheckbox;
	private CheckBox _consoleCheckbox;
	private CheckBox _pluginsCheckbox;
	private Button _pickPluginsButton;
	private FileDialog _pluginsFileDialog;
	private Button _installButton;
	private CheckBox _advancedCheckbox;
	private LineEdit _advancedCommandsLineEdit;
	private ScrollContainer _scrollContainer;
	private RichTextLabel _logOutput;
	private ProgressBar _progressBar;
	private AcceptDialog _resultDialog;

	private readonly List<string> _operationErrors = new();

	private const string SettingsPath = "user://settings.cfg";
	private const string UiSection = "ui";
	private const string CompletionPopupsKey = "show_completion_popups";
	private const string VerboseOutputKey = "verbose_output_enabled";

	private string _selectedGamePath = null;
	private string _selectedPluginZipPath = null;

	public override void _Ready()
	{
		_installer = new InstallerBackend();
		SetupInstallerCallbacks();
		GetUIReferences();
		ConnectSignals();
		LoadGamesAsync();
	}

	private void SetupInstallerCallbacks()
	{
		_installer.OnLog = (message) =>
		{
			CallDeferred(nameof(AppendLog), $"[color=white]{message}[/color]");
		};

		_installer.OnVerboseLog = (message, type) =>
		{
			string color = type switch
			{
				InstallerBackend.MessageType.Info => "green",
				InstallerBackend.MessageType.Warning => "yellow",
				InstallerBackend.MessageType.Error => "red",
				_ => "white"
			};
			CallDeferred(nameof(AppendLog), $"[color={color}][{type}] {message}[/color]");
		};

		_installer.OnError = (message) =>
		{
			_operationErrors.Add(message);
			CallDeferred(nameof(AppendLog), $"[color=red]ERROR: {message}[/color]");
		};

		_installer.OnProgress = (progress) =>
		{
			CallDeferred(nameof(UpdateProgress), progress);
		};
	}

	private void GetUIReferences()
	{
		_gameOptionButton = GetNode<OptionButton>("MarginContainer2/VBoxContainer/OptionButton");
		_recheckGamesButton = GetNode<Button>("MarginContainer2/VBoxContainer/RecheckGames");
		_pickManualButton = GetNode<Button>("MarginContainer2/VBoxContainer/pick");
		_fileDialog = GetNode<FileDialog>("MarginContainer2/VBoxContainer/FileDialog");
		_logCheckbox = GetNode<CheckBox>("MarginContainer2/VBoxContainer/LogCheck");
		_consoleCheckbox = GetNode<CheckBox>("MarginContainer2/VBoxContainer/ConsoleCheckBox");
		_pluginsCheckbox = GetNode<CheckBox>("MarginContainer2/VBoxContainer/PluginsCheckBox2");
		_pluginsFileDialog = GetNode<FileDialog>("MarginContainer2/VBoxContainer/PluginsCheckBox2/FileDialog");
		_pickPluginsButton = GetNode<Button>("MarginContainer2/VBoxContainer/PickPlugins");
		_installButton = GetNode<Button>("MarginContainer2/VBoxContainer/Install");
		_advancedCheckbox = GetNode<CheckBox>("MarginContainer2/VBoxContainer/adv");
		_advancedCommandsLineEdit = GetNode<LineEdit>("MarginContainer2/VBoxContainer/cmds");
		_scrollContainer = GetNode<ScrollContainer>("MarginContainer2/VBoxContainer/ScrollContainer");
		_logOutput = GetNode<RichTextLabel>("MarginContainer2/VBoxContainer/ScrollContainer/LogOutput");
		_progressBar = GetNode<ProgressBar>("MarginContainer2/VBoxContainer/ProgressBar");
		_resultDialog = GetNode<AcceptDialog>("MarginContainer2/VBoxContainer/ResultDialog");

		_fileDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
		_fileDialog.Access = FileDialog.AccessEnum.Filesystem;
		_fileDialog.Title = "Select Game Directory";

		_pluginsFileDialog.Access = FileDialog.AccessEnum.Filesystem;
		_pluginsFileDialog.Title = "Select Plugins.zip Files to install";

		_advancedCommandsLineEdit.Visible = false;
		_consoleCheckbox.Visible = false;
		_pickPluginsButton.Visible = false;

		_gameOptionButton.Clear();
		_gameOptionButton.AddItem("Loading games...");
		_gameOptionButton.Disabled = true;

		SetVerboseFromSettings(ReadVerboseSetting());
		StyleResultDialog();
	}

	private void ConnectSignals()
	{
		_gameOptionButton.ItemSelected += OnGameSelected;
		_recheckGamesButton.Pressed += OnRecheckGamesPressed;
		_pickManualButton.Pressed += OnPickManualPressed;
		_fileDialog.DirSelected += OnDirectorySelected;
		_installButton.Pressed += OnInstallPressed;
		_logCheckbox.Toggled += OnLogCheckToggled;
		_consoleCheckbox.Toggled += OnConsoleToggled;
		_pluginsCheckbox.Toggled += OnPluginsToggled;
		_pickPluginsButton.Pressed += () => _pluginsFileDialog.PopupCentered(new Vector2I(600, 400));
		_pluginsFileDialog.FileSelected += OnPluginFileSelected;
		_advancedCheckbox.Toggled += OnAdvancedToggled;
	}

	private async void LoadGamesAsync()
	{
		AppendLog("[color=cyan]Searching for installed Unity games...[/color]");
		_installButton.Disabled = true;
		_recheckGamesButton.Disabled = true;

		_games = await _installer.GetInstalledGamesAsync();

		_gameOptionButton.Clear();

		if (_games.Count == 0)
		{
			_gameOptionButton.AddItem("No Unity games found");
			_gameOptionButton.Disabled = true;
			AppendLog("[color=yellow]No Unity games found. Use 'Pick Gamefiles manually' to select a game directory.[/color]");
		}
		else
		{
			_gameOptionButton.AddItem("-- Select a game --");
			foreach (var game in _games)
			{
				_gameOptionButton.AddItem(game.Name);
			}
			_gameOptionButton.Selected = 0;
			_gameOptionButton.Disabled = false;
			AppendLog($"[color=green]Found {_games.Count} Unity games![/color]");
		}

		_installButton.Disabled = false;
		_recheckGamesButton.Disabled = false;
	}

	private void OnRecheckGamesPressed()
	{
		AppendLog("[color=cyan]Manual rescan requested...[/color]");
		_selectedGamePath = null;
		_pickManualButton.Visible = true;
		LoadGamesAsync();
	}

	private void OnGameSelected(long index)
	{
		if (index <= 0 || _games == null || index > _games.Count)
		{
			_selectedGamePath = null;
			_pickManualButton.Visible = true;
			return;
		}

		var game = _games[(int)index - 1];
		_selectedGamePath = game.InstallPath;
		_pickManualButton.Visible = false;
		AppendLog($"[color=cyan]Selected: {game.Name}[/color]");
		AppendLog($"[color=gray]Path: {_selectedGamePath}[/color]");
	}

	private void OnPickManualPressed()
	{
		_fileDialog.PopupCentered(new Vector2I(800, 600));
	}

	private void OnDirectorySelected(string dir)
	{
		_selectedGamePath = dir;
		_pickManualButton.Visible = false;
		AppendLog("[color=cyan]Manually selected directory:[/color]");
		AppendLog($"[color=gray]{dir}[/color]");
	}

	private async void OnInstallPressed()
	{
		if (string.IsNullOrEmpty(_selectedGamePath))
		{
			AppendLog("[color=red]Please select a game first![/color]");
			return;
		}

		_installButton.Disabled = true;
		_installButton.Text = "Installing...";
		_progressBar.Value = 0;
		_operationErrors.Clear();

		AppendLog("[color=cyan]========== Starting Installation ==========[/color]");

		bool success = await _installer.InstallBepInExAsync(_selectedGamePath);
		bool pluginSuccess = true;

		if (success)
		{
			AppendLog("[color=lime]========== Installation Complete! ==========[/color]");

			if (!string.IsNullOrEmpty(_selectedPluginZipPath))
			{
				AppendLog("[color=cyan]========== Installing Plugins ==========[/color]");
				pluginSuccess = await _installer.InstallPluginsAsync(_selectedGamePath, _selectedPluginZipPath);

				if (pluginSuccess)
				{
					AppendLog("[color=lime]========== Plugin Installation Complete! ==========[/color]");
				}
				else
				{
					AppendLog("[color=red]========== Plugin Installation Failed ==========[/color]");
				}
			}

			EmitSignal(SignalName.InstallCompleted);
		}
		else
		{
			AppendLog("[color=red]========== Installation Failed ==========[/color]");
		}

		ShowResultPopup(success, pluginSuccess);

		_installButton.Disabled = false;
		_installButton.Text = "Install";
	}

	private void ShowResultPopup(bool installSuccess, bool pluginSuccess)
	{
		if (!ShouldShowCompletionPopups())
		{
			return;
		}

		if (installSuccess && pluginSuccess)
		{
			_resultDialog.Title = "Install Finished";
			_resultDialog.DialogText = "BepInEx was installed successfully.";
		}
		else if (installSuccess)
		{
			_resultDialog.Title = "Install Finished With Problems";
			_resultDialog.DialogText = BuildProblemMessage("BepInEx installed, but plugin installation failed.");
		}
		else
		{
			_resultDialog.Title = "Install Failed";
			_resultDialog.DialogText = BuildProblemMessage("BepInEx installation failed.");
		}

		_resultDialog.PopupCentered(new Vector2I(560, 260));
	}

	private string BuildProblemMessage(string header)
	{
		if (_operationErrors.Count == 0)
		{
			return header + "\n\nNo detailed errors were reported.";
		}

		int count = Math.Min(_operationErrors.Count, 4);
		var lines = new string[count];
		for (int i = 0; i < count; i++)
		{
			lines[i] = $"- {_operationErrors[i]}";
		}

		return header + "\n\nProblems:\n" + string.Join("\n", lines);
	}

	private void StyleResultDialog()
	{
		// Apply theme colors to match app styling
		var darkBg = new Color(0.15f, 0.15f, 0.15f, 0.95f);
		var lightText = new Color(0.9f, 0.9f, 0.9f, 1.0f);
		var accentGreen = new Color(0.55153227f, 0.9798047f, 0.7516775f, 1.0f);

		_resultDialog.AddThemeColorOverride("font_color", lightText);
		_resultDialog.AddThemeColorOverride("title_color", accentGreen);
		_resultDialog.AddThemeColorOverride("panel_color", darkBg);
		_resultDialog.AddThemeColorOverride("button_font_color", lightText);
		_resultDialog.AddThemeColorOverride("button_focus_color", new Color(0.3f, 0.3f, 0.3f, 1.0f));
		_resultDialog.AddThemeColorOverride("button_hover_color", new Color(0.25f, 0.25f, 0.25f, 1.0f));
	}

	private bool ShouldShowCompletionPopups()
	{
		var config = new ConfigFile();
		if (config.Load(SettingsPath) != Error.Ok)
		{
			return true;
		}

		return (bool)config.GetValue(UiSection, CompletionPopupsKey, true);
	}

	private void OnLogCheckToggled(bool pressed)
	{
		_scrollContainer.Visible = pressed;
	}

	public void SetVerboseFromSettings(bool enabled)
	{
		_installer.Verbose = enabled;
	}

	private bool ReadVerboseSetting()
	{
		var config = new ConfigFile();
		if (config.Load(SettingsPath) != Error.Ok)
		{
			return true;
		}

		return (bool)config.GetValue(UiSection, VerboseOutputKey, true);
	}

	private void OnConsoleToggled(bool pressed)
	{
		_installer.ConfigureConsole = pressed;
		AppendLog($"[color=yellow]BepInEx Console: {(pressed ? "ENABLED" : "DISABLED")}[/color]");
	}

	private void OnAdvancedToggled(bool pressed)
	{
		_advancedCommandsLineEdit.Visible = pressed;
		_consoleCheckbox.Visible = pressed;
	}

	private void OnPluginsToggled(bool pressed)
	{
		if (pressed)
		{
			_pickPluginsButton.Visible = true;
		}
		else
		{
			_pickPluginsButton.Visible = false;
			_selectedPluginZipPath = null;
			AppendLog("[color=yellow]Plugin installation disabled[/color]");
		}
	}

	private void OnPluginFileSelected(string path)
	{
		_selectedPluginZipPath = path;
		AppendLog("[color=cyan]Selected plugin zip:[/color]");
		AppendLog($"[color=gray]{path}[/color]");
	}

	private void AppendLog(string message)
	{
		if (_logOutput != null)
		{
			_logOutput.AppendText(message + "\n");
		}
		else
		{
			GD.Print(message);
		}
	}

	private void UpdateProgress(double progress)
	{
		if (_progressBar != null)
		{
			_progressBar.Value = progress * 100;
		}
	}

	public override void _Process(double delta)
	{
	}
}
