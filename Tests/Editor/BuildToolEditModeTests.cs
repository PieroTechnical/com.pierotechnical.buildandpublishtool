using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

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

            Assert.AreEqual(
                "push \"Builds/My Game\" \"my-studio/cool-game:windows\" --userversion \"1.2.3\"",
                args);
        }

        [Test]
        public void QuoteEscapesEmbeddedQuotesAndTrailingSlashes()
        {
            Assert.AreEqual("\"say \\\"hi\\\"\"", ButlerCommand.Quote("say \"hi\""));
            Assert.AreEqual("\"C:\\\\\"", ButlerCommand.Quote("C:\\"));
        }

        [Test]
        public void StatusArgumentsIncludeTheItchTarget()
        {
            Assert.AreEqual("status \"my-studio/cool-game\"", ButlerCommand.StatusArguments("My Studio", "Cool Game"));
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
            var pending = new List<BuildRequest>
            {
                SteamRequest("windows", "Windows", "1001"),
                SteamRequest("webgl", "WebGL", "1004")
            };
            var completed = new List<TargetResult>
            {
                new TargetResult { BuildSucceeded = true },
                new TargetResult { BuildSucceeded = false }
            };

            List<SteamDepotUpload> depots = SteamCommand.SelectDepots(pending, completed);
            Assert.AreEqual(1, depots.Count);
            Assert.AreEqual("windows", depots[0].PlatformId);
            Assert.AreEqual("1001", depots[0].DepotId);
            Assert.AreEqual("depot_windows.vdf", depots[0].ScriptName);
            Assert.AreEqual(BuildPaths.VersionedFolder("Builds", "Game", "0.1.0", "windows"), depots[0].ContentRoot);
            Assert.IsFalse(PlatformCatalog.SupportsSteamPlatform("webgl"));
            Assert.IsTrue(PlatformCatalog.SupportsSteamPlatform("linux"));
            Assert.IsTrue(SteamCommand.EverySteamTargetSucceeded(pending, completed));
            Assert.AreEqual("beta", SteamCommand.ResolveSetLive(" beta ", true));
        }

        [Test]
        public void SteamSetLiveIsWithheldWhenASteamPlatformFailed()
        {
            var pending = new List<BuildRequest>
            {
                SteamRequest("windows", "Windows", "1001"),
                SteamRequest("mac", "Mac", "1002")
            };
            var completed = new List<TargetResult>
            {
                new TargetResult { BuildSucceeded = true },
                new TargetResult { BuildSucceeded = false }
            };

            List<SteamDepotUpload> depots = SteamCommand.SelectDepots(pending, completed);
            Assert.AreEqual(1, depots.Count);
            Assert.AreEqual("windows", depots[0].PlatformId);
            Assert.IsFalse(SteamCommand.EverySteamTargetSucceeded(pending, completed));
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

        static BuildRequest SteamRequest(string platformId, string label, string depotId)
        {
            return new BuildRequest
            {
                PublishSteam = true,
                PlatformId = platformId,
                Label = label,
                SteamDepotId = depotId,
                ItchGame = "Game",
                Version = "0.1.0",
                OutputRoot = "Builds"
            };
        }
    }
}
