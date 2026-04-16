using Godot;
using System.IO;

public partial class Settings : PanelContainer
{
	[Signal]
	public delegate void VerboseSettingChangedEventHandler(bool enabled);

	[Signal]
	public delegate void SteamPathChangedEventHandler(string steamPath);

	private const string SettingsPath = "user://settings.cfg";

	private const string ColorSection = "appearance";
	private const string BgColorKey = "background_color";
	private const string TransparentKey = "transparent_background";
	private const string UiSection = "ui";
	private const string CompletionPopupsKey = "show_completion_popups";
	private const string VerboseOutputKey = "verbose_output_enabled";
	private const string SteamSection = "steam";
	private const string SteamPathKey = "steam_path";

	private Button _pickColorButton;
	private Button _resetButton;
	private Button _pickSteamPathButton;
	private Label _steamPathLabel;
	private MarginContainer _pickerContainer;
	private ColorPicker _colorPicker;
	private CheckBox _transparentCheck;
	private CheckBox _completionPopupsCheck;
	private CheckBox _verboseOutputCheck;
	private ConfirmationDialog _resetConfirmDialog;
	private FileDialog _steamPathDialog;

	private ColorRect _background;
	private PanelContainer _glassPanel;
	private Color DefaultBaseColor = new Color(0.22647282f, 0.144768f, 0.25970086f, 1.0f);
	private Color _baseColor = new Color(0.22647282f, 0.144768f, 0.25970086f, 1.0f);
	private bool _useTransparentBackground = true;
	private bool _showCompletionPopups = true;
	private bool _verboseOutputEnabled = true;
	private string _steamPath = string.Empty;

	public override void _Ready()
	{
		DefaultBaseColor = GetNode<ColorRect>("../../../../Background").Color;
		_baseColor = DefaultBaseColor;
		GetUiReferences();
		LoadSettings();
		ConnectSignals();
		ApplySettings();
	}

	private void GetUiReferences()
	{
		_pickColorButton = GetNode<Button>("ScrollContainer/MarginContainer3/VBoxContainer/pickcolor");
		_resetButton = GetNode<Button>("ScrollContainer/MarginContainer3/VBoxContainer/Reset");
		_pickSteamPathButton = GetNode<Button>("ScrollContainer/MarginContainer3/VBoxContainer/SteamPathButton");
		_steamPathLabel = GetNode<Label>("ScrollContainer/MarginContainer3/VBoxContainer/SteamPathLabel");
		_pickerContainer = GetNode<MarginContainer>("ScrollContainer/MarginContainer3/VBoxContainer/MarginContainer");
		_colorPicker = GetNode<ColorPicker>("ScrollContainer/MarginContainer3/VBoxContainer/MarginContainer/ColorPicker");
		_transparentCheck = GetNode<CheckBox>("ScrollContainer/MarginContainer3/VBoxContainer/TransparentCheck");
		_completionPopupsCheck = GetNode<CheckBox>("ScrollContainer/MarginContainer3/VBoxContainer/PopupCheck");
		_verboseOutputCheck = GetNode<CheckBox>("ScrollContainer/MarginContainer3/VBoxContainer/VerboseCheck");
		_resetConfirmDialog = GetNode<ConfirmationDialog>("ScrollContainer/MarginContainer3/VBoxContainer/ResetConfirmDialog");
		_steamPathDialog = GetNode<FileDialog>("ScrollContainer/MarginContainer3/VBoxContainer/SteamPathDialog");

		// Settings node is Control/PanelContainer/MarginContainer/TabContainer/Settings.
		_background = GetNode<ColorRect>("../../../../Background");
		_glassPanel = GetNode<PanelContainer>("../../../");

		_steamPathDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
		_steamPathDialog.Access = FileDialog.AccessEnum.Filesystem;
		_steamPathDialog.Title = "Select Steam Installation Folder";

		StyleResetDialog();
	}

	private void ConnectSignals()
	{
		_pickColorButton.Pressed += OnPickColorPressed;
		_resetButton.Pressed += OnResetPressed;
		_pickSteamPathButton.Pressed += OnPickSteamPathPressed;
		_colorPicker.ColorChanged += OnColorChanged;
		_transparentCheck.Toggled += OnTransparentToggled;
		_completionPopupsCheck.Toggled += OnCompletionPopupsToggled;
		_verboseOutputCheck.Toggled += OnVerboseOutputToggled;
		_resetConfirmDialog.Confirmed += OnResetConfirmed;
		_steamPathDialog.DirSelected += OnSteamPathSelected;
	}

	private void OnPickColorPressed()
	{
		_pickerContainer.Visible = !_pickerContainer.Visible;
	}

	private void OnPickSteamPathPressed()
	{
		_steamPathDialog.PopupCentered(new Vector2I(800, 600));
	}

	private void OnColorChanged(Color color)
	{
		_baseColor = new Color(color.R, color.G, color.B, 1.0f);
		ApplySettings();
		SaveSettings();
	}

	private void OnTransparentToggled(bool toggledOn)
	{
		_useTransparentBackground = toggledOn;
		ApplySettings();
		SaveSettings();
	}

	private void OnCompletionPopupsToggled(bool toggledOn)
	{
		_showCompletionPopups = toggledOn;
		SaveSettings();
	}

