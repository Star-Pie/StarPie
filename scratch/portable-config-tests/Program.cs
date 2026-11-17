using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinPieGestures;

namespace PortableConfigTests;

internal static class Program
{
	private static readonly string[] SvgFiles =
	{
		"icon_a.svg",
		"icon_b.svg",
		"icon_c.svg",
		"icon_d.svg"
	};

	private static int _passed;
	private static int _failed;
	private static string _root = "";
	private static string _sourceIcons = "";

	public static int Main()
	{
		string artifactsRoot = Path.GetFullPath(Path.Combine(
			Directory.GetCurrentDirectory(),
			"scratch",
			"portable-config-tests",
			"artifacts"));
		Directory.CreateDirectory(artifactsRoot);
		string processTemp = Path.Combine(artifactsRoot, "temp");
		Directory.CreateDirectory(processTemp);
		Environment.SetEnvironmentVariable("TEMP", processTemp, EnvironmentVariableTarget.Process);
		Environment.SetEnvironmentVariable("TMP", processTemp, EnvironmentVariableTarget.Process);
		_root = Path.Combine(artifactsRoot, "run_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
		try
		{
			Directory.CreateDirectory(_root);
			_sourceIcons = Path.Combine(_root, "source-icons");
			Directory.CreateDirectory(_sourceIcons);
			foreach (string fileName in SvgFiles)
			{
				WriteSvg(Path.Combine(_sourceIcons, fileName), "#336699");
			}
			if (!VerifyCandidateAssembly())
			{
				Console.WriteLine("RESULT: FAIL (candidate assembly identity)");
				return 1;
			}
			Console.WriteLine("TEST_TEMP=" + Path.GetTempPath());

			Run("cross-directory roundtrip and same-stem protection", RoundTrip);
			Run("nested actions and duplicate references", NestedActions);
			Run("gesture mappings only and nested icons roundtrip", GestureMappingsOnly);
			Run("gesture mapping shared key remapped", GestureMappingSharedKey);
			Run("lowercase known fields roundtrip", LowercaseKnownFields);
			Run("mixed-case known fields roundtrip", MixedCaseKnownFields);
			Run("case-equivalent known properties rejected", CaseEquivalentKnownPropertiesRejected);
			Run("unknown field case variants preserved", UnknownFieldCaseVariantsPreserved);
			Run("unknown fields and extension data are preserved", UnknownFieldsPreserved);
			Run("missing custom icon fails export", MissingIcon);
			Run("bitmap custom icon fails export", BitmapIcon);
			Run("invalid SVG and external references fail export", InvalidSvg);
			Run("DTD SVG is rejected", DtdSvg);
			Run("existing target is unchanged after export failure", ExportFailureLeavesTarget);
			Run("damaged ZIP is rejected", DamagedZip);
			Run("missing manifest/config/resource is rejected", MissingEntries);
			Run("wrong asset size and hash are rejected", WrongSizeAndHash);
			Run("path traversal and extra entries are rejected", UnsafeEntries);
			Run("duplicate manifest properties and case aliases are rejected", ManifestSecurity);
			Run("dispose before commit rolls back resources", DisposeBeforeCommit);
			Run("zero-asset dispose removes the created directory", ZeroAssetDispose);
			Run("commit keeps resources after dispose", CommitKeepsResources);
#if !STARPIE_CANDIDATE_MODE
			Run("prepare failure rolls back resources", PrepareFailureRollsBack);
#else
			Run("prepare failure leaves no resources", PrepareFailureRollsBack);
#endif
			Run("inline SVG remains unchanged", InlineSvgUnchanged);
			Run("config limit is enforced", ConfigLimit);
			Run("asset size limit is enforced", AssetSizeLimit);
			Run("asset count limit is enforced", AssetCountLimit);
			Run("total asset size limit is enforced", TotalAssetLimit);
		}
		catch (Exception ex)
		{
			_failed++;
			Console.WriteLine("FATAL: " + ex);
		}

		Console.WriteLine($"RESULT: {(_failed == 0 ? "PASS" : "FAIL")} ({_passed} passed, {_failed} failed)");
		Console.WriteLine("ARTIFACTS: " + _root);
		return _failed == 0 ? 0 : 1;
	}

	private static void RoundTrip()
	{
		string package = Path.Combine(_root, "roundtrip.starpie-config");
		string targetIcons = Path.Combine(_root, "target-icons");
		Directory.CreateDirectory(targetIcons);
		string existingIcon = Path.Combine(targetIcons, "icon_a.svg");
		byte[] existingBytes = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0 L1 1\" fill=\"currentColor\"/></svg>");
		File.WriteAllBytes(existingIcon, existingBytes);

		JsonObject config = CreateConfig(
			Action("custom:icon_a"),
			null,
			null,
			null);
		PortableConfigBundle.Export(config.ToJsonString(), package, _sourceIcons);
		Assert(File.Exists(package), "package was not created");

		using var transaction = PortableConfigBundle.PrepareImport(package, targetIcons);
		Assert(File.ReadAllBytes(existingIcon).SequenceEqual(existingBytes), "existing icon was overwritten");
		Assert(transaction.ConfigJson.Contains("custom:icon_a_portable_", StringComparison.Ordinal), "new key was not assigned");
		string importedKey = FindFirstIconKey(transaction.ConfigJson);
		string importedStem = importedKey.Substring("custom:".Length);
		Assert(File.Exists(Path.Combine(targetIcons, importedStem + ".svg")), "imported icon was not created");
		transaction.Commit();
		transaction.Dispose();
		Assert(File.Exists(Path.Combine(targetIcons, importedStem + ".svg")), "committed icon was removed");
	}

