using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Pierotechnical.BuildAndUploadTool.Editor.Tests
{
    public class BuildToolEditModeTests
    {
        [Test]
        public void ParsesThreePartVersion()
        {
            int major;
            int minor;
            int patch;
            Assert.IsTrue(GameVersion.TryParse("1.2.3", out major, out minor, out patch));
            Assert.AreEqual(1, major);
            Assert.AreEqual(2, minor);
            Assert.AreEqual(3, patch);
        }

        [Test]
        public void RejectsVersionsThatAreNotMajorMinorPatch()
        {
            Assert.IsFalse(GameVersion.TryParse(null, out _, out _, out _));
            Assert.IsFalse(GameVersion.TryParse("", out _, out _, out _));
            Assert.IsFalse(GameVersion.TryParse("1.2", out _, out _, out _));
            Assert.IsFalse(GameVersion.TryParse("1.2.3.4", out _, out _, out _));
            Assert.IsFalse(GameVersion.TryParse("a.b.c", out _, out _, out _));
            Assert.IsFalse(GameVersion.TryParse("-1.0.0", out _, out _, out _));
            Assert.IsFalse(GameVersion.TryParse("1. 2.3", out _, out _, out _));
        }

        [Test]
        public void IncrementMinorResetsPatch()
        {
            string result;
            Assert.IsTrue(GameVersion.TryIncrementMinor("1.2.9", out result));
            Assert.AreEqual("1.3.0", result);
        }

        [Test]
        public void IncrementPatchKeepsMinor()
        {
            string result;
            Assert.IsTrue(GameVersion.TryIncrementPatch("1.2.9", out result));
            Assert.AreEqual("1.2.10", result);
        }

        [Test]
        public void IncrementRejectsInvalidText()
        {
            string result;
            Assert.IsFalse(GameVersion.TryIncrementMinor("nope", out result));
            Assert.IsNull(result);
            Assert.IsFalse(GameVersion.TryIncrementPatch("1.2", out result));
            Assert.IsNull(result);
        }

        [Test]
        public void SanitizeReplacesInvalidFileNameChars()
        {
            char invalid = Path.GetInvalidFileNameChars()[0];
            string sanitized = BuildPaths.SanitizePathSegment("A" + invalid + "B");
            Assert.AreEqual("A_B", sanitized);
        }

        [Test]
        public void SanitizeEmptyAndTraversalUsesFallback()
        {
            Assert.AreEqual("game", BuildPaths.SanitizePathSegment(null));
            Assert.AreEqual("game", BuildPaths.SanitizePathSegment("   "));
            Assert.AreEqual("game", BuildPaths.SanitizePathSegment(".."));
            Assert.AreEqual("game", BuildPaths.SanitizePathSegment("."));
        }

        [Test]
        public void SanitizeReservedWindowsDeviceName()
        {
            Assert.AreEqual("CON_", BuildPaths.SanitizePathSegment("CON"));
        }

        [Test]
        public void VersionedFolderUsesGameVersionAndPlatform()
        {
            string path = BuildPaths.VersionedFolder("Builds", "My Game", "1.2.3", "windows");
            Assert.AreEqual(Path.Combine("Builds", "My Game", "1.2.3", "windows"), path);
        }

        [Test]
        public void TempFolderStaysOutOfTheVersionedDirectory()
        {
            string finalPath = BuildPaths.VersionedFolder("Builds", "My Game", "1.2.3", "linux");
            string tempPath = BuildPaths.TempFolder("Builds", "My Game", "1.2.3", "linux");
            StringAssert.Contains(BuildPaths.TempFolderName, tempPath);
            Assert.IsFalse(tempPath.StartsWith(finalPath, StringComparison.Ordinal));
        }

        [Test]
        public void PlayerLocationsMatchEachPlatform()
        {
            Assert.AreEqual(Path.Combine("Out", "MyGame.exe"), BuildPaths.PlayerLocation("Out", "windows", "MyGame"));
            Assert.AreEqual(Path.Combine("Out", "MacBuild.app"), BuildPaths.PlayerLocation("Out", "mac", "MyGame"));
            Assert.AreEqual(Path.Combine("Out", "LinuxBuild.x86_64"), BuildPaths.PlayerLocation("Out", "linux", "Cool"));
            Assert.AreEqual(Path.Combine("Out", "WebGLBuild"), BuildPaths.PlayerLocation("Out", "webgl", "Cool"));
            Assert.AreEqual(Path.Combine("Out", "MyGame.exe"), BuildPaths.PlayerLocation("Out", BuildTarget.StandaloneWindows64, "MyGame"));
            Assert.AreEqual(Path.Combine("Out", "MacBuild.app"), BuildPaths.PlayerLocation("Out", BuildTarget.StandaloneOSX, "MyGame"));
            Assert.AreEqual(Path.Combine("Out", "LinuxBuild.x86_64"), BuildPaths.PlayerLocation("Out", BuildTarget.StandaloneLinux64, "Cool"));
            Assert.AreEqual(Path.Combine("Out", "WebGLBuild"), BuildPaths.PlayerLocation("Out", BuildTarget.WebGL, "Cool"));
            Assert.AreEqual(Path.Combine("Out", "MyGame.apk"), BuildPaths.PlayerLocation("Out", BuildTarget.Android, "MyGame"));
            Assert.AreEqual(Path.Combine("Out", "MyGame"), BuildPaths.PlayerLocation("Out", BuildTarget.iOS, "MyGame"));
        }

        [Test]
        public void AndroidAppBundleUsesAnAabPath()
        {
            Assert.AreEqual(Path.Combine("Out", "MyGame.apk"), BuildPaths.PlayerLocation("Out", BuildTarget.Android, "MyGame", false));
            Assert.AreEqual(Path.Combine("Out", "MyGame.aab"), BuildPaths.PlayerLocation("Out", BuildTarget.Android, "MyGame", true));
            Assert.AreEqual(Path.Combine("Out", "MyGame.exe"), BuildPaths.PlayerLocation("Out", BuildTarget.StandaloneWindows64, "MyGame", true));
        }

        [Test]
        public void SecondWindowsTargetDoesNotReuseTheWindowsFolder()
        {
            var existing = new List<BuildTargetEntry>();
            existing.Add(BuildTargetSet.Seed("windows", "Windows", BuildTarget.StandaloneWindows64, "windows", true, "1001", false, true));
            BuildTargetEntry second = BuildTargetSet.Create(BuildTarget.StandaloneWindows64, existing);
            Assert.IsFalse(string.Equals(second.FolderKey, "windows", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual((int)BuildTarget.StandaloneWindows64, second.TargetValue);
            Assert.AreNotEqual("windows", second.FolderKey);
            Assert.IsFalse(existing[0].PublishItch);
            Assert.IsTrue(existing[0].PublishSteam);
            Assert.IsTrue(second.PublishItch);
            Assert.IsFalse(second.PublishSteam);
            BuildTargetEntry web = BuildTargetSet.Seed("webgl", "WebGL", BuildTarget.WebGL, "webgl", true, "1004", true, true);
            Assert.IsTrue(web.PublishItch);
            Assert.IsFalse(web.PublishSteam);

            BuildTarget[] addable = BuildTargetSet.ListAddableTargets();
            Assert.IsFalse(Array.IndexOf(addable, BuildTarget.NoTarget) >= 0);
            Assert.IsTrue(Array.IndexOf(addable, BuildTarget.Android) >= 0);
            Assert.IsTrue(Array.IndexOf(addable, BuildTarget.StandaloneWindows64) >= 0);
        }

        [Test]
        public void ProjectTargetNormalizationRepairsIdentityAndOutputCollisions()
        {
            var targets = new List<BuildTargetEntry>
            {
                BuildTargetSet.Seed("windows", "Windows", BuildTarget.StandaloneWindows64, "windows", true, "1001", true, true),
                BuildTargetSet.Seed("windows", "Web", BuildTarget.WebGL, "html", true, "1002", true, true)
            };
            targets[1].Id = targets[0].Id;

            BuildToolProjectSettings.NormalizeTargets(targets);

            Assert.AreNotEqual(targets[0].Id, targets[1].Id);
            Assert.AreEqual("windows", targets[0].FolderKey);
            Assert.AreEqual("windows-2", targets[1].FolderKey);
            Assert.IsFalse(targets[1].PublishSteam);
        }

        [Test]
        public void ChangingBuildTargetResetsPlatformBindingsButKeepsOutputIdentity()
        {
            BuildTargetEntry entry = BuildTargetSet.Seed(
                "windows",
                "Windows",
                BuildTarget.StandaloneWindows64,
                "windows",
                true,
                "1001",
                true,
                true);

            Assert.IsTrue(BuildTargetSet.ChangeTarget(entry, BuildTarget.StandaloneOSX));
            Assert.AreEqual("windows", entry.FolderKey);
            Assert.AreEqual("Mac", entry.Name);
            Assert.AreEqual("mac", entry.Channel);
            Assert.AreEqual(string.Empty, entry.SteamDepotId);
            Assert.IsTrue(entry.PublishSteam);

            Assert.IsTrue(BuildTargetSet.ChangeTarget(entry, BuildTarget.WebGL));
            Assert.IsFalse(entry.PublishSteam);
        }

        [Test]
        public void BuildPlanSnapshotsScenesAndReportsAllPreflightProblems()
        {
            var targets = new List<BuildTargetEntry>
            {
                BuildTargetSet.Seed("same", "Windows", BuildTarget.StandaloneWindows64, "bad:channel", true, "100", true, true),
                BuildTargetSet.Seed("same", "Mac", BuildTarget.StandaloneOSX, "mac", true, "100", false, true)
            };
            var configuration = new BuildConfiguration
            {
                Targets = targets,
                SelectedIndices = new List<int> { 0, 1 },
                Upload = true,
                Version = "invalid",
                GameName = "Game/Unsafe",
                ItchOwner = "owner@invalid",
                ItchProject = "project",
                ButlerPath = "missing-butler",
                SteamUser = string.Empty,
                SteamAppId = "0",
                SteamCmdPath = "missing-steamcmd"
            };
            var environment = new BuildEnvironmentSnapshot
            {
                ProjectRoot = "Project",
                Scenes = new[] { "Assets/One.unity" },
                ActiveBuildTargetValue = (int)BuildTarget.StandaloneWindows64
            };

            BuildPlanResult result = BuildPlanFactory.Create(configuration, environment);
            Assert.IsTrue(result.HasErrors);
            Assert.IsNull(result.Plan);
            Assert.GreaterOrEqual(result.Issues.Count, 8);

            string executable = Path.GetTempFileName();
            try
            {
                targets[0].FolderKey = "windows";
                targets.RemoveAt(1);
                targets[0].Channel = "windows";
                targets[0].PublishSteam = false;
                configuration.SelectedIndices = new List<int> { 0 };
                configuration.Version = "1.2.3";
                configuration.GameName = "Game";
                configuration.ItchOwner = "owner";
                configuration.ButlerPath = executable;
                BuildPlanResult valid = BuildPlanFactory.Create(configuration, environment);
                Assert.IsFalse(valid.HasErrors);
                Assert.IsNotNull(valid.Plan);
                environment.Scenes[0] = "Assets/Changed.unity";
                Assert.AreEqual("Assets/One.unity", valid.Plan.Scenes[0]);
                Assert.AreEqual("Game", valid.Plan.GameName);
                Assert.AreEqual(1, valid.Plan.PublishJobs.Count);
            }
            finally
            {
                File.Delete(executable);
            }
        }

        [Test]
        public void BuildTargetsReorderAroundTheDraggedRow()
        {
            var items = new List<string> { "a", "b", "c", "d" };
            BuildTargetSet.Move(items, 0, 4);
            CollectionAssert.AreEqual(new[] { "b", "c", "d", "a" }, items);

            items = new List<string> { "a", "b", "c", "d" };
            BuildTargetSet.Move(items, 3, 0);
            CollectionAssert.AreEqual(new[] { "d", "a", "b", "c" }, items);

            items = new List<string> { "a", "b", "c", "d" };
            BuildTargetSet.Move(items, 1, 3);
            CollectionAssert.AreEqual(new[] { "a", "c", "b", "d" }, items);

            items = new List<string> { "a", "b", "c", "d" };
            BuildTargetSet.Move(items, 1, 2);
            CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, items);
        }

        [Test]
        public void GetRelativePathReturnsChildPath()
        {
            string root = Path.Combine(Path.GetTempPath(), "pierobuildpaths-" + Guid.NewGuid().ToString("N"));
            string nested = Path.Combine(root, "Sub", "File.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(nested));
            File.WriteAllText(nested, "x");
            try
            {
                string relative = BuildPaths.GetRelativePath(root, nested);
                Assert.AreEqual(Path.Combine("Sub", "File.txt"), relative);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ManagedPathsRejectSiblingPrefixEscapes()
        {
            string root = Path.Combine(Path.GetTempPath(), "pierobuild-root");
            string full;
            string error;
            Assert.IsTrue(SafeFileSystem.TryGetContainedPath(
                root,
                Path.Combine(root, "child", "file.txt"),
                out full,
                out error));
            Assert.IsNull(error);
            Assert.IsFalse(SafeFileSystem.TryGetContainedPath(
                root,
                root + "-other" + Path.DirectorySeparatorChar + "file.txt",
                out full,
                out error));
            StringAssert.Contains("escaped", error);
        }

        [Test]
        public void ArtifactPromotionKeepsAndRecoversThePreviousBuild()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "pierobuild-promotion-" + Guid.NewGuid().ToString("N"));
            string temporary = Path.Combine(root, ".in-progress", "queue", "game");
            string final = Path.Combine(root, "Game", "1.0.0", "windows");
            Directory.CreateDirectory(temporary);
            Directory.CreateDirectory(final);
            File.WriteAllText(Path.Combine(temporary, "new.txt"), "new");
            File.WriteAllText(Path.Combine(final, "old.txt"), "old");
            try
            {
                string error;
                Assert.IsTrue(SafeFileSystem.TryPromote(
                    root,
                    temporary,
                    final,
                    "queue",
                    out error), error);
                Assert.IsTrue(File.Exists(Path.Combine(final, "new.txt")));
                Assert.IsTrue(File.Exists(Path.Combine(final + ".previous", "old.txt")));

                string replacement = final + ".replacing-queue";
                Directory.Move(final, replacement);
                Assert.IsTrue(SafeFileSystem.TryRecoverPromotion(
                    root,
                    final,
                    replacement,
                    final + ".previous",
                    final + ".promotion",
                    out error), error);
                Assert.IsTrue(File.Exists(Path.Combine(final, "new.txt")));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Test]
        public void ProcessOutputRedactsCredentialLikeValues()
        {
            string output = ProcessOutput.Redact(
                "password=hunter2 access_token: abc123 Steam Guard code: 456789 ordinary=value");
            StringAssert.DoesNotContain("hunter2", output);
            StringAssert.DoesNotContain("abc123", output);
            StringAssert.DoesNotContain("456789", output);
            StringAssert.Contains("ordinary=value", output);
        }

        [Test]
        public void ExcludesDoNotShipFoldersBySuffix()
        {
            Assert.IsTrue(PublishExclusions.IsExcluded("Game_BackUpThisFolder_ButDontShipItWithYourGame/foo.dll"));
            Assert.IsTrue(PublishExclusions.IsExcluded(@"Something_BurstDebugInformation_DoNotShip\bar"));
            Assert.IsTrue(PublishExclusions.IsExcluded("foo_DoNotShip/a.txt"));
            Assert.IsFalse(PublishExclusions.IsExcluded("Game_Data/Managed/Assembly.dll"));
            Assert.IsFalse(PublishExclusions.IsExcluded("DoNotShipNotes/readme.txt"));
            Assert.IsFalse(PublishExclusions.IsExcluded(null));
        }

        [Test]
        public void ItchSlugIsLowercaseWithHyphens()
        {
            Assert.AreEqual(string.Empty, BuildPaths.ToItchSlug(null));
            Assert.AreEqual("my-studio", BuildPaths.ToItchSlug(" My Studio "));
            Assert.AreEqual("https://my-studio.itch.io/cool-game", BuildPaths.ItchPageUrl("My Studio", "Cool Game"));
            Assert.AreEqual(string.Empty, BuildPaths.ItchPageUrl("", "Cool Game"));
            Assert.AreEqual(string.Empty, BuildPaths.ItchPageUrl("studio@evil.example", "Cool Game"));
            Assert.IsFalse(ButlerCommand.IsValidChannel("windows:beta"));
            Assert.IsTrue(ButlerCommand.IsValidChannel("windows-beta"));
        }

        [Test]
        public void PushArgumentsQuoteTheFolderTargetAndVersion()
        {
            string args = ButlerCommand.PushArguments(
                "Builds/My Game",
                "My Studio",
                "Cool Game",
                "windows",
                "1.2.3");

            StringAssert.StartsWith(
                "push \"Builds/My Game\" \"my-studio/cool-game:windows\" --userversion \"1.2.3\"",
                args);
            StringAssert.Contains("--ignore \"*_DoNotShip*\"", args);
            StringAssert.Contains("--ignore \"*BackUpThisFolder_ButDontShipItWithYourGame*\"", args);
            StringAssert.Contains("--ignore \"*BurstDebugInformation_DoNotShip*\"", args);
        }

        [Test]
        public void QuoteEscapesEmbeddedQuotesAndTrailingSlashes()
        {
            Assert.AreEqual("\"say \\\"hi\\\"\"", ButlerCommand.Quote("say \"hi\""));
            Assert.AreEqual("\"C:\\\\\"", ButlerCommand.Quote("C:\\"));
            Assert.AreEqual("\"a\\\\\\\"b\"", ButlerCommand.Quote("a\\\"b"));
            Assert.AreEqual("\"C:\\\\\\\\\"", ButlerCommand.Quote("C:\\\\"));
        }

        [Test]
        public void StatusArgumentsIncludeTheItchTarget()
        {
            Assert.AreEqual("status \"my-studio/cool-game\"", ButlerCommand.StatusArguments("My Studio", "Cool Game"));
            Assert.AreEqual("status \"my-studio/cool-game\" --json", ButlerCommand.StatusJsonArguments("My Studio", "Cool Game"));
            Assert.AreEqual("login", ButlerCommand.LoginArguments());
        }

        [Test]
        public void ItchChannelMenuKeepsATypedChannel()
        {
            string output = "{\"type\":\"log\",\"message\":\"listing channels\"}\n"
                + "{\"type\":\"result\",\"value\":{\"target\":\"my-studio/cool-game\",\"channels\":["
                + "{\"name\":\"windows\",\"head\":{\"userVersion\":\"0.1.0\",\"state\":\"completed\"}},"
                + "{\"name\":\"html\"}]}}";
            List<string> channels = ButlerCommand.ParseStatusChannels(output);
            Assert.AreEqual(2, channels.Count);
            Assert.AreEqual("windows", channels[0]);
            Assert.AreEqual("html", channels[1]);
            Assert.AreEqual("custom", ButlerCommand.ChooseChannel(" custom ", channels));
            Assert.AreEqual(string.Empty, ButlerCommand.ChooseChannel("  ", channels));

            var only = new List<string>();
            only.Add("windows");
            Assert.AreEqual("windows", ButlerCommand.ChooseChannel(string.Empty, only));
            Assert.AreEqual("html", ButlerCommand.ChooseChannel("html", only));
            Assert.AreEqual(0, ButlerCommand.ParseStatusChannels("{\"type\":\"log\",\"message\":\"name is windows\"}").Count);
        }

        [Test]
        public void LookupIdentityChangesWithEveryExternalInput()
        {
            string itch = CatalogLookupKey.Itch("C:/tools/butler.exe", "studio", "game");
            Assert.AreNotEqual(itch, CatalogLookupKey.Itch("D:/tools/butler.exe", "studio", "game"));
            Assert.AreNotEqual(itch, CatalogLookupKey.Itch("C:/tools/butler.exe", "other", "game"));
            Assert.AreNotEqual(itch, CatalogLookupKey.Itch("C:/tools/butler.exe", "studio", "other"));

            string steam = CatalogLookupKey.Steam("C:/tools/steamcmd.exe", "account", "1000");
            Assert.AreNotEqual(steam, CatalogLookupKey.Steam("D:/tools/steamcmd.exe", "account", "1000"));
            Assert.AreNotEqual(steam, CatalogLookupKey.Steam("C:/tools/steamcmd.exe", "other", "1000"));
            Assert.AreNotEqual(steam, CatalogLookupKey.Steam("C:/tools/steamcmd.exe", "account", "2000"));
            Assert.AreEqual(
                steam,
                CatalogLookupKey.Steam("C:/tools/steamcmd.exe", "ACCOUNT", "01000"));
        }

        [Test]
        public void QueueTerminalPhasesAreExplicit()
        {
            Assert.IsFalse(BuildQueueController.IsTerminal(BuildQueuePhase.Preparing));
            Assert.IsFalse(BuildQueueController.IsTerminal(BuildQueuePhase.Finalizing));
            Assert.IsTrue(BuildQueueController.IsTerminal(BuildQueuePhase.Completed));
            Assert.IsTrue(BuildQueueController.IsTerminal(BuildQueuePhase.Cancelled));
            Assert.IsTrue(BuildQueueController.IsTerminal(BuildQueuePhase.Failed));
        }

        [Test]
        public void QueueRejectsCallbacksFromAnOldOperationOrRun()
        {
            var state = new BuildQueueState
            {
                Id = "current-queue",
                ActiveOperationId = "current-operation"
            };

            Assert.IsTrue(BuildQueueController.MatchesOperation(
                state,
                "current-queue",
                "current-operation"));
            Assert.IsFalse(BuildQueueController.MatchesOperation(
                state,
                "old-queue",
                "current-operation"));
            Assert.IsFalse(BuildQueueController.MatchesOperation(
                state,
                "current-queue",
                "old-operation"));
            Assert.IsFalse(BuildQueueController.MatchesOperation(null, "current-queue", "current-operation"));
        }

        [Test]
        public void MissingStatusTargetIsNotALoginFailure()
        {
            string output = "butler: error: required argument 'target' not provided\n\nusage: butler status [<flags>] <target>";
            Assert.IsFalse(ButlerCommand.IsLoginFailure(output));
            Assert.AreEqual(output, ButlerCommand.DescribeStatusFailure(output));
        }

        [Test]
        public void MissingCredentialsAreALoginFailure()
        {
            string output = "No saved credentials. Run `butler login` first.";
            Assert.IsTrue(ButlerCommand.IsLoginFailure(output));
            StringAssert.StartsWith(ButlerCommand.LoginRequiredMessage, ButlerCommand.DescribeStatusFailure(output));
        }

        [Test]
        public void UploadRunsOnlyAfterASuccessfulBuild()
        {
            Assert.IsFalse(ButlerCommand.ShouldUpload(true, false));
            Assert.IsFalse(ButlerCommand.ShouldUpload(false, true));
            Assert.IsFalse(ButlerCommand.ShouldUpload(false, false));
            Assert.IsTrue(ButlerCommand.ShouldUpload(true, true));
        }

        [Test]
        public void SteamIdsArePositiveIntegers()
        {
            string id;
            Assert.IsTrue(SteamCommand.TryParseSteamId("1000", out id));
            Assert.AreEqual("1000", id);
            Assert.IsTrue(SteamCommand.TryParseSteamId(" 4294967295 ", out id));
            Assert.AreEqual("4294967295", id);
            Assert.IsFalse(SteamCommand.TryParseSteamId("0", out id));
            Assert.IsFalse(SteamCommand.TryParseSteamId("4294967296", out id));
            Assert.IsFalse(SteamCommand.TryParseSteamId("12abc", out id));
            Assert.IsFalse(SteamCommand.TryParseSteamId(null, out id));
        }

        [Test]
        public void SteamUploadArgumentsQuoteTheUserAndScript()
        {
            Assert.AreEqual("+login \"studio\" +quit", SteamCommand.LoginCheckArguments("studio"));
            Assert.AreEqual("+login \"studio\"", SteamCommand.InteractiveLoginArguments("studio"));
            Assert.AreEqual(
                "+login \"studio\" +run_app_build \"C:/Builds/My Game/app.vdf\" +quit",
                SteamCommand.UploadArguments("studio", "C:/Builds/My Game/app.vdf"));
        }

        [Test]
        public void SteamGuardPromptIsALoginFailure()
        {
            string output = "This account is protected by a Steam Guard code.";
            Assert.IsTrue(SteamCommand.IsLoginFailure(output));
            StringAssert.StartsWith(SteamCommand.LoginRequiredMessage, SteamCommand.DescribeLoginFailure(output));
            Assert.IsTrue(SteamCommand.IsLoginFailure("Please enter your password"));
            Assert.IsTrue(SteamCommand.IsLoginFailure("Two-factor code required"));
            Assert.IsFalse(SteamCommand.IsLoginFailure("ERROR! Failed to install app (No subscription)"));
        }

        [Test]
        public void SteamDepotsOmitWebGl()
        {
            var plan = new BuildPlan
            {
                Targets = new List<BuildTargetPlan>
                {
                    SteamTarget("windows", "Windows", BuildTarget.StandaloneWindows64),
                    SteamTarget("webgl", "WebGL", BuildTarget.WebGL)
                }
            };
            var payload = new SteamPublishPayload
            {
                Targets = new List<SteamTargetPayload>
                {
                    new SteamTargetPayload { TargetIndex = 0, DepotId = "1001" }
                }
            };
            var completed = new List<TargetResult>
            {
                new TargetResult
                {
                    BuildSucceeded = true,
                    ArtifactPath = BuildPaths.VersionedFolder("Builds", "Game", "0.1.0", "windows")
                },
                new TargetResult { BuildSucceeded = false }
            };

            List<SteamDepotUpload> depots = SteamCommand.SelectDepots(payload, plan, completed);
            Assert.AreEqual(1, depots.Count);
            Assert.AreEqual("windows", depots[0].PlatformId);
            Assert.AreEqual("1001", depots[0].DepotId);
            Assert.AreEqual("depot_windows.vdf", depots[0].ScriptName);
            Assert.AreEqual(BuildPaths.VersionedFolder("Builds", "Game", "0.1.0", "windows"), depots[0].ContentRoot);
            Assert.IsFalse(PlatformCatalog.SupportsSteamPlatform("webgl"));
            Assert.IsTrue(PlatformCatalog.SupportsSteamPlatform("linux"));
            Assert.IsTrue(SteamCommand.EverySteamTargetSucceeded(payload, completed));
            Assert.AreEqual("beta", SteamCommand.ResolveSetLive(" beta ", true));
        }

        [Test]
        public void DuplicateSteamDepotsAreRejectedBeforeTheQueueStarts()
        {
            string steamcmd = Path.GetTempFileName();
            try
            {
                var targets = new List<BuildTargetEntry>();
                targets.Add(BuildTargetSet.Seed("windows", "Windows", BuildTarget.StandaloneWindows64, "windows", true, "100", false, true));
                targets.Add(BuildTargetSet.Seed("mac", "Mac", BuildTarget.StandaloneOSX, "mac", true, "100", false, true));
                BuildPlanResult result = BuildPlanFactory.Create(
                    new BuildConfiguration
                    {
                        Targets = targets,
                        SelectedIndices = new List<int> { 0, 1 },
                        Upload = true,
                        Version = "1.0.0",
                        GameName = "Game",
                        SteamUser = "steamuser",
                        SteamAppId = "10",
                        SteamCmdPath = steamcmd
                    },
                    new BuildEnvironmentSnapshot
                    {
                        ProjectRoot = "Project",
                        Scenes = new[] { "Assets/Scene.unity" },
                        ActiveBuildTargetValue = (int)BuildTarget.StandaloneWindows64
                    });

                Assert.IsTrue(result.HasErrors);
                Assert.IsNull(result.Plan);
                Assert.IsTrue(HasIssue(result.Issues, "Depot 100 is used by more than one platform."));
            }
            finally
            {
                File.Delete(steamcmd);
            }
        }

        [Test]
        public void SteamSetLiveIsWithheldWhenASteamPlatformFailed()
        {
            var plan = new BuildPlan
            {
                Targets = new List<BuildTargetPlan>
                {
                    SteamTarget("windows", "Windows", BuildTarget.StandaloneWindows64),
                    SteamTarget("mac", "Mac", BuildTarget.StandaloneOSX)
                }
            };
            var payload = new SteamPublishPayload
            {
                Targets = new List<SteamTargetPayload>
                {
                    new SteamTargetPayload { TargetIndex = 0, DepotId = "1001" },
                    new SteamTargetPayload { TargetIndex = 1, DepotId = "1002" }
                }
            };
            var completed = new List<TargetResult>
            {
                new TargetResult { BuildSucceeded = true, ArtifactPath = "Builds/windows" },
                new TargetResult { BuildSucceeded = false }
            };

            List<SteamDepotUpload> depots = SteamCommand.SelectDepots(payload, plan, completed);
            Assert.AreEqual(1, depots.Count);
            Assert.AreEqual("windows", depots[0].PlatformId);
            Assert.IsFalse(SteamCommand.EverySteamTargetSucceeded(payload, completed));
            Assert.AreEqual(string.Empty, SteamCommand.ResolveSetLive("beta", false));
            Assert.AreEqual(string.Empty, SteamCommand.ResolveSetLive("  ", true));
        }

        [Test]
        public void SteamScriptsDescribeTheAppAndExcludeDoNotShipFolders()
        {
            var depots = new List<SteamDepotUpload>
            {
                new SteamDepotUpload
                {
                    PlatformId = "windows",
                    Label = "Windows",
                    DepotId = "1001",
                    ContentRoot = "D:\\Builds\\Game\\0.1.0\\windows",
                    ScriptName = "depot_windows.vdf"
                },
                new SteamDepotUpload
                {
                    PlatformId = "mac",
                    Label = "Mac",
                    DepotId = "1002",
                    ContentRoot = "D:/Builds/Game/0.1.0/mac",
                    ScriptName = "depot_mac.vdf"
                }
            };

            string app = SteamCommand.BuildAppScript("1000", SteamCommand.DescribeBuild("0.1.0", depots), "D:\\Builds\\output", "beta", depots);
            StringAssert.Contains("\"appid\" \"1000\"", app);
            StringAssert.Contains("\"desc\" \"0.1.0 (Windows, Mac)\"", app);
            StringAssert.Contains("\"buildoutput\" \"D:/Builds/output\"", app);
            StringAssert.Contains("\"setlive\" \"beta\"", app);
            StringAssert.Contains("\"1001\" \"depot_windows.vdf\"", app);
            StringAssert.Contains("\"1002\" \"depot_mac.vdf\"", app);
            StringAssert.DoesNotContain("webgl", app);

            string blank = SteamCommand.BuildAppScript("1000", "0.1.0 (Windows)", "output", string.Empty, depots);
            StringAssert.Contains("\"setlive\" \"\"", blank);

            string depot = SteamCommand.BuildDepotScript(depots[0]);
            StringAssert.Contains("\"DepotID\" \"1001\"", depot);
            StringAssert.Contains("\"ContentRoot\" \"D:/Builds/Game/0.1.0/windows\"", depot);
            StringAssert.Contains("\"LocalPath\" \"*\"", depot);
            StringAssert.Contains("\"DepotPath\" \".\"", depot);
            StringAssert.Contains("\"recursive\" \"1\"", depot);
            StringAssert.Contains("\"FileExclusion\" \"*_DoNotShip*\"", depot);
            StringAssert.Contains("\"FileExclusion\" \"*BackUpThisFolder_ButDontShipItWithYourGame*\"", depot);
            StringAssert.Contains("\"FileExclusion\" \"*BurstDebugInformation_DoNotShip*\"", depot);
            StringAssert.Contains("\"FileExclusion\" \"steam_appid.txt\"", depot);
        }

        [Test]
        public void SteamCacheFolderStaysOutsideTheQueueFolder()
        {
            string cache = BuildPaths.SteamCacheFolder("Builds", "1000");
            string scripts = BuildPaths.SteamWorkFolder("Builds", "abc123");
            Assert.AreEqual(Path.Combine("Builds", BuildPaths.SteamCacheFolderName, "1000"), cache);
            StringAssert.Contains(BuildPaths.TempFolderName, scripts);
            Assert.IsFalse(cache.StartsWith(scripts, StringComparison.Ordinal));
            Assert.IsFalse(cache.Contains(BuildPaths.TempFolderName));
        }

        [Test]
        public void SteamDefaultBranchIsNotSetLive()
        {
            Assert.AreEqual(string.Empty, SteamCommand.ResolveSetLive("default", true));
            Assert.AreEqual(string.Empty, SteamCommand.ResolveSetLive(" Default ", true));
            Assert.AreEqual("beta", SteamCommand.ResolveSetLive("beta", true));
            string notice = SteamCommand.DescribeUnsetLive("default", true, "1000");
            StringAssert.Contains(SteamCommand.DefaultBranchMessage, notice);
            StringAssert.Contains("https://partner.steamgames.com/apps/builds/1000", notice);
            Assert.AreEqual(SteamCommand.SetLiveWithheldMessage, SteamCommand.DescribeUnsetLive("beta", false, "1000"));
            Assert.AreEqual(string.Empty, SteamCommand.DescribeUnsetLive("  ", true, "1000"));
        }

        [Test]
        public void SteamBuildIdIsReadFromTheSuccessLine()
        {
            string buildId;
            Assert.IsTrue(SteamCommand.TryParseBuildId(
                "Successfully finished AppID 1000 build (BuildID 1234567).",
                out buildId));
            Assert.AreEqual("1234567", buildId);
            Assert.IsFalse(SteamCommand.TryParseBuildId("Successfully finished build preview.", out buildId));
            Assert.IsTrue(SteamCommand.HasUploadSuccess(
                "Successfully finished AppID 1000 build (BuildID 1234567).",
                out buildId));
            Assert.IsFalse(SteamCommand.HasUploadSuccess(
                "Previous BuildID 1234567 was found before an error.",
                out buildId));
        }

        [Test]
        public void SteamDepotCatalogListsEveryDepotAndBranch()
        {
            string info = "\"1000\" { \"depots\" { \"1001\" { \"config\" { \"oslist\" \"windows\" } } \"1002\" { \"config\" { \"oslist\" \"macos\" } } \"1003\" { \"config\" { \"oslist\" \"linux\" } } \"1004\" { \"config\" { \"oslist\" \"windows,macos\" } } \"branches\" { \"public\" { \"buildid\" \"5\" } \"beta\" { \"buildid\" \"4\" } } } }";
            SteamDepotLookup lookup = SteamCommand.ParseAppDepots(info);
            Assert.IsTrue(lookup.FoundDepots);
            Assert.AreEqual(4, lookup.Depots.Count);
            Assert.AreEqual("1001", lookup.Depots[0].Id);
            Assert.AreEqual("windows", lookup.Depots[0].PlatformId);
            Assert.AreEqual("1001 \u2014 Windows", lookup.Depots[0].MenuLabel);
            Assert.AreEqual("1002 \u2014 Mac", lookup.Depots[1].MenuLabel);
            Assert.AreEqual("1003 \u2014 Linux", lookup.Depots[2].MenuLabel);
            Assert.IsNull(lookup.Depots[3].PlatformId);
            Assert.AreEqual("1004 \u2014 All OSes", lookup.Depots[3].MenuLabel);
            Assert.AreEqual(2, lookup.Branches.Count);
            Assert.AreEqual("public", lookup.Branches[0]);
            Assert.AreEqual("beta", lookup.Branches[1]);

            SteamDepotLookup empty = SteamCommand.ParseAppDepots("\"common\" { \"name\" \"Game\" }");
            Assert.IsFalse(empty.FoundDepots);
            Assert.AreEqual(SteamCommand.NoDepotsMessage, empty.Message);
        }

        [Test]
        public void SteamSavedDepotIsNotOverwrittenWhenSeveralMatch()
        {
            string info = "\"depots\" { \"1001\" { \"config\" { \"oslist\" \"windows\" } } \"1005\" { \"config\" { \"oslist\" \"windows\" } } \"1006\" { } }";
            SteamDepotLookup lookup = SteamCommand.ParseAppDepots(info);
            Assert.AreEqual(3, lookup.Depots.Count);
            Assert.AreEqual("1006 \u2014 All OSes", lookup.Depots[2].MenuLabel);
            Assert.AreEqual(string.Empty, SteamCommand.ChooseDepotId(string.Empty, "windows", lookup.Depots));
            Assert.AreEqual("9999", SteamCommand.ChooseDepotId("9999", "windows", lookup.Depots));
            Assert.AreEqual("1005", SteamCommand.ChooseDepotId(" 1005 ", "windows", lookup.Depots));

            var onlyWindows = new List<SteamCatalogDepot>();
            onlyWindows.Add(lookup.Depots[0]);
            Assert.AreEqual("1001", SteamCommand.ChooseDepotId(string.Empty, "windows", onlyWindows));
            Assert.AreEqual("4242", SteamCommand.ChooseDepotId("4242", "windows", onlyWindows));
        }

        [Test]
        public void SteamSavedLoginRequiresConnectCache()
        {
            string saved = "\"InstallConfigStore\" { \"Accounts\" { \"zealen_2\" { \"SteamID\" \"1\" } } \"ConnectCache\" { \"a51386c01\" \"token\" } }";
            Assert.IsTrue(SteamCommand.HasCachedAccount(saved, "Zealen_2"));
            Assert.IsFalse(SteamCommand.HasCachedAccount(saved, "someone_else"));
            string accountsOnly = "\"InstallConfigStore\" { \"Accounts\" { \"zealen_2\" { \"SteamID\" \"1\" } } }";
            Assert.IsFalse(SteamCommand.HasCachedAccount(accountsOnly, "zealen_2"));
            string cacheOnly = "\"InstallConfigStore\" { \"ConnectCache\" { \"a51386c01\" \"token\" } }";
            Assert.IsFalse(SteamCommand.HasCachedAccount(cacheOnly, "zealen_2"));
            Assert.IsTrue(SteamCommand.IsLoginFailure("Cached credentials not found.\npassword:"));
        }

        [Test]
        public void SteamAppInfoArgumentsDoNotIncludeAPassword()
        {
            Assert.AreEqual(
                "+login \"studio\" +app_info_print 1000 +quit",
                SteamCommand.AppInfoArguments("studio", "1000"));
            Assert.IsFalse(SteamCommand.AppInfoArguments("studio", "1000").Contains("password"));
        }

        static BuildTargetPlan SteamTarget(
            string platformId,
            string label,
            BuildTarget target)
        {
            return new BuildTargetPlan
            {
                Id = platformId,
                OutputKey = platformId,
                Label = label,
                TargetValue = (int)target
            };
        }

        static bool HasIssue(IList<ValidationIssue> issues, string message)
        {
            if (issues == null)
                return false;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i] != null && issues[i].Message == message)
                    return true;
            }

            return false;
        }
    }
}
