using System.Collections.Generic;
using System.ComponentModel;

namespace WinPieGestures;

/// <summary>
/// 独立轮盘层模型（支持无限多层轮盘，每层独立拥有扇区数量、槽位动作列表与中心核圆）
/// </summary>
public class WheelLayer : INotifyPropertyChanged
{
	private string _name = "第 1 层";

	public string Name
	{
		get => _name;
		set
		{
			if (_name != value)
			{
				_name = value;
				OnPropertyChanged(nameof(Name));
				OnPropertyChanged(nameof(DisplayName));
			}
		}
	}

	/// <summary>
	/// UI 层展示名称（若为默认 "第 N 层" / "Layer N" 则依据当前语言本地化，若为用户自定义名称则保持原样）
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public string DisplayName
	{
		get
		{
			if (string.IsNullOrWhiteSpace(_name))
			{
				return _name;
			}
			if (TryParseDefaultLayerNumber(_name, out string? numStr))
			{
				return string.Format(I18n.T("WheelLayerFmt"), numStr);
			}
			return _name;
		}
	}

	private static bool TryParseDefaultLayerNumber(string name, out string? numberStr)
	{
		numberStr = null;
		ReadOnlySpan<char> span = name.AsSpan().Trim();
		if (span.IsEmpty) return false;

		// 1. "第" ... "层/層"
		if (span.StartsWith("第", StringComparison.Ordinal) && (span.EndsWith("层", StringComparison.Ordinal) || span.EndsWith("層", StringComparison.Ordinal)) && span.Length >= 3)
		{
			ReadOnlySpan<char> inner = span.Slice(1, span.Length - 2).Trim();
			if (!inner.IsEmpty && IsAllDigits(inner))
			{
				numberStr = inner.ToString();
				return true;
			}
		}

		// 2. "Layer" ...
		if (span.StartsWith("Layer", StringComparison.OrdinalIgnoreCase) && span.Length >= 6)
		{
			ReadOnlySpan<char> inner = span.Slice(5).Trim();
			if (!inner.IsEmpty && IsAllDigits(inner))
			{
				numberStr = inner.ToString();
				return true;
			}
		}

		// 3. "レイヤー" ...
		if (span.StartsWith("レイヤー", StringComparison.Ordinal) && span.Length >= 5)
		{
			ReadOnlySpan<char> inner = span.Slice(4).Trim();
			if (!inner.IsEmpty && IsAllDigits(inner))
			{
				numberStr = inner.ToString();
				return true;
			}
		}

		return false;
	}

	private static bool IsAllDigits(ReadOnlySpan<char> span)
	{
		for (int i = 0; i < span.Length; i++)
		{
			if (!char.IsDigit(span[i])) return false;
		}
		return true;
	}

	public int SectorCount { get; set; } = 8;

	public TriggerConfig? ActivationTrigger { get; set; }

	public List<ActionItem> Actions { get; set; } = new List<ActionItem>();

	/// <summary>本层专属中心核心圆动作</summary>
	public ActionItem? CenterAction { get; set; }

	/// <summary>是否启用本层专属中心核心圆动作</summary>
	public bool EnableCenterAction { get; set; } = false;

	public event PropertyChangedEventHandler? PropertyChanged;

	protected void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	public WheelLayer Clone()
	{
		WheelLayer clone = new WheelLayer
		{
			Name = this.Name,
			ActivationTrigger = this.ActivationTrigger?.Clone(),
			SectorCount = this.SectorCount,
			EnableCenterAction = this.EnableCenterAction,
			CenterAction = this.CenterAction?.Clone(),
			Actions = new List<ActionItem>()
		};
		if (this.Actions != null)
		{
			foreach (var action in this.Actions)
			{
				clone.Actions.Add(action.Clone());
			}
		}
		return clone;
	}

	public override string ToString()
	{
		return Name;
	}
}
