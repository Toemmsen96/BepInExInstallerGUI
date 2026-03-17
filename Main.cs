using Godot;
using System;

public partial class Main : Control
{
	private Install _installTab;
	private Uninstall _uninstallTab;

	// Info tab buttons
	private Button _githubButton;
	private Button _nexusModsButton;
	private Button _aedenthornButton;
	private Button _bepinexButton;

	public override void _Ready()
	{
		GetTabReferences();
		GetUIReferences();
		ConnectSignals();
	}

	private void GetTabReferences()
	{
		_installTab = GetNode<Install>("PanelContainer/MarginContainer/TabContainer/Install");
		_uninstallTab = GetNode<Uninstall>("PanelContainer/MarginContainer/TabContainer/Uninstall");
	}

	private void GetUIReferences()
	{
		// Info tab buttons
		_githubButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/GitHub");
		_nexusModsButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/Nexus Mods");
		_aedenthornButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/aedenthorn");
		_bepinexButton = GetNode<Button>("PanelContainer/MarginContainer/TabContainer/Info/MarginContainer/VBoxContainer/HBoxContainer/BepInEx");
	}

	private void ConnectSignals()
	{
		_installTab.InstallCompleted += OnInstallCompleted;

		// Info buttons
		_githubButton.Pressed += () => OS.ShellOpen("https://github.com/Toemmsen96/BepInExInstaller");
		_nexusModsButton.Pressed += () => OS.ShellOpen("https://www.nexusmods.com/site/mods/1601");
		_aedenthornButton.Pressed += () => OS.ShellOpen("https://github.com/aedenthorn/BepInExUnityInstaller");
		_bepinexButton.Pressed += () => OS.ShellOpen("https://docs.bepinex.dev/index.html");
	}

	private void OnInstallCompleted()
	{
		_uninstallTab.RefreshGames();
	}

	public override void _Process(double delta)
	{
	}
}
