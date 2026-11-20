using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
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
			RunDoctypeSecurityAndBenignW3c(root);
			RunHarmlessMetadataAttributes(root);
			RunBasicShapesAndFillRules(root);
			RunTransformFlattening(root);
			RunMonochromeStrokeAndLucideIcons(root);
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

	private static void RunDoctypeSecurityAndBenignW3c(string root)
	{
		// Benign W3C SVG 1.1 DOCTYPE (iconfont / Illustrator style)
		string w3c11 = ImportCanonical(root, "w3c11.svg",
			"<?xml version=\"1.0\" standalone=\"no\"?>" +
			"<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">" +
			"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1024 1024\"><path d=\"M100 100 L900 100 L900 900 Z\"/></svg>",
			"w3c 1.1 doctype");
		Assert(w3c11.StartsWith("M", StringComparison.Ordinal), "w3c 1.1 canonical starts with M");

		// Benign W3C SVG 1.0 DOCTYPE with UTF-8 BOM and comment
		string bomPath = Path.Combine(root, "w3c10-bom.svg");
		File.WriteAllText(bomPath,
			"<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
			"<!-- Generator: Test -->" +
			"<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.0//EN\" \"http://www.w3.org/TR/2001/REC-SVG-20010904/DTD/svg10.dtd\">" +
			"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><circle cx=\"12\" cy=\"12\" r=\"8\"/></svg>",
			new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		var bomResult = SvgIconBatchImporter.ImportFiles(new[] { bomPath }, NewDirectory(root, "w3c10-target"), () => { });
		Assert(bomResult.SuccessCount == 1, "w3c 1.0 with BOM and comment");

		// Malicious / non-whitelisted DTD rejections
		AssertFailure(root, Write(root, "dtd-hybrid.svg",
			"<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\" [<!ENTITY xxe \"1\">]><svg><path d=\"M0 0 L1 1\"/></svg>"),
			SvgIconReasonCodes.DtdForbidden, "w3c doctype with internal entity subset");
		AssertFailure(root, Write(root, "dtd-empty-subset.svg",
			"<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\" []><svg><path d=\"M0 0 L1 1\"/></svg>"),
			SvgIconReasonCodes.DtdForbidden, "w3c doctype with empty bracket subset");
		AssertFailure(root, Write(root, "dtd-system.svg",
			"<!DOCTYPE svg SYSTEM \"http://evil.example/xxe.dtd\"><svg><path d=\"M0 0 L1 1\"/></svg>"),
			SvgIconReasonCodes.DtdForbidden, "external system doctype");
		AssertFailure(root, Write(root, "dtd-evil-url.svg",
			"<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://evil.example/xxe.dtd\"><svg><path d=\"M0 0 L1 1\"/></svg>"),
			SvgIconReasonCodes.DtdForbidden, "mismatched w3c system url");
		AssertFailure(root, Write(root, "dtd-double.svg",
			"<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">" +
			"<!DOCTYPE svg SYSTEM \"http://evil.example/x.dtd\"><svg><path d=\"M0 0 L1 1\"/></svg>"),
			SvgIconReasonCodes.DtdForbidden, "double doctype");
		AssertFailure(root, Write(root, "dtd-attlist.svg",
			"<svg><metadata><!ATTLIST x y CDATA #IMPLIED></metadata><path d=\"M0 0 L1 1\"/></svg>"),
			SvgIconReasonCodes.DtdForbidden, "embedded ATTLIST");
		AssertFailure(root, Write(root, "dtd-element.svg",
			"<!DOCTYPE svg [<!ELEMENT svg ANY>]><svg><path d=\"M0 0 L1 1\"/></svg>"),
			SvgIconReasonCodes.DtdForbidden, "ELEMENT declaration");
		AssertFailure(root, Write(root, "dtd-notation.svg",
			"<!DOCTYPE svg [<!NOTATION gif SYSTEM \"gif\">]><svg><path d=\"M0 0 L1 1\"/></svg>"),
			SvgIconReasonCodes.DtdForbidden, "NOTATION declaration");
	}

	private static void RunHarmlessMetadataAttributes(string root)
	{
		string iconfontSvg =
			"<svg t=\"1700000000\" class=\"icon\" viewBox=\"0 0 1024 1024\" version=\"1.1\" " +
			"xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
			"p-id=\"100\" data-name=\"home\" aria-hidden=\"true\" role=\"img\" focusable=\"false\" xml:space=\"preserve\">" +
			"<title class=\"t\" data-title=\"1\">Home</title>" +
			"<g class=\"layer\" data-layer=\"1\">" +
			"<path d=\"M100 100 L900 100 L900 900 Z\" p-id=\"101\" fill-rule=\"evenodd\" clip-rule=\"evenodd\" aria-label=\"triangle\"/>" +
			"</g></svg>";
		string canonical = ImportCanonical(root, "iconfont-meta.svg", iconfontSvg, "iconfont metadata");
		Geometry g = Geometry.Parse(canonical);
		Assert(!g.Bounds.IsEmpty && g.Bounds.Width > 700, "iconfont metadata geometry bounds");
	}

	private static void RunBasicShapesAndFillRules(string root)
	{
		// 1. <circle>
		Geometry circle = Geometry.Parse(ImportCanonical(root, "circle.svg",
			Svg("<circle cx=\"12\" cy=\"12\" r=\"8\"/>"), "circle"));
		Assert(circle.FillContains(new Point(12, 12)) && !circle.FillContains(new Point(12, 2)), "circle geometry");

		// 2. <rect> with optional rx / ry inheritance
		Geometry rectRx = Geometry.Parse(ImportCanonical(root, "rect-rx.svg",
			Svg("<rect x=\"2\" y=\"3\" width=\"20\" height=\"16\" rx=\"4\"/>"), "rect rx only"));
		Assert(rectRx.FillContains(new Point(12, 11)) && !rectRx.FillContains(new Point(2.2, 3.2)), "rect rx rounded corner");

		Geometry rectRy = Geometry.Parse(ImportCanonical(root, "rect-ry.svg",
			Svg("<rect x=\"2\" y=\"3\" width=\"20\" height=\"16\" ry=\"4\"/>"), "rect ry only"));
		Assert(rectRy.FillContains(new Point(12, 11)) && !rectRy.FillContains(new Point(2.2, 3.2)), "rect ry rounded corner");

		Geometry rectDefaults = Geometry.Parse(ImportCanonical(root, "rect-defaults.svg",
			Svg("<rect width=\"10\" height=\"10\"/>"), "rect default x/y"));
		Assert(Math.Abs(rectDefaults.Bounds.X) < 1e-4 && Math.Abs(rectDefaults.Bounds.Width - 10) < 1e-4, "rect default bounds");

		// 3. <ellipse>
		Geometry ellipse = Geometry.Parse(ImportCanonical(root, "ellipse.svg",
			Svg("<ellipse cx=\"12\" cy=\"12\" rx=\"10\" ry=\"5\"/>"), "ellipse"));
		Assert(Math.Abs(ellipse.Bounds.Width - 20) < 1e-3 && Math.Abs(ellipse.Bounds.Height - 10) < 1e-3, "ellipse bounds");

		// 4. <line> (stroked vs unstroked)
		Geometry line = Geometry.Parse(ImportCanonical(root, "line.svg",
			Svg("<line x1=\"2\" y1=\"2\" x2=\"22\" y2=\"22\" stroke=\"#000\" stroke-width=\"2\" stroke-linecap=\"round\"/>"), "line"));
		Assert(line.FillContains(new Point(12, 12)), "line widened fillContains");
		AssertFailure(root, Write(root, "line-nostroke.svg", Svg("<line x1=\"2\" y1=\"2\" x2=\"22\" y2=\"22\"/>")),
			SvgIconReasonCodes.InvisiblePath, "line without stroke is invisible");

		// 5. <polygon>
		Geometry polygon = Geometry.Parse(ImportCanonical(root, "polygon.svg",
			Svg("<polygon points=\"12,2 22,20 2,20\"/>"), "polygon"));
		Assert(polygon.FillContains(new Point(12, 14)) && !polygon.FillContains(new Point(2, 2)), "polygon triangle");

		// 6. <polyline> (open when stroked, auto-closed when filled)
		Geometry polylineStroked = Geometry.Parse(ImportCanonical(root, "polyline-stroke.svg",
			Svg("<polyline points=\"2,20 12,2 22,20\" fill=\"none\" stroke=\"#000\" stroke-width=\"2\"/>"), "polyline stroked"));
		Assert(polylineStroked.FillContains(new Point(12, 3)) && !polylineStroked.FillContains(new Point(12, 20)), "polyline stays open on stroke");

		Geometry polylineFilled = Geometry.Parse(ImportCanonical(root, "polyline-fill.svg",
			Svg("<polyline points=\"2,20 12,2 22,20\" fill=\"#000\"/>"), "polyline filled"));
		Assert(polylineFilled.FillContains(new Point(12, 14)), "polyline filled interior");

		// 7. fill-rule preservation (evenodd vs nonzero on self-intersecting star and same-winding donut)
		Geometry starNonzero = Geometry.Parse(ImportCanonical(root, "star-nz.svg",
			Svg("<path d=\"M12,2 L15,20 L2,8 L22,8 L9,20 Z\"/>"), "star default nonzero"));
		Assert(starNonzero.FillContains(new Point(12, 12)), "star nonzero fills center");

		Geometry starEvenOdd = Geometry.Parse(ImportCanonical(root, "star-eo.svg",
			Svg("<path fill-rule=\"evenodd\" d=\"M12,2 L15,20 L2,8 L22,8 L9,20 Z\"/>"), "star evenodd"));
		Assert(!starEvenOdd.FillContains(new Point(12, 12)), "star evenodd leaves center hollow");

		Geometry donutEvenOdd = Geometry.Parse(ImportCanonical(root, "donut-eo.svg",
			Svg("<path fill-rule=\"evenodd\" d=\"M0,0 L20,0 L20,20 L0,20 Z M5,5 L15,5 L15,15 L5,15 Z\"/>"), "donut evenodd"));
		Assert(donutEvenOdd.FillContains(new Point(2, 2)) && !donutEvenOdd.FillContains(new Point(10, 10)), "donut evenodd hole");

		Geometry donutNonzero = Geometry.Parse(ImportCanonical(root, "donut-nz.svg",
			Svg("<path fill-rule=\"nonzero\" d=\"M0,0 L20,0 L20,20 L0,20 Z M5,5 L15,5 L15,15 L5,15 Z\"/>"), "donut nonzero"));
		Assert(donutNonzero.FillContains(new Point(2, 2)) && donutNonzero.FillContains(new Point(10, 10)), "donut nonzero solid");

		// 8. Negative / zero dimension & malformed points checks
		AssertFailure(root, Write(root, "circle-neg.svg", Svg("<circle cx=\"12\" cy=\"12\" r=\"-2\"/>")),
			SvgIconReasonCodes.InvalidPathData, "circle negative r");
		AssertFailure(root, Write(root, "circle-zero.svg", Svg("<circle cx=\"12\" cy=\"12\" r=\"0\"/>")),
			SvgIconReasonCodes.EmptyGeometry, "circle zero r");
		AssertFailure(root, Write(root, "rect-neg.svg", Svg("<rect x=\"0\" y=\"0\" width=\"-5\" height=\"10\"/>")),
			SvgIconReasonCodes.InvalidPathData, "rect negative width");
		AssertFailure(root, Write(root, "rect-zero.svg", Svg("<rect x=\"0\" y=\"0\" width=\"0\" height=\"10\"/>")),
			SvgIconReasonCodes.EmptyGeometry, "rect zero width");
		AssertFailure(root, Write(root, "ellipse-zero.svg", Svg("<ellipse cx=\"12\" cy=\"12\" rx=\"5\" ry=\"0\"/>")),
			SvgIconReasonCodes.EmptyGeometry, "ellipse zero ry");
		AssertFailure(root, Write(root, "poly-odd.svg", Svg("<polygon points=\"10,20,30\"/>")),
			SvgIconReasonCodes.InvalidPathData, "polygon odd coords");
		AssertFailure(root, Write(root, "poly-one.svg", Svg("<polygon points=\"10,20\"/>")),
			SvgIconReasonCodes.EmptyGeometry, "polygon single point");
		AssertFailure(root, Write(root, "circle-inf.svg", Svg("<circle cx=\"1e9999\" cy=\"12\" r=\"5\"/>")),
			SvgIconReasonCodes.NonFiniteCoordinate, "circle nonfinite cx");
	}

	private static void RunTransformFlattening(string root)
	{
		// Nested parent-to-child <g> composition: translate(10,20) -> scale(2) -> rect(1,2,3,4) => (12,24,6,8)
		Geometry nested = Geometry.Parse(ImportCanonical(root, "tx-nested.svg",
			Svg("<g transform=\"translate(10, 20)\"><g transform=\"scale(2)\"><rect x=\"1\" y=\"2\" width=\"3\" height=\"4\"/></g></g>"),
			"nested group transforms"));
		Assert(
			Math.Abs(nested.Bounds.X - 12.0) < 1e-4 &&
			Math.Abs(nested.Bounds.Y - 24.0) < 1e-4 &&
			Math.Abs(nested.Bounds.Width - 6.0) < 1e-4 &&
			Math.Abs(nested.Bounds.Height - 8.0) < 1e-4,
			"nested group transform bounds");

		// Chained transform list on leaf element: translate(10,0) scale(2) on rect(1,0,5,5) => (12,0,10,10)
		Geometry chained = Geometry.Parse(ImportCanonical(root, "tx-chained.svg",
			Svg("<rect x=\"1\" y=\"0\" width=\"5\" height=\"5\" transform=\"translate(10, 0) scale(2)\"/>"),
			"chained transform"));
		Assert(
			Math.Abs(chained.Bounds.X - 12.0) < 1e-4 &&
			Math.Abs(chained.Bounds.Width - 10.0) < 1e-4,
			"chained transform order");

		// rotate(angle, cx, cy)
		Geometry rotated = Geometry.Parse(ImportCanonical(root, "tx-rotate.svg",
			Svg("<rect x=\"10\" y=\"5\" width=\"4\" height=\"2\" transform=\"rotate(90, 10, 10)\"/>"),
			"rotate around pivot"));
		Assert(
			Math.Abs(rotated.Bounds.X - 13.0) < 1e-3 &&
			Math.Abs(rotated.Bounds.Y - 10.0) < 1e-3 &&
			Math.Abs(rotated.Bounds.Width - 2.0) < 1e-3 &&
			Math.Abs(rotated.Bounds.Height - 4.0) < 1e-3,
			"rotate around pivot bounds");

		// skewX / skewY
		Geometry skewX = Geometry.Parse(ImportCanonical(root, "tx-skewx.svg",
			Svg("<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" transform=\"skewX(45)\"/>"),
			"skewX"));
		Assert(Math.Abs(skewX.Bounds.Width - 20.0) < 1e-3 && Math.Abs(skewX.Bounds.Height - 10.0) < 1e-3, "skewX bounds");

		Geometry skewY = Geometry.Parse(ImportCanonical(root, "tx-skewy.svg",
			Svg("<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" transform=\"skewY(45)\"/>"),
			"skewY"));
		Assert(Math.Abs(skewY.Bounds.Width - 10.0) < 1e-3 && Math.Abs(skewY.Bounds.Height - 20.0) < 1e-3, "skewY bounds");

		// matrix(a,b,c,d,e,f)
		Geometry matrix = Geometry.Parse(ImportCanonical(root, "tx-matrix.svg",
			Svg("<circle cx=\"0\" cy=\"0\" r=\"1\" transform=\"matrix(2, 0, 0, 3, 5, 6)\"/>"),
			"matrix"));
		Assert(
			Math.Abs(matrix.Bounds.X - 3.0) < 1e-2 &&
			Math.Abs(matrix.Bounds.Y - 3.0) < 1e-2 &&
			Math.Abs(matrix.Bounds.Width - 4.0) < 1e-2 &&
			Math.Abs(matrix.Bounds.Height - 6.0) < 1e-2,
			"matrix bounds");

		// Non-uniform scale on Arc path
		Geometry scaledArc = Geometry.Parse(ImportCanonical(root, "tx-arc.svg",
			Svg("<path d=\"M 0,10 A 5,5 0 0 1 10,10 Z\" transform=\"scale(2, 1)\"/>"),
			"non-uniform scale arc"));
		Assert(
			Math.Abs(scaledArc.Bounds.Width - 20.0) < 1e-2 &&
			Math.Abs(scaledArc.Bounds.Height - 5.0) < 1e-2,
			"non-uniform scale arc bounds");

		// Open path inside translated group
		Geometry translatedOpen = Geometry.Parse(ImportCanonical(root, "tx-open.svg",
			Svg("<g transform=\"translate(1,1)\"><path d=\"M0 0 L1 1\"/></g>"),
			"translated open path"));
		Assert(
			Math.Abs(translatedOpen.Bounds.X - 1.0) < 1e-4 &&
			Math.Abs(translatedOpen.Bounds.Y - 1.0) < 1e-4,
			"translated open path bounds");

		// InvariantCulture formatting under comma-decimal culture (de-DE)
		CultureInfo savedCulture = Thread.CurrentThread.CurrentCulture;
		try
		{
			Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
			string dePath = ImportCanonical(root, "tx-culture.svg",
				Svg("<circle cx=\"12.5\" cy=\"12.5\" r=\"3.25\" transform=\"translate(1.5, 2.5)\"/>"),
				"de-DE invariant formatting");
			Geometry parsedDe = Geometry.Parse(dePath);
			Assert(Math.Abs(parsedDe.Bounds.X - 10.75) < 1e-2, "de-DE invariant culture parse");
		}
		finally
		{
			Thread.CurrentThread.CurrentCulture = savedCulture;
		}
	}

	private static void RunMonochromeStrokeAndLucideIcons(string root)
	{
		// Lucide-style stroke-only icon (circle-plus)
		string lucideSvg =
			"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" " +
			"fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-miterlimit=\"4\" " +
			"class=\"lucide lucide-circle-plus\">" +
			"<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"M8 12h8\"/><path d=\"M12 8v8\"/></svg>";
		Geometry lucide = Geometry.Parse(ImportCanonical(root, "lucide-plus.svg", lucideSvg, "lucide circle-plus"));
		Assert(
			lucide.FillContains(new Point(12, 12)) &&
			lucide.FillContains(new Point(22, 12)) &&
			!lucide.FillContains(new Point(16, 16)),
			"lucide circle-plus geometry");

		// Tabler-style icon with invisible artboard spacer path
		string tablerSvg =
			"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" " +
			"fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">" +
			"<path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\"/>" +
			"<path d=\"M5 12l5 5l10 -10\"/></svg>";
		Geometry tabler = Geometry.Parse(ImportCanonical(root, "tabler-check.svg", tablerSvg, "tabler check"));
		Assert(tabler.FillContains(new Point(10, 17)), "tabler check geometry");

		// Mixed fill + stroke with color normalization equivalence (#336699 == #369 == rgb(51, 102, 153))
		string mixedEquivalent = Svg(
			"<circle cx=\"12\" cy=\"12\" r=\"5\" fill=\"#336699\" stroke=\"#369\" stroke-width=\"2\"/>" +
			"<line x1=\"0\" y1=\"0\" x2=\"24\" y2=\"24\" stroke=\"rgb(51, 102, 153)\" stroke-width=\"2\"/>");
		Geometry mixed = Geometry.Parse(ImportCanonical(root, "mixed-mono.svg", mixedEquivalent, "mixed fill+stroke equivalent rgb/hex"));
		Assert(mixed.FillContains(new Point(12, 12)) && mixed.FillContains(new Point(1, 1)), "mixed fill+stroke geometry");

		// Named vs rgb equivalent
		ImportCanonical(root, "named-rgb.svg",
			Svg("<path d=\"M0 0 L10 0 L10 10 Z\" fill=\"black\" stroke=\"rgb(0%, 0%, 0%)\" stroke-width=\"2\"/>"),
			"named black == rgb(0%,0%,0%)");

		// Zero-length line with round cap succeeds, with butt cap fails EmptyGeometry
		Geometry dotLine = Geometry.Parse(ImportCanonical(root, "line-dot.svg",
			Svg("<line x1=\"5\" y1=\"5\" x2=\"5\" y2=\"5\" stroke=\"#000\" stroke-width=\"4\" stroke-linecap=\"round\"/>"),
			"zero-length line round cap"));
		Assert(Math.Abs(dotLine.Bounds.Width - 4.0) < 0.2, "zero-length line round cap bounds");
		AssertFailure(root, Write(root, "line-butt-zero.svg",
			Svg("<line x1=\"5\" y1=\"5\" x2=\"5\" y2=\"5\" stroke=\"#000\" stroke-width=\"4\" stroke-linecap=\"butt\"/>")),
			SvgIconReasonCodes.EmptyGeometry, "zero-length line butt cap");

		// Invisible stroke cases
		AssertFailure(root, Write(root, "stroke-none.svg", Svg("<path d=\"M0 0 L10 10\" fill=\"none\" stroke=\"none\"/>")),
			SvgIconReasonCodes.InvisiblePath, "fill none and stroke none");
		AssertFailure(root, Write(root, "stroke-zero-width.svg", Svg("<path d=\"M0 0 L10 10\" fill=\"none\" stroke=\"#000\" stroke-width=\"0\"/>")),
			SvgIconReasonCodes.InvisiblePath, "fill none and stroke-width 0");

		// Multi-color stroke+fill rejections
		AssertFailure(root, Write(root, "multi-fill-stroke.svg", Svg("<path d=\"M0 0 L10 0 L10 10 Z\" fill=\"#000\" stroke=\"#fff\" stroke-width=\"2\"/>")),
			SvgIconReasonCodes.MultiColor, "multi-color fill and stroke");
		AssertFailure(root, Write(root, "multi-current-vs-hex.svg",
			Svg("<g fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><circle cx=\"12\" cy=\"12\" r=\"5\"/><path d=\"M5 12h14\" stroke=\"#000\"/></g>")),
			SvgIconReasonCodes.MultiColor, "multi-color currentColor vs black");
	}

	private static void RunRejectedAttributesAndElements(string root)
	{
		AssertFailure(root, Write(root, "stroke-dasharray.svg", Svg("<path d=\"M0 0 L10 10\" fill=\"none\" stroke=\"#000\" stroke-dasharray=\"2 2\"/>")),
			SvgIconReasonCodes.StrokeUnsupported, "unsupported stroke-dasharray");
		AssertFailure(root, Write(root, "stroke-neg-width.svg", Svg("<path d=\"M0 0 L10 10\" fill=\"none\" stroke=\"#000\" stroke-width=\"-1\"/>")),
			SvgIconReasonCodes.StrokeUnsupported, "negative stroke-width");
		AssertFailure(root, Write(root, "stroke-bad-cap.svg", Svg("<path d=\"M0 0 L10 10\" fill=\"none\" stroke=\"#000\" stroke-linecap=\"invalid\"/>")),
			SvgIconReasonCodes.StrokeUnsupported, "invalid stroke-linecap");
		AssertFailure(root, Write(root, "stroke-bad-miter.svg", Svg("<path d=\"M0 0 L10 10\" fill=\"none\" stroke=\"#000\" stroke-miterlimit=\"0.5\"/>")),
			SvgIconReasonCodes.StrokeUnsupported, "stroke-miterlimit < 1");
		AssertFailure(root, Write(root, "transform-3d.svg", Svg("<g transform=\"rotate3d(1,1,1,45)\"><path d=\"M0 0 L1 1\"/></g>")),
			SvgIconReasonCodes.TransformUnsupported, "unsupported transform function");
		AssertFailure(root, Write(root, "transform-singular.svg", Svg("<g transform=\"scale(0)\"><path d=\"M0 0 L1 1\"/></g>")),
			SvgIconReasonCodes.TransformUnsupported, "singular scale(0)");
		AssertFailure(root, Write(root, "transform-skew90.svg", Svg("<g transform=\"skewX(90)\"><path d=\"M0 0 L1 1\"/></g>")),
			SvgIconReasonCodes.TransformUnsupported, "singular skewX(90)");
		AssertFailure(root, Write(root, "transform-extreme.svg", Svg("<g transform=\"scale(1e9)\"><path d=\"M0 0 L1 1\"/></g>")),
			SvgIconReasonCodes.TransformUnsupported, "extreme transform scale");
		AssertFailure(root, Write(root, "transform-inf.svg", Svg("<g transform=\"translate(1e9999, 0)\"><path d=\"M0 0 L1 1\"/></g>")),
			SvgIconReasonCodes.NonFiniteCoordinate, "nonfinite transform");
		AssertFailure(root, Write(root, "multi.svg",
			Svg("<path d=\"M0 0 L1 1\" fill=\"#000\"/><path d=\"M2 2 L3 3\" fill=\"#fff\"/>")),
			SvgIconReasonCodes.MultiColor, "multi color");
		AssertFailure(root, Write(root, "gradient.svg",
			Svg("<linearGradient id=\"g\"/><path d=\"M0 0 L1 1\" fill=\"url(#g)\"/>")),
			SvgIconReasonCodes.GradientUnsupported, "gradient");
		AssertFailure(root, Write(root, "stroke-gradient.svg",
			Svg("<path d=\"M0 0 L10 10\" fill=\"none\" stroke=\"url(#g)\"/>")),
			SvgIconReasonCodes.GradientUnsupported, "stroke gradient");
		AssertFailure(root, Write(root, "use.svg", Svg("<use href=\"#x\"/><path d=\"M0 0 L1 1\"/>")),
			SvgIconReasonCodes.ExternalReference, "use");
		AssertFailure(root, Write(root, "script.svg", Svg("<script>alert(1)</script><path d=\"M0 0 L1 1\"/>")),
			SvgIconReasonCodes.ExternalReference, "script element");
		AssertFailure(root, Write(root, "foreign.svg", Svg("<foreignObject width=\"10\" height=\"10\"/><path d=\"M0 0 L1 1\"/>")),
			SvgIconReasonCodes.ExternalReference, "foreignObject element");
		AssertFailure(root, Write(root, "image.svg", Svg("<image href=\"https://evil.test/x.png\"/><path d=\"M0 0 L1 1\"/>")),
			SvgIconReasonCodes.ExternalReference, "image element");
		AssertFailure(root, Write(root, "xlink.svg",
			"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><path d=\"M0 0 L1 1\" xlink:href=\"https://evil.test/x\"/></svg>"),
			SvgIconReasonCodes.ExternalReference, "xlink:href attribute");
		AssertFailure(root, Write(root, "style-attr.svg", Svg("<path d=\"M0 0 L1 1\" style=\"fill:#000\"/>")),
			SvgIconReasonCodes.StyleUnsupported, "style attribute");
		AssertFailure(root, Write(root, "style-elem.svg", Svg("<style>.a{fill:#000}</style><path class=\"a\" d=\"M0 0 L1 1\"/>")),
			SvgIconReasonCodes.StyleUnsupported, "style element");
		AssertFailure(root, Write(root, "onclick.svg", Svg("<path d=\"M0 0 L1 1\" onclick=\"alert(1)\"/>")),
			SvgIconReasonCodes.UnsupportedAttribute, "onclick event");
		AssertFailure(root, Write(root, "data-url.svg", Svg("<path d=\"M0 0 L1 1\" data-x=\"url(https://evil.test/leak)\"/>")),
			SvgIconReasonCodes.ExternalReference, "url() inside data-*");
		AssertFailure(root, Write(root, "text-elem.svg", Svg("<text>Hello</text>")),
			SvgIconReasonCodes.UnsupportedElement, "unsupported text element");
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

	private static string ImportCanonical(string root, string fileName, string content, string name)
	{
		string source = Write(root, fileName, content);
		string target = NewDirectory(root, "ok-" + Guid.NewGuid().ToString("N"));
		var result = SvgIconBatchImporter.ImportFiles(new[] { source }, target, () => { });
		Assert(result.SuccessCount == 1, name + " success");
		if (result.SuccessCount != 1 || result.Items[0].TargetPath == null)
		{
			return "M0 0";
		}
		return File.ReadAllText(result.Items[0].TargetPath!);
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
