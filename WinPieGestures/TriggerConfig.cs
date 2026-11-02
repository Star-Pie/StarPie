namespace WinPieGestures;

public class TriggerConfig
{
	public string TriggerType { get; set; } = "Mouse";

	public string MouseButton { get; set; } = "RightButton";

	public string Key { get; set; } = "None";

	public uint VkCode { get; set; }

	public bool RequireCtrl { get; set; }

	public bool RequireShift { get; set; }

	public bool RequireAlt { get; set; }

	public bool RequireWin { get; set; }

	public string DisplayText { get; set; } = "\ud83d\uddb1\ufe0f 鼠标右键 (Right Button)";

	public TriggerConfig Clone()
	{
		return new TriggerConfig
		{
			TriggerType = TriggerType,
			MouseButton = MouseButton,
			Key = Key,
			VkCode = VkCode,
			RequireCtrl = RequireCtrl,
			RequireShift = RequireShift,
			RequireAlt = RequireAlt,
			RequireWin = RequireWin,
			DisplayText = DisplayText
		};
	}
}
