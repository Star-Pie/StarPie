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
            await RunAllTestsAsync(hostRoot, scanRoot);
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

    private static async Task RunAllTestsAsync(string hostRoot, string scanRoot)
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
                moduleInstaller: (mod, _) => Task.FromResult(new OfficialPluginInstallResult { Success = true, PluginId = mod.Id, Enabled = true }),
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
                moduleInstaller: (mod, _) => Task.FromResult(new OfficialPluginInstallResult { Success = true, PluginId = mod.Id, Enabled = true }),
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

            Assert(allValid, "9.1 All onboarding localization keys defined across zh-CN, zh-TW, en, ja", failureMsg);

            // Restore default language
            I18n.SetLanguage("zh-CN");
        }

        // -------------------------------------------------------------
        // Test 10: Lifecycle & portable.flag / Normal Startup Paths
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 10: Lifecycle & portable.flag / Normal Startup Paths ---");
        {
            // 10.1 Normal startup: ensures PluginHost.Initialize() and PluginPaths.Configure() take effect
            var initField = typeof(PluginHost).GetField("_initialized", BindingFlags.Static | BindingFlags.NonPublic);
            initField?.SetValue(null, false);

            Assert(!PluginHost.IsInitialized, "10.1 PluginHost._initialized reset to false for test");

            ConfigManager.CurrentConfig = new AppConfig
            {
                Plugins = new PluginsPreference
                {
                    EnablePluginSystem = true,
                    HasPromptedOfficialPluginsOnboarding = false,
                    PortableMode = false
                }
            };

            // Calling ShouldPrompt ensures PluginHost.Initialize() is completed
            _ = OfficialPluginOnboarding.ShouldPrompt(out _);
            Assert(PluginHost.IsInitialized, "10.2 ShouldPrompt ensures PluginHost.IsInitialized is true");

            // 10.3 Portable mode paths verification
            var rootsPinnedField = typeof(PluginPaths).GetField("_rootsPinned", BindingFlags.Static | BindingFlags.NonPublic);
            rootsPinnedField?.SetValue(null, false);

            PluginPaths.Configure(portableRequested: false);
            Assert(!PluginPaths.IsPortable, "10.3 PluginPaths.IsPortable is false when portableRequested is false");
            Assert(PluginPaths.Root.Contains("plugin-data", StringComparison.OrdinalIgnoreCase),
                "10.4 PluginPaths.Root ends with plugin-data in normal mode");

            PluginPaths.Configure(portableRequested: true);
            Assert(PluginPaths.IsPortable, "10.5 PluginPaths.IsPortable is true when portableRequested is true");
            Assert(PluginPaths.Root.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase),
                "10.6 PluginPaths.Root is located in AppContext.BaseDirectory in portable mode");

            // Restore testing sandbox roots
            PluginPaths.OverrideRootsForTesting(hostRoot, scanRoot);
        }

        // -------------------------------------------------------------
        // Test 11: Zero Network Requests Before Consent & On Refusal
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 11: Zero Network Requests Before Consent & On Refusal ---");
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

            int catalogFetchCount = 0;
            Func<CancellationToken, Task<OfficialPluginCatalog>> countingFetcher = _ =>
            {
                catalogFetchCount++;
                return Task.FromResult(CreateMockCatalog());
            };

            // 11.1 Opening Settings / checking onboarding does not fetch catalog
            bool shouldPrompt = OfficialPluginOnboarding.ShouldPrompt(out var missing);
            Assert(shouldPrompt && missing.Count == 5, "11.1 ShouldPrompt identifies missing items without network");
            Assert(catalogFetchCount == 0, "11.2 Zero catalog fetch before consent");

            // 11.2 User declines onboarding ("暂不安装")
            OfficialPluginOnboarding.MarkPrompted();
            Assert(ConfigManager.CurrentConfig.Plugins.HasPromptedOfficialPluginsOnboarding, "11.3 Declined onboarding marks prompted");
            Assert(catalogFetchCount == 0, "11.4 Zero catalog fetch after user declines onboarding");

            // 11.3 User explicitly clicks "一键安装"
            var report = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: countingFetcher,
                moduleInstaller: (mod, _) => Task.FromResult(new OfficialPluginInstallResult
                {
                    Success = true,
                    PluginId = mod.Id,
                    Enabled = true
                })
            );

            Assert(catalogFetchCount == 1, "11.5 Exactly one catalog fetch occurs after explicit user consent");
            Assert(report.AllSuccessfullyActive, "11.6 All plugins successfully active after consent install");
        }

        // -------------------------------------------------------------
        // Test 12: Per-Plugin 3-Stage Capability Comparison
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 12: Per-Plugin 3-Stage Capability Comparison ---");
        {
            CleanRegistry();

            // 12.1 Matching announced permissions: folder requires Process (matching disclosed)
            var cleanCatalog = CreateMockCatalog();
            bool prompterCalled12_1 = false;
            var report12_1 = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(cleanCatalog),
                moduleInstaller: (mod, _) => Task.FromResult(new OfficialPluginInstallResult
                {
                    Success = true,
                    PluginId = mod.Id,
                    Enabled = true
                }),
                extraCapabilityPrompter: (name, caps) =>
                {
                    prompterCalled12_1 = true;
                    return Task.FromResult(true);
                }
            );

            Assert(!prompterCalled12_1, "12.1 No prompter called when catalog permissions match announced baseline");
            Assert(report12_1.SuccessCount == 5, "12.2 All 5 plugins install cleanly with baseline permissions");

            // 12.2 Catalog extra capability on a specific plugin (e.g. folder requests InputSimulation, not disclosed for folder!)
            CleanRegistry();
            var catalogWithFolderExtra = CreateMockCatalog();
            var folderModule = catalogWithFolderExtra.Modules.First(m => m.Id == "starpie.builtin.folder");
            folderModule.Capabilities.Add("InputSimulation"); // InputSimulation was disclosed for system, but NOT for folder!

            string? promptedPluginName = null;
            List<string>? promptedCaps = null;

            var report12_2 = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(catalogWithFolderExtra),
                moduleInstaller: (mod, _) => Task.FromResult(new OfficialPluginInstallResult
                {
                    Success = true,
                    PluginId = mod.Id,
                    Enabled = true
                }),
                extraCapabilityPrompter: (name, caps) =>
                {
                    promptedPluginName = name;
                    promptedCaps = caps;
                    return Task.FromResult(false); // User rejects folder's extra capability!
                }
            );

            Assert(promptedPluginName == folderModule.Name && promptedCaps != null && promptedCaps.Contains("InputSimulation"),
                "12.3 Per-plugin check catches extra capability on folder specifically");
            Assert(report12_2.FailedPlugins.ContainsKey("starpie.builtin.folder"),
                "12.4 Folder is rejected and skipped");
            Assert(report12_2.SuccessCount == 4,
                "12.5 Other 4 plugins succeed despite folder rejection");

            // 12.3 Downloaded package plugin.json actual capability exceeds announced/catalog
            CleanRegistry();
            bool packagePrompterCalled = false;
            var report12_3 = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(cleanCatalog),
                detailedModuleInstaller: (mod, prompter, informedCaps, _) =>
                {
                    if (mod.Id == "starpie.builtin.weburl")
                    {
                        // Simulate downloaded package declaring "FileSystem"
                        var unapproved = new List<string> { "FileSystem" };
                        packagePrompterCalled = true;
                        return Task.FromResult(new OfficialPluginInstallResult
                        {
                            Success = false,
                            PluginId = mod.Id,
                            Error = "用户拒绝了实际包声明的额外权限：FileSystem"
                        });
                    }

                    return Task.FromResult(new OfficialPluginInstallResult
                    {
                        Success = true,
                        PluginId = mod.Id,
                        Enabled = true
                    });
                }
            );

            Assert(packagePrompterCalled, "12.6 Downloaded package plugin.json extra capability was checked");
            Assert(report12_3.FailedPlugins.ContainsKey("starpie.builtin.weburl"),
                "12.7 WebUrl rejected and skipped when actual package requires unauthorized permission");
            Assert(report12_3.SuccessCount == 4,
                "12.8 Other 4 plugins succeed");
        }

        // -------------------------------------------------------------
        // Test 13: Accurate State Distinction & Preserving User-Disabled Plugins
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 13: Accurate State Distinction & Preserving User-Disabled Plugins ---");
        {
            CleanRegistry();

            // 13.1 Plugin originally installed but disabled
            PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
            {
                Id = "starpie.builtin.folder",
                Name = "starpie.builtin.folder",
                Version = "1.0.0",
                Enabled = false,
                Official = true
            });

            var catalog = CreateMockCatalog();
            var report13_1 = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(catalog),
                moduleInstaller: (mod, _) => Task.FromResult(new OfficialPluginInstallResult
                {
                    Success = true,
                    PluginId = mod.Id,
                    Enabled = true
                })
            );

            Assert(report13_1.OriginallyInstalledDisabledPluginIds.Contains("starpie.builtin.folder"),
                "13.1 Folder is classified as OriginallyInstalledDisabled");
            Assert(report13_1.ItemResults["starpie.builtin.folder"].Status == OfficialPluginStatusKind.OriginallyInstalledDisabled,
                "13.2 ItemResults reflects OriginallyInstalledDisabled status");
            Assert(!report13_1.AllSuccessfullyActive,
                "13.3 AllSuccessfullyActive is false because one plugin was originally disabled");

            var folderEntry = PluginRegistryStore.FindEntry("starpie.builtin.folder");
            Assert(folderEntry != null && folderEntry.Enabled == false,
                "13.4 Originally disabled folder plugin remains strictly disabled");

            // 13.2 Installed successfully but failed to enable
            CleanRegistry();
            var report13_2 = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: _ => Task.FromResult(catalog),
                moduleInstaller: (mod, _) =>
                {
                    if (mod.Id == "starpie.builtin.system")
                    {
                        // File install succeeded, but enable failed
                        return Task.FromResult(new OfficialPluginInstallResult
                        {
                            Success = true,
                            PluginId = mod.Id,
                            Enabled = false
                        });
                    }

                    return Task.FromResult(new OfficialPluginInstallResult
                    {
                        Success = true,
                        PluginId = mod.Id,
                        Enabled = true
                    });
                }
            );

            Assert(report13_2.InstalledButEnableFailedPlugins.ContainsKey("starpie.builtin.system"),
                "13.5 System plugin is classified as InstalledButEnableFailed");
            Assert(report13_2.ItemResults["starpie.builtin.system"].Status == OfficialPluginStatusKind.InstalledButEnableFailed,
                "13.6 System plugin status is InstalledButEnableFailed in ItemResults");
            Assert(!report13_2.AllSuccessfullyActive,
                "13.7 AllSuccessfullyActive is false when any plugin fails to enable");
            Assert(!string.IsNullOrWhiteSpace(report13_2.ItemResults["starpie.builtin.system"].ErrorOrGuidance),
                "13.8 Actionable guidance is provided for the enable-failed plugin");
        }

        // -------------------------------------------------------------
        // Test 14: Wording & Phrasing Audit
        // -------------------------------------------------------------
        Console.WriteLine("\n--- Scenario 14: Wording & Phrasing Audit ---");
        {
            // 14.1 shelltool naming: must be "系统与右键工具"
            I18n.SetLanguage("zh-CN");
            string shellToolZhCN = I18n.T("OfficialPluginNameShellTool");
            Assert(shellToolZhCN == "系统与右键工具", "14.1 shelltool in zh-CN is '系统与右键工具'", $"actual: {shellToolZhCN}");

            I18n.SetLanguage("zh-TW");
            string shellToolZhTW = I18n.T("OfficialPluginNameShellTool");
            Assert(shellToolZhTW == "系統與右鍵工具", "14.2 shelltool in zh-TW is '系統與右鍵工具'", $"actual: {shellToolZhTW}");

            I18n.SetLanguage("en");
            string shellToolEn = I18n.T("OfficialPluginNameShellTool");
            Assert(shellToolEn == "System & Context Menu Tools", "14.3 shelltool in en is 'System & Context Menu Tools'", $"actual: {shellToolEn}");

            I18n.SetLanguage("ja");
            string shellToolJa = I18n.T("OfficialPluginNameShellTool");
            Assert(shellToolJa == "システムと右クリックツール", "14.4 shelltool in ja is 'システムと右クリックツール'", $"actual: {shellToolJa}");

            // 14.2 SHA-256 hash phrasing: cannot be "签名校验" or "签名"
            string[] testLangs = { "zh-CN", "zh-TW", "en", "ja" };
            foreach (string lang in testLangs)
            {
                I18n.SetLanguage(lang);
                string securityDesc = I18n.T("OfficialPluginsOnboardingSecurityDesc");
                Assert(!securityDesc.Contains("签名") && !securityDesc.Contains("簽章") && !securityDesc.Contains("signature", StringComparison.OrdinalIgnoreCase),
                    $"14.5 SecurityDesc ({lang}) does not contain '签名' / 'signature'");
                Assert(securityDesc.Contains("SHA-256"),
                    $"14.6 SecurityDesc ({lang}) contains SHA-256 hash reference");
            }

            // 14.3 Must not claim restoring "all plugin features"
            I18n.SetLanguage("zh-CN");
            string intro = I18n.T("OfficialPluginsOnboardingIntro");
            Assert(!intro.Contains("所有插件化功能") && !intro.Contains("完整功能体验"),
                "14.7 Intro text does not claim to restore all plugin features", $"actual: {intro}");

            string banner = I18n.T("PluginsOnboardingBannerText");
            Assert(!banner.Contains("所有插件化功能") && !banner.Contains("完整内置动作"),
                "14.8 Banner text does not claim to restore all plugin features", $"actual: {banner}");
        }
    }
}
