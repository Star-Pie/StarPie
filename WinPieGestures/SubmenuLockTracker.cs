using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace WinPieGestures;

/// <summary>
/// 二级子菜单父扇区锁定状态跟踪器 (Submenu Parent Sector Lock Tracker)
/// 负责管理光标划入二级菜单后的父扇区会话锁定、迟滞防抖退出及子项命中安全守卫。
/// </summary>
public sealed class SubmenuLockTracker
{
	private sealed record FanGeometry((string Shape, double Outer, double Inner, double SubOuter, double Gap, double Corner) Key, Geometry[] Slots);
	private FanGeometry? _fanGeometry;
	private sealed record WheelGeometry((string Shape, double Outer, double Inner, double SubOuter, double Gap, double Corner) Key,
		double SectorGap, double SectorCorner, int SectorCount, Dictionary<int, Geometry[]> Layouts);
	private WheelGeometry? _wheelGeometry;

	private static (string Shape, double Outer, double Inner, double SubOuter, double Gap, double Corner) FanKey(AppConfig config) =>
		(LegacyRingGeometry.SecondaryShape(config), config.WheelRadius, config.InnerRadius, config.SubWheelOuterRadius, config.SubWheelInnerGap, config.SubWheelCornerRadius);

	// 只在界面准备阶段构造几何；钩子线程读取冻结快照，不逐帧创建 WPF 对象。
	public void PrepareFanGeometry(AppConfig config)
	{
		var key = FanKey(config);
		if (Volatile.Read(ref _fanGeometry)?.Key == key) return;
		double ratio = key.SubOuter > 0 && key.Outer > 0 ? key.SubOuter / (key.Outer * 1.55) : 1.0;
		double radius = (key.Outer - key.Inner) * 0.40 * Math.Clamp(ratio, 0.5, 2.5);
		double orbit = (key.Inner + key.Outer) / 2.0 * ratio + (key.Gap >= 0 ? key.Gap : 4.0);
		var slots = new Geometry[RadialWindow.FanSubmenuSlotCount];
		for (int slot = 0; slot < slots.Length; slot++)
		{
			var (du, dv) = RadialWindow.GetFanSubOffsetForShape(key.Shape, slot);
			slots[slot] = FreezeHitGeometry(RadialWindow.CreateSubMenuGeometry(key.Shape, du * orbit, dv * orbit, radius, 0, 0, 0, key.Corner));
		}
		Volatile.Write(ref _fanGeometry, new FanGeometry(key, slots));
	}

	public void PrepareGeometry(AppConfig config, WheelProfile profile)
	{
		if (config.SubmenuStyle == "Fan") { PrepareFanGeometry(config); return; }
		var key = FanKey(config);
		double gap = key.Gap >= 0 ? key.Gap : Math.Max(0, config.SectorGap);
		double corner = key.Corner >= 0 ? key.Corner : Math.Max(0, config.SectorCornerRadius);
		double inner = key.Outer + gap + 2;
		double outer = key.SubOuter > 0 ? key.SubOuter : key.Outer * 1.55;
		double sectorAngle = 360.0 / profile.SectorCount;
		var layouts = new Dictionary<int, Geometry[]>();
		for (int parent = 0; parent < profile.SectorCount; parent++)
		{
			int count = profile.GetEffectiveAction(parent)?.SubActions?.Count ?? 0;
			if (count == 0 || layouts.ContainsKey(count)) continue;
			var slots = new Geometry[count];
			for (int sub = 0; sub < count; sub++)
			{
				double start = -sectorAngle / 2 + sub * sectorAngle / count;
				slots[sub] = FreezeHitGeometry(IconHelper.CreateAdvancedSectorGeometry(0, 0, start,
					start + sectorAngle / count, inner, outer, key.Shape, gap, corner));
			}
			layouts.Add(count, slots);
		}
		Volatile.Write(ref _wheelGeometry, new WheelGeometry(key, config.SectorGap, config.SectorCornerRadius, profile.SectorCount, layouts));
	}

	public bool ContainsWheelSub(AppConfig config, double dx, double dy, int parent, int sectorCount, int subCount, int sub)
	{
		WheelGeometry? geometry = Volatile.Read(ref _wheelGeometry);
		if (geometry == null || geometry.Key != FanKey(config) || geometry.SectorCount != sectorCount ||
			geometry.SectorGap != config.SectorGap || geometry.SectorCorner != config.SectorCornerRadius ||
			!geometry.Layouts.TryGetValue(subCount, out Geometry[]? slots) || sub < 0 || sub >= slots.Length) return false;
		double angle = parent * (2 * Math.PI / sectorCount);
		double cos = Math.Cos(angle), sin = Math.Sin(angle);
		return slots[sub].FillContains(new Point(dx * cos + dy * sin, -dx * sin + dy * cos));
	}

