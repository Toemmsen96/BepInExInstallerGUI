using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using BepInExInstaller;

public partial class Main : Control
{
	private InstallerBackend _installer;
	private List<InstallerBackend.GameInfo> _games;
	private List<InstallerBackend.GameInfo> _gamesWithBepInEx;
	
	// UI References - Install Tab
	private OptionButton _gameOptionButton;
	private Button _pickManualButton;
	private FileDialog _fileDialog;
	private CheckBox _logCheckbox;
	private CheckBox _verboseCheckbox;
	private CheckBox _consoleCheckbox;
	private CheckBox _pluginsCheckbox;
	private FileDialog _pluginsFileDialog;
	private Button _installButton;
	private CheckBox _advancedCheckbox;
	private LineEdit _advancedCommandsLineEdit;
	private ScrollContainer _scrollContainer;
	private RichTextLabel _logOutput;
	private ProgressBar _progressBar;
	
	// UI References - Uninstall Tab
	private OptionButton _uninstallGameOptionButton;
	private Button _uninstallPickManualButton;
	private FileDialog _uninstallFileDialog;
	private CheckBox _uninstallVerboseCheckbox;
	private Button _uninstallButton;
	private RichTextLabel _uninstallLogOutput;
	
	// Info tab buttons
	private Button _githubButton;
	private Button _nexusModsButton;
	private Button _aedenthornButton;
	private Button _bepinexButton;
	
	private string _selectedGamePath = null;
	private string _selectedUninstallGamePath = null;
	private string _selectedPluginZipPath = null;

	public override void _Ready()
	{
		_installer = new InstallerBackend();
		SetupInstallerCallbacks();
		GetUIReferences();
		ConnectSignals();
		LoadGamesAsync();
		LoadInstalledBepInExGamesAsync();
	}

	private bool _isUninstallMode = false;
	
	private void SetupInstallerCallbacks()
	{
		_installer.OnLog = (message) => 
		{
			if (_isUninstallMode)
				CallDeferred(nameof(AppendUninstallLog), $"[color=white]{message}[/color]");
			else
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
			if (_isUninstallMode)
				CallDeferred(nameof(AppendUninstallLog), $"[color={color}][{type}] {message}[/color]");
			else
				CallDeferred(nameof(AppendLog), $"[color={color}][{type}] {message}[/color]");
		};
		
		_installer.OnError = (message) => 
		{
			if (_isUninstallMode)
				CallDeferred(nameof(AppendUninstallLog), $"[color=red]ERROR: {message}[/color]");
			else
				CallDeferred(nameof(AppendLog), $"[color=red]ERROR: {message}[/color]");
		};
		
		_installer.OnProgress = (progress) => 
		{
			CallDeferred(nameof(UpdateProgress), progress);
		};
	}

	private void GetUIReferences()
	{
		// Install tab references
		_gameOptionButton = GetNode<OptionButton>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/OptionButton");
		_pickManualButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/pick");
		_fileDialog = GetNode<FileDialog>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/FileDialog");
		_logCheckbox = GetNode<CheckBox>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/LogCheck");
		_verboseCheckbox = GetNode<CheckBox>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/CheckBox");
		_consoleCheckbox = GetNode<CheckBox>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/ConsoleCheckBox");
		_pluginsCheckbox = GetNode<CheckBox>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/PluginsCheckBox2");
		_pluginsFileDialog = GetNode<FileDialog>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/PluginsCheckBox2/FileDialog");
		_installButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/Install");
		_advancedCheckbox = GetNode<CheckBox>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/adv");
		_advancedCommandsLineEdit = GetNode<LineEdit>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/cmds");
		_scrollContainer = GetNode<ScrollContainer>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/ScrollContainer");
		_logOutput = GetNode<RichTextLabel>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/ScrollContainer/LogOutput");
		_progressBar = GetNode<ProgressBar>("PanelContainer/MarginContainer/TabContainer/Install/MarginContainer2/VBoxContainer/ProgressBar");
		
		// Uninstall tab references
		_uninstallGameOptionButton = GetNode<OptionButton>("PanelContainer/MarginContainer/TabContainer/Uninstall/MarginContainer2/VBoxContainer/OptionButton");
		_uninstallPickManualButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Uninstall/MarginContainer2/VBoxContainer/pick");
		_uninstallFileDialog = GetNode<FileDialog>("PanelContainer/MarginContainer/TabContainer/Uninstall/MarginContainer2/VBoxContainer/FileDialog");
		_uninstallVerboseCheckbox = GetNode<CheckBox>("PanelContainer/MarginContainer/TabContainer/Uninstall/MarginContainer2/VBoxContainer/CheckBox");
		_uninstallButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Uninstall/MarginContainer2/VBoxContainer/Install");
		_uninstallLogOutput = GetNode<RichTextLabel>("PanelContainer/MarginContainer/TabContainer/Uninstall/MarginContainer2/VBoxContainer/ScrollContainer/LogOutput");
		
		// Info tab buttons
		_githubButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/GitHub");
		_nexusModsButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/Nexus Mods");
		_aedenthornButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/aedenthorn");
		_bepinexButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/BepInEx");
		
		// Setup file dialogs
		_fileDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
		_fileDialog.Access = FileDialog.AccessEnum.Filesystem;
		_fileDialog.Title = "Select Game Directory";
		
		_pluginsFileDialog.Access = FileDialog.AccessEnum.Filesystem;
		
		_uninstallFileDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
		_uninstallFileDialog.Access = FileDialog.AccessEnum.Filesystem;
		_uninstallFileDialog.Title = "Select Game Directory to Uninstall From";
		
		// Hide advanced commands by default
		_advancedCommandsLineEdit.Visible = false;
		_consoleCheckbox.Visible = false; 
		
		// Clear option buttons immediately to prevent duplicates
		_gameOptionButton.Clear();
		_gameOptionButton.AddItem("Loading games...");
		_gameOptionButton.Disabled = true;
		
		_uninstallGameOptionButton.Clear();
		_uninstallGameOptionButton.AddItem("Loading games...");
		_uninstallGameOptionButton.Disabled = true;
	}