	private static void NestedActions()
	{
		string package = Path.Combine(_root, "nested.starpie-config");
		string targetIcons = Path.Combine(_root, "nested-icons");
		JsonObject config = CreateConfig(
			Action("custom:icon_a", new JsonObject
			{
				["IconKey"] = "custom:icon_a",
				["SubActions"] = new JsonArray(Action("custom:icon_b"))
			}),
			Action("custom:icon_b"),
			new JsonArray(Action("custom:icon_a")),
			new JsonArray(Action("custom:icon_c")));
		config["ActionPanels"] = new JsonArray(new JsonObject
		{
			["Keys"] = new JsonArray(Action("custom:icon_a"), Action("custom:icon_c"))
		});
		config["CancelAction"] = Action("custom:icon_d");
		config["CoreCustomIconKey"] = "custom:icon_b";

		PortableConfigBundle.Export(config.ToJsonString(), package, _sourceIcons);
		using var transaction = PortableConfigBundle.PrepareImport(package, targetIcons);
		JsonNode root = JsonNode.Parse(transaction.ConfigJson)!;
		Assert(AllKnownCustomKeysChanged(root), "a known nested custom key was not remapped");
		Assert(CountFiles(targetIcons) == 4, "duplicate references were not packaged once");
		transaction.Commit();
	}

	private static void GestureMappingsOnly()
	{
		string package = Path.Combine(_root, "gesture-only.starpie-config");
		string targetIcons = Path.Combine(_root, "gesture-only-icons");
		var nestedAction = new JsonObject
		{
			["Type"] = "Hotkey",
			["Name"] = "Nested",
			["IconKey"] = "custom:icon_b",
			["SubActions"] = new JsonArray()
		};
		var gestureAction = new JsonObject
		{
			["Type"] = "Hotkey",
			["Name"] = "Gesture",
			["IconKey"] = "custom:icon_a",
			["SubActions"] = new JsonArray(nestedAction)
		};
		var config = new JsonObject
		{
			["Profiles"] = new JsonArray(),
			["GestureMappings"] = new JsonArray(new JsonObject
			{
				["Pattern"] = "D",
				["Action"] = gestureAction
			})
		};

		PortableConfigBundle.Export(config.ToJsonString(), package, _sourceIcons);
		using var transaction = PortableConfigBundle.PrepareImport(package, targetIcons);
		JsonNode root = JsonNode.Parse(transaction.ConfigJson)!;
		JsonNode importedAction = root["GestureMappings"]![0]!["Action"]!;
		Assert(IsPortableCustomKey(importedAction["IconKey"]), "gesture mapping action icon was not remapped");
		Assert(IsPortableCustomKey(importedAction["SubActions"]![0]!["IconKey"]), "gesture mapping subaction icon was not remapped");
		Assert(CountFiles(targetIcons) == 2, "gesture mapping assets were not packaged exactly once");
		transaction.Commit();
	}

	private static void GestureMappingSharedKey()
	{
		string package = Path.Combine(_root, "gesture-shared.starpie-config");
		string targetIcons = Path.Combine(_root, "gesture-shared-icons");
		const string sharedKey = "custom:icon_a";
		JsonObject config = CreateConfig(Action(sharedKey), null, null, null);
		config["GestureMappings"] = new JsonArray(new JsonObject
		{
			["Pattern"] = "D",
			["Action"] = Action(sharedKey)
		});

		PortableConfigBundle.Export(config.ToJsonString(), package, _sourceIcons);
		using var transaction = PortableConfigBundle.PrepareImport(package, targetIcons);
		JsonNode root = JsonNode.Parse(transaction.ConfigJson)!;
		Assert(IsPortableCustomKey(root["Profiles"]![0]!["Actions"]![0]!["IconKey"]), "profile icon was not remapped");
		Assert(IsPortableCustomKey(root["GestureMappings"]![0]!["Action"]!["IconKey"]), "shared gesture mapping icon was not remapped");
		Assert(CountFiles(targetIcons) == 1, "shared icon was not packaged once");
		transaction.Commit();
	}

	private static void LowercaseKnownFields()
		=> CaseVariantKnownFields(
			"lowercase",
			"profiles",
			"actions",
			"centerAction",
			"layers",
			"iconKey",
			"subActions",
			"gestureMappings",
			"action",
			"coreCustomIconKey");

	private static void MixedCaseKnownFields()
		=> CaseVariantKnownFields(
			"mixed",
			"pRoFiLeS",
			"aCtIoNs",
			"cEnTeRaCtIoN",
			"lAyErS",
			"iCoNkEy",
			"sUbAcTiOnS",
			"gEsTuReMaPpInGs",
			"aCtIoN",
			"cOrEcUsToMiCoNkEy");

	private static void CaseVariantKnownFields(
		string suffix,
		string profilesName,
		string actionsName,
		string centerActionName,
		string layersName,
		string iconName,
		string subActionsName,
		string gestureMappingsName,
		string gestureActionName,
		string coreName)
	{
		string package = Path.Combine(_root, "case-" + suffix + ".starpie-config");
		string targetIcons = Path.Combine(_root, "case-" + suffix + "-icons");
		JsonObject config = BuildCaseVariantConfig(
			profilesName,
			actionsName,
			centerActionName,
			layersName,
			iconName,
			subActionsName,
			gestureMappingsName,
			gestureActionName,
			coreName);

		PortableConfigBundle.Export(config.ToJsonString(), package, _sourceIcons);
		using var transaction = PortableConfigBundle.PrepareImport(package, targetIcons);
		JsonNode root = JsonNode.Parse(transaction.ConfigJson)!;
		JsonNode profile = root[profilesName]![0]!;
		JsonNode actions = profile[actionsName]!;
		Assert(IsPortableCustomKey(actions[0]![iconName]), suffix + " profile action icon was not remapped");
		Assert(IsPortableCustomKey(actions[0]![subActionsName]![0]![iconName]), suffix + " nested action icon was not remapped");
		Assert(IsPortableCustomKey(profile[centerActionName]![iconName]), suffix + " center action icon was not remapped");
		Assert(IsPortableCustomKey(profile[layersName]![0]![actionsName]![0]![iconName]), suffix + " layer action icon was not remapped");
		Assert(IsPortableCustomKey(root[gestureMappingsName]![0]![gestureActionName]![iconName]), suffix + " gesture mapping icon was not remapped");
		Assert(IsPortableCustomKey(root[coreName]), suffix + " core icon was not remapped");
		Assert(CountFiles(targetIcons) == 4, suffix + " assets were not packaged exactly once");
		transaction.Commit();
	}

