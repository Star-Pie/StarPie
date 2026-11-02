using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using WinPieGestures;

#if !STARPIE_CANDIDATE_REFERENCE
namespace WinPieGestures
{
	internal static class IconHelper
	{
		public static string GetCustomIconsDirectory()
			=> throw new InvalidOperationException("Tests must inject a temporary target directory.");

		public static void InvalidateCustomIconCache()
			=> throw new InvalidOperationException("Tests must inject a cache callback.");
	}
}
#endif

internal static class Program
{
	private static int _passed;
	private static int _failed;

	private static int Main(string[] args)
	{
		string root = Path.Combine(Path.GetTempPath(), "starpie-beta3-svg-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			RunSinglePath(root);
			RunContainerAndHarmlessNodes(root);
			RunInvalidXml(root);
			RunDtd(root);
			RunRejectedAttributesAndElements(root);
			RunPaintValidation(root);
			RunUnknownExtension(root);
			RunUniqueNamesAndDuplicateSource(root);
			RunOversizeAndPartialFailure(root);
			RunBarePathAndCancellation(root);
			RunFolderImport(root);
			Console.WriteLine($"RESULT passed={_passed} failed={_failed}");
			return _failed == 0 ? 0 : 1;
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}

	private static void RunSinglePath(string root)
	{
		string source = Write(root, "single.svg", Svg("<path d=\"M0 0 L10 0 L10 10 Z\" fill=\"#000\"/>"));
		string target = NewDirectory(root, "single-target");
		int cacheCalls = 0;
		var result = SvgIconBatchImporter.ImportFiles(new[] { source }, target, () => cacheCalls++);
		Assert(result.SuccessCount == 1, "single success");
		Assert(result.FailureCount == 0, "single no failure");
		Assert(result.CacheInvalidated && cacheCalls == 1, "single cache once");
		Assert(File.Exists(result.Items[0].TargetPath), "single target exists");
		Assert(File.ReadAllText(result.Items[0].TargetPath!).StartsWith("M0 0", StringComparison.Ordinal), "single canonical path");
	}

	private static void RunContainerAndHarmlessNodes(string root)
	{
		string source = Write(root, "group.svg", Svg(
			"<title>Example</title><desc>Description</desc><metadata><x>ignored</x></metadata>" +
			"<g id=\"layer\"><path d=\"M1 1 L8 1 L8 8 Z\"/></g>"));
		string target = NewDirectory(root, "group-target");
		var result = SvgIconBatchImporter.ImportFiles(new[] { source }, target, () => { });
		Assert(result.SuccessCount == 1, "group success");
	}

	private static void RunInvalidXml(string root)
	{
		string source = Write(root, "bad.svg", "<svg><path d=\"M0 0 L1 1\">");
		AssertFailure(root, source, SvgIconReasonCodes.InvalidXml, "bad xml");
	}

	private static void RunDtd(string root)
	{
		string source = Write(root, "dtd.svg",
			"<!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///does-not-exist\">]><svg><path d=\"M0 0 L1 1\"/></svg>");
		AssertFailure(root, source, SvgIconReasonCodes.DtdForbidden, "dtd");
	}

	private static void RunRejectedAttributesAndElements(string root)
	{
		AssertFailure(root, Write(root, "stroke.svg", Svg("<path d=\"M0 0 L1 1\" stroke=\"red\"/>")),
			SvgIconReasonCodes.StrokeUnsupported, "stroke");
		AssertFailure(root, Write(root, "transform.svg", Svg("<g transform=\"translate(1,1)\"><path d=\"M0 0 L1 1\"/></g>")),
			SvgIconReasonCodes.TransformUnsupported, "transform");
		AssertFailure(root, Write(root, "multi.svg",
			Svg("<path d=\"M0 0 L1 1\" fill=\"#000\"/><path d=\"M2 2 L3 3\" fill=\"#fff\"/>")),
			SvgIconReasonCodes.MultiColor, "multi color");
		AssertFailure(root, Write(root, "gradient.svg",
			Svg("<linearGradient id=\"g\"/><path d=\"M0 0 L1 1\" fill=\"url(#g)\"/>")),
			SvgIconReasonCodes.GradientUnsupported, "gradient");
		AssertFailure(root, Write(root, "use.svg", Svg("<use href=\"#x\"/><path d=\"M0 0 L1 1\"/>")),
			SvgIconReasonCodes.ExternalReference, "use");
		AssertFailure(root, Write(root, "shape.svg", Svg("<rect x=\"0\" y=\"0\" width=\"1\" height=\"1\"/>")),
			SvgIconReasonCodes.UnsupportedElement, "shape");
		AssertFailure(root, Write(root, "empty.svg", Svg("<path d=\"M0 0\"/>")),
			SvgIconReasonCodes.EmptyGeometry, "empty geometry");
		AssertFailure(root, Write(root, "nonfinite.svg", Svg("<path d=\"M0 0 L1e9999 0\"/>")),
			SvgIconReasonCodes.NonFiniteCoordinate, "nonfinite");
	}

	private static void RunUnknownExtension(string root)
	{
		string source = Write(root, "ignored.png", "not an svg");
		string target = NewDirectory(root, "extension-target");
		var result = SvgIconBatchImporter.ImportFiles(new[] { source }, target, () => { });
		Assert(result.SkippedCount == 1, "unknown extension skipped");
		Assert(result.Items[0].ReasonCode == SvgIconReasonCodes.UnknownExtension, "unknown extension code");
	}

	private static void RunPaintValidation(string root)
	{
		string target = NewDirectory(root, "paint-target");
		var opaque = SvgIconBatchImporter.ImportFiles(
			new[] { Write(root, "opaque.svg", Svg("<path d=\"M0 0 L1 1\" fill=\"#336699\"/>")) },
			target,
			() => { });
		Assert(opaque.SuccessCount == 1, "opaque paint accepted");

		var equivalent = SvgIconBatchImporter.ImportFiles(
			new[]
			{
				Write(root, "equivalent.svg",
					Svg("<path d=\"M0 0 L1 1\" fill=\"#336699\"/><path d=\"M2 2 L3 3\" fill=\"#369\"/>"))
			},
			target,
			() => { });
		Assert(equivalent.SuccessCount == 1, "equivalent opaque paints accepted");

		AssertFailure(root, Write(root, "transparent.svg", Svg("<path d=\"M0 0 L1 1\" fill=\"transparent\"/>")),
			SvgIconReasonCodes.PaintTransparent, "transparent paint");
		AssertFailure(root, Write(root, "transparent-hex.svg", Svg("<path d=\"M0 0 L1 1\" fill=\"#0000\"/>")),
			SvgIconReasonCodes.PaintUnsupported, "alpha hex outside supported RGB subset");
		foreach (string paint in new[] { "#f000", "#ff000000", "#f00f", "#ff0000ff", "sc#1,1,0,0" })
			AssertFailure(root, Write(root, "svg-paint-" + Guid.NewGuid().ToString("N") + ".svg", Svg("<path d=\"M0 0 L1 1\" fill=\"" + paint + "\"/>")),
				SvgIconReasonCodes.PaintUnsupported, "reject SVG alpha layout and WPF private paint " + paint);
		AssertFailure(root, Write(root, "rgba.svg", Svg("<path d=\"M0 0 L1 1\" fill=\"rgba(0,0,0,0.5)\"/>")),
			SvgIconReasonCodes.PaintTransparent, "rgba paint");
		AssertFailure(root, Write(root, "unknown-paint.svg", Svg("<path d=\"M0 0 L1 1\" fill=\"not-a-color\"/>")),
			SvgIconReasonCodes.PaintUnsupported, "unknown paint");
		AssertFailure(root, Write(root, "inherit-paint.svg", Svg("<path d=\"M0 0 L1 1\" fill=\"inherit\"/>")),
			SvgIconReasonCodes.PaintUnsupported, "inherit paint");
		AssertFailure(root, Write(root, "none-paint.svg", Svg("<path d=\"M0 0 L1 1\" fill=\"none\"/>")),
			SvgIconReasonCodes.InvisiblePath, "none paint");
	}

	private static void RunUniqueNamesAndDuplicateSource(string root)
	{
		string dirA = NewDirectory(root, "same-a");
		string dirB = NewDirectory(root, "same-b");
		string sourceA = Write(dirA, "icon.svg", Svg("<path d=\"M0 0 L1 1\"/>"));
		string sourceB = Write(dirB, "icon.svg", Svg("<path d=\"M2 2 L3 3\"/>"));
		string target = NewDirectory(root, "same-target");
		int cacheCalls = 0;
		var result = SvgIconBatchImporter.ImportFiles(new[] { sourceA, sourceB }, target, () => cacheCalls++);
		Assert(result.SuccessCount == 2, "same names success");
		Assert(result.Items.Select(item => item.TargetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2, "same names unique");
		Assert(cacheCalls == 1, "same names cache once");

		var duplicate = SvgIconBatchImporter.ImportFiles(new[] { sourceA, sourceA }, NewDirectory(root, "dup-target"), () => { });
		Assert(duplicate.SuccessCount == 1 && duplicate.SkippedCount == 1, "duplicate source");
		Assert(duplicate.Items[1].ReasonCode == SvgIconReasonCodes.DuplicateSource, "duplicate code");
	}

	private static void RunOversizeAndPartialFailure(string root)
	{
		string oversize = Write(root, "oversize.svg", new string('x', checked((int)SvgIconBatchImporter.MaxSvgFileBytes + 1)));
		string good = Write(root, "good.svg", Svg("<path d=\"M0 0 L1 1\"/>"));
		string bad = Write(root, "bad2.svg", "<svg><path d=\"M0 0\"");
		string target = NewDirectory(root, "partial-target");
		var result = SvgIconBatchImporter.ImportFiles(new[] { oversize, good, bad }, target, () => { });
		Assert(result.SuccessCount == 1, "partial success");
		Assert(result.FailureCount == 2, "partial failures");
		Assert(result.Items[0].ReasonCode == SvgIconReasonCodes.FileTooLarge, "oversize code");
		Assert(result.Items[2].ReasonCode == SvgIconReasonCodes.InvalidXml, "partial bad code");
	}

	private static void RunBarePathAndCancellation(string root)
	{
		string target = NewDirectory(root, "bare-target");
		int cacheCalls = 0;
		var bare = SvgIconBatchImporter.ImportBarePath("M0 0 L2 0 L2 2 Z", "Bare", target, () => cacheCalls++);
		Assert(bare.SuccessCount == 1 && cacheCalls == 1, "bare path");

		using var cts = new CancellationTokenSource();
		cts.Cancel();
		string source = Write(root, "cancel.svg", Svg("<path d=\"M0 0 L1 1\"/>"));
		var canceled = SvgIconBatchImporter.ImportFiles(new[] { source }, NewDirectory(root, "cancel-target"), () => { }, cts.Token);
		Assert(canceled.Cancelled, "cancel state");
		Assert(canceled.SuccessCount == 0, "cancel no success");
		Assert(canceled.Items.Count == 0, "cancel before first has no items");

		using var afterFirstCts = new CancellationTokenSource();
		string first = Write(root, "cancel-first.svg", Svg("<path d=\"M0 0 L2 2\"/>"));
		string second = Write(root, "cancel-second.svg", Svg("<path d=\"M2 2 L4 4\"/>"));
		string rollbackTarget = NewDirectory(root, "cancel-rollback-target");
		IEnumerable<string> CancelAfterFirst()
		{
			yield return first;
			afterFirstCts.Cancel();
			yield return second;
		}
		var rollback = SvgIconBatchImporter.ImportFiles(CancelAfterFirst(), rollbackTarget, () => { }, afterFirstCts.Token);
		Assert(rollback.Cancelled, "cancel after first state");
		Assert(rollback.SuccessCount == 0, "cancel after first no success");
		Assert(rollback.Items.Count == 1, "cancel after first item count");
		Assert(rollback.Items[0].Status == SvgIconBatchItemStatus.Failure, "cancel converted failure");
		Assert(rollback.Items[0].ReasonCode == SvgIconReasonCodes.Cancelled, "cancel converted code");
		Assert(rollback.Items[0].TargetPath == null, "cancel target cleared");
		Assert(!Directory.EnumerateFiles(rollbackTarget).Any(), "cancel files cleaned");
	}

	private static void RunFolderImport(string root)
	{
		string sourceDir = NewDirectory(root, "folder-source");
		Write(sourceDir, "a.svg", Svg("<path d=\"M0 0 L1 1\"/>"));
		Write(sourceDir, "b.svg", Svg("<path d=\"M1 1 L2 2\"/>"));
		Write(sourceDir, "note.txt", "ignored");
		string target = NewDirectory(root, "folder-target");
		int cacheCalls = 0;
		var result = SvgIconBatchImporter.ImportFolder(sourceDir, target, () => cacheCalls++);
		Assert(result.SuccessCount == 2 && result.SkippedCount == 1, "folder import");
		Assert(cacheCalls == 1, "folder cache once");
	}

	private static void AssertFailure(string root, string source, string expectedReason, string name)
	{
		var result = SvgIconBatchImporter.ImportFiles(new[] { source }, NewDirectory(root, "fail-" + Guid.NewGuid().ToString("N")), () => { });
		Assert(result.FailureCount == 1, name + " failure");
		Assert(result.Items[0].ReasonCode == expectedReason, name + " reason");
	}

	private static string Svg(string body)
		=> "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\">" + body + "</svg>";

	private static string Write(string directory, string name, string content)
	{
		string path = Path.Combine(directory, name);
		File.WriteAllText(path, content, new UTF8Encoding(false));
		return path;
	}

	private static string NewDirectory(string root, string name)
	{
		string path = Path.Combine(root, name);
		Directory.CreateDirectory(path);
		return path;
	}

	private static void Assert(bool condition, string name)
	{
		if (condition)
		{
			_passed++;
			return;
		}
		_failed++;
		Console.WriteLine("FAIL: " + name);
	}
}
