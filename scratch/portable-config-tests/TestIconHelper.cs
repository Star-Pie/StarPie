using System;
using System.IO;

namespace WinPieGestures;

public static class IconHelper
{
	public static bool FailInvalidation { get; set; }

	public static string GetCustomIconsDirectory()
		=> Path.Combine(Path.GetTempPath(), "StarPiePortableConfigTests", "CustomIcons");

	public static void InvalidateCustomIconCache()
	{
		if (FailInvalidation)
		{
			throw new InvalidOperationException("Injected cache invalidation failure.");
		}
	}
}