	private static JsonObject BuildCaseVariantConfig(
		string profilesName,
		string actionsName,
		string centerActionName,
		string layersName,
		string iconName,
		string subActionsName,
		string gestureMappingsName,
		string gestureActionName,
		string coreName)
	{
		var nestedAction = ActionWithCase("custom:icon_b", iconName, subActionsName);
		var profileAction = ActionWithCase(
			"custom:icon_a",
			iconName,
			subActionsName,
			new JsonArray(nestedAction));
		var layerAction = ActionWithCase("custom:icon_c", iconName, subActionsName);
		var gestureAction = ActionWithCase("custom:icon_d", iconName, subActionsName);
		return new JsonObject
		{
			[profilesName] = new JsonArray(new JsonObject
			{
				[actionsName] = new JsonArray(profileAction),
				[centerActionName] = ActionWithCase("custom:icon_a", iconName, subActionsName),
				[layersName] = new JsonArray(new JsonObject
				{
					[actionsName] = new JsonArray(layerAction)
				})
			}),
			[gestureMappingsName] = new JsonArray(new JsonObject
			{
				[gestureActionName] = gestureAction
			}),
			[coreName] = "custom:icon_a"
		};
	}

	private static JsonObject ActionWithCase(
		string iconKey,
		string iconName,
		string subActionsName,
		JsonArray? subActions = null)
	{
		return new JsonObject
		{
			["Type"] = "Hotkey",
			["Name"] = "Test",
			[iconName] = iconKey,
			[subActionsName] = subActions ?? new JsonArray()
		};
	}

	private static void CaseEquivalentKnownPropertiesRejected()
	{
		JsonObject rootDuplicate = CreateConfig(Action("custom:icon_a"), null, null, null);
		rootDuplicate["profiles"] = new JsonArray(new JsonObject
		{
			["actions"] = new JsonArray(Action("custom:icon_b"))
		});
		AssertExportConfigInvalid(rootDuplicate, "duplicate-root.starpie-config");

		JsonObject actionDuplicateAction = Action("custom:icon_a");
		actionDuplicateAction["iconKey"] = "custom:icon_b";
		JsonObject actionDuplicate = CreateConfig(actionDuplicateAction, null, null, null);
		AssertExportConfigInvalid(actionDuplicate, "duplicate-action.starpie-config");

		JsonObject gestureDuplicate = CreateConfig(Action("custom:icon_a"), null, null, null);
		gestureDuplicate["GestureMappings"] = new JsonArray();
		gestureDuplicate["gestureMappings"] = new JsonArray(new JsonObject
		{
			["Action"] = Action("custom:icon_b")
		});
		AssertExportConfigInvalid(gestureDuplicate, "duplicate-gesture.starpie-config");

		JsonObject coreDuplicate = CreateConfig(Action("custom:icon_a"), null, null, null);
		coreDuplicate["CoreCustomIconKey"] = "custom:icon_a";
		coreDuplicate["coreCustomIconKey"] = "custom:icon_b";
		AssertExportConfigInvalid(coreDuplicate, "duplicate-core.starpie-config");
	}

	private static void AssertExportConfigInvalid(JsonObject config, string fileName)
	{
		string package = Path.Combine(_root, fileName);
		AssertThrows(PortableConfigBundleReasonCodes.ConfigInvalid, () =>
			PortableConfigBundle.Export(config.ToJsonString(), package, _sourceIcons));
	}

	private static void UnknownFieldCaseVariantsPreserved()
	{
		string package = Path.Combine(_root, "unknown-case.starpie-config");
		string targetIcons = Path.Combine(_root, "unknown-case-icons");
		JsonObject action = Action("custom:icon_a");
		action["Extras"] = new JsonObject
		{
			["iconKey"] = "custom:unknown-lower",
			["IconKey"] = "custom:unknown-upper"
		};
		action["ExtensionData"] = new JsonObject
		{
			["iconKey"] = "custom:plugin-lower",
			["IconKey"] = "custom:plugin-upper"
		};
		JsonObject config = CreateConfig(action, null, null, null);
		config["Extras"] = new JsonObject
		{
			["profiles"] = "shadow-lower",
			["Profiles"] = "shadow-upper"
		};
		string originalActionExtras = action["Extras"]!.ToJsonString();
		string originalActionExtensionData = action["ExtensionData"]!.ToJsonString();
		string originalRootExtras = config["Extras"]!.ToJsonString();

		PortableConfigBundle.Export(config.ToJsonString(), package, _sourceIcons);
		using var transaction = PortableConfigBundle.PrepareImport(package, targetIcons);
		JsonNode root = JsonNode.Parse(transaction.ConfigJson)!;
		JsonNode importedAction = root["Profiles"]![0]!["Actions"]![0]!;
		Assert(importedAction["Extras"]!.ToJsonString() == originalActionExtras, "action Extras case variants changed");
		Assert(importedAction["ExtensionData"]!.ToJsonString() == originalActionExtensionData, "action ExtensionData case variants changed");
		Assert(root["Extras"]!.ToJsonString() == originalRootExtras, "root Extras case variants changed");
		transaction.Commit();
	}

