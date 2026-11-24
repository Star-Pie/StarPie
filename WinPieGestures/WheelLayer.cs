using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

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

	internal static int GetNextDefaultLayerNumber(IEnumerable<WheelLayer>? layers)
	{
		int count = 0;
		int maxNum = 0;
		if (layers != null)
		{
			foreach (WheelLayer layer in layers)
			{
				count++;
				if (!string.IsNullOrWhiteSpace(layer?.Name) &&
					TryParseDefaultLayerNumber(layer.Name, out string? numStr) &&
					int.TryParse(numStr, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed) &&
					parsed > maxNum)
				{
					maxNum = parsed;
				}
			}
		}
		return Math.Max(count + 1, maxNum + 1);
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

	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string? LayerId { get; set; }

	[System.Text.Json.Serialization.JsonExtensionData]
	public Dictionary<string, System.Text.Json.JsonElement>? Extras { get; set; }

	internal string EnsureLayerId()
	{
		if (string.IsNullOrWhiteSpace(LayerId)) LayerId = Guid.NewGuid().ToString("N");
		return LayerId;
	}

	// The root is level one. Keep imported values intact until explicitly edited.
	public int MaxMenuDepth { get; set; } = 2;

	[System.Text.Json.Serialization.JsonIgnore]
	public int EffectiveMaxMenuDepth => Math.Clamp(MaxMenuDepth, 2, 5);

	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public FixedRingMenu? FixedRings { get; set; }

	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
	public bool FixedRingsPaused { get; set; }

	[System.Text.Json.Serialization.JsonIgnore]
	public FixedRingMenu? ActiveFixedRings => FixedRingsPaused ? null : FixedRings;

	public TriggerConfig? ActivationTrigger { get; set; }

	public List<ActionItem> Actions { get; set; } = new List<ActionItem>();

	/// <summary>按扇区档位（"4" / "8" / "12"）无损暂存的历史槽位动作快照，防止切向时丢失动作</summary>
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, List<ActionItem>>? SectorCountSnapshots { get; set; }

	internal static int NormalizeSupportedSectorCount(int count) => count is 4 or 8 or 12 ? count : 8;

	internal static bool TryParseSnapshotKey(string? key, out int count)
	{
		count = 0;
		return key switch
		{
			"4" => (count = 4) == 4,
			"8" => (count = 8) == 8,
			"12" => (count = 12) == 12,
			_ => false
		};
	}

	internal static Dictionary<string, List<ActionItem>>? CloneSectorCountSnapshots(Dictionary<string, List<ActionItem>>? source)
	{
		if (source == null || source.Count == 0) return null;
		var result = new Dictionary<string, List<ActionItem>>(StringComparer.Ordinal);
		foreach (var (key, actions) in source)
		{
			if (!TryParseSnapshotKey(key, out int expectedCount) || actions == null) continue;
			var cloned = new List<ActionItem>(expectedCount);
			for (int i = 0; i < expectedCount; i++)
			{
				ActionItem? item = i < actions.Count ? actions[i] : null;
				cloned.Add(item?.Clone() ?? new ActionItem
				{
					Type = "Hotkey",
					Name = $"动作 {i + 1}",
					Parameter = "",
					SubActions = new List<ActionItem>()
				});
			}
			result[key] = cloned;
		}
		return result.Count == 0 ? null : result;
	}

	internal static List<ActionItem> SwitchSectorCountWithSnapshots(
		WheelLayer layer,
		List<ActionItem>? currentActions,
		int oldSectorCount,
		int newSectorCount,
		Func<List<ActionItem>?, int, int, Dictionary<ActionItem, int>?, List<ActionItem>> migrate,
		out Dictionary<int, int> mapping)
	{
		oldSectorCount = NormalizeSupportedSectorCount(oldSectorCount);
		newSectorCount = NormalizeSupportedSectorCount(newSectorCount);

		var currentSnapshot = new List<ActionItem>(oldSectorCount);
		for (int i = 0; i < oldSectorCount; i++)
		{
			ActionItem? item = currentActions != null && i < currentActions.Count ? currentActions[i] : null;
			currentSnapshot.Add(item?.Clone() ?? new ActionItem
			{
				Type = "Hotkey",
				Name = $"动作 {i + 1}",
				Parameter = "",
				SubActions = new List<ActionItem>()
			});
		}

		layer.SectorCountSnapshots ??= new Dictionary<string, List<ActionItem>>(StringComparer.Ordinal);
		string oldKey = oldSectorCount.ToString(CultureInfo.InvariantCulture);
		string newKey = newSectorCount.ToString(CultureInfo.InvariantCulture);
		layer.SectorCountSnapshots[oldKey] = currentSnapshot;

		mapping = new Dictionary<int, int>();
		if (layer.SectorCountSnapshots.TryGetValue(newKey, out var existing) &&
			existing != null &&
			existing.Count == newSectorCount &&
			existing.All(a => a != null))
		{
			var tempOrigins = new Dictionary<ActionItem, int>();
			var geometric = migrate(currentActions, oldSectorCount, newSectorCount, tempOrigins);
			for (int i = 0; i < geometric.Count; i++)
			{
				if (tempOrigins.TryGetValue(geometric[i], out int prev))
				{
					mapping[prev] = i;
				}
			}
			return existing.Select(a => a.Clone()).ToList();
		}

		var origins = new Dictionary<ActionItem, int>();
		var migrated = migrate(currentActions, oldSectorCount, newSectorCount, origins);
		for (int i = 0; i < migrated.Count; i++)
		{
			if (origins.TryGetValue(migrated[i], out int prev))
			{
				mapping[prev] = i;
			}
		}
		return migrated;
	}

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
			Extras = Extras == null ? null : new(Extras),
			ActivationTrigger = this.ActivationTrigger?.Clone(),
			SectorCount = this.SectorCount,
			MaxMenuDepth = this.MaxMenuDepth,
			FixedRings = FixedRings?.Clone(),
			FixedRingsPaused = FixedRingsPaused,
			EnableCenterAction = this.EnableCenterAction,
			CenterAction = this.CenterAction?.Clone(),
			Actions = new List<ActionItem>(),
			SectorCountSnapshots = CloneSectorCountSnapshots(this.SectorCountSnapshots)
		};
		if (!string.IsNullOrWhiteSpace(LayerId)) clone.EnsureLayerId();
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
