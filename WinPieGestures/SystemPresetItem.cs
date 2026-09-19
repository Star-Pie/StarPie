namespace WinPieGestures;

public class SystemPresetItem
{
	public string Key { get; set; } = "";

	private string _category = "";
	public string Category
	{
		get
		{
			string loc = I18n.T("SysCategory_" + Key);
			return (!string.IsNullOrEmpty(loc) && loc != "SysCategory_" + Key) ? loc : _category;
		}
		set => _category = value;
	}

	private string _displayName = "";
	public string DisplayName
	{
		get
		{
			string loc = I18n.T("SysPreset_" + Key);
			return (!string.IsNullOrEmpty(loc) && loc != "SysPreset_" + Key) ? loc : _displayName;
		}
		set => _displayName = value;
	}

	private string _defaultName = "";
	public string DefaultName
	{
		get
		{
			string loc = I18n.T("SysPresetName_" + Key);
			return (!string.IsNullOrEmpty(loc) && loc != "SysPresetName_" + Key) ? loc : _defaultName;
		}
		set => _defaultName = value;
	}

	public string DefaultIconKey { get; set; } = "";

	public string FormattedDisplay => "[" + Category + "] " + DisplayName;
}