	private static void UnknownFieldsPreserved()
	{
		string package = Path.Combine(_root, "unknown.starpie-config");
		string targetIcons = Path.Combine(_root, "unknown-icons");
		var action = Action("custom:icon_a");
		action["ExtensionData"] = new JsonObject
		{
			["IconKey"] = "custom:plugin-extension-shadow"
		};
		action["Extras"] = new JsonObject
		{
			["IconKey"] = "custom:not-a-package-asset",
			["PluginExtensionData"] = new JsonObject
			{
				["IconKey"] = "custom:still-not-an-asset"
			}
		};
		JsonObject config = CreateConfig(action, null, null, null);
		config["Extras"] = new JsonObject
		{
			["IconKey"] = "custom:root-shadow",
			["Arbitrary"] = new JsonArray(1, "two", true)
		};
		config["ActionPanels"] = new JsonArray(new JsonObject
		{
			["AdditionalData"] = new JsonObject
			{
				["IconKey"] = "custom:panel-shadow"
			},
			["Keys"] = new JsonArray(Action("custom:icon_b"))
		});
		string originalRootExtras = config["Extras"]!.ToJsonString();
		string originalActionExtras = action["Extras"]!.ToJsonString();

		PortableConfigBundle.Export(config.ToJsonString(), package, _sourceIcons);
		using var transaction = PortableConfigBundle.PrepareImport(package, targetIcons);
		JsonNode root = JsonNode.Parse(transaction.ConfigJson)!;
		JsonNode profileAction = root["Profiles"]![0]!["Actions"]![0]!;
		Assert(profileAction["Extras"]!["IconKey"]!.GetValue<string>() == "custom:not-a-package-asset", "action extension data changed");
		Assert(profileAction["Extras"]!.ToJsonString() == originalActionExtras, "action extension data layout changed");
		Assert(profileAction["ExtensionData"]!["IconKey"]!.GetValue<string>() == "custom:plugin-extension-shadow", "plugin extension data changed");
		Assert(root["Extras"]!["IconKey"]!.GetValue<string>() == "custom:root-shadow", "root extension data changed");
		Assert(root["Extras"]!.ToJsonString() == originalRootExtras, "root extension data layout changed");
		Assert(root["ActionPanels"]![0]!["AdditionalData"]!["IconKey"]!.GetValue<string>() == "custom:panel-shadow", "panel extension data changed");
		transaction.Commit();
	}

	private static void MissingIcon()
	{
		string package = Path.Combine(_root, "missing.starpie-config");
		JsonObject config = CreateConfig(Action("custom:does-not-exist"), null, null, null);
		AssertThrows(PortableConfigBundleReasonCodes.IconMissing, () =>
			PortableConfigBundle.Export(config.ToJsonString(), package, _sourceIcons));
	}

	private static void BitmapIcon()
	{
		string bitmapDir = Path.Combine(_root, "bitmap-icons");
		Directory.CreateDirectory(bitmapDir);
		File.WriteAllBytes(Path.Combine(bitmapDir, "bitmap.svg.png"), new byte[] { 1, 2, 3 });
		string package = Path.Combine(_root, "bitmap.starpie-config");
		JsonObject config = CreateConfig(Action("custom:bitmap.svg"), null, null, null);
		AssertThrows(PortableConfigBundleReasonCodes.IconBitmapUnsupported, () =>
			PortableConfigBundle.Export(config.ToJsonString(), package, bitmapDir));
	}

	private static void InvalidSvg()
	{
		string badDir = Path.Combine(_root, "bad-icons");
		Directory.CreateDirectory(badDir);
		File.WriteAllText(Path.Combine(badDir, "bad.svg"),
			"<svg xmlns=\"http://www.w3.org/2000/svg\"><use href=\"https://example.test/x.svg\"/></svg>");
		string package = Path.Combine(_root, "bad.starpie-config");
		JsonObject config = CreateConfig(Action("custom:bad"), null, null, null);
		AssertThrows(PortableConfigBundleReasonCodes.SvgInvalid, () =>
			PortableConfigBundle.Export(config.ToJsonString(), package, badDir));
	}

	private static void DtdSvg()
	{
		string dtdDir = Path.Combine(_root, "dtd-icons");
		Directory.CreateDirectory(dtdDir);
		File.WriteAllText(Path.Combine(dtdDir, "dtd.svg"),
			"<!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>" +
			"<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0 L1 1\"/></svg>");
		JsonObject config = CreateConfig(Action("custom:dtd"), null, null, null);
		AssertThrows(PortableConfigBundleReasonCodes.SvgInvalid, () =>
			PortableConfigBundle.Export(config.ToJsonString(), Path.Combine(_root, "dtd.starpie-config"), dtdDir));
	}

	private static void ExportFailureLeavesTarget()
	{
		string package = Path.Combine(_root, "preserve.starpie-config");
		byte[] sentinel = Encoding.UTF8.GetBytes("existing package bytes");
		File.WriteAllBytes(package, sentinel);
		JsonObject config = CreateConfig(Action("custom:missing"), null, null, null);
		AssertThrows(PortableConfigBundleReasonCodes.IconMissing, () =>
			PortableConfigBundle.Export(config.ToJsonString(), package, _sourceIcons));
		Assert(File.ReadAllBytes(package).SequenceEqual(sentinel), "failed export changed the existing target");
	}

