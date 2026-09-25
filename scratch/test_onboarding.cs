using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WinPieGestures;
using WinPieGestures.Plugins;

public class Program
{
    private static int _passedCount = 0;
    private static int _failedCount = 0;

    public static async Task<int> Main()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine(" StarPie Official Plugins Onboarding Test Suite ");
        Console.WriteLine("=================================================");

        string sandbox = Path.Combine(Path.GetTempPath(), "StarPie-OnboardingTest-" + Guid.NewGuid().ToString("N"));
        string hostRoot = Path.Combine(sandbox, "plugin-data");
        string scanRoot = Path.Combine(sandbox, "plugin");
        Directory.CreateDirectory(hostRoot);
        Directory.CreateDirectory(scanRoot);

        PluginPaths.OverrideRootsForTesting(hostRoot, scanRoot);
        PluginHost.HeadlessMode = false;

        try
        {
            await RunAllTestsAsync(hostRoot);
        }
        finally
        {
            try
            {
                if (Directory.Exists(sandbox))
                {
                    Directory.Delete(sandbox, recursive: true);
                }
            }
            catch
            {
            }
        }

        Console.WriteLine();
        Console.WriteLine("=================================================");
        Console.WriteLine($"Total Tests: {_passedCount + _failedCount} | Passed: {_passedCount} | Failed: {_failedCount}");
        Console.WriteLine("=================================================");

