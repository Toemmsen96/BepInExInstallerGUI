using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using BepInExInstaller;

public partial class Uninstall : PanelContainer
{
	private InstallerBackend _installer;
	private List<InstallerBackend.GameInfo> _gamesWithBepInEx;

	private OptionButton _uninstallGameOptionButton;
	private Button _uninstallRecheckGamesButton;
	private Button _uninstallPickManualButton;
	private FileDialog _uninstallFileDialog;
	private CheckBox _uninstallKeepPluginsCheckbox;
	private Button _uninstallButton;
	private RichTextLabel _uninstallLogOutput;
	private AcceptDialog _resultDialog;

	private readonly List<string> _operationErrors = new();

	private const string SettingsPath = "user://settings.cfg";
	private const string UiSection = "ui";
	private const string CompletionPopupsKey = "show_completion_popups";
	private const string VerboseOutputKey = "verbose_output_enabled";

	private string _selectedUninstallGamePath = null;

	public override void _Ready()
	{
		_installer = new InstallerBackend();
		SetupInstallerCallbacks();
		GetUIReferences();
		ConnectSignals();
		LoadInstalledBepInExGamesAsync();
	}

	private void SetupInstallerCallbacks()
	{
		_installer.OnLog = (message) =>
		{
			CallDeferred(nameof(AppendUninstallLog), $"[color=white]{message}[/color]");
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
			CallDeferred(nameof(AppendUninstallLog), $"[color={color}][{type}] {message}[/color]");
		};

		_installer.OnError = (message) =>
		{
			_operationErrors.Add(message);
			CallDeferred(nameof(AppendUninstallLog), $"[color=red]ERROR: {message}[/color]");
		};
	}

	private void GetUIReferences()
	{
		_uninstallGameOptionButton = GetNode<OptionButton>("MarginContainer2/VBoxContainer/OptionButton");
		_uninstallRecheckGamesButton = GetNode<Button>("MarginContainer2/VBoxContainer/RecheckGames");
		_uninstallPickManualButton = GetNode<Button>("MarginContainer2/VBoxContainer/pick");
		_uninstallFileDialog = GetNode<FileDialog>("MarginContainer2/VBoxContainer/FileDialog");
		_uninstallKeepPluginsCheckbox = GetNode<CheckBox>("MarginContainer2/VBoxContainer/KeepPluginsCheck");
		_uninstallButton = GetNode<Button>("MarginContainer2/VBoxContainer/Uninstall");
		_uninstallLogOutput = GetNode<RichTextLabel>("MarginContainer2/VBoxContainer/ScrollContainer/LogOutput");
		_resultDialog = GetNode<AcceptDialog>("MarginContainer2/VBoxContainer/ResultDialog");

		_uninstallFileDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
		_uninstallFileDialog.Access = FileDialog.AccessEnum.Filesystem;
		_uninstallFileDialog.Title = "Select Game Directory to Uninstall From";

		_uninstallGameOptionButton.Clear();
		_uninstallGameOptionButton.AddItem("Loading games...");
		_uninstallGameOptionButton.Disabled = true;

		SetVerboseFromSettings(ReadVerboseSetting());
		StyleResultDialog();
	}

	private void ConnectSignals()
	{
		_uninstallGameOptionButton.ItemSelected += OnUninstallGameSelected;
		_uninstallRecheckGamesButton.Pressed += OnUninstallRecheckGamesPressed;
		_uninstallPickManualButton.Pressed += OnUninstallPickManualPressed;
		_uninstallFileDialog.DirSelected += OnUninstallDirectorySelected;
		_uninstallButton.Pressed += OnUninstallPressed;
	}

	public void RefreshGames()
	{
		LoadInstalledBepInExGamesAsync();
	}

	private async void LoadInstalledBepInExGamesAsync()
	{
		AppendUninstallLog("[color=cyan]Searching for games with BepInEx installed...[/color]");
		_uninstallButton.Disabled = true;
		_uninstallRecheckGamesButton.Disabled = true;

		var allGames = await _installer.GetInstalledGamesAsync();
		_gamesWithBepInEx = allGames.Where(game =>
			_installer.IsBepInExInstalled(game.InstallPath)
		).ToList();

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
		_uninstallRecheckGamesButton.Disabled = false;
	}

	private void OnUninstallRecheckGamesPressed()
	{
		AppendUninstallLog("[color=cyan]Manual rescan requested...[/color]");
		_selectedUninstallGamePath = null;
		_uninstallPickManualButton.Visible = true;
		LoadInstalledBepInExGamesAsync();
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
		AppendUninstallLog("[color=cyan]Manually selected directory:[/color]");
		AppendUninstallLog($"[color=gray]{dir}[/color]");
	}

	private async void OnUninstallPressed()
	{
		if (string.IsNullOrEmpty(_selectedUninstallGamePath))
		{
			AppendUninstallLog("[color=red]Please select a game first![/color]");
			return;
		}

		_uninstallButton.Disabled = true;
		_uninstallButton.Text = "Uninstalling...";
		_operationErrors.Clear();

		AppendUninstallLog("[color=cyan]========== Starting Uninstallation ==========[/color]");

		bool keepPlugins = _uninstallKeepPluginsCheckbox.ButtonPressed;
		bool success = await _installer.UninstallBepInExAsync(_selectedUninstallGamePath, keepPlugins);

		if (success)
		{
			AppendUninstallLog("[color=lime]========== Uninstallation Complete! ==========[/color]");
			LoadInstalledBepInExGamesAsync();
		}
		else
		{
			AppendUninstallLog("[color=red]========== Uninstallation Failed ==========[/color]");
		}

		ShowResultPopup(success);

		_uninstallButton.Disabled = false;
		_uninstallButton.Text = "Uninstall";
	}

	private void ShowResultPopup(bool success)
	{
		if (!ShouldShowCompletionPopups())
		{
			return;
		}

		if (success)
		{
			_resultDialog.Title = "Uninstall Finished";
			_resultDialog.DialogText = "BepInEx was uninstalled successfully.";
		}
		else
		{
			_resultDialog.Title = "Uninstall Failed";
			_resultDialog.DialogText = BuildProblemMessage("BepInEx uninstallation failed.");
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
		var accentRed = new Color(0.979804f, 0.551532f, 0.539867f, 1.0f);

		_resultDialog.AddThemeColorOverride("font_color", lightText);
		_resultDialog.AddThemeColorOverride("title_color", accentRed);
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

	public override void _Process(double delta)
	{
	}
}