	private static void DamagedZip()
	{
		string package = Path.Combine(_root, "damaged.starpie-config");
		File.WriteAllBytes(package, Encoding.UTF8.GetBytes("not a zip"));
		AssertThrows(PortableConfigBundleReasonCodes.ZipInvalid, () =>
			PortableConfigBundle.PrepareImport(package, Path.Combine(_root, "damaged-icons")).Dispose());
	}

	private static void MissingEntries()
	{
		string noManifest = Path.Combine(_root, "no-manifest.starpie-config");
		CreateZip(noManifest, new Dictionary<string, byte[]>
		{
			["config.json"] = Encoding.UTF8.GetBytes("{}")
		});
		AssertThrows(PortableConfigBundleReasonCodes.MissingEntry, () =>
			PortableConfigBundle.PrepareImport(noManifest, Path.Combine(_root, "missing-icons-a")).Dispose());

		string noConfig = Path.Combine(_root, "no-config.starpie-config");
		CreateZip(noConfig, new Dictionary<string, byte[]>
		{
			["manifest.json"] = BuildManifestBytes(new JsonArray())
		});
		AssertThrows(PortableConfigBundleReasonCodes.MissingEntry, () =>
			PortableConfigBundle.PrepareImport(noConfig, Path.Combine(_root, "missing-icons-b")).Dispose());

		string noAsset = Path.Combine(_root, "no-asset.starpie-config");
		byte[] svg = ValidSvgBytes();
		string entry = "icons/" + Hash(svg) + ".svg";
		JsonArray assets = new JsonArray(new JsonObject
		{
			["key"] = "custom:icon_a",
			["entry"] = entry,
			["size"] = svg.LongLength,
			["sha256"] = Hash(svg)
		});
		CreateZip(noAsset, new Dictionary<string, byte[]>
		{
			["manifest.json"] = BuildManifestBytes(assets),
			["config.json"] = Encoding.UTF8.GetBytes(CreateConfig(Action("custom:icon_a"), null, null, null).ToJsonString())
		});
		AssertThrows(PortableConfigBundleReasonCodes.MissingEntry, () =>
			PortableConfigBundle.PrepareImport(noAsset, Path.Combine(_root, "missing-icons-c")).Dispose());
	}

	private static void WrongSizeAndHash()
	{
		byte[] svg = ValidSvgBytes();
		string entry = "icons/" + Hash(svg) + ".svg";
		string wrongSize = Path.Combine(_root, "wrong-size.starpie-config");
		JsonArray sizeAssets = new JsonArray(new JsonObject
		{
			["key"] = "custom:icon_a",
			["entry"] = entry,
			["size"] = svg.Length + 1,
			["sha256"] = Hash(svg)
		});
		CreateZip(wrongSize, new Dictionary<string, byte[]>
		{
			["manifest.json"] = BuildManifestBytes(sizeAssets),
			["config.json"] = Encoding.UTF8.GetBytes(CreateConfig(Action("custom:icon_a"), null, null, null).ToJsonString()),
			[entry] = svg
		});
		AssertThrows(PortableConfigBundleReasonCodes.SizeMismatch, () =>
			PortableConfigBundle.PrepareImport(wrongSize, Path.Combine(_root, "wrong-icons-a")).Dispose());

		string wrongHash = Path.Combine(_root, "wrong-hash.starpie-config");
		JsonArray hashAssets = new JsonArray(new JsonObject
		{
			["key"] = "custom:icon_a",
			["entry"] = entry,
			["size"] = svg.Length,
			["sha256"] = new string('0', 64)
		});
		CreateZip(wrongHash, new Dictionary<string, byte[]>
		{
			["manifest.json"] = BuildManifestBytes(hashAssets),
			["config.json"] = Encoding.UTF8.GetBytes(CreateConfig(Action("custom:icon_a"), null, null, null).ToJsonString()),
			[entry] = svg
		});
		AssertThrows(PortableConfigBundleReasonCodes.HashMismatch, () =>
			PortableConfigBundle.PrepareImport(wrongHash, Path.Combine(_root, "wrong-icons-b")).Dispose());
	}

	private static void UnsafeEntries()
	{
		byte[] manifest = BuildManifestBytes(new JsonArray());
		string traversal = Path.Combine(_root, "traversal.starpie-config");
		CreateZip(traversal, new Dictionary<string, byte[]>
		{
			["manifest.json"] = manifest,
			["config.json"] = Encoding.UTF8.GetBytes("{}"),
			["../evil"] = new byte[] { 1 }
		});
		AssertThrows(PortableConfigBundleReasonCodes.EntryInvalid, () =>
			PortableConfigBundle.PrepareImport(traversal, Path.Combine(_root, "unsafe-icons-a")).Dispose());

		string extra = Path.Combine(_root, "extra.starpie-config");
		CreateZip(extra, new Dictionary<string, byte[]>
		{
			["manifest.json"] = manifest,
			["config.json"] = Encoding.UTF8.GetBytes("{}"),
			["extra.txt"] = Encoding.UTF8.GetBytes("extra")
		});
		AssertThrows(PortableConfigBundleReasonCodes.ExtraEntry, () =>
			PortableConfigBundle.PrepareImport(extra, Path.Combine(_root, "unsafe-icons-b")).Dispose());
	}

