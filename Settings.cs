using Godot;
using System;

public partial class Settings : PanelContainer
{
	private const string SettingsPath = "user://settings.cfg";

	private const string ColorSection = "appearance";
	private const string BgColorKey = "background_color";
	private const string TransparentKey = "transparent_background";
	private const string UiSection = "ui";
	private const string CompletionPopupsKey = "show_completion_popups";

	private Button _pickColorButton;
	private Button _resetButton;
	private MarginContainer _pickerContainer;
	private ColorPicker _colorPicker;
	private CheckBox _transparentCheck;
	private CheckBox _completionPopupsCheck;
	private ConfirmationDialog _resetConfirmDialog;

	private ColorRect _background;
	private PanelContainer _glassPanel;

	private Color _baseColor = new Color(0.22647282f, 0.144768f, 0.25970086f, 1.0f);
	private bool _useTransparentBackground = true;
	private bool _showCompletionPopups = true;

	public override void _Ready()
	{
		GetUiReferences();
		LoadSettings();
		ConnectSignals();
		ApplySettings();
	}

	private void GetUiReferences()
	{
		_pickColorButton = GetNode<Button>("ScrollContainer/MarginContainer3/VBoxContainer/pickcolor");
		_resetButton = GetNode<Button>("ScrollContainer/MarginContainer3/VBoxContainer/Reset");
		_pickerContainer = GetNode<MarginContainer>("ScrollContainer/MarginContainer3/VBoxContainer/MarginContainer");
		_colorPicker = GetNode<ColorPicker>("ScrollContainer/MarginContainer3/VBoxContainer/MarginContainer/ColorPicker");
		_transparentCheck = GetNode<CheckBox>("ScrollContainer/MarginContainer3/VBoxContainer/TransparentCheck");
		_completionPopupsCheck = GetNode<CheckBox>("ScrollContainer/MarginContainer3/VBoxContainer/VerboseCheckBox");
		_resetConfirmDialog = GetNode<ConfirmationDialog>("ScrollContainer/MarginContainer3/VBoxContainer/ResetConfirmDialog");

		// Settings node is Control/PanelContainer/MarginContainer/TabContainer/Settings.
		_background = GetNode<ColorRect>("../../../../Background");
		_glassPanel = GetNode<PanelContainer>("../../../");
	}

	private void ConnectSignals()
	{
		_pickColorButton.Pressed += OnPickColorPressed;
		_resetButton.Pressed += OnResetPressed;
		_colorPicker.ColorChanged += OnColorChanged;
		_transparentCheck.Toggled += OnTransparentToggled;
		_completionPopupsCheck.Toggled += OnCompletionPopupsToggled;
		_resetConfirmDialog.Confirmed += OnResetConfirmed;
	}

	private void OnPickColorPressed()
	{
		_pickerContainer.Visible = !_pickerContainer.Visible;
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

	private void OnResetPressed()
	{
		_resetConfirmDialog.PopupCentered(new Vector2I(420, 180));
	}

	private void OnResetConfirmed()
	{
		ResetToDefaults();
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
	}

	private void SaveSettings()
	{
		var config = new ConfigFile();
		config.SetValue(ColorSection, BgColorKey, _baseColor);
		config.SetValue(ColorSection, TransparentKey, _useTransparentBackground);
		config.SetValue(UiSection, CompletionPopupsKey, _showCompletionPopups);
		config.Save(SettingsPath);
	}

	private void ResetToDefaults()
	{
		_baseColor = new Color(0.22647282f, 0.144768f, 0.25970086f, 1.0f);
		_useTransparentBackground = true;
		_showCompletionPopups = true;
		_pickerContainer.Visible = false;
		ApplySettings();
		SaveSettings();
	}
}
