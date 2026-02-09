using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using BepInExInstaller;

public partial class Main : Control
{
	private InstallerBackend _installer;
	private List<InstallerBackend.GameInfo> _games;
	
	// UI References
	private OptionButton _gameOptionButton;
	private Button _pickManualButton;
	private FileDialog _fileDialog;
	private CheckBox _verboseCheckbox;
	private CheckBox _consoleCheckbox;
	private Button _installButton;
	private CheckBox _advancedCheckbox;
	private LineEdit _advancedCommandsLineEdit;
	private RichTextLabel _logOutput;
	
	// Info tab buttons
	private Button _githubButton;
	private Button _nexusModsButton;
	private Button _aedenthornButton;
	private Button _bepinexButton;
	
	private string _selectedGamePath = null;

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
			CallDeferred(nameof(AppendLog), $"[color=red]ERROR: {message}[/color]");
		};
		
		_installer.OnProgress = (progress) => 
		{
			// TODO: Update progress bar if you add one
		};
	}

	private void GetUIReferences()
	{
		// Install tab references
		_gameOptionButton = GetNode<OptionButton>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/OptionButton");
		_pickManualButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/pick");
		_fileDialog = GetNode<FileDialog>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/FileDialog");
		_verboseCheckbox = GetNode<CheckBox>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/CheckBox");
		_consoleCheckbox = GetNode<CheckBox>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/ConsoleCheckBox");
		_installButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/Install");
		_advancedCheckbox = GetNode<CheckBox>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/adv");
		_advancedCommandsLineEdit = GetNode<LineEdit>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/cmds");
		_logOutput = GetNode<RichTextLabel>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/ScrollContainer/LogOutput");
		
		// Info tab buttons
		_githubButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/GitHub");
		_nexusModsButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/Nexus Mods");
		_aedenthornButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/aedenthorn");
		_bepinexButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/BepInEx");
		
		// Setup file dialog
		_fileDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
		_fileDialog.Access = FileDialog.AccessEnum.Filesystem;
		_fileDialog.Title = "Select Game Directory";
		
		// Hide advanced commands by default
		_advancedCommandsLineEdit.Visible = false;
	}

	private void ConnectSignals()
	{
		_gameOptionButton.ItemSelected += OnGameSelected;
		_pickManualButton.Pressed += OnPickManualPressed;
		_fileDialog.DirSelected += OnDirectorySelected;
		_installButton.Pressed += OnInstallPressed;
		_verboseCheckbox.Toggled += OnVerboseToggled;
		_consoleCheckbox.Toggled += OnConsoleToggled;
		_advancedCheckbox.Toggled += OnAdvancedToggled;
		
		// Info buttons
		_githubButton.Pressed += () => OS.ShellOpen("https://github.com/Toemmsen96/BepInEx-Installer");
		_nexusModsButton.Pressed += () => OS.ShellOpen("https://www.nexusmods.com/site/mods/540");
		_aedenthornButton.Pressed += () => OS.ShellOpen("https://github.com/aedenthorn");
		_bepinexButton.Pressed += () => OS.ShellOpen("https://github.com/BepInEx/BepInEx");
	}

	private async void LoadGamesAsync()
	{
		AppendLog("[color=cyan]Searching for installed Unity games...[/color]");
		_installButton.Disabled = true;
		
		_games = await _installer.GetInstalledGamesAsync();
		
		// Clear existing items
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
	}

	private void OnGameSelected(long index)
	{
		if (index <= 0 || _games == null || index > _games.Count)
		{
			_selectedGamePath = null;
			return;
		}
		
		var game = _games[(int)index - 1];
		_selectedGamePath = game.InstallPath;
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
		AppendLog($"[color=cyan]Manually selected directory:[/color]");
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
		
		AppendLog("[color=cyan]========== Starting Installation ==========[/color]");
		
		bool success = await _installer.InstallBepInExAsync(_selectedGamePath);
		
		if (success)
		{
			AppendLog("[color=lime]========== Installation Complete! ==========[/color]");
		}
		else
		{
			AppendLog("[color=red]========== Installation Failed ==========[/color]");
		}
		
		_installButton.Disabled = false;
		_installButton.Text = "Install";
	}

	private void OnVerboseToggled(bool pressed)
	{
		_installer.Verbose = pressed;
		AppendLog($"[color=yellow]Verbose output: {(pressed ? "ON" : "OFF")}[/color]");
	}

	private void OnConsoleToggled(bool pressed)
	{
		_installer.ConfigureConsole = pressed;
		AppendLog($"[color=yellow]BepInEx Console: {(pressed ? "ENABLED" : "DISABLED")}[/color]");
	}

	private void OnAdvancedToggled(bool pressed)
	{
		_advancedCommandsLineEdit.Visible = pressed;
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

	public override void _Process(double delta)
	{
	}
}