	private static void ManifestSecurity()
	{
		string duplicate = Path.Combine(_root, "duplicate-manifest.starpie-config");
		CreateZip(duplicate, new Dictionary<string, byte[]>
		{
			["manifest.json"] = Encoding.UTF8.GetBytes(
				"{\"format\":\"StarPiePortableConfig\",\"format\":\"StarPiePortableConfig\",\"version\":1,\"config\":\"config.json\",\"assets\":[]}"),
			["config.json"] = Encoding.UTF8.GetBytes("{}")
		});
		AssertThrows(PortableConfigBundleReasonCodes.ManifestInvalid, () =>
			PortableConfigBundle.PrepareImport(duplicate, Path.Combine(_root, "manifest-icons-a")).Dispose());

		string caseAlias = Path.Combine(_root, "case-alias.starpie-config");
		CreateZip(caseAlias, new Dictionary<string, byte[]>
		{
			["manifest.json"] = BuildManifestBytes(new JsonArray()),
			["config.json"] = Encoding.UTF8.GetBytes("{}"),
			["MANIFEST.JSON"] = BuildManifestBytes(new JsonArray())
		});
		AssertThrows(PortableConfigBundleReasonCodes.DuplicateEntry, () =>
			PortableConfigBundle.PrepareImport(caseAlias, Path.Combine(_root, "manifest-icons-b")).Dispose());

		string backslash = Path.Combine(_root, "backslash.starpie-config");
		CreateZip(backslash, new Dictionary<string, byte[]>
		{
			["manifest.json"] = BuildManifestBytes(new JsonArray()),
			["config.json"] = Encoding.UTF8.GetBytes("{}"),
			["icons\\bad.svg"] = new byte[] { 1 }
		});
		AssertThrows(PortableConfigBundleReasonCodes.EntryInvalid, () =>
			PortableConfigBundle.PrepareImport(backslash, Path.Combine(_root, "manifest-icons-c")).Dispose());
	}

	private static void DisposeBeforeCommit()
	{
		string package = Path.Combine(_root, "dispose.starpie-config");
		string targetIcons = Path.Combine(_root, "dispose-icons");
		PortableConfigBundle.Export(CreateConfig(Action("custom:icon_a"), null, null, null).ToJsonString(), package, _sourceIcons);
		var transaction = PortableConfigBundle.PrepareImport(package, targetIcons);
		Assert(Directory.Exists(targetIcons) && CountFiles(targetIcons) == 1, "prepared resource was not created");
		transaction.Dispose();
		Assert(!Directory.Exists(targetIcons) || CountFiles(targetIcons) == 0, "dispose before commit did not roll back resources");
	}

	private static void ZeroAssetDispose()
	{
		string package = Path.Combine(_root, "zero-assets.starpie-config");
		string targetIcons = Path.Combine(_root, "zero-assets-icons");
		PortableConfigBundle.Export(new JsonObject { ["Profiles"] = new JsonArray() }.ToJsonString(), package, _sourceIcons);
		var transaction = PortableConfigBundle.PrepareImport(package, targetIcons);
		Assert(Directory.Exists(targetIcons), "zero-asset import did not create its directory");
		transaction.Dispose();
		Assert(!Directory.Exists(targetIcons), "zero-asset rollback left the created directory");
	}

	private static void CommitKeepsResources()
	{
		string package = Path.Combine(_root, "commit.starpie-config");
		string targetIcons = Path.Combine(_root, "commit-icons");
		PortableConfigBundle.Export(CreateConfig(Action("custom:icon_a"), null, null, null).ToJsonString(), package, _sourceIcons);
		var transaction = PortableConfigBundle.PrepareImport(package, targetIcons);
		string key = FindFirstIconKey(transaction.ConfigJson);
		string file = Path.Combine(targetIcons, key.Substring("custom:".Length) + ".svg");
		transaction.Commit();
		transaction.Dispose();
		Assert(File.Exists(file), "committed resource was removed");
	}

	private static void PrepareFailureRollsBack()
	{
		string package = Path.Combine(_root, "rollback.starpie-config");
		string targetIcons = Path.Combine(_root, "rollback-icons");
		PortableConfigBundle.Export(CreateConfig(Action("custom:icon_a"), null, null, null).ToJsonString(), package, _sourceIcons);
#if STARPIE_CANDIDATE_MODE
		File.WriteAllText(targetIcons, "not a directory");
		AssertThrows(PortableConfigBundleReasonCodes.IoError, () =>
			PortableConfigBundle.PrepareImport(package, targetIcons).Dispose());
		Assert(File.Exists(targetIcons), "candidate-mode failure changed the target path");
#else
		IconHelper.FailInvalidation = true;
		try
		{
			AssertThrows(PortableConfigBundleReasonCodes.IoError, () =>
				PortableConfigBundle.PrepareImport(package, targetIcons).Dispose());
		}
		finally
		{
			IconHelper.FailInvalidation = false;
		}
		Assert(!Directory.Exists(targetIcons) || CountFiles(targetIcons) == 0, "prepare failure left resources behind");
#endif
	}

	private static void InlineSvgUnchanged()
	{
		string package = Path.Combine(_root, "inline.starpie-config");
		string targetIcons = Path.Combine(_root, "inline-icons");
		var action = Action("custom:icon_a");
		action["CustomIconSvg"] = "<svg><path d=\"M0 0 L1 1\"/></svg>";
		PortableConfigBundle.Export(CreateConfig(action, null, null, null).ToJsonString(), package, _sourceIcons);
		using var transaction = PortableConfigBundle.PrepareImport(package, targetIcons);
		JsonNode root = JsonNode.Parse(transaction.ConfigJson)!;
		Assert(root["Profiles"]![0]!["Actions"]![0]!["CustomIconSvg"]!.GetValue<string>() ==
			"<svg><path d=\"M0 0 L1 1\"/></svg>", "inline SVG changed");
		transaction.Commit();
	}

