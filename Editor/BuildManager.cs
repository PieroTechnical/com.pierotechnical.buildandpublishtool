using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    public class BuildManager : EditorWindow
    {
        string version = "0.1.0";
        string committedVersion = "0.1.0";
        string itchUser = string.Empty;
        string itchGame = string.Empty;
        bool publishItch = true;
        bool publishSteam;
        string steamUser = string.Empty;
        string steamAppId = string.Empty;
        string steamBranch = string.Empty;
        string[] steamDepots = new string[0];
        bool[] enabledPlatforms = new bool[0];
        bool versionFieldFocused;
        bool commitVersionScheduled;
        [NonSerialized] List<SteamCatalogDepot> steamCatalog = new List<SteamCatalogDepot>();
        [NonSerialized] List<string> steamBranches = new List<string>();
        [NonSerialized] string fetchedAppId;
        [NonSerialized] string lookupMessage;
        [NonSerialized] bool lookupInProgress;
        [NonSerialized] bool appIdFieldFocused;
        [NonSerialized] bool steamUserFieldFocused;
        [NonSerialized] bool lookupScheduled;

        [MenuItem("Tools/Build Automation and Publish Tool %#u")]
        public static void ShowWindow()
        {
            GetWindow<BuildManager>("Build Automation and Publish Tool");
        }

        void OnEnable()
        {
            LoadSettings();
        }

        void LoadSettings()
        {
            if (steamCatalog == null)
                steamCatalog = new List<SteamCatalogDepot>();
            if (steamBranches == null)
                steamBranches = new List<string>();
            itchUser = ToolSettings.LoadItchUser();
            itchGame = ToolSettings.LoadItchGame();
            publishItch = ToolSettings.LoadPublishItch();
            publishSteam = ToolSettings.LoadPublishSteam();
            steamUser = ToolSettings.LoadSteamUser();
            steamAppId = ToolSettings.LoadSteamAppId();
            steamBranch = ToolSettings.LoadSteamBranch();

            string loadedVersion;
            bool createVersionFile = false;
            bool hasFile = ToolSettings.TryReadVersionFile(out loadedVersion);
            if (hasFile)
            {
                version = loadedVersion;
                committedVersion = GameVersion.TryParse(loadedVersion, out _, out _, out _)
                    ? loadedVersion
                    : "0.1.0";
            }
            else if (GameVersion.TryParse(PlayerSettings.bundleVersion, out _, out _, out _))
            {
                version = PlayerSettings.bundleVersion;
                committedVersion = PlayerSettings.bundleVersion;
                createVersionFile = true;
            }
            else
            {
                version = "0.1.0";
                committedVersion = "0.1.0";
                createVersionFile = true;
            }

            if (createVersionFile)
            {
                string initialVersion = committedVersion;
                EditorApplication.delayCall += () =>
                {
                    if (this == null || File.Exists(ToolSettings.VersionFilePath))
                        return;

                    ToolSettings.TryCommitVersion(initialVersion, out _);
                };
            }

            PlatformCatalog.Entry[] platforms = PlatformCatalog.All();
            enabledPlatforms = new bool[platforms.Length];
            steamDepots = new string[platforms.Length];
            for (int i = 0; i < platforms.Length; i++)
            {
                enabledPlatforms[i] = ToolSettings.LoadEnabled(platforms[i].Id, false);
                steamDepots[i] = ToolSettings.LoadSteamDepot(platforms[i].Id);
            }

            EditorApplication.delayCall += () =>
            {
                if (this == null)
                    return;

                ScheduleDepotLookup();
            };
        }

        void OnGUI()
        {
            DrawTitle();
            DrawVersion();
            DrawIdentity();
            DrawDestinations();
            DrawButler();
            DrawSteam();
            DrawPlatforms();
            DrawActions();

            if (BuildQueue.HasPending())
                EditorGUILayout.HelpBox("A build is in progress and will resume automatically.", MessageType.Info);

            if (!GameVersion.TryParse(version, out _, out _, out _))
                EditorGUILayout.HelpBox("Version must be major.minor.patch using non-negative integers.", MessageType.Warning);
        }

        void DrawTitle()
        {
            GUILayout.Label("Build Automation and Publish Tool (PieroTechnical)", EditorStyles.largeLabel);
        }

        void DrawVersion()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Project Options", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Version", GUILayout.Width(100));
            GUI.SetNextControlName("VersionField");
            version = EditorGUILayout.TextField(version);

            if (Event.current.type == EventType.Repaint)
            {
                bool focused = GUI.GetNameOfFocusedControl() == "VersionField";
                if (versionFieldFocused && !focused && !commitVersionScheduled)
                {
                    commitVersionScheduled = true;
                    string draft = version;
                    EditorApplication.delayCall += () => CommitVersionDraft(draft);
                }

                versionFieldFocused = focused;
            }

            if (GUILayout.Button("Increment Minor", GUILayout.Width(120)))
                Increment(true);
            if (GUILayout.Button("Increment Patch", GUILayout.Width(120)))
                Increment(false);
            EditorGUILayout.EndHorizontal();
        }

        void Increment(bool minor)
        {
            string basis = GameVersion.TryParse(version, out _, out _, out _) ? version : committedVersion;
            string next = null;
            bool parsed = minor
                ? GameVersion.TryIncrementMinor(basis, out next)
                : GameVersion.TryIncrementPatch(basis, out next);

            string error = null;
            if (!parsed || !ToolSettings.TryCommitVersion(next, out error))
            {
                EditorUtility.DisplayDialog("Version", error ?? "Version must be major.minor.patch using non-negative integers.", "OK");
                return;
            }

            version = next;
            committedVersion = next;
            GUI.FocusControl(null);
            Repaint();
        }

        void CommitVersionDraft(string draft)
        {
            commitVersionScheduled = false;
            if (this == null)
                return;
            if (version != draft || draft == committedVersion)
                return;

            string error;
            if (!ToolSettings.TryCommitVersion(draft, out error))
            {
                Debug.LogError(error);
                version = committedVersion;
                Repaint();
                return;
            }

            version = draft.Trim();
            int major;
            int minor;
            int patch;
            if (GameVersion.TryParse(version, out major, out minor, out patch))
                version = major + "." + minor + "." + patch;

            committedVersion = version;
            Repaint();
        }

        void DrawIdentity()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Itch username", GUILayout.Width(100));
            itchUser = EditorGUILayout.TextField(itchUser ?? string.Empty);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
                ToolSettings.SaveItchUser(itchUser);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Game Title", GUILayout.Width(100));
            itchGame = EditorGUILayout.TextField(itchGame ?? string.Empty);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
                ToolSettings.SaveItchGame(itchGame);

            string slugUser = BuildPaths.ToItchSlug(itchUser);
            string slugGame = BuildPaths.ToItchSlug(itchGame);
            string target = string.IsNullOrEmpty(slugUser) || string.IsNullOrEmpty(slugGame)
                ? "(enter a username and game title)"
                : slugUser + "/" + slugGame;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Itch target", GUILayout.Width(100));
            EditorGUILayout.LabelField(target);
            EditorGUILayout.EndHorizontal();

            string url = BuildPaths.ItchPageUrl(itchUser, itchGame);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Game URL", GUILayout.Width(100));
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(url));
            if (GUILayout.Button(string.IsNullOrEmpty(url) ? "Game URL" : url, EditorStyles.linkLabel))
                Application.OpenURL(url);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        void DrawDestinations()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            bool nextItch = EditorGUILayout.ToggleLeft("itch.io", publishItch, GUILayout.Width(80));
            bool nextSteam = EditorGUILayout.ToggleLeft("Steam", publishSteam, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
            if (nextItch != publishItch)
            {
                publishItch = nextItch;
                ToolSettings.SavePublishItch(publishItch);
            }

            if (nextSteam != publishSteam)
            {
                publishSteam = nextSteam;
                ToolSettings.SavePublishSteam(publishSteam);
            }

            if (!publishItch && !publishSteam)
                EditorGUILayout.HelpBox("Select itch.io or Steam to upload. Build still runs locally.", MessageType.Info);
        }

        void DrawButler()
        {
            EditorGUILayout.Space();
            string butlerPath = ButlerUploader.GetButlerPath();
            bool butlerMissing = string.IsNullOrEmpty(butlerPath) || !File.Exists(butlerPath);
            EditorGUILayout.LabelField("Butler", butlerMissing ? "Not set" : butlerPath);
            if (publishItch && butlerMissing)
                EditorGUILayout.HelpBox("Locate the Butler executable before uploading to itch.io.", MessageType.Warning);

            if (GUILayout.Button("Locate Butler", GUILayout.Width(140)))
                ButlerUploader.PromptForPath();
        }

        void DrawSteam()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Steam", EditorStyles.boldLabel);
            string steamcmdPath = SteamUploader.GetSteamCmdPath();
            bool steamcmdMissing = string.IsNullOrEmpty(steamcmdPath) || !File.Exists(steamcmdPath);
            EditorGUILayout.LabelField("steamcmd", steamcmdMissing ? "Not set" : steamcmdPath);
            if (publishSteam && steamcmdMissing)
                EditorGUILayout.HelpBox("Locate steamcmd before uploading to Steam.", MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Locate steamcmd", GUILayout.Width(140)))
            {
                SteamUploader.PromptForPath();
                ScheduleDepotLookup();
            }
            if (GUILayout.Button("Login with steamcmd", GUILayout.Width(160)))
                LoginWithSteamCmd();
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Steam username", GUILayout.Width(110));
            GUI.SetNextControlName("SteamUser");
            steamUser = EditorGUILayout.TextField(steamUser ?? string.Empty);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
                ToolSettings.SaveSteamUser(steamUser);
            if (Event.current.type == EventType.Repaint)
            {
                bool userFocused = GUI.GetNameOfFocusedControl() == "SteamUser";
                if (steamUserFieldFocused && !userFocused)
                    ScheduleDepotLookup();
                steamUserFieldFocused = userFocused;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("App ID", GUILayout.Width(110));
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName("SteamAppId");
            steamAppId = EditorGUILayout.TextField(steamAppId ?? string.Empty);
            bool appIdChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndHorizontal();
            if (appIdChanged)
                ToolSettings.SaveSteamAppId(steamAppId);
            if (Event.current.type == EventType.Repaint)
            {
                bool focused = GUI.GetNameOfFocusedControl() == "SteamAppId";
                if (appIdFieldFocused && !focused)
                    ScheduleDepotLookup();
                appIdFieldFocused = focused;
            }

            if (!string.IsNullOrEmpty(lookupMessage))
                EditorGUILayout.HelpBox(lookupMessage, lookupInProgress ? MessageType.Info : MessageType.Warning);

            EnsureSteamDepots();
            PlatformCatalog.Entry[] platforms = PlatformCatalog.All();
            for (int i = 0; i < platforms.Length; i++)
            {
                if (!platforms[i].SupportsSteam)
                    continue;

                int platformIndex = i;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(platforms[i].Label + " depot", GUILayout.Width(110));
                steamDepots[i] = EditorGUILayout.TextField(steamDepots[i] ?? string.Empty);
                EditorGUI.BeginDisabledGroup(steamCatalog == null || steamCatalog.Count == 0);
                if (GUILayout.Button("\u25BC", GUILayout.Width(24)))
                    ShowDepotMenu(platformIndex);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
                if (EditorGUI.EndChangeCheck())
                    ToolSettings.SaveSteamDepot(platforms[i].Id, steamDepots[i]);
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Branch", GUILayout.Width(110));
            steamBranch = EditorGUILayout.TextField(steamBranch ?? string.Empty);
            EditorGUI.BeginDisabledGroup(steamBranches == null || steamBranches.Count == 0);
            if (GUILayout.Button("\u25BC", GUILayout.Width(24)))
                ShowBranchMenu();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
                ToolSettings.SaveSteamBranch(steamBranch);

            if (SteamCommand.IsDefaultBranch(steamBranch))
                EditorGUILayout.HelpBox("Steam cannot set the default branch live from steamcmd. Set that build live on the App Admin builds page.", MessageType.Warning);
            else if (!string.IsNullOrWhiteSpace(steamBranch))
                EditorGUILayout.HelpBox("Setting this branch live publishes only the depots in that upload. A live Steam build contains only those depots.", MessageType.Warning);

            EditorGUILayout.LabelField("Leave the branch blank to upload without setting the build live.");
            EditorGUILayout.HelpBox("Steam keeps a chunk cache in Builds/.steam-cache. Removing that folder is safe and makes the next upload of the same App ID a complete upload.", MessageType.Info);
        }

        void ScheduleDepotLookup()
        {
            if (lookupScheduled || lookupInProgress)
                return;

            string id;
            if (!SteamCommand.TryParseSteamId(steamAppId, out id))
            {
                fetchedAppId = null;
                steamCatalog = new List<SteamCatalogDepot>();
                steamBranches = new List<string>();
                lookupMessage = null;
                return;
            }

            if (id == fetchedAppId)
                return;

            string steamcmdPath = SteamUploader.GetSteamCmdPath();
            if (string.IsNullOrEmpty(steamcmdPath) || !File.Exists(steamcmdPath))
            {
                lookupMessage = "Locate steamcmd before looking up depots.";
                return;
            }

            if (string.IsNullOrWhiteSpace(steamUser))
            {
                lookupMessage = "Enter a Steam username before looking up depots.";
                return;
            }

            lookupScheduled = true;
            string user = steamUser.Trim();
            string appId = id;
            string path = steamcmdPath;
            EditorApplication.delayCall += () => RunDepotLookup(path, user, appId);
        }

        void RunDepotLookup(string path, string user, string appId)
        {
            lookupScheduled = false;
            if (this == null)
                return;

            string current;
            if (!SteamCommand.TryParseSteamId(steamAppId, out current) || current != appId)
            {
                ScheduleDepotLookup();
                return;
            }

            lookupInProgress = true;
            lookupMessage = "Looking up depots\u2026";
            Repaint();

            SteamDepotQuery query = SteamUploader.LookupDepots(path, user, appId);
            lookupInProgress = false;

            if (!SteamCommand.TryParseSteamId(steamAppId, out current) || current != appId)
            {
                ScheduleDepotLookup();
                Repaint();
                return;
            }

            if (query.Cancelled)
            {
                lookupMessage = string.IsNullOrEmpty(query.Error) ? "Depot lookup was cancelled." : query.Error;
                Repaint();
                return;
            }

            if (!query.Ok && query.Error == SteamCommand.MissingCacheMessage)
            {
                lookupMessage = query.Error;
                Repaint();
                return;
            }

            fetchedAppId = appId;
            if (!query.Ok || query.Depots == null || !query.Depots.FoundDepots)
            {
                steamCatalog = new List<SteamCatalogDepot>();
                steamBranches = new List<string>();
                lookupMessage = query.Error;
                if (string.IsNullOrEmpty(lookupMessage) && query.Depots != null)
                    lookupMessage = query.Depots.Message;
                if (string.IsNullOrEmpty(lookupMessage))
                    lookupMessage = SteamCommand.NoDepotsMessage;
                Repaint();
                return;
            }

            steamCatalog = query.Depots.Depots ?? new List<SteamCatalogDepot>();
            steamBranches = query.Depots.Branches ?? new List<string>();
            ApplySuggestedDepots();
            lookupMessage = null;
            Repaint();
        }

        void ApplySuggestedDepots()
        {
            EnsureSteamDepots();
            PlatformCatalog.Entry[] platforms = PlatformCatalog.All();
            for (int i = 0; i < platforms.Length; i++)
            {
                if (!platforms[i].SupportsSteam)
                    continue;

                string chosen = SteamCommand.ChooseDepotId(steamDepots[i], platforms[i].Id, steamCatalog);
                string current = steamDepots[i] == null ? string.Empty : steamDepots[i].Trim();
                if (chosen == current)
                    continue;

                steamDepots[i] = chosen;
                ToolSettings.SaveSteamDepot(platforms[i].Id, chosen);
            }
        }

        void ShowDepotMenu(int platformIndex)
        {
            if (steamCatalog == null || steamCatalog.Count == 0)
                return;

            var menu = new GenericMenu();
            for (int i = 0; i < steamCatalog.Count; i++)
            {
                SteamCatalogDepot depot = steamCatalog[i];
                if (depot == null || string.IsNullOrEmpty(depot.Id))
                    continue;

                string id = depot.Id;
                string label = string.IsNullOrEmpty(depot.MenuLabel) ? id : depot.MenuLabel;
                int index = platformIndex;
                bool selected = index < steamDepots.Length && steamDepots[index] == id;
                menu.AddItem(new GUIContent(label), selected, () => AssignDepot(index, id));
            }

            menu.ShowAsContext();
        }

        void AssignDepot(int index, string depotId)
        {
            EnsureSteamDepots();
            if (index < 0 || index >= steamDepots.Length)
                return;

            steamDepots[index] = depotId;
            PlatformCatalog.Entry[] platforms = PlatformCatalog.All();
            if (index < platforms.Length)
                ToolSettings.SaveSteamDepot(platforms[index].Id, depotId);
            Repaint();
        }

        void ShowBranchMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(" "), string.IsNullOrEmpty(steamBranch), () => AssignBranch(string.Empty));
            if (steamBranches != null)
            {
                for (int i = 0; i < steamBranches.Count; i++)
                {
                    string name = steamBranches[i];
                    if (string.IsNullOrEmpty(name))
                        continue;

                    string branch = name;
                    menu.AddItem(new GUIContent(branch), steamBranch == branch, () => AssignBranch(branch));
                }
            }

            menu.ShowAsContext();
        }

        void AssignBranch(string branch)
        {
            steamBranch = branch ?? string.Empty;
            ToolSettings.SaveSteamBranch(steamBranch);
            Repaint();
        }

        void LoginWithSteamCmd()
        {
            string steamcmdPath = SteamUploader.GetSteamCmdPath();
            if (string.IsNullOrEmpty(steamcmdPath) || !File.Exists(steamcmdPath))
            {
                EditorUtility.DisplayDialog("Steam", "Locate steamcmd before logging in.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(steamUser))
            {
                EditorUtility.DisplayDialog("Steam", "Enter a Steam username before logging in.", "OK");
                return;
            }

            try
            {
                SteamUploader.OpenLogin(steamcmdPath, steamUser.Trim());
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Steam", "Could not start steamcmd: " + exception.Message, "OK");
            }
        }

        void EnsureSteamDepots()
        {
            PlatformCatalog.Entry[] platforms = PlatformCatalog.All();
            if (steamDepots != null && steamDepots.Length == platforms.Length)
                return;

            steamDepots = new string[platforms.Length];
            for (int i = 0; i < platforms.Length; i++)
                steamDepots[i] = ToolSettings.LoadSteamDepot(platforms[i].Id);
        }

        void DrawPlatforms()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Build Options", EditorStyles.boldLabel);

            PlatformCatalog.Entry[] platforms = PlatformCatalog.All();
            if (enabledPlatforms.Length != platforms.Length)
                enabledPlatforms = new bool[platforms.Length];

            bool queueRunning = BuildQueue.HasPending();
            for (int i = 0; i < platforms.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                bool next = EditorGUILayout.Toggle(enabledPlatforms[i], GUILayout.Width(20));
                if (next != enabledPlatforms[i])
                {
                    enabledPlatforms[i] = next;
                    ToolSettings.SaveEnabled(platforms[i].Id, next);
                }

                EditorGUILayout.LabelField(platforms[i].Label, GUILayout.Width(70));
                EditorGUI.BeginDisabledGroup(queueRunning);
                if (GUILayout.Button("Build", GUILayout.Width(110)))
                    StartQueue(new[] { i }, false);
                if (GUILayout.Button("Build and Upload", GUILayout.Width(140)))
                    StartQueue(new[] { i }, true);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawActions()
        {
            EditorGUILayout.Space();
            EditorGUI.BeginDisabledGroup(BuildQueue.HasPending());
            if (GUILayout.Button("Build Selected Platforms"))
                StartSelected(false);
            if (GUILayout.Button("Build and Upload Selected Platforms"))
                StartSelected(true);
            EditorGUI.EndDisabledGroup();
        }

        void StartSelected(bool upload)
        {
            var indices = new List<int>();
            for (int i = 0; i < enabledPlatforms.Length; i++)
            {
                if (enabledPlatforms[i])
                    indices.Add(i);
            }

            if (indices.Count == 0)
            {
                EditorUtility.DisplayDialog("Build", "Select at least one platform.", "OK");
                return;
            }

            StartQueue(indices, upload);
        }

        void StartQueue(IList<int> indices, bool upload)
        {
            if (BuildQueue.HasPending())
                return;

            if (!GameVersion.TryParse(version, out _, out _, out _))
            {
                EditorUtility.DisplayDialog("Build", "Version must be major.minor.patch using non-negative integers.", "OK");
                return;
            }

            bool publishToItch = upload && publishItch;
            bool publishToSteam = upload && publishSteam;
            if (upload && !publishToItch && !publishToSteam)
            {
                EditorUtility.DisplayDialog("Build", "Select itch.io or Steam before uploading.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(itchGame))
            {
                EditorUtility.DisplayDialog("Build", "Enter a game title.", "OK");
                return;
            }

            if (publishToItch)
            {
                if (string.IsNullOrWhiteSpace(itchUser))
                {
                    EditorUtility.DisplayDialog("Build", "Enter an itch username and game title.", "OK");
                    return;
                }

                if (string.IsNullOrEmpty(BuildPaths.ToItchSlug(itchUser)) || string.IsNullOrEmpty(BuildPaths.ToItchSlug(itchGame)))
                {
                    EditorUtility.DisplayDialog("Build", "Username and game title must contain a usable itch slug.", "OK");
                    return;
                }
            }

            string canonicalAppId = null;
            if (publishToSteam)
            {
                string steamError = ValidateSteam(indices, out canonicalAppId);
                if (!string.IsNullOrEmpty(steamError))
                {
                    EditorUtility.DisplayDialog("Build", steamError, "OK");
                    return;
                }
            }

            if (ProjectBuilder.GetEnabledScenes().Length == 0)
            {
                EditorUtility.DisplayDialog("Build", "Enable at least one scene in Build Settings.", "OK");
                return;
            }

            ToolSettings.SaveItchUser(itchUser);
            ToolSettings.SaveItchGame(itchGame);
            ToolSettings.SaveSteamUser(steamUser);
            ToolSettings.SaveSteamAppId(steamAppId);
            ToolSettings.SaveSteamBranch(steamBranch);

            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                EditorUtility.DisplayDialog("Build", "Could not resolve the project folder.", "OK");
                return;
            }

            string outputRoot = Path.Combine(projectRoot.FullName, "Builds");
            PlatformCatalog.Entry[] platforms = PlatformCatalog.All();
            EnsureSteamDepots();
            string steamcmdPath = SteamUploader.GetSteamCmdPath();
            string branch = steamBranch == null ? string.Empty : steamBranch.Trim();
            var requests = new List<BuildRequest>();
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index < 0 || index >= platforms.Length)
                    continue;

                PlatformCatalog.Entry platform = platforms[index];
                string depotId = string.Empty;
                if (publishToSteam && platform.SupportsSteam)
                    SteamCommand.TryParseSteamId(steamDepots[index], out depotId);

                requests.Add(new BuildRequest
                {
                    Version = version.Trim(),
                    ItchUser = itchUser == null ? string.Empty : itchUser.Trim(),
                    ItchGame = itchGame.Trim(),
                    PlatformId = platform.Id,
                    ChannelName = platform.Channel,
                    Label = platform.Label,
                    OutputRoot = outputRoot,
                    Upload = upload,
                    PublishItch = publishToItch,
                    PublishSteam = publishToSteam,
                    SteamAppId = canonicalAppId ?? string.Empty,
                    SteamDepotId = depotId,
                    SteamBranch = branch,
                    SteamUser = steamUser == null ? string.Empty : steamUser.Trim(),
                    SteamCmdPath = steamcmdPath ?? string.Empty
                });
            }

            if (requests.Count == 0)
            {
                EditorUtility.DisplayDialog("Build", "Select at least one platform.", "OK");
                return;
            }

            BuildQueue.Start(requests, null);
        }

        string ValidateSteam(IList<int> indices, out string canonicalAppId)
        {
            canonicalAppId = null;
            string steamcmdPath = SteamUploader.GetSteamCmdPath();
            if (string.IsNullOrEmpty(steamcmdPath) || !File.Exists(steamcmdPath))
                return "Locate steamcmd before uploading to Steam.";

            if (string.IsNullOrWhiteSpace(steamUser))
                return "Enter a Steam username.";

            if (!SteamCommand.TryParseSteamId(steamAppId, out canonicalAppId))
                return "Steam App ID must be a positive integer.";

            EnsureSteamDepots();
            PlatformCatalog.Entry[] platforms = PlatformCatalog.All();
            bool anyDesktop = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index < 0 || index >= platforms.Length)
                    continue;
                if (!platforms[index].SupportsSteam)
                    continue;

                anyDesktop = true;
                string depotId;
                if (!SteamCommand.TryParseSteamId(steamDepots[index], out depotId))
                    return platforms[index].Label + " depot ID must be a positive integer.";
            }

            if (!anyDesktop)
                return "Steam publishing needs Windows, Mac, or Linux. WebGL is not uploaded to Steam.";

            return null;
        }
    }
}
