using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal sealed class BuildManager : EditorWindow
    {
        string version = "0.1.0";
        string committedVersion = "0.1.0";
        string gameName = string.Empty;
        string itchUser = string.Empty;
        string itchGame = string.Empty;
        string steamUser = string.Empty;
        string steamAppId = string.Empty;
        string steamBranch = string.Empty;
        List<BuildTargetEntry> targets = new List<BuildTargetEntry>();
        bool versionFieldFocused;
        bool commitVersionScheduled;
        bool publishOpen = true;
        bool itchOpen = true;
        bool steamOpen = true;
        int drawerStateVersion;
        [NonSerialized] List<SteamCatalogDepot> steamCatalog = new List<SteamCatalogDepot>();
        [NonSerialized] List<string> steamBranches = new List<string>();
        [NonSerialized] CatalogLookup<SteamDepotQuery> depotLookup;
        [NonSerialized] bool appIdFieldFocused;
        [NonSerialized] bool steamUserFieldFocused;
        [NonSerialized] List<string> itchChannels = new List<string>();
        [NonSerialized] CatalogLookup<ItchChannelQuery> channelLookup;
        [NonSerialized] bool itchUserFieldFocused;
        [NonSerialized] bool itchGameFieldFocused;
        [NonSerialized] List<ValidationIssue> preflightIssues = new List<ValidationIssue>();
        [NonSerialized] List<string> openTargetIds = new List<string>();
        [NonSerialized] List<string> advancedTargetIds = new List<string>();
        [NonSerialized] List<string> seenTargetIds = new List<string>();
        [NonSerialized] int dragFromIndex = -1;
        [NonSerialized] int dragInsertIndex = -1;
        [NonSerialized] bool dragMoved;
        [NonSerialized] bool dragRelease;
        [NonSerialized] Vector2 dragOrigin;
        [NonSerialized] List<int> targetSpanIndices = new List<int>();
        [NonSerialized] List<Rect> targetSpanRects = new List<Rect>();
        static readonly int TargetDragHint = "BuildTargetDrag".GetHashCode();
        Vector2 scroll;
        const float FieldLabelWidth = 110f;
        const float HeaderBarHeight = 24f;
        const float WindowInset = 6f;
        GUIStyle groupStyle;
        GUIStyle insetStyle;
        GUIStyle headerFoldoutStyle;
        bool stylesPro;
        bool settingsSavePending;
        double settingsSaveDue;
        bool preflightDirty = true;
        double preflightDue;
        double nextRunRepaint;
        [NonSerialized] BuildQueueStatus queueStatus;
        [NonSerialized] BuildRunHistory runHistory;
        bool historyOpen;

        [MenuItem("Tools/Build and Publish %#u")]
        public static void ShowWindow()
        {
            GetWindow<BuildManager>("Build and Publish");
        }

        void OnEnable()
        {
            minSize = new Vector2(430f, 500f);
            if (drawerStateVersion < 1)
            {
                publishOpen = true;
                itchOpen = true;
                steamOpen = true;
                drawerStateVersion = 1;
            }

            LoadSettings();
            runHistory = BuildLog.LoadHistory(BuildMaintenance.BuildsRoot());
            queueStatus = BuildQueueController.Status;
            BuildQueueController.StatusChanged -= OnQueueStatusChanged;
            BuildQueueController.StatusChanged += OnQueueStatusChanged;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            SchedulePreflightRefresh();
        }

        void OnDisable()
        {
            BuildQueueController.StatusChanged -= OnQueueStatusChanged;
            EditorApplication.update -= OnEditorUpdate;
            FlushSettingsSave();
        }

        void OnQueueStatusChanged()
        {
            queueStatus = BuildQueueController.Status;
            if (queueStatus != null && BuildQueueController.IsTerminal(queueStatus.Phase))
                runHistory = BuildLog.LoadHistory(queueStatus.OutputRoot);
            Repaint();
        }

        void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (settingsSavePending && now >= settingsSaveDue)
                FlushSettingsSave();
            if (preflightDirty && now >= preflightDue && !QueueRunning())
                RefreshPreflightPreview();
            if (QueueRunning() && now >= nextRunRepaint)
            {
                nextRunRepaint = now + 0.25d;
                queueStatus = BuildQueueController.Status;
                Repaint();
            }
        }

        void LoadSettings()
        {
            if (steamCatalog == null)
                steamCatalog = new List<SteamCatalogDepot>();
            if (steamBranches == null)
                steamBranches = new List<string>();
            if (itchChannels == null)
                itchChannels = new List<string>();
            EnsureLookups();
            gameName = ToolSettings.LoadGameName();
            itchUser = ToolSettings.LoadItchUser();
            itchGame = ToolSettings.LoadItchGame();
            steamUser = ToolSettings.LoadSteamUser();
            steamAppId = ToolSettings.LoadSteamAppId();
            steamBranch = ToolSettings.LoadSteamBranch();
            targets = ToolSettings.LoadTargets();
            if (targets == null)
                targets = new List<BuildTargetEntry>();
            string loadedVersion;
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
            }
            else
            {
                version = "0.1.0";
                committedVersion = "0.1.0";
            }
        }

        bool QueueRunning()
        {
            return queueStatus != null
                && !BuildQueueController.IsTerminal(queueStatus.Phase);
        }

        void ScheduleSettingsSave()
        {
            settingsSavePending = true;
            settingsSaveDue = EditorApplication.timeSinceStartup + 0.35d;
            SchedulePreflightRefresh();
        }

        void FlushSettingsSave()
        {
            if (!settingsSavePending)
                return;

            settingsSavePending = false;
            ToolSettings.SaveProjectConfiguration(
                gameName,
                itchUser,
                itchGame,
                steamAppId,
                steamBranch,
                targets);
            ToolSettings.SaveSteamUser(steamUser);
        }

        void SchedulePreflightRefresh()
        {
            preflightDirty = true;
            preflightDue = EditorApplication.timeSinceStartup + 0.15d;
        }

        void RefreshPreflightPreview()
        {
            preflightDirty = false;
            List<int> indices = EnabledTargetIndices();
            bool upload = SelectionPublishes(indices, false)
                || SelectionPublishes(indices, true);
            BuildPlanResult result = BuildPlanFactory.Create(
                CreateConfiguration(indices, upload),
                UnityBuildEnvironment.Capture(targets, indices));
            preflightIssues = result.Issues;
            Repaint();
        }

        BuildConfiguration CreateConfiguration(IList<int> indices, bool upload)
        {
            return new BuildConfiguration
            {
                Targets = targets,
                SelectedIndices = indices,
                Upload = upload,
                Version = version,
                GameName = gameName,
                ItchOwner = itchUser,
                ItchProject = itchGame,
                ButlerPath = ButlerUploader.GetButlerPath(),
                SteamUser = steamUser,
                SteamAppId = steamAppId,
                SteamBranch = steamBranch,
                SteamCmdPath = SteamUploader.GetSteamCmdPath()
            };
        }

        List<int> EnabledTargetIndices()
        {
            var indices = new List<int>();
            if (targets == null)
                return indices;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && targets[i].Enabled)
                    indices.Add(i);
            }
            return indices;
        }

        void OpenCurrentLog()
        {
            if (queueStatus == null || string.IsNullOrEmpty(queueStatus.OutputRoot))
                return;
            string path = BuildLog.RunLogPath(
                queueStatus.OutputRoot,
                queueStatus.QueueId);
            EditorUtility.RevealInFinder(File.Exists(path) ? path : queueStatus.OutputRoot);
        }

        void RevealCurrentArtifact()
        {
            if (queueStatus != null && !string.IsNullOrEmpty(queueStatus.LatestArtifactPath))
                EditorUtility.RevealInFinder(queueStatus.LatestArtifactPath);
        }

        void OpenCurrentItchPage()
        {
            if (queueStatus != null && !string.IsNullOrEmpty(queueStatus.ItchUrl))
                Application.OpenURL(queueStatus.ItchUrl);
        }

        void OpenCurrentSteamPage()
        {
            if (queueStatus != null && !string.IsNullOrEmpty(queueStatus.SteamUrl))
                Application.OpenURL(queueStatus.SteamUrl);
        }

        void OnGUI()
        {
            EnsureStyles();
            string settingsError = ToolSettings.ProjectSettingsError();
            DrawSettingsNotices(settingsError);
            if (!string.IsNullOrEmpty(BuildQueueController.RecoveryMessage))
            {
                EditorGUILayout.HelpBox(
                    BuildQueueController.RecoveryMessage,
                    MessageType.Error);
            }
            queueStatus = BuildQueueController.Status;
            BuildRunStatusSection.Draw(
                queueStatus,
                position.width,
                BuildQueueController.RequestCancel,
                OpenCurrentLog,
                RevealCurrentArtifact,
                OpenCurrentItchPage,
                OpenCurrentSteamPage,
                BuildQueueController.ClearFinished);

            EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(settingsError));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(WindowInset);
            EditorGUILayout.BeginVertical();
            GUILayout.Space(WindowInset);
            EditorGUI.BeginDisabledGroup(QueueRunning());
            DrawHeading();
            EditorGUI.EndDisabledGroup();
            DrawTargets();
            DrawPublish();
            BuildPreflightSection.Draw(preflightIssues);
            historyOpen = BuildHistorySection.Draw(runHistory, historyOpen);
            GUILayout.Space(WindowInset);
            EditorGUILayout.EndVertical();
            GUILayout.Space(WindowInset);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
            EditorGUI.EndDisabledGroup();
            DrawActions();
        }

        void DrawSettingsNotices(string settingsError)
        {
            if (!string.IsNullOrEmpty(settingsError))
            {
                EditorGUILayout.HelpBox(settingsError, MessageType.Error);
                return;
            }

            if (!ToolSettings.HasLegacyProjectSettings())
                return;

            EditorGUILayout.HelpBox(
                "Machine-wide settings from an older package version were found. Review this project before importing them; they may belong to another game.",
                MessageType.Warning);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Import legacy settings"))
            {
                string error;
                if (!ToolSettings.TryImportLegacyProjectSettings(out error))
                    EditorUtility.DisplayDialog("Settings", error, "OK");
                else
                    LoadSettings();
            }

            if (GUILayout.Button("Use project defaults"))
            {
                ToolSettings.InitializeProjectDefaults();
                LoadSettings();
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawHeading()
        {
            Rect card = BeginCard();
            BeginInset(PanelColor());
            EditorGUI.BeginChangeCheck();
            gameName = DrawLabeledText("Game Title", gameName, "GameName");
            if (EditorGUI.EndChangeCheck())
                ScheduleSettingsSave();

            Rect row = FieldRow();
            Rect labelRect;
            Rect fieldRect;
            SplitField(row, out labelRect, out fieldRect);
            EditorGUI.LabelField(labelRect, "Version");
            const float buttonWidth = 120f;
            const float buttonGap = 4f;
            bool wideVersionRow = fieldRect.width >= buttonWidth * 2f + 130f;
            float buttons = wideVersionRow ? buttonWidth * 2f + buttonGap : 0f;
            var textRect = new Rect(
                fieldRect.x,
                fieldRect.y,
                Mathf.Max(20f, fieldRect.width - buttons - (wideVersionRow ? buttonGap : 0f)),
                fieldRect.height);
            var minorRect = new Rect(textRect.xMax + buttonGap, fieldRect.y, buttonWidth, fieldRect.height);
            var patchRect = new Rect(minorRect.xMax + buttonGap, fieldRect.y, buttonWidth, fieldRect.height);
            GUI.SetNextControlName("VersionField");
            EditorGUI.BeginChangeCheck();
            version = EditorGUI.TextField(textRect, version);
            if (EditorGUI.EndChangeCheck())
                SchedulePreflightRefresh();

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

            if (wideVersionRow)
            {
                if (GUI.Button(minorRect, "Increment Minor"))
                    Increment(true);
                if (GUI.Button(patchRect, "Increment Patch"))
                    Increment(false);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(FieldLabelWidth + 4f);
                if (GUILayout.Button("Increment Minor"))
                    Increment(true);
                if (GUILayout.Button("Increment Patch"))
                    Increment(false);
                EditorGUILayout.EndHorizontal();
            }

            if (!GameVersion.TryParse(version, out _, out _, out _))
                EditorGUILayout.HelpBox("Version must be major.minor.patch using non-negative integers.", MessageType.Warning);
            EndInset();
            EndCard(card);
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
            SchedulePreflightRefresh();
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
            SchedulePreflightRefresh();
            Repaint();
        }

        void DrawPublish()
        {
            GUILayout.Space(10f);
            Rect card = BeginCard();
            publishOpen = DrawBarFoldout(publishOpen, "Publish");
            if (publishOpen)
            {
                BeginInset(PanelColor());
                bool showItch = AnyTargetPublishesToItch();
                bool showSteam = AnyTargetPublishesToSteam();
                if (!showItch && !showSteam)
                    EditorGUILayout.HelpBox(
                        "Publisher setup is available here. Enable a publisher on a target when you are ready to upload.",
                        MessageType.Info);

                DrawPublisherSection(
                    showItch ? "itch.io (selected)" : "itch.io",
                    ref itchOpen,
                    DrawItch);
                GUILayout.Space(8f);
                DrawPublisherSection(
                    showSteam ? "Steam (selected)" : "Steam",
                    ref steamOpen,
                    DrawSteam);
                EndInset();
            }

            EndCard(card);
        }

        void DrawPublisherSection(string title, ref bool open, Action draw)
        {
            Rect card = BeginCard();
            open = DrawBarFoldout(open, title);
            if (open)
            {
                BeginInset(NestedColor());
                EditorGUI.BeginDisabledGroup(QueueRunning());
                draw();
                EditorGUI.EndDisabledGroup();
                EndInset();
            }

            EndCard(card);
        }

        void DrawItch()
        {
            EditorGUI.BeginChangeCheck();
            itchUser = DrawLabeledText("Itch username", itchUser, "ItchUser");
            if (EditorGUI.EndChangeCheck())
                ScheduleSettingsSave();
            if (Event.current.type == EventType.Repaint)
            {
                bool userFocused = GUI.GetNameOfFocusedControl() == "ItchUser";
                if (itchUserFieldFocused && !userFocused)
                    ScheduleChannelLookup();
                itchUserFieldFocused = userFocused;
            }

            EditorGUI.BeginChangeCheck();
            itchGame = DrawLabeledText("Itch project", itchGame, "ItchProject");
            if (EditorGUI.EndChangeCheck())
                ScheduleSettingsSave();
            if (Event.current.type == EventType.Repaint)
            {
                bool projectFocused = GUI.GetNameOfFocusedControl() == "ItchProject";
                if (itchGameFieldFocused && !projectFocused)
                    ScheduleChannelLookup();
                itchGameFieldFocused = projectFocused;
            }

            string slugUser = BuildPaths.ToItchSlug(itchUser);
            string slugGame = BuildPaths.ToItchSlug(itchGame);
            string target = string.IsNullOrEmpty(slugUser) || string.IsNullOrEmpty(slugGame)
                ? "(enter a username and game title)"
                : slugUser + "/" + slugGame;

            DrawLabeledValue("Itch target", target);

            string url = BuildPaths.ItchPageUrl(itchUser, itchGame);
            Rect urlRow = FieldRow();
            Rect urlLabel;
            Rect urlField;
            SplitField(urlRow, out urlLabel, out urlField);
            EditorGUI.LabelField(urlLabel, "Game URL");
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(url));
            if (GUI.Button(urlField, string.IsNullOrEmpty(url) ? "Game URL" : url, EditorStyles.linkLabel))
                Application.OpenURL(url);
            EditorGUI.EndDisabledGroup();

            if (channelLookup != null && !string.IsNullOrEmpty(channelLookup.Message))
                EditorGUILayout.HelpBox(channelLookup.Message, channelLookup.InProgress ? MessageType.Info : MessageType.Warning);

            GUILayout.Space(8f);
            BeginInset(FooterColor());
            string butlerPath = ButlerUploader.GetButlerPath();
            bool butlerMissing = string.IsNullOrEmpty(butlerPath) || !File.Exists(butlerPath);
            DrawLabeledPath("Butler", butlerMissing ? "Not set" : butlerPath);
            if (butlerMissing)
                EditorGUILayout.HelpBox("Locate the Butler executable before uploading to itch.io.", MessageType.Warning);

            bool narrow = position.width < 560f;
            if (!narrow)
                EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Locate Butler"))
            {
                ButlerUploader.PromptForPath();
                SchedulePreflightRefresh();
                channelLookup.Invalidate();
                ScheduleChannelLookup();
            }
            if (GUILayout.Button("Login with Butler"))
                LoginWithButler();
            if (GUILayout.Button("Verify / Refresh Channels"))
            {
                channelLookup.Invalidate();
                ScheduleChannelLookup();
            }
            if (!narrow)
                EditorGUILayout.EndHorizontal();
            EndInset();
        }

        void DrawSteam()
        {
            EditorGUI.BeginChangeCheck();
            steamUser = DrawLabeledText("Steam username", steamUser, "SteamUser");
            if (EditorGUI.EndChangeCheck())
                ScheduleSettingsSave();
            if (Event.current.type == EventType.Repaint)
            {
                bool userFocused = GUI.GetNameOfFocusedControl() == "SteamUser";
                if (steamUserFieldFocused && !userFocused)
                    ScheduleDepotLookup();
                steamUserFieldFocused = userFocused;
            }

            EditorGUI.BeginChangeCheck();
            steamAppId = DrawLabeledText("App ID", steamAppId, "SteamAppId");
            bool appIdChanged = EditorGUI.EndChangeCheck();
            if (appIdChanged)
                ScheduleSettingsSave();
            if (Event.current.type == EventType.Repaint)
            {
                bool focused = GUI.GetNameOfFocusedControl() == "SteamAppId";
                if (appIdFieldFocused && !focused)
                    ScheduleDepotLookup();
                appIdFieldFocused = focused;
            }

            if (depotLookup != null && !string.IsNullOrEmpty(depotLookup.Message))
                EditorGUILayout.HelpBox(depotLookup.Message, depotLookup.InProgress ? MessageType.Info : MessageType.Warning);

            EditorGUI.BeginChangeCheck();
            bool branchMenu;
            Rect branchAnchor;
            steamBranch = DrawLabeledMenuField(
                "Branch",
                steamBranch,
                true,
                out branchMenu,
                out branchAnchor);
            if (branchMenu)
                ShowBranchMenu(branchAnchor);
            if (EditorGUI.EndChangeCheck())
                ScheduleSettingsSave();

            if (SteamCommand.IsDefaultBranch(steamBranch))
                EditorGUILayout.HelpBox("Steam cannot set the default branch live from steamcmd. Set that build live on the App Admin builds page.", MessageType.Warning);
            else if (!string.IsNullOrWhiteSpace(steamBranch))
                EditorGUILayout.HelpBox("Setting this branch live publishes only the depots in that upload. A live Steam build contains only those depots.", MessageType.Warning);

            EditorGUILayout.HelpBox("Steam keeps a chunk cache in Builds/.steam-cache. Removing that folder is safe and makes the next upload of the same App ID a complete upload.", MessageType.Info);

            GUILayout.Space(8f);
            BeginInset(FooterColor());
            string steamcmdPath = SteamUploader.GetSteamCmdPath();
            bool steamcmdMissing = string.IsNullOrEmpty(steamcmdPath) || !File.Exists(steamcmdPath);
            DrawLabeledPath("steamcmd", steamcmdMissing ? "Not set" : steamcmdPath);
            if (steamcmdMissing)
                EditorGUILayout.HelpBox("Locate steamcmd before uploading to Steam.", MessageType.Warning);

            bool narrow = position.width < 560f;
            if (!narrow)
                EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Locate steamcmd"))
            {
                SteamUploader.PromptForPath();
                SchedulePreflightRefresh();
                depotLookup.Invalidate();
                ScheduleDepotLookup();
            }
            if (GUILayout.Button("Login with steamcmd"))
                LoginWithSteamCmd();
            if (GUILayout.Button("Verify / Refresh Depots"))
            {
                depotLookup.Invalidate();
                ScheduleDepotLookup();
            }
            if (!narrow)
                EditorGUILayout.EndHorizontal();
            EndInset();
        }

        void EnsureLookups()
        {
            if (channelLookup == null)
                channelLookup = new CatalogLookup<ItchChannelQuery>();
            if (depotLookup == null)
                depotLookup = new CatalogLookup<SteamDepotQuery>();
        }

        void ScheduleChannelLookup()
        {
            EnsureLookups();
            channelLookup.Request(
                this,
                ItchLookupKey,
                ItchLookupBlocked,
                "Looking up channels\u2026",
                (key, done) => ButlerUploader.LookupChannelsAsync(
                    ButlerUploader.GetButlerPath(),
                    itchUser == null ? string.Empty : itchUser.Trim(),
                    itchGame == null ? string.Empty : itchGame.Trim(),
                    done),
                InterpretChannelLookup,
                ApplyChannelCatalog,
                ClearChannelCatalog);
        }

        string ItchLookupKey()
        {
            return CatalogLookupKey.Itch(ButlerUploader.GetButlerPath(), itchUser, itchGame);
        }

        string ItchLookupBlocked()
        {
            string butlerPath = ButlerUploader.GetButlerPath();
            if (string.IsNullOrEmpty(butlerPath) || !File.Exists(butlerPath))
                return "Locate Butler before looking up channels.";

            return null;
        }

        static CatalogLookupDecision InterpretChannelLookup(ItchChannelQuery query)
        {
            if (query.Cancelled || (!query.Ok && ButlerCommand.IsLoginFailure(query.Error)))
            {
                return new CatalogLookupDecision
                {
                    Remember = false,
                    Results = CatalogLookupResults.Keep,
                    Message = string.IsNullOrEmpty(query.Error) ? "Channel lookup was cancelled." : query.Error
                };
            }

            if (!query.Ok)
            {
                return new CatalogLookupDecision
                {
                    Remember = false,
                    Results = CatalogLookupResults.Clear,
                    Message = string.IsNullOrEmpty(query.Error) ? "Channel lookup failed." : query.Error
                };
            }

            if (query.Channels == null || query.Channels.Count == 0)
            {
                return new CatalogLookupDecision
                {
                    Remember = true,
                    Results = CatalogLookupResults.Clear,
                    Message = ButlerCommand.NoChannelsMessage
                };
            }

            return new CatalogLookupDecision
            {
                Remember = true,
                Results = CatalogLookupResults.Apply,
                Message = null
            };
        }

        void ApplyChannelCatalog(ItchChannelQuery query)
        {
            itchChannels = query.Channels ?? new List<string>();
            ApplySuggestedChannels();
        }

        void ClearChannelCatalog()
        {
            itchChannels = new List<string>();
        }

        void ApplySuggestedChannels()
        {
            if (targets == null)
                return;

            bool changed = false;
            for (int i = 0; i < targets.Count; i++)
            {
                BuildTargetEntry entry = targets[i];
                if (entry == null)
                    continue;

                string chosen = ButlerCommand.ChooseChannel(entry.Channel, itchChannels);
                string current = entry.Channel == null ? string.Empty : entry.Channel.Trim();
                if (chosen == current)
                    continue;

                entry.Channel = chosen;
                changed = true;
            }

            if (changed)
                ScheduleSettingsSave();
        }

        void ShowChannelMenu(int targetIndex, Rect anchor)
        {
            if (itchChannels == null || itchChannels.Count == 0)
                return;
            if (targets == null || targetIndex < 0 || targetIndex >= targets.Count || targets[targetIndex] == null)
                return;

            var menu = new GenericMenu();
            for (int i = 0; i < itchChannels.Count; i++)
            {
                string name = itchChannels[i];
                if (string.IsNullOrEmpty(name))
                    continue;

                string channel = name;
                int index = targetIndex;
                string current = targets[index].Channel == null ? string.Empty : targets[index].Channel.Trim();
                menu.AddItem(new GUIContent(channel), current == channel, () => AssignChannel(index, channel));
            }

            menu.DropDown(anchor);
        }

        void AssignChannel(int index, string channel)
        {
            if (targets == null || index < 0 || index >= targets.Count || targets[index] == null)
                return;

            targets[index].Channel = channel ?? string.Empty;
            ScheduleSettingsSave();
            Repaint();
        }

        void ScheduleDepotLookup()
        {
            EnsureLookups();
            depotLookup.Request(
                this,
                SteamLookupKey,
                SteamLookupBlocked,
                "Looking up depots\u2026",
                (key, done) => SteamUploader.LookupDepotsAsync(
                    SteamUploader.GetSteamCmdPath(),
                    steamUser == null ? string.Empty : steamUser.Trim(),
                    steamAppId,
                    done),
                InterpretDepotLookup,
                ApplyDepotCatalog,
                ClearDepotCatalog);
        }

        string SteamLookupKey()
        {
            return CatalogLookupKey.Steam(
                SteamUploader.GetSteamCmdPath(),
                steamUser,
                steamAppId);
        }

        string SteamLookupBlocked()
        {
            string steamcmdPath = SteamUploader.GetSteamCmdPath();
            if (string.IsNullOrEmpty(steamcmdPath) || !File.Exists(steamcmdPath))
                return "Locate steamcmd before looking up depots.";

            if (string.IsNullOrWhiteSpace(steamUser))
                return "Enter a Steam username before looking up depots.";

            return null;
        }

        static CatalogLookupDecision InterpretDepotLookup(SteamDepotQuery query)
        {
            if (query == null || query.Cancelled)
            {
                string message = query == null || string.IsNullOrEmpty(query.Error)
                    ? "Depot lookup was cancelled."
                    : query.Error;
                return new CatalogLookupDecision
                {
                    Remember = false,
                    Results = CatalogLookupResults.Keep,
                    Message = message
                };
            }

            if (!query.Ok)
            {
                return new CatalogLookupDecision
                {
                    Remember = false,
                    Results = CatalogLookupResults.Clear,
                    Message = string.IsNullOrEmpty(query.Error) ? "Depot lookup failed." : query.Error
                };
            }

            if (query.Depots == null || !query.Depots.FoundDepots)
            {
                string message = null;
                if (query.Depots != null)
                    message = query.Depots.Message;
                if (string.IsNullOrEmpty(message))
                    message = SteamCommand.NoDepotsMessage;

                return new CatalogLookupDecision
                {
                    Remember = true,
                    Results = CatalogLookupResults.Clear,
                    Message = message
                };
            }

            return new CatalogLookupDecision
            {
                Remember = true,
                Results = CatalogLookupResults.Apply,
                Message = null
            };
        }

        void ApplyDepotCatalog(SteamDepotQuery query)
        {
            steamCatalog = query.Depots.Depots ?? new List<SteamCatalogDepot>();
            steamBranches = query.Depots.Branches ?? new List<string>();
            ApplySuggestedDepots();
        }

        void ClearDepotCatalog()
        {
            steamCatalog = new List<SteamCatalogDepot>();
            steamBranches = new List<string>();
        }

        void ApplySuggestedDepots()
        {
            if (targets == null)
                return;

            bool changed = false;
            for (int i = 0; i < targets.Count; i++)
            {
                BuildTargetEntry entry = targets[i];
                if (entry == null)
                    continue;

                BuildTarget buildTarget = (BuildTarget)entry.TargetValue;
                if (!BuildTargetSet.SupportsSteam(buildTarget))
                    continue;

                string platformId = BuildTargetSet.SteamPlatformId(buildTarget);
                string chosen = SteamCommand.ChooseDepotId(entry.SteamDepotId, platformId, steamCatalog);
                string current = entry.SteamDepotId == null ? string.Empty : entry.SteamDepotId.Trim();
                if (chosen == current)
                    continue;

                entry.SteamDepotId = chosen;
                changed = true;
            }

            if (changed)
                ScheduleSettingsSave();
        }

        void ShowDepotMenu(int targetIndex, Rect anchor)
        {
            if (steamCatalog == null || steamCatalog.Count == 0)
                return;
            if (targets == null || targetIndex < 0 || targetIndex >= targets.Count || targets[targetIndex] == null)
                return;

            var menu = new GenericMenu();
            for (int i = 0; i < steamCatalog.Count; i++)
            {
                SteamCatalogDepot depot = steamCatalog[i];
                if (depot == null || string.IsNullOrEmpty(depot.Id))
                    continue;

                string id = depot.Id;
                string label = string.IsNullOrEmpty(depot.MenuLabel) ? id : depot.MenuLabel;
                int index = targetIndex;
                bool selected = targets[index].SteamDepotId == id;
                menu.AddItem(new GUIContent(label), selected, () => AssignDepot(index, id));
            }

            menu.DropDown(anchor);
        }

        void AssignDepot(int index, string depotId)
        {
            if (targets == null || index < 0 || index >= targets.Count || targets[index] == null)
                return;

            targets[index].SteamDepotId = depotId;
            ScheduleSettingsSave();
            Repaint();
        }

        void ShowBranchMenu(Rect anchor)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Do not set live"), string.IsNullOrEmpty(steamBranch), () => AssignBranch(string.Empty));
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

            menu.DropDown(anchor);
        }

        void AssignBranch(string branch)
        {
            steamBranch = branch ?? string.Empty;
            ScheduleSettingsSave();
            Repaint();
        }

        void LoginWithButler()
        {
            string butlerPath = ButlerUploader.GetButlerPath();
            if (string.IsNullOrEmpty(butlerPath) || !File.Exists(butlerPath))
            {
                EditorUtility.DisplayDialog("Butler", "Locate Butler before logging in.", "OK");
                return;
            }

            try
            {
                ButlerUploader.OpenLogin(butlerPath);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Butler", "Could not start Butler: " + exception.Message, "OK");
            }
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

        void DrawTargets()
        {
            GUILayout.Space(12f);
            EditorGUILayout.LabelField("Build targets", EditorStyles.boldLabel);
            GUILayout.Space(4f);
            if (targets == null)
                targets = new List<BuildTargetEntry>();

            bool queueRunning = QueueRunning();
            bool drewTarget = false;
            if (Event.current.type != EventType.Layout)
            {
                if (targetSpanIndices == null)
                    targetSpanIndices = new List<int>();
                if (targetSpanRects == null)
                    targetSpanRects = new List<Rect>();
                targetSpanIndices.Clear();
                targetSpanRects.Clear();
            }

            for (int i = 0; i < targets.Count; i++)
            {
                BuildTargetEntry entry = targets[i];
                if (entry == null)
                    continue;
                if (string.IsNullOrEmpty(entry.Id))
                    entry.Id = string.IsNullOrEmpty(entry.FolderKey) ? "target" + i : entry.FolderKey;

                if (drewTarget)
                    GUILayout.Space(6f);
                drewTarget = true;

                Rect card = BeginCard();
                if (Event.current.type != EventType.Layout)
                {
                    targetSpanIndices.Add(i);
                    targetSpanRects.Add(card);
                }

                bool open = IsTargetOpen(entry.Id);
                bool nextOpen = DrawTargetBar(entry, i, open, queueRunning);
                if (nextOpen != open)
                    SetTargetOpen(entry.Id, nextOpen);

                if (nextOpen)
                {
                    BeginInset(PanelColor());
                    EditorGUI.BeginDisabledGroup(queueRunning);
                    DrawTargetBody(entry, i, queueRunning);
                    EditorGUI.EndDisabledGroup();
                    EndInset();
                }

                EndCard(card);
            }

            UpdateTargetDrag();

            GUILayout.Space(8f);
            EditorGUI.BeginDisabledGroup(queueRunning);
            if (GUILayout.Button("Add target", GUILayout.Width(120)))
                ShowAddTargetMenu();
            EditorGUI.EndDisabledGroup();
        }

        void DrawTargetBody(BuildTargetEntry entry, int index, bool queueRunning)
        {
            EditorGUI.BeginChangeCheck();
            entry.Name = DrawLabeledText("Name", entry.Name, null);
            if (EditorGUI.EndChangeCheck())
                ScheduleSettingsSave();

            BuildTarget[] options = BuildTargetSet.ListAddableTargets();
            BuildTarget current = (BuildTarget)entry.TargetValue;
            options = IncludeCurrentTarget(options, current);
            int selected = 0;
            var labels = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                labels[i] = TargetMenuLabel(options[i], options);
                if (options[i] == current)
                    selected = i;
            }

            EditorGUI.BeginChangeCheck();
            int nextSelected = DrawLabeledPopup("Build target", selected, labels);
            if (EditorGUI.EndChangeCheck() && nextSelected >= 0 && nextSelected < options.Length)
            {
                if (BuildTargetSet.ChangeTarget(entry, options[nextSelected]))
                    ScheduleSettingsSave();
            }

            bool advancedOpen = IsTargetAdvanced(entry.Id);
            bool nextAdvancedOpen = EditorGUILayout.Foldout(
                advancedOpen,
                new GUIContent(
                    "Advanced",
                    "The output key is a stable folder and publisher identity. Change it only deliberately."),
                true);
            if (nextAdvancedOpen != advancedOpen)
                SetTargetAdvanced(entry.Id, nextAdvancedOpen);
            if (nextAdvancedOpen)
            {
                EditorGUI.BeginChangeCheck();
                entry.FolderKey = DrawLabeledText("Output key", entry.FolderKey, null);
                if (EditorGUI.EndChangeCheck())
                    ScheduleSettingsSave();
            }

            BuildTarget buildTarget = (BuildTarget)entry.TargetValue;
            bool publishChanged = false;
            GUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            bool nextItch = EditorGUILayout.ToggleLeft("itch.io", entry.PublishItch, GUILayout.Width(80));
            if (nextItch != entry.PublishItch)
            {
                entry.PublishItch = nextItch;
                publishChanged = true;
            }

            if (BuildTargetSet.SupportsSteam(buildTarget))
            {
                bool nextSteam = EditorGUILayout.ToggleLeft("Steam", entry.PublishSteam, GUILayout.Width(80));
                if (nextSteam != entry.PublishSteam)
                {
                    entry.PublishSteam = nextSteam;
                    publishChanged = true;
                }
            }

            EditorGUILayout.EndHorizontal();
            if (publishChanged)
                ScheduleSettingsSave();

            if (entry.PublishItch)
            {
                EditorGUI.BeginChangeCheck();
                bool channelMenu;
                Rect channelAnchor;
                entry.Channel = DrawLabeledMenuField(
                    "Itch channel",
                    entry.Channel,
                    itchChannels != null && itchChannels.Count > 0,
                    out channelMenu,
                    out channelAnchor);
                if (channelMenu)
                    ShowChannelMenu(index, channelAnchor);
                if (EditorGUI.EndChangeCheck())
                    ScheduleSettingsSave();
            }

            if (entry.PublishSteam && BuildTargetSet.SupportsSteam(buildTarget))
            {
                EditorGUI.BeginChangeCheck();
                bool depotMenu;
                Rect depotAnchor;
                entry.SteamDepotId = DrawLabeledMenuField(
                    "Steam depot",
                    entry.SteamDepotId,
                    steamCatalog != null && steamCatalog.Count > 0,
                    out depotMenu,
                    out depotAnchor);
                if (depotMenu)
                    ShowDepotMenu(index, depotAnchor);
                if (EditorGUI.EndChangeCheck())
                    ScheduleSettingsSave();
            }

            GUILayout.Space(8f);
            BeginInset(FooterColor());
            EditorGUI.BeginDisabledGroup(queueRunning);
            bool narrow = position.width < 540f;
            if (!narrow)
                EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Build"))
                StartQueue(new[] { index }, false);
            if (GUILayout.Button("Build and Upload"))
                StartQueue(new[] { index }, true);
            if (!narrow)
                EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
            EndInset();
        }

        void ShowAddTargetMenu()
        {
            BuildTarget[] options = BuildTargetSet.ListAddableTargets();
            var menu = new GenericMenu();
            for (int i = 0; i < options.Length; i++)
            {
                BuildTarget target = options[i];
                string label = TargetMenuLabel(target, options);
                menu.AddItem(new GUIContent(label), false, () => AddTarget(target));
            }

            menu.ShowAsContext();
        }

        void AddTarget(BuildTarget target)
        {
            if (targets == null)
                targets = new List<BuildTargetEntry>();

            BuildTargetEntry entry = BuildTargetSet.Create(target, targets);
            targets.Add(entry);
            if (seenTargetIds == null)
                seenTargetIds = new List<string>();
            if (!seenTargetIds.Contains(entry.Id))
                seenTargetIds.Add(entry.Id);
            SetTargetOpen(entry.Id, true);
            ScheduleSettingsSave();
            Repaint();
        }

        static BuildTarget[] IncludeCurrentTarget(BuildTarget[] options, BuildTarget current)
        {
            if (options == null)
                options = new BuildTarget[0];
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] == current)
                    return options;
            }

            var withCurrent = new BuildTarget[options.Length + 1];
            withCurrent[0] = current;
            for (int i = 0; i < options.Length; i++)
                withCurrent[i + 1] = options[i];
            return withCurrent;
        }

        static string TargetMenuLabel(BuildTarget target, BuildTarget[] options)
        {
            string name = BuildTargetSet.DisplayName(target);
            int count = 0;
            if (options != null)
            {
                for (int i = 0; i < options.Length; i++)
                {
                    if (BuildTargetSet.DisplayName(options[i]) == name)
                        count++;
                }
            }

            if (count > 1)
                return name + " (" + target + ")";

            return name;
        }

        bool IsTargetOpen(string id)
        {
            if (seenTargetIds == null)
                seenTargetIds = new List<string>();
            if (openTargetIds == null)
                openTargetIds = new List<string>();
            if (!seenTargetIds.Contains(id))
            {
                seenTargetIds.Add(id);
                openTargetIds.Add(id);
                return true;
            }

            return openTargetIds.Contains(id);
        }

        void SetTargetOpen(string id, bool open)
        {
            if (openTargetIds == null)
                openTargetIds = new List<string>();
            bool has = openTargetIds.Contains(id);
            if (open && !has)
                openTargetIds.Add(id);
            if (!open && has)
                openTargetIds.Remove(id);
        }

        bool IsTargetAdvanced(string id)
        {
            if (advancedTargetIds == null)
                advancedTargetIds = new List<string>();
            return !string.IsNullOrEmpty(id) && advancedTargetIds.Contains(id);
        }

        void SetTargetAdvanced(string id, bool open)
        {
            if (advancedTargetIds == null)
                advancedTargetIds = new List<string>();
            bool has = advancedTargetIds.Contains(id);
            if (open && !has)
                advancedTargetIds.Add(id);
            else if (!open && has)
                advancedTargetIds.Remove(id);
        }

        void ForgetTarget(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;
            if (openTargetIds != null)
                openTargetIds.Remove(id);
            if (advancedTargetIds != null)
                advancedTargetIds.Remove(id);
            if (seenTargetIds != null)
                seenTargetIds.Remove(id);
        }

        string DrawLabeledText(string label, string value, string controlName)
        {
            Rect row = FieldRow();
            Rect labelRect;
            Rect fieldRect;
            SplitField(row, out labelRect, out fieldRect);
            EditorGUI.LabelField(labelRect, label);
            if (!string.IsNullOrEmpty(controlName))
                GUI.SetNextControlName(controlName);
            return EditorGUI.TextField(fieldRect, value ?? string.Empty);
        }

        void DrawLabeledValue(string label, string value)
        {
            Rect row = FieldRow();
            Rect labelRect;
            Rect fieldRect;
            SplitField(row, out labelRect, out fieldRect);
            EditorGUI.LabelField(labelRect, label);
            EditorGUI.LabelField(fieldRect, value ?? string.Empty);
        }

        void DrawLabeledPath(string label, string value)
        {
            Rect row = FieldRow();
            Rect labelRect;
            Rect fieldRect;
            SplitField(row, out labelRect, out fieldRect);
            EditorGUI.LabelField(labelRect, new GUIContent(label, value ?? string.Empty));
            EditorGUI.SelectableLabel(
                fieldRect,
                value ?? string.Empty,
                EditorStyles.textField);
        }

        int DrawLabeledPopup(string label, int selected, string[] labels)
        {
            Rect row = FieldRow();
            Rect labelRect;
            Rect fieldRect;
            SplitField(row, out labelRect, out fieldRect);
            EditorGUI.LabelField(labelRect, label);
            return EditorGUI.Popup(fieldRect, selected, labels ?? new string[0]);
        }

        string DrawLabeledMenuField(
            string label,
            string value,
            bool menuEnabled,
            out bool menuClicked,
            out Rect menuAnchor)
        {
            Rect row = FieldRow();
            Rect labelRect;
            Rect fieldRect;
            SplitField(row, out labelRect, out fieldRect);
            EditorGUI.LabelField(labelRect, label);
            const float menuWidth = 24f;
            var menuRect = new Rect(fieldRect.xMax - menuWidth, fieldRect.y, menuWidth, fieldRect.height);
            menuAnchor = menuRect;
            var textRect = new Rect(fieldRect.x, fieldRect.y, Mathf.Max(20f, fieldRect.width - menuWidth - 4f), fieldRect.height);
            EditorGUI.BeginDisabledGroup(!menuEnabled);
            menuClicked = GUI.Button(menuRect, "\u25BC");
            EditorGUI.EndDisabledGroup();
            return EditorGUI.TextField(textRect, value ?? string.Empty);
        }

        void DrawActions()
        {
            Rect bar = EditorGUILayout.BeginVertical(insetStyle);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(bar, HeaderColor());
                EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width, 1f), BorderColor());
            }

            EditorGUI.BeginDisabledGroup(
                QueueRunning()
                || !string.IsNullOrEmpty(ToolSettings.ProjectSettingsError()));
            if (GUILayout.Button("Build Selected Targets"))
                StartSelected(false);
            GUILayout.Space(4f);
            if (GUILayout.Button("Build and Upload Selected Targets"))
                StartSelected(true);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();
        }

        Rect BeginCard()
        {
            return EditorGUILayout.BeginVertical(groupStyle);
        }

        void EndCard(Rect area)
        {
            EditorGUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint)
                DrawOutline(area, BorderColor());
        }

        void BeginInset(Color color)
        {
            Rect body = EditorGUILayout.BeginVertical(insetStyle);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(body, color);
        }

        void EndInset()
        {
            EditorGUILayout.EndVertical();
        }

        bool DrawBarFoldout(bool open, string title)
        {
            Rect header = EditorGUILayout.GetControlRect(false, HeaderBarHeight, groupStyle, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(header, HeaderColor());

            var foldRect = new Rect(header.x + 6f, header.y, header.width - 12f, header.height);
            return EditorGUI.Foldout(foldRect, open, title, true, headerFoldoutStyle);
        }

        bool DrawTargetBar(BuildTargetEntry entry, int index, bool open, bool queueRunning)
        {
            Rect header = EditorGUILayout.GetControlRect(false, HeaderBarHeight, groupStyle, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(header, HeaderColor());
                if (dragMoved && dragFromIndex == index)
                    EditorGUI.DrawRect(header, new Color(0.24f, 0.48f, 0.90f, 0.28f));
            }

            float line = EditorGUIUtility.singleLineHeight;
            float y = header.y + (header.height - line) * 0.5f;
            const float includeWidth = 112f;
            const float menuWidth = 24f;
            var toggleRect = new Rect(header.x + 6f, y, includeWidth, line);
            EditorGUI.BeginDisabledGroup(queueRunning);
            bool nextEnabled = GUI.Toggle(
                toggleRect,
                entry.Enabled,
                new GUIContent("Include in batch", "Include this target in batch actions."));
            EditorGUI.EndDisabledGroup();
            if (nextEnabled != entry.Enabled)
            {
                entry.Enabled = nextEnabled;
                ScheduleSettingsSave();
            }

            var menuRect = new Rect(header.xMax - menuWidth - 4f, y, menuWidth, line);
            var dragRect = new Rect(toggleRect.xMax + 2f, y, 20f, line);
            var foldRect = new Rect(
                dragRect.xMax + 2f,
                header.y,
                Mathf.Max(20f, menuRect.x - dragRect.xMax - 6f),
                header.height);
            string title = string.IsNullOrEmpty(entry.Name) ? "Target" : entry.Name;
            bool nextOpen = EditorGUI.Foldout(
                foldRect,
                open,
                new GUIContent(title, "Expand target settings."),
                true,
                headerFoldoutStyle);
            GUI.Label(dragRect, new GUIContent("\u2261", "Drag to reorder."));
            if (!queueRunning)
                EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.MoveArrow);

            int controlId = GUIUtility.GetControlID(TargetDragHint, FocusType.Passive, header);
            Event evt = Event.current;
            if (GUI.Button(
                menuRect,
                new GUIContent("\u22ee", "Target actions"),
                EditorStyles.miniButton))
            {
                ShowTargetContextMenu(entry.Id, queueRunning);
            }
            if (evt != null && header.Contains(evt.mousePosition) && evt.type == EventType.ContextClick)
            {
                ShowTargetContextMenu(entry.Id, queueRunning);
                evt.Use();
            }
            else if (!queueRunning && evt != null && evt.button == 0)
            {
                if (evt.type == EventType.MouseDown && dragRect.Contains(evt.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    dragFromIndex = index;
                    dragInsertIndex = index;
                    dragOrigin = evt.mousePosition;
                    dragMoved = false;
                    dragRelease = false;
                    evt.Use();
                }
                else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlId && dragFromIndex == index)
                {
                    if ((evt.mousePosition - dragOrigin).sqrMagnitude > 16f)
                        dragMoved = true;
                    evt.Use();
                }
                else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlId && dragFromIndex == index)
                {
                    GUIUtility.hotControl = 0;
                    evt.Use();
                    if (dragMoved)
                        dragRelease = true;

                    if (!dragRelease)
                    {
                        dragFromIndex = -1;
                        dragMoved = false;
                    }
                }
            }

            return nextOpen;
        }

        void ShowTargetContextMenu(string id, bool queueRunning)
        {
            var menu = new GenericMenu();
            int index = TargetIndex(id);
            if (queueRunning || string.IsNullOrEmpty(id))
            {
                menu.AddDisabledItem(new GUIContent("Move Up"));
                menu.AddDisabledItem(new GUIContent("Move Down"));
                menu.AddDisabledItem(new GUIContent("Remove"));
            }
            else
            {
                if (index > 0)
                    menu.AddItem(new GUIContent("Move Up"), false, () => MoveTarget(id, -1));
                else
                    menu.AddDisabledItem(new GUIContent("Move Up"));
                if (index >= 0 && targets != null && index < targets.Count - 1)
                    menu.AddItem(new GUIContent("Move Down"), false, () => MoveTarget(id, 1));
                else
                    menu.AddDisabledItem(new GUIContent("Move Down"));
                menu.AddSeparator(string.Empty);
                string removeId = id;
                menu.AddItem(new GUIContent("Remove"), false, () => RemoveTarget(removeId));
            }

            menu.ShowAsContext();
        }

        void RemoveTarget(string id)
        {
            if (QueueRunning() || targets == null || string.IsNullOrEmpty(id))
                return;

            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] == null || targets[i].Id != id)
                    continue;

                string label = string.IsNullOrEmpty(targets[i].Name)
                    ? "this target"
                    : "'" + targets[i].Name + "'";
                if (!EditorUtility.DisplayDialog(
                    "Remove build target",
                    "Remove " + label + " from the build profile?",
                    "Remove",
                    "Cancel"))
                {
                    return;
                }

                targets.RemoveAt(i);
                ForgetTarget(id);
                ScheduleSettingsSave();
                Repaint();
                return;
            }
        }

        int TargetIndex(string id)
        {
            if (targets == null || string.IsNullOrEmpty(id))
                return -1;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && targets[i].Id == id)
                    return i;
            }

            return -1;
        }

        void MoveTarget(string id, int offset)
        {
            if (QueueRunning())
                return;
            int from = TargetIndex(id);
            int to = from + offset;
            if (from < 0 || targets == null || to < 0 || to >= targets.Count)
                return;
            BuildTargetEntry entry = targets[from];
            targets.RemoveAt(from);
            targets.Insert(to, entry);
            ScheduleSettingsSave();
            Repaint();
        }

        void UpdateTargetDrag()
        {
            Event evt = Event.current;
            if (evt == null)
                return;

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && dragFromIndex >= 0)
            {
                dragFromIndex = -1;
                dragInsertIndex = -1;
                dragMoved = false;
                dragRelease = false;
                GUIUtility.hotControl = 0;
                evt.Use();
                Repaint();
                return;
            }

            if (dragFromIndex < 0)
                return;

            if (dragMoved && (evt.type == EventType.MouseDrag || evt.type == EventType.MouseUp || evt.type == EventType.Repaint))
                dragInsertIndex = TargetInsertIndex(evt.mousePosition.y);

            if (evt.type == EventType.Repaint && dragMoved)
            {
                DrawTargetInsertLine();
                EditorGUIUtility.AddCursorRect(new Rect(0f, 0f, position.width, position.height), MouseCursor.MoveArrow);
            }

            if (evt.type == EventType.MouseDrag && dragMoved)
                Repaint();

            if (!dragRelease)
                return;

            int from = dragFromIndex;
            int insert = dragInsertIndex;
            dragFromIndex = -1;
            dragInsertIndex = -1;
            dragMoved = false;
            dragRelease = false;
            if (targets != null && BuildTargetSet.DropIndex(from, insert, targets.Count) != from)
            {
                BuildTargetSet.Move(targets, from, insert);
                ScheduleSettingsSave();
            }

            Repaint();
        }

        int TargetInsertIndex(float mouseY)
        {
            if (targetSpanRects == null || targetSpanIndices == null || targetSpanRects.Count == 0)
                return dragFromIndex < 0 ? 0 : dragFromIndex;

            for (int i = 0; i < targetSpanRects.Count; i++)
            {
                if (mouseY < targetSpanRects[i].center.y)
                    return targetSpanIndices[i];
            }

            return targetSpanIndices[targetSpanIndices.Count - 1] + 1;
        }

        void DrawTargetInsertLine()
        {
            if (targets == null || targetSpanRects == null || targetSpanIndices == null || targetSpanRects.Count == 0)
                return;
            if (BuildTargetSet.DropIndex(dragFromIndex, dragInsertIndex, targets.Count) == dragFromIndex)
                return;

            Rect anchor = targetSpanRects[0];
            float y = anchor.y;
            for (int i = 0; i < targetSpanIndices.Count && i < targetSpanRects.Count; i++)
            {
                anchor = targetSpanRects[i];
                if (targetSpanIndices[i] >= dragInsertIndex)
                {
                    y = anchor.y;
                    break;
                }

                y = anchor.yMax;
            }

            Color line = EditorGUIUtility.isProSkin
                ? new Color(0.35f, 0.55f, 0.90f)
                : new Color(0.20f, 0.40f, 0.80f);
            EditorGUI.DrawRect(new Rect(anchor.x, y - 1f, anchor.width, 2f), line);
        }

        Rect FieldRow()
        {
            return EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, groupStyle, GUILayout.ExpandWidth(true));
        }

        void SplitField(Rect row, out Rect label, out Rect field)
        {
            label = new Rect(row.x, row.y, FieldLabelWidth, row.height);
            float x = row.x + FieldLabelWidth + 4f;
            field = new Rect(x, row.y, Mathf.Max(20f, row.xMax - x), row.height);
        }

        void DrawOutline(Rect rect, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        void EnsureStyles()
        {
            if (groupStyle != null && stylesPro == EditorGUIUtility.isProSkin)
                return;

            stylesPro = EditorGUIUtility.isProSkin;
            groupStyle = new GUIStyle();
            groupStyle.margin = new RectOffset(0, 0, 0, 0);
            groupStyle.padding = new RectOffset(0, 0, 0, 0);
            groupStyle.stretchWidth = true;
            insetStyle = new GUIStyle();
            insetStyle.margin = new RectOffset(0, 0, 0, 0);
            insetStyle.padding = new RectOffset(10, 10, 8, 8);
            insetStyle.stretchWidth = true;
            headerFoldoutStyle = new GUIStyle(EditorStyles.foldout);
            headerFoldoutStyle.fontStyle = FontStyle.Bold;
        }

        Color HeaderColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.30f, 0.30f, 0.30f)
                : new Color(0.68f, 0.68f, 0.68f);
        }

        Color PanelColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.18f, 0.18f)
                : new Color(0.90f, 0.90f, 0.90f);
        }

        Color NestedColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.24f, 0.24f, 0.24f)
                : new Color(0.97f, 0.97f, 0.97f);
        }

        Color FooterColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.14f, 0.14f, 0.14f)
                : new Color(0.80f, 0.80f, 0.80f);
        }

        Color BorderColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0f, 0f, 0f, 0.7f)
                : new Color(0f, 0f, 0f, 0.28f);
        }

        bool AnyTargetPublishesToItch()
        {
            return SelectionPublishes(AllTargetIndices(), false);
        }

        bool AnyTargetPublishesToSteam()
        {
            return SelectionPublishes(AllTargetIndices(), true);
        }

        List<int> AllTargetIndices()
        {
            var indices = new List<int>();
            if (targets == null)
                return indices;

            for (int i = 0; i < targets.Count; i++)
                indices.Add(i);
            return indices;
        }

        bool SelectionPublishes(IList<int> indices, bool steam)
        {
            return BuildTargetSet.Publishes(targets, indices, steam);
        }

        void StartSelected(bool upload)
        {
            List<int> indices = EnabledTargetIndices();

            if (indices.Count == 0)
            {
                EditorUtility.DisplayDialog("Build", "Select at least one build target.", "OK");
                return;
            }

            StartQueue(indices, upload);
        }

        void StartQueue(IList<int> indices, bool upload)
        {
            if (QueueRunning())
                return;

            BuildConfiguration configuration = CreateConfiguration(indices, upload);
            BuildPlanResult planResult = BuildPlanFactory.Create(
                configuration,
                UnityBuildEnvironment.Capture(targets, indices));
            preflightIssues = planResult.Issues;
            if (planResult.HasErrors)
            {
                Repaint();
                return;
            }

            if (HasDirtyScenes()
                && !EditorUtility.DisplayDialog(
                    "Save scenes",
                    "Open scenes have unsaved changes. Save them before starting this build?",
                    "Save and build",
                    "Cancel"))
            {
                return;
            }

            if (HasDirtyScenes() && !EditorSceneManager.SaveOpenScenes())
                return;

            if (upload
                && SelectionPublishes(indices, true)
                && !string.IsNullOrWhiteSpace(steamBranch)
                && !SteamCommand.IsDefaultBranch(steamBranch)
                && !EditorUtility.DisplayDialog(
                    "Set Steam branch live",
                    "A successful upload will set branch '" + steamBranch.Trim()
                        + "' live. Continue?",
                    "Build, upload, and set live",
                    "Cancel"))
            {
                return;
            }

            settingsSavePending = true;
            FlushSettingsSave();
            string error;
            if (!BuildQueueController.Start(planResult.Plan, out error))
                EditorUtility.DisplayDialog("Build", error, "OK");
            queueStatus = BuildQueueController.Status;
            Repaint();
        }

        static bool HasDirtyScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                    return true;
            }

            return false;
        }

    }
}