	private static void ConfigLimit()
	{
		string package = Path.Combine(_root, "config-limit.starpie-config");
		string huge = new string(' ', (int)PortableConfigBundle.MaxConfigBytes + 1);
		AssertThrows(PortableConfigBundleReasonCodes.ConfigTooLarge, () =>
			PortableConfigBundle.Export(huge, package, _sourceIcons));
	}

	private static void AssetSizeLimit()
	{
		string bigDir = Path.Combine(_root, "big-icons");
		Directory.CreateDirectory(bigDir);
		File.WriteAllBytes(Path.Combine(bigDir, "big.svg"), new byte[PortableConfigBundle.MaxAssetBytes + 1]);
		JsonObject config = CreateConfig(Action("custom:big"), null, null, null);
		AssertThrows(PortableConfigBundleReasonCodes.IconTooLarge, () =>
			PortableConfigBundle.Export(config.ToJsonString(), Path.Combine(_root, "big.starpie-config"), bigDir));
	}

	private static void AssetCountLimit()
	{
		byte[] svg = ValidSvgBytes();
		var assets = new JsonArray();
		var entries = new Dictionary<string, byte[]>
		{
			["manifest.json"] = Array.Empty<byte>(),
			["config.json"] = Encoding.UTF8.GetBytes("{}")
		};
		for (int index = 0; index <= PortableConfigBundle.MaxAssetCount; index++)
		{
			string entry = "icons/count_" + index.ToString("D3") + ".svg";
			assets.Add(new JsonObject
			{
				["key"] = "custom:count_" + index.ToString("D3"),
				["entry"] = entry,
				["size"] = svg.LongLength,
				["sha256"] = Hash(svg)
			});
			entries[entry] = svg;
		}
		entries["manifest.json"] = BuildManifestBytes(assets);
		string package = Path.Combine(_root, "count-limit.starpie-config");
		CreateZip(package, entries);
		AssertThrows(PortableConfigBundleReasonCodes.AssetCountLimit, () =>
			PortableConfigBundle.PrepareImport(package, Path.Combine(_root, "count-icons")).Dispose());
	}

	private static void TotalAssetLimit()
	{
		byte[] svg = new byte[checked((int)PortableConfigBundle.MaxAssetBytes)];
		string svgHash = Hash(svg);
		var assets = new JsonArray();
		var entries = new Dictionary<string, byte[]>
		{
			["config.json"] = Encoding.UTF8.GetBytes("{}")
		};
		for (int index = 0; index < 33; index++)
		{
			string entry = "icons/total_" + index.ToString("D2") + ".svg";
			assets.Add(new JsonObject
			{
				["key"] = "custom:total_" + index.ToString("D2"),
				["entry"] = entry,
				["size"] = svg.LongLength,
				["sha256"] = svgHash
			});
			if (index < 32)
			{
				entries[entry] = svg;
			}
		}
		entries["manifest.json"] = BuildManifestBytes(assets);
		string package = Path.Combine(_root, "total-limit.starpie-config");
		CreateZip(package, entries);
		AssertThrows(PortableConfigBundleReasonCodes.ResourceSizeLimit, () =>
			PortableConfigBundle.PrepareImport(package, Path.Combine(_root, "total-icons")).Dispose());
	}

	private static JsonObject CreateConfig(
		JsonObject primaryAction,
		JsonObject? centerAction,
		JsonArray? layerActions,
		JsonArray? actionPanelActions)
	{
		var profile = new JsonObject
		{
			["ProcessName"] = "Test.exe",
			["Actions"] = new JsonArray(primaryAction),
			["Layers"] = new JsonArray(new JsonObject
			{
				["Actions"] = layerActions ?? new JsonArray()
			})
		};
		if (centerAction != null)
		{
			profile["CenterAction"] = centerAction;
			((JsonObject)profile["Layers"]![0]!)["CenterAction"] = centerAction.DeepClone();
		}
		var config = new JsonObject
		{
			["Profiles"] = new JsonArray(profile),
			["ActionPanels"] = new JsonArray()
		};
		if (actionPanelActions != null)
		{
			config["ActionPanels"] = new JsonArray(new JsonObject
			{
				["Keys"] = actionPanelActions
			});
		}
		return config;
	}

	private static JsonObject Action(string iconKey, JsonObject? extras = null)
	{
		var action = new JsonObject
		{
			["Type"] = "Hotkey",
			["Name"] = "Test",
			["IconKey"] = iconKey,
			["CustomIconSvg"] = "",
			["SubActions"] = new JsonArray()
		};
		if (extras != null)
		{
			action["SubActions"] = extras["SubActions"]?.DeepClone() ?? new JsonArray();
			action["IconKey"] = extras["IconKey"]?.GetValue<string>() ?? iconKey;
		}
		return action;
	}

	private static void WriteSvg(string path, string fill)
	{
		File.WriteAllText(path,
			"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">" +
			"<path d=\"M0 0 L10 0 L10 10 Z\" fill=\"" + fill + "\"/></svg>");
	}

	private static byte[] ValidSvgBytes()
	{
		return Encoding.UTF8.GetBytes(
			"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">" +
			"<path d=\"M0 0 L10 0 L10 10 Z\" fill=\"currentColor\"/></svg>");
	}

	private static string Hash(byte[] bytes)
		=> Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

	private static byte[] BuildManifestBytes(JsonArray assets)
	{
		var manifest = new JsonObject
		{
			["format"] = PortableConfigBundle.Format,
			["version"] = PortableConfigBundle.Version,
			["config"] = "config.json",
			["assets"] = assets
		};
		return Encoding.UTF8.GetBytes(manifest.ToJsonString());
	}