	private void OnVerboseOutputToggled(bool toggledOn)
	{
		_verboseOutputEnabled = toggledOn;
		SaveSettings();
		EmitSignal(SignalName.VerboseSettingChanged, toggledOn);
	}

	private void OnResetPressed()
	{
		_resetConfirmDialog.PopupCentered(new Vector2I(420, 180));
	}

	private void OnResetConfirmed()
	{
		ResetToDefaults();
	}

	private void OnSteamPathSelected(string dir)
	{
		if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
		{
			return;
		}

		_steamPath = dir;
		SaveSettings();
		UpdateSteamPathUi();
		EmitSignal(SignalName.SteamPathChanged, _steamPath);
	}

	private void ApplySettings()
	{
		if (_colorPicker != null)
		{
			_colorPicker.Color = _baseColor;
		}

		if (_transparentCheck != null)
		{
			_transparentCheck.ButtonPressed = _useTransparentBackground;
		}

		if (_completionPopupsCheck != null)
		{
			_completionPopupsCheck.ButtonPressed = _showCompletionPopups;
		}

		if (_verboseOutputCheck != null)
		{
			_verboseOutputCheck.ButtonPressed = _verboseOutputEnabled;
		}

		UpdateSteamPathUi();

		float alpha = _useTransparentBackground ? 0.85f : 1.0f;
		if (_background != null)
		{
			_background.Color = new Color(_baseColor.R, _baseColor.G, _baseColor.B, alpha);
		}

		if (_glassPanel?.Material is ShaderMaterial shaderMat)
		{
			shaderMat.SetShaderParameter("u_tint_color", _baseColor);
		}
	}

	private void LoadSettings()
	{
		var config = new ConfigFile();
		var err = config.Load(SettingsPath);
		if (err != Error.Ok)
		{
			return;
		}

		if (config.HasSectionKey(ColorSection, BgColorKey))
		{
			_baseColor = (Color)config.GetValue(ColorSection, BgColorKey, _baseColor);
			_baseColor.A = 1.0f;
		}

		if (config.HasSectionKey(ColorSection, TransparentKey))
		{
			_useTransparentBackground = (bool)config.GetValue(ColorSection, TransparentKey, _useTransparentBackground);
		}

		if (config.HasSectionKey(UiSection, CompletionPopupsKey))
		{
			_showCompletionPopups = (bool)config.GetValue(UiSection, CompletionPopupsKey, _showCompletionPopups);
		}

		if (config.HasSectionKey(UiSection, VerboseOutputKey))
		{
			_verboseOutputEnabled = (bool)config.GetValue(UiSection, VerboseOutputKey, _verboseOutputEnabled);
		}

		if (config.HasSectionKey(SteamSection, SteamPathKey))
		{
			_steamPath = (string)config.GetValue(SteamSection, SteamPathKey, string.Empty) as string ?? string.Empty;
		}
	}

	private void SaveSettings()
	{
		var config = new ConfigFile();
		config.SetValue(ColorSection, BgColorKey, _baseColor);
		config.SetValue(ColorSection, TransparentKey, _useTransparentBackground);
		config.SetValue(UiSection, CompletionPopupsKey, _showCompletionPopups);
		config.SetValue(UiSection, VerboseOutputKey, _verboseOutputEnabled);
		config.SetValue(SteamSection, SteamPathKey, _steamPath);
		config.Save(SettingsPath);
	}

	private void StyleResetDialog()
	{
		// Apply theme colors to match app styling
		var darkBg = new Color(0.15f, 0.15f, 0.15f, 0.95f);
		var lightText = new Color(0.9f, 0.9f, 0.9f, 1.0f);
		var accentYellow = new Color(0.8745098f, 0.8745098f, 0.49803922f, 1.0f);

		_resetConfirmDialog.AddThemeColorOverride("font_color", lightText);
		_resetConfirmDialog.AddThemeColorOverride("title_color", accentYellow);
		_resetConfirmDialog.AddThemeColorOverride("panel_color", darkBg);
		_resetConfirmDialog.AddThemeColorOverride("button_font_color", lightText);
		_resetConfirmDialog.AddThemeColorOverride("button_focus_color", new Color(0.3f, 0.3f, 0.3f, 1.0f));
		_resetConfirmDialog.AddThemeColorOverride("button_hover_color", new Color(0.25f, 0.25f, 0.25f, 1.0f));
	}

	private void ResetToDefaults()
	{
		_baseColor = DefaultBaseColor;
		_useTransparentBackground = true;
		_showCompletionPopups = true;
		_verboseOutputEnabled = true;
		_steamPath = string.Empty;
		_pickerContainer.Visible = false;
		ApplySettings();
		SaveSettings();
		EmitSignal(SignalName.VerboseSettingChanged, _verboseOutputEnabled);
		EmitSignal(SignalName.SteamPathChanged, _steamPath);
	}

	private void UpdateSteamPathUi()
	{
		if (_steamPathLabel != null)
		{
			_steamPathLabel.Text = string.IsNullOrWhiteSpace(_steamPath)
				? "Steam Location: not set"
				: $"Steam Location: {_steamPath}";
		}

		if (_pickSteamPathButton != null)
		{
			_pickSteamPathButton.Text = string.IsNullOrWhiteSpace(_steamPath)
				? "Select Steam Location"
				: "Change Steam Location";
		}
	}

	public bool GetVerboseOutputSetting()
	{
		return _verboseOutputEnabled;
	}
}