	private void ConnectSignals()
	{
		// Install tab signals
		_gameOptionButton.ItemSelected += OnGameSelected;
		_pickManualButton.Pressed += OnPickManualPressed;
		_fileDialog.DirSelected += OnDirectorySelected;
		_installButton.Pressed += OnInstallPressed;
		_logCheckbox.Toggled += OnLogCheckToggled;
		_verboseCheckbox.Toggled += OnVerboseToggled;
		_consoleCheckbox.Toggled += OnConsoleToggled;
		_pluginsCheckbox.Toggled += OnPluginsToggled;
		_pluginsFileDialog.FileSelected += OnPluginFileSelected;
		_advancedCheckbox.Toggled += OnAdvancedToggled;
		
		// Uninstall tab signals
		_uninstallGameOptionButton.ItemSelected += OnUninstallGameSelected;
		_uninstallPickManualButton.Pressed += OnUninstallPickManualPressed;
		_uninstallFileDialog.DirSelected += OnUninstallDirectorySelected;
		_uninstallButton.Pressed += OnUninstallPressed;
		_uninstallVerboseCheckbox.Toggled += OnUninstallVerboseToggled;
		
		// Info buttons
		_githubButton.Pressed += () => OS.ShellOpen("https://github.com/Toemmsen96/BepInExInstaller");
		_nexusModsButton.Pressed += () => OS.ShellOpen("https://www.nexusmods.com/site/mods/1601");
		_aedenthornButton.Pressed += () => OS.ShellOpen("https://github.com/aedenthorn/BepInExUnityInstaller");
		_bepinexButton.Pressed += () => OS.ShellOpen("https://docs.bepinex.dev/index.html");
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

	private async void LoadInstalledBepInExGamesAsync()
	{
		AppendUninstallLog("[color=cyan]Searching for games with BepInEx installed...[/color]");
		_uninstallButton.Disabled = true;
		
		// Get all games and filter for those with BepInEx
		var allGames = await _installer.GetInstalledGamesAsync();
		_gamesWithBepInEx = allGames.Where(game => 
			System.IO.Directory.Exists(System.IO.Path.Combine(game.InstallPath, "BepInEx"))
		).ToList();
		
		// Clear existing items
		_uninstallGameOptionButton.Clear();
		
		if (_gamesWithBepInEx.Count == 0)
		{
			_uninstallGameOptionButton.AddItem("No games with BepInEx found");
			_uninstallGameOptionButton.Disabled = true;
			AppendUninstallLog("[color=yellow]No games with BepInEx installed found.[/color]");
		}
		else
		{
			_uninstallGameOptionButton.AddItem("-- Select a game --");
			foreach (var game in _gamesWithBepInEx)
			{
				_uninstallGameOptionButton.AddItem(game.Name);
			}
			_uninstallGameOptionButton.Selected = 0;
			_uninstallGameOptionButton.Disabled = false;
			AppendUninstallLog($"[color=green]Found {_gamesWithBepInEx.Count} games with BepInEx installed![/color]");
		}
		
		_uninstallButton.Disabled = false;
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
		
		_isUninstallMode = false;
		_installButton.Disabled = true;
		_installButton.Text = "Installing...";
		_progressBar.Value = 0;
		
		AppendLog("[color=cyan]========== Starting Installation ==========[/color]");
		
		bool success = await _installer.InstallBepInExAsync(_selectedGamePath);
		
		if (success)
		{
			AppendLog("[color=lime]========== Installation Complete! ==========[/color]");
			
			// Install plugins if a zip was selected
			if (!string.IsNullOrEmpty(_selectedPluginZipPath))
			{
				AppendLog("[color=cyan]========== Installing Plugins ==========[/color]");
				bool pluginSuccess = await _installer.InstallPluginsAsync(_selectedGamePath, _selectedPluginZipPath);
				
				if (pluginSuccess)
				{
					AppendLog("[color=lime]========== Plugin Installation Complete! ==========[/color]");
				}
				else
				{
					AppendLog("[color=red]========== Plugin Installation Failed ==========[/color]");
				}
			}
		}
		else
		{
			AppendLog("[color=red]========== Installation Failed ==========[/color]");
		}
		
		_installButton.Disabled = false;
		_installButton.Text = "Install";
	}

	private void OnLogCheckToggled(bool pressed)
	{
		_verboseCheckbox.Visible = pressed;
		_scrollContainer.Visible = pressed;
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
		_consoleCheckbox.Visible = pressed;
	}

	private void OnPluginsToggled(bool pressed)
	{
		if (pressed)
		{
			_pluginsFileDialog.PopupCentered(new Vector2I(600, 400));
		}
		else
		{
			_selectedPluginZipPath = null;
			AppendLog("[color=yellow]Plugin installation disabled[/color]");
		}
	}

	private void OnPluginFileSelected(string path)
	{
		_selectedPluginZipPath = path;
		AppendLog($"[color=cyan]Selected plugin zip:[/color]");
		AppendLog($"[color=gray]{path}[/color]");
	}

	private void OnUninstallGameSelected(long index)
	{
		if (index <= 0 || _gamesWithBepInEx == null || index > _gamesWithBepInEx.Count)
		{
			_selectedUninstallGamePath = null;
			_uninstallPickManualButton.Visible = true;
			return;
		}
		
		var game = _gamesWithBepInEx[(int)index - 1];
		_selectedUninstallGamePath = game.InstallPath;
		_uninstallPickManualButton.Visible = false;
		AppendUninstallLog($"[color=cyan]Selected: {game.Name}[/color]");
		AppendUninstallLog($"[color=gray]Path: {_selectedUninstallGamePath}[/color]");
	}

	private void OnUninstallPickManualPressed()
	{
		_uninstallFileDialog.PopupCentered(new Vector2I(800, 600));
	}

	private void OnUninstallDirectorySelected(string dir)
	{
		_selectedUninstallGamePath = dir;
		_uninstallPickManualButton.Visible = false;
		AppendUninstallLog($"[color=cyan]Manually selected directory:[/color]");
		AppendUninstallLog($"[color=gray]{dir}[/color]");
	}

	private async void OnUninstallPressed()
	{
		if (string.IsNullOrEmpty(_selectedUninstallGamePath))
		{
			AppendUninstallLog("[color=red]Please select a game first![/color]");
			return;
		}
		
		_isUninstallMode = true;
		_uninstallButton.Disabled = true;
		_uninstallButton.Text = "Uninstalling...";
		
		AppendUninstallLog("[color=cyan]========== Starting Uninstallation ==========[/color]");
		
		bool success = await _installer.UninstallBepInExAsync(_selectedUninstallGamePath);
		
		if (success)
		{
			AppendUninstallLog("[color=lime]========== Uninstallation Complete! ==========[/color]");
			// Refresh the list of games with BepInEx
			LoadInstalledBepInExGamesAsync();
		}
		else
		{
			AppendUninstallLog("[color=red]========== Uninstallation Failed ==========[/color]");
		}
		
		_uninstallButton.Disabled = false;
		_uninstallButton.Text = "Uninstall";
	}

	private void OnUninstallVerboseToggled(bool pressed)
	{
		_installer.Verbose = pressed;
		AppendUninstallLog($"[color=yellow]Verbose output: {(pressed ? "ON" : "OFF")}[/color]");
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

	private void AppendUninstallLog(string message)
	{
		if (_uninstallLogOutput != null)
		{
			_uninstallLogOutput.AppendText(message + "\n");
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