	private static void CreateZip(string path, IReadOnlyDictionary<string, byte[]> entries)
	{
		using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
		using var archive = new ZipArchive(file, ZipArchiveMode.Create);
		foreach (KeyValuePair<string, byte[]> entry in entries)
		{
			ZipArchiveEntry zipEntry = archive.CreateEntry(entry.Key, CompressionLevel.Optimal);
			using Stream stream = zipEntry.Open();
			stream.Write(entry.Value, 0, entry.Value.Length);
		}
	}

	private static string FindFirstIconKey(string configJson)
	{
		JsonNode root = JsonNode.Parse(configJson)!;
		return root["Profiles"]![0]!["Actions"]![0]!["IconKey"]!.GetValue<string>();
	}

	private static bool AllKnownCustomKeysChanged(JsonNode root)
	{
		foreach (string value in EnumerateKnownIconKeys(root))
		{
			if (!value.StartsWith("custom:icon_", StringComparison.Ordinal) ||
				!value.Contains("_portable_", StringComparison.Ordinal))
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsPortableCustomKey(JsonNode? node)
	{
		return node is JsonValue value &&
			value.TryGetValue<string>(out string? text) &&
			text != null &&
			text.StartsWith("custom:icon_", StringComparison.Ordinal) &&
			text.Contains("_portable_", StringComparison.Ordinal);
	}

	private static IEnumerable<string> EnumerateKnownIconKeys(JsonNode root)
	{
		var result = new List<string>();
		void AddAction(JsonNode? node)
		{
			if (node is not JsonObject action)
			{
				return;
			}
			if (action["IconKey"] is JsonValue value && value.TryGetValue<string>(out string? text) && text != null)
			{
				result.Add(text);
			}
			if (action["SubActions"] is JsonArray subs)
			{
				foreach (JsonNode? sub in subs)
				{
					AddAction(sub);
				}
			}
		}
		void AddActions(JsonNode? node)
		{
			if (node is JsonArray actions)
			{
				foreach (JsonNode? action in actions)
				{
					AddAction(action);
				}
			}
		}
		if (root["Profiles"] is JsonArray profiles)
		{
			foreach (JsonNode? profile in profiles)
			{
				AddActions(profile?["Actions"]);
				AddAction(profile?["CenterAction"]);
				if (profile?["Layers"] is JsonArray layers)
				{
					foreach (JsonNode? layer in layers)
					{
						AddActions(layer?["Actions"]);
						AddAction(layer?["CenterAction"]);
					}
				}
			}
		}
		if (root["ActionPanels"] is JsonArray panels)
		{
			foreach (JsonNode? panel in panels)
			{
				AddActions(panel?["Keys"]);
			}
		}
		AddAction(root["CancelAction"]);
		if (root["CoreCustomIconKey"] is JsonValue coreValue &&
			coreValue.TryGetValue<string>(out string? coreText) &&
			coreText != null)
		{
			result.Add(coreText);
		}
		return result;
	}

	private static int CountFiles(string directory)
	{
		return Directory.Exists(directory)
			? Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).Count()
			: 0;
	}

	private static void AssertThrows(string reasonCode, Action action)
	{
		try
		{
			action();
		}
		catch (PortableConfigBundleException ex) when (string.Equals(ex.ReasonCode, reasonCode, StringComparison.Ordinal))
		{
			return;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Expected {reasonCode}, got {ex.GetType().Name}: {ex.Message}", ex);
		}
		throw new InvalidOperationException($"Expected {reasonCode}, but no exception was thrown.");
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}

	private static void Run(string name, Action test)
	{
		try
		{
			test();
			_passed++;
			Console.WriteLine("PASS: " + name);
		}
		catch (Exception ex)
		{
			_failed++;
			Console.WriteLine("FAIL: " + name + " -> " + ex.Message);
		}
	}

	private static bool VerifyCandidateAssembly()
	{
#if STARPIE_CANDIDATE_MODE
		string? candidatePath = Environment.GetEnvironmentVariable("STARPIE_CANDIDATE_DLL");
		if (string.IsNullOrWhiteSpace(candidatePath))
		{
			string? candidateDirectory = Environment.GetEnvironmentVariable("STARPIE_CANDIDATE_DIR");
			if (!string.IsNullOrWhiteSpace(candidateDirectory))
			{
				candidatePath = Path.Combine(candidateDirectory, "StarPie.dll");
			}
		}
		if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
		{
			Console.WriteLine("CANDIDATE_CHECK=FAIL missing STARPIE_CANDIDATE_DLL/STARPIE_CANDIDATE_DIR");
			return false;
		}

		string loadedPath = typeof(PortableConfigBundle).Assembly.Location;
		if (string.IsNullOrWhiteSpace(loadedPath) || !File.Exists(loadedPath))
		{
			Console.WriteLine("CANDIDATE_CHECK=FAIL loaded StarPie assembly has no readable location");
			return false;
		}
		string expectedHash = Hash(File.ReadAllBytes(Path.GetFullPath(candidatePath)));
		string loadedHash = Hash(File.ReadAllBytes(Path.GetFullPath(loadedPath)));
		Console.WriteLine("CANDIDATE_DLL=" + Path.GetFullPath(candidatePath));
		Console.WriteLine("LOADED_DLL=" + Path.GetFullPath(loadedPath));
		Console.WriteLine("CANDIDATE_SHA256=" + expectedHash);
		Console.WriteLine("LOADED_SHA256=" + loadedHash);
		if (!string.Equals(expectedHash, loadedHash, StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("CANDIDATE_CHECK=FAIL hash mismatch");
			return false;
		}
		Console.WriteLine("CANDIDATE_CHECK=PASS");
		return true;
#else
		Console.WriteLine("CANDIDATE_CHECK=SOURCE_LINKED");
		return true;
#endif
	}
}