	private static StreamGeometry FreezeHitGeometry(Geometry geometry)
	{
		// 某些 Geometry 派生类型每次查询都会序列化路径；准备期转为流几何。
		if (geometry is StreamGeometry stream) { stream.Freeze(); return stream; }
		PathGeometry flattened = geometry.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
		Matrix matrix = flattened.Transform?.Value ?? Matrix.Identity;
		var result = new StreamGeometry { FillRule = flattened.FillRule };
		using (StreamGeometryContext context = result.Open())
		{
			foreach (PathFigure figure in flattened.Figures)
			{
				context.BeginFigure(matrix.Transform(figure.StartPoint), figure.IsFilled, figure.IsClosed);
				foreach (PathSegment segment in figure.Segments)
				{
					if (segment is LineSegment line) context.LineTo(matrix.Transform(line.Point), line.IsStroked, line.IsSmoothJoin);
					else if (segment is PolyLineSegment poly)
					{
						foreach (Point point in poly.Points) context.LineTo(matrix.Transform(point), poly.IsStroked, poly.IsSmoothJoin);
					}
				}
			}
		}
		result.Freeze();
		return result;
	}

	public int HitTestFan(AppConfig config, double dx, double dy, int parent, int sectorCount, int subCount, int previousSub)
	{
		FanGeometry? geometry = Volatile.Read(ref _fanGeometry);
		if (geometry == null || geometry.Key != FanKey(config) || parent < 0 || sectorCount <= 0) return -1;
		double angle = parent * (2 * Math.PI / sectorCount);
		double cos = Math.Cos(angle), sin = Math.Sin(angle);
		Point localPoint = new(dx * cos + dy * sin, -dx * sin + dy * cos);
		double mouseAngle = Math.Atan2(localPoint.Y, localPoint.X);
		double bestAngle = double.MaxValue;
		int bestSub = -1;
		int count = Math.Min(subCount, RadialWindow.FanSubmenuSlotCount);
		for (int sub = 0; sub < count; sub++)
		{
			int slot = RadialWindow.GetFanSlotIndex(sub, count);
			if (!geometry.Slots[slot].FillContains(localPoint)) continue;
			var (du, dv) = RadialWindow.GetFanSubOffsetForShape(geometry.Key.Shape, slot);
			double diff = Math.Abs(Math.IEEERemainder(mouseAngle - Math.Atan2(dv, du), 2 * Math.PI));
			if (sub == previousSub) diff -= 0.08;
			if (diff < bestAngle) { bestAngle = diff; bestSub = sub; }
		}
		return bestSub;
	}
	public int LockedParentSector { get; private set; } = -1;

	public bool IsLocked => LockedParentSector >= 0;

	public void Reset()
	{
		LockedParentSector = -1;
	}

	public void Lock(int parentSector)
	{
		LockedParentSector = parentSector;
	}

	/// <summary>
	/// 计算进入二级菜单的触发距离阈值
	/// </summary>
	public static double GetSubmenuEntryDistance(AppConfig config, bool isFan, double wheelRadius)
	{
		if (isFan)
		{
			return (config.SubWheelTriggerDistance > 20.0) ? config.SubWheelTriggerDistance : 95.0;
		}

		double innerGap = (config.SubWheelInnerGap >= 0.0) ? config.SubWheelInnerGap : 4.0;
		double subWheelInner = wheelRadius + innerGap + 2.0;
		if (config.AutoExpandSubRingsOnPopup)
		{
			return subWheelInner;
		}

		double triggerDist = (config.SubWheelTriggerDistance > 20.0) ? config.SubWheelTriggerDistance : 95.0;
		return Math.Max(subWheelInner, triggerDist);
	}

	/// <summary>
	/// 计算退回一级选择区域的解锁距离阈值 (具备迟滞防抖)
	/// </summary>
	public static double GetSubmenuExitDistance(AppConfig config, bool isFan, double wheelRadius)
	{
		if (isFan)
		{
			double trigger = (config.SubWheelTriggerDistance > 20.0) ? config.SubWheelTriggerDistance : 95.0;
			return Math.Max(25.0, Math.Min(wheelRadius - 6.0, trigger - 22.0));
		}

		return Math.Max(25.0, wheelRadius - 6.0);
	}

	/// <summary>
	/// 解析当前指向的父级扇区。若已锁定且光标未明显缩回一级区域，保持父扇区不变；
	/// 若光标退回一级区域则解除锁定。调用方确认有效子项命中后才调用 Lock。
	/// </summary>
	public int ResolveParentSector(
		bool lockEnabled,
		bool enableMultiTier,
		bool isFan,
		double distance,
		int rawSector,
		double wheelRadius,
		AppConfig config)
	{
		if (!lockEnabled || !enableMultiTier)
		{
			Reset();
			return rawSector;
		}

		double exitDist = GetSubmenuExitDistance(config, isFan, wheelRadius);

		if (IsLocked)
		{
			if (distance < exitDist)
			{
				Reset();
				return rawSector;
			}
			return LockedParentSector;
		}

		return rawSector;
	}
}