        return _failedCount == 0 ? 0 : 1;
    }

    private static void Assert(bool condition, string testName, string message = "")
    {
        if (condition)
        {
            Console.WriteLine($"[PASS] {testName}");
            _passedCount++;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {testName}: {message}");
            Console.ResetColor();
            _failedCount++;
        }
    }

    private static OfficialPluginCatalog CreateMockCatalog(IEnumerable<string>? extraCapabilities = null)
    {
        var catalog = new OfficialPluginCatalog
        {
            SchemaVersion = 1,
            CatalogVersion = "2026.09.25",
            ReleaseTag = "v2026.09.25",
            ReleaseChannel = "stable"
        };

        foreach (var info in OfficialPluginOnboarding.TargetPlugins)
        {
            var caps = new List<string>(info.DisclosedCapabilities);
            if (extraCapabilities != null && info.Id == "starpie.builtin.shelltool")
            {
                caps.AddRange(extraCapabilities);
            }

            catalog.Modules.Add(new OfficialPluginModule
            {
                Id = info.Id,
                Name = info.Id,
                Version = "1.0.0",
                ReleaseTag = "v2026.09.25",
                AssetName = info.Id + ".spkg",
                PackageUrl = "https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/v2026.09.25/" + info.Id + ".spkg",
                Sha256 = new string('a', 64),
                Size = 1024,
                Capabilities = caps
            });
        }

        return catalog;
    }

    private static void CleanRegistry()
    {
        foreach (string id in OfficialPluginOnboarding.TargetPluginIds)
        {
            PluginRegistryStore.RemoveEntry(id);
        }
    }

    private static async Task RunAllTestsAsync(string hostRoot)
    {
        // -------------------------------------------------------------
        // Test 1: First Prompt vs Non-First Prompt
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 1: First Prompt vs Non-First Prompt ---");
        {
            CleanRegistry();
            ConfigManager.CurrentConfig = new AppConfig
            {
                Plugins = new PluginsPreference
                {
                    EnablePluginSystem = true,
                    HasPromptedOfficialPluginsOnboarding = false
                }
            };

            bool shouldPrompt1 = OfficialPluginOnboarding.ShouldPrompt(out var missing1);
            Assert(shouldPrompt1 && missing1.Count == 5, "1.1 First interactive entrance should prompt with 5 missing items",
                $"shouldPrompt={shouldPrompt1}, missingCount={missing1.Count}");

            OfficialPluginOnboarding.MarkPrompted();
            Assert(ConfigManager.CurrentConfig.Plugins.HasPromptedOfficialPluginsOnboarding,
                "1.2 MarkPrompted sets HasPromptedOfficialPluginsOnboarding to true");

            bool shouldPrompt2 = OfficialPluginOnboarding.ShouldPrompt(out var missing2);
            Assert(!shouldPrompt2, "1.3 Already prompted user should not be prompted again",
                $"shouldPrompt={shouldPrompt2}");

            // Reset prompted flag, but pre-install all 5 plugins
            ConfigManager.CurrentConfig.Plugins.HasPromptedOfficialPluginsOnboarding = false;
            foreach (string id in OfficialPluginOnboarding.TargetPluginIds)
            {
                PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
                {
                    Id = id,
                    Name = id,
                    Version = "1.0.0",
                    Enabled = true,
                    Official = true
                });
            }

            bool shouldPrompt3 = OfficialPluginOnboarding.ShouldPrompt(out var missing3);
            Assert(!shouldPrompt3, "1.4 All 5 plugins already installed should not prompt",
                $"shouldPrompt={shouldPrompt3}");
            Assert(ConfigManager.CurrentConfig.Plugins.HasPromptedOfficialPluginsOnboarding,
                "1.5 All 5 plugins already installed automatically marks prompted");
        }

        // -------------------------------------------------------------
        // Test 2: Agree vs Decline (Flag Saved, Decline Installs Zero)
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 2: Agree vs Decline ---");
        {
            CleanRegistry();
            ConfigManager.CurrentConfig = new AppConfig
            {
                Plugins = new PluginsPreference
                {
                    EnablePluginSystem = true,
                    HasPromptedOfficialPluginsOnboarding = false
                }
            };

            // Decline scenario: user clicks "暂不安装"
            OfficialPluginOnboarding.MarkPrompted();
            Assert(ConfigManager.CurrentConfig.Plugins.HasPromptedOfficialPluginsOnboarding,
                "2.1 Decline marks HasPromptedOfficialPluginsOnboarding = true");
            Assert(OfficialPluginOnboarding.GetMissingPluginIds().Count == 5,
                "2.2 Decline does not install any plugins (all 5 still missing)");

            // Agree scenario: user clicks "一键安装"
            var catalog = CreateMockCatalog();
            var installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var report = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(catalog),
                moduleInstaller: (mod, _) =>
                {
                    installedIds.Add(mod.Id);
                    PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
                    {
                        Id = mod.Id,
                        Name = mod.Name,
                        Version = mod.Version,
                        Enabled = true,
                        Official = true
                    });
                    return Task.FromResult(new OfficialPluginInstallResult
                    {
                        Success = true,
                        PluginId = mod.Id,
                        Enabled = true
                    });
                }
            );

            Assert(report.AllSucceeded && report.SuccessCount == 5, "2.3 Agree installs all 5 missing plugins",
                $"successCount={report.SuccessCount}, failureCount={report.FailureCount}");
            Assert(installedIds.Count == 5, "2.4 Exactly 5 modules passed to installer");
            Assert(OfficialPluginOnboarding.GetMissingPluginIds().Count == 0,
                "2.5 Zero missing plugins after successful batch installation");
        }

        // -------------------------------------------------------------
        // Test 3: Partially Installed (Only Missing Installed, Existing Untouched)
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 3: Partially Installed ---");
        {
            CleanRegistry();
            // Install 2 plugins beforehand with custom timestamp/version
            PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
            {
                Id = "starpie.builtin.folder",
                Name = "starpie.builtin.folder",
                Version = "0.9.9-custom",
                Description = "Custom folder plugin",
                Enabled = true,
                Official = true
            });
            PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
            {
                Id = "starpie.builtin.weburl",
                Name = "starpie.builtin.weburl",
                Version = "0.9.9-custom",
                Description = "Custom weburl plugin",
                Enabled = true,
                Official = true
            });

            var missing = OfficialPluginOnboarding.GetMissingPluginIds();
            Assert(missing.Count == 3, "3.1 Exactly 3 plugins are missing", $"count={missing.Count}");
            Assert(!missing.Contains("starpie.builtin.folder") && !missing.Contains("starpie.builtin.weburl"),
                "3.2 Missing list excludes folder and weburl");

            var catalog = CreateMockCatalog();
            var newlyInstalled = new List<string>();

            var report = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(catalog),
                moduleInstaller: (mod, _) =>
                {
                    newlyInstalled.Add(mod.Id);
                    PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
                    {
                        Id = mod.Id,
                        Name = mod.Name,
                        Version = mod.Version,
                        Enabled = true,
                        Official = true
                    });
                    return Task.FromResult(new OfficialPluginInstallResult
                    {
                        Success = true,
                        PluginId = mod.Id,
                        Enabled = true
                    });
                }
            );

            Assert(report.SuccessCount == 3, "3.3 Only 3 missing plugins installed", $"successCount={report.SuccessCount}");
            Assert(report.SkippedPluginIds.Contains("starpie.builtin.folder") && report.SkippedPluginIds.Contains("starpie.builtin.weburl"),
                "3.4 Skipped list contains folder and weburl");

            // Verify pre-installed entry was NOT overwritten
            var folderEntry = PluginRegistryStore.FindEntry("starpie.builtin.folder");
            Assert(folderEntry?.Version == "0.9.9-custom" && folderEntry.Description == "Custom folder plugin",
                "3.5 Pre-installed folder plugin was not overwritten or upgraded");
        }

        // -------------------------------------------------------------
        // Test 4: User-Disabled Plugin Preserved (Not Re-Enabled)
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 4: User-Disabled Plugin Preserved ---");
        {
            CleanRegistry();
            // User previously disabled system plugin
            PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
            {
                Id = "starpie.builtin.system",
                Name = "starpie.builtin.system",
                Version = "1.0.0",
                Enabled = false,
                Official = true
            });

            Assert(OfficialPluginOnboarding.IsPluginInstalled("starpie.builtin.system"),
                "4.1 Disabled system plugin is recognized as installed");

            var missing = OfficialPluginOnboarding.GetMissingPluginIds();
            Assert(!missing.Contains("starpie.builtin.system"),
                "4.2 Disabled system plugin is excluded from missing list");

            var catalog = CreateMockCatalog();
            var report = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(catalog),
                moduleInstaller: (mod, _) =>
                {
                    PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
                    {
                        Id = mod.Id,
                        Name = mod.Name,
                        Version = mod.Version,
                        Enabled = true,
                        Official = true
                    });
                    return Task.FromResult(new OfficialPluginInstallResult
                    {
                        Success = true,
                        PluginId = mod.Id,
                        Enabled = true
                    });
                }
            );

            var systemEntry = PluginRegistryStore.FindEntry("starpie.builtin.system");
            Assert(systemEntry != null && systemEntry.Enabled == false,
                "4.3 User-disabled system plugin remains disabled after batch installation");
        }

        // -------------------------------------------------------------
        // Test 5: Offline/Network Failure & Retry Mechanism
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 5: Offline/Network Failure & Retry ---");
        {
            CleanRegistry();

            // 5.1 Complete network failure
            bool caught = false;
            try
            {
                var failReport = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                    catalogFetcher: _ => throw new HttpRequestException("Network down: connection refused"),
                    moduleInstaller: (mod, _) => Task.FromResult(new OfficialPluginInstallResult { Success = true })
                );
            }
            catch (HttpRequestException)
            {
                caught = true;
            }
            Assert(caught, "5.1 Network failure is caught and surfaced cleanly");
            Assert(OfficialPluginOnboarding.GetMissingPluginIds().Count == 5,
                "5.2 Zero plugins installed during total network failure");

            // 5.2 Partial failure: 2 succeed, 3 fail
            var catalog = CreateMockCatalog();
            var partialReport = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(catalog),
                moduleInstaller: (mod, _) =>
                {
                    if (mod.Id == "starpie.builtin.folder" || mod.Id == "starpie.builtin.weburl")
                    {
                        PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
                        {
                            Id = mod.Id,
                            Name = mod.Name,
                            Version = mod.Version,
                            Enabled = true,
                            Official = true
                        });
                        return Task.FromResult(new OfficialPluginInstallResult
                        {
                            Success = true,
                            PluginId = mod.Id,
                            Enabled = true
                        });
                    }
                    return Task.FromResult(new OfficialPluginInstallResult
                    {
                        Success = false,
                        PluginId = mod.Id,
                        Error = "Download timeout (simulated)"
                    });
                }
            );

            Assert(!partialReport.AllSucceeded && partialReport.SuccessCount == 2 && partialReport.FailureCount == 3,
                "5.3 Partial failure reports 2 successes and 3 failures",
                $"success={partialReport.SuccessCount}, fail={partialReport.FailureCount}");
            Assert(PluginRegistryStore.FindEntry("starpie.builtin.folder") != null,
                "5.4 Successful plugins are preserved and not rolled back");

            // 5.3 Retry: only installs the 3 failed ones
            var retryMissing = OfficialPluginOnboarding.GetMissingPluginIds();
            Assert(retryMissing.Count == 3, "5.5 Retry missing count is exactly 3");

            var retryReport = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(catalog),
                moduleInstaller: (mod, _) =>
                {
                    PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
                    {
                        Id = mod.Id,
                        Name = mod.Name,
                        Version = mod.Version,
                        Enabled = true,
                        Official = true
                    });
                    return Task.FromResult(new OfficialPluginInstallResult
                    {
                        Success = true,
                        PluginId = mod.Id,
                        Enabled = true
                    });
                }
            );

            Assert(retryReport.AllSucceeded && retryReport.SuccessCount == 3,
                "5.6 Retry installs remaining 3 missing plugins",
                $"success={retryReport.SuccessCount}");
            Assert(OfficialPluginOnboarding.GetMissingPluginIds().Count == 0,
                "5.7 All 5 plugins installed after retry");
        }

        // -------------------------------------------------------------
        // Test 6: Re-entry / Double-Click Prevention
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 6: Re-entry / Double-Click Prevention ---");
        {
            CleanRegistry();
            var catalog = CreateMockCatalog();

            var barrier = new TaskCompletionSource<bool>();
            var installStarted = new TaskCompletionSource<bool>();

            var longRunningTask = OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: async _ =>
                {
                    installStarted.TrySetResult(true);
                    await barrier.Task;
                    return catalog;
                },
                moduleInstaller: (mod, _) => Task.FromResult(new OfficialPluginInstallResult { Success = true, PluginId = mod.Id })
            );

            await installStarted.Task;

            // Attempt second concurrent call
            bool secondCallBlocked = false;
            try
            {
                await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                    catalogFetcher: _ => Task.FromResult(catalog),
                    moduleInstaller: (mod, _) => Task.FromResult(new OfficialPluginInstallResult { Success = true, PluginId = mod.Id })
                );
            }
            catch (InvalidOperationException ex)
            {
                secondCallBlocked = ex.Message.Contains("进行中") || ex.Message.Contains("请勿重复操作");
            }

            Assert(secondCallBlocked, "6.1 Concurrent re-entry is blocked with InvalidOperationException");

            // Complete the first task
            barrier.TrySetResult(true);
            var result1 = await longRunningTask;
            Assert(result1 != null, "6.2 First task completes successfully after releasing barrier");
        }

        // -------------------------------------------------------------
        // Test 7: Plugin System Disabled / Safe Mode Blocking
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 7: Plugin System Disabled / Safe Mode Blocking ---");
        {
            CleanRegistry();
            ConfigManager.CurrentConfig = new AppConfig
            {
                Plugins = new PluginsPreference
                {
                    EnablePluginSystem = false,
                    HasPromptedOfficialPluginsOnboarding = false
                }
            };

            bool promptDisabled = OfficialPluginOnboarding.ShouldPrompt(out _);
            Assert(!promptDisabled, "7.1 ShouldPrompt returns false when plugin system is disabled");

            ConfigManager.CurrentConfig.Plugins.EnablePluginSystem = true;

            // Set Safe Mode active via reflection on PluginHost._safeModeActive
            var safeModeField = typeof(PluginHost).GetField("_safeModeActive", BindingFlags.Static | BindingFlags.NonPublic);
            safeModeField?.SetValue(null, true);

            bool promptSafeMode = OfficialPluginOnboarding.ShouldPrompt(out _);
            Assert(!promptSafeMode, "7.2 ShouldPrompt returns false when Safe Mode is active");

            // Reset Safe Mode
            safeModeField?.SetValue(null, false);
        }

        // -------------------------------------------------------------
        // Test 8: Extra Capability Interception
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 8: Extra Capability Interception ---");
        {
            CleanRegistry();
            // ShellTool declares "RawInputHook" (outside Process / InputSimulation)
            var catalogWithExtra = CreateMockCatalog(extraCapabilities: new[] { "RawInputHook" });

            // Case 8.1: User denies extra capability
            var reportDenied = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(catalogWithExtra),
                moduleInstaller: (mod, _) => Task.FromResult(new OfficialPluginInstallResult { Success = true, PluginId = mod.Id }),
                extraCapabilityPrompter: (name, extraCaps) => Task.FromResult(false)
            );

            Assert(reportDenied.FailedPlugins.ContainsKey("starpie.builtin.shelltool"),
                "8.1 Plugin with denied extra capability is intercepted and recorded as failure",
                $"failed: {string.Join(", ", reportDenied.FailedPlugins.Keys)}");
            Assert(reportDenied.SucceededPluginIds.Count == 4,
                "8.2 Other 4 standard plugins succeed despite shelltool denial");

            // Case 8.2: User confirms extra capability
            CleanRegistry();
            var reportAllowed = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(catalogWithExtra),
                moduleInstaller: (mod, _) => Task.FromResult(new OfficialPluginInstallResult { Success = true, PluginId = mod.Id }),
                extraCapabilityPrompter: (name, extraCaps) => Task.FromResult(true)
            );

            Assert(reportAllowed.AllSucceeded && reportAllowed.SuccessCount == 5,
                "8.3 Plugin with approved extra capability installs successfully");
        }

        // -------------------------------------------------------------
        // Test 9: 4-Language I18n Completeness Verification
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 9: 4-Language I18n Completeness ---");
        {
            string[] testLanguages = { "zh-CN", "zh-TW", "en", "ja" };
            string[] requiredKeys =
            {
                "OfficialPluginNameFolder", "OfficialPluginDescFolder",
                "OfficialPluginNameWebUrl", "OfficialPluginDescWebUrl",
                "OfficialPluginNameLaunch", "OfficialPluginDescLaunch",
                "OfficialPluginNameSystem", "OfficialPluginDescSystem",
                "OfficialPluginNameShellTool", "OfficialPluginDescShellTool",
                "OfficialPluginsOnboardingTitle", "OfficialPluginsOnboardingIntro",
                "OfficialPluginsOnboardingSecurityHeader", "OfficialPluginsOnboardingSecuritySource",
                "OfficialPluginsOnboardingSecurityPerms", "OfficialPluginsOnboardingSecurityDesc",
                "OfficialPluginsOnboardingInstallBtn", "OfficialPluginsOnboardingLaterBtn",
                "OfficialPluginsOnboardingRetryBtn", "OfficialPluginsOnboardingDoneBtn",
                "OfficialPluginsOnboardingHint", "OfficialPluginsOnboardingStatusReady",
                "OfficialPluginsOnboardingStatusFetchingCatalog", "OfficialPluginsOnboardingStatusInstallingItem",
                "OfficialPluginsOnboardingAllSucceeded", "OfficialPluginsOnboardingPartialFailed",
                "OfficialPluginsOnboardingExtraCapPromptTitle", "OfficialPluginsOnboardingExtraCapPrompt",
                "OfficialPluginsStateToInstall", "PluginsOnboardingBannerTitle",
                "PluginsOnboardingBannerText", "PluginsOnboardingBannerButton"
            };

            bool allValid = true;
            string failureMsg = "";

            foreach (string lang in testLanguages)
            {
                I18n.SetLanguage(lang);
                foreach (string key in requiredKeys)
                {
                    string translation = I18n.T(key);
                    if (string.IsNullOrWhiteSpace(translation) || string.Equals(translation, key, StringComparison.Ordinal))
                    {
                        allValid = false;
                        failureMsg = $"Missing or untranslated key '{key}' for language '{lang}'";
                        break;
                    }
                }
                if (!allValid) break;
            }

            Assert(allValid, "9.1 All 32 onboarding localization keys defined across zh-CN, zh-TW, en, ja", failureMsg);

            // Restore default language
            I18n.SetLanguage("zh-CN");
        }
    }
}
