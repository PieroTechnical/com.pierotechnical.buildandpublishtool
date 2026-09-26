using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class BuildPlanFactory
    {
        internal static BuildPlanResult Create(
            BuildConfiguration configuration,
            BuildEnvironmentSnapshot environment)
        {
            var result = new BuildPlanResult();
            if (configuration == null)
            {
                AddError(result, "configuration.missing", null, null, "Build configuration was missing.");
                return result;
            }

            if (environment == null)
            {
                AddError(result, "environment.missing", null, null, "Build environment was missing.");
                return result;
            }

            string normalizedVersion = NormalizeVersion(configuration.Version, result);
            string gameName = NormalizeGameName(configuration.GameName, result);
            ValidateEnvironment(environment, result);

            string outputRoot = string.IsNullOrEmpty(environment.ProjectRoot)
                ? null
                : Path.Combine(environment.ProjectRoot, "Builds");
            if (string.IsNullOrEmpty(outputRoot))
                AddError(result, "project.root", "outputRoot", null, "Could not resolve the project folder.");

            var plan = new BuildPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Version = normalizedVersion,
                GameName = gameName,
                OutputRoot = outputRoot,
                Scenes = CloneScenes(environment.Scenes),
                AndroidAppBundle = environment.AndroidAppBundle,
                OriginalAndroidAppBundle = environment.AndroidAppBundle,
                OriginalBuildTargetValue = environment.ActiveBuildTargetValue
            };

            var outputKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var steamDepots = new HashSet<string>(StringComparer.Ordinal);
            var steamPayload = new SteamPublishPayload
            {
                Branch = Trim(configuration.SteamBranch),
                Username = Trim(configuration.SteamUser),
                SteamCmdPath = configuration.SteamCmdPath ?? string.Empty
            };
            bool wantsItch = false;
            bool wantsSteam = false;

            if (configuration.SelectedIndices == null || configuration.SelectedIndices.Count == 0)
            {
                AddError(result, "targets.empty", "targets", null, "Select at least one build target.");
            }
            else
            {
                for (int i = 0; i < configuration.SelectedIndices.Count; i++)
                {
                    AddTarget(
                        configuration,
                        environment,
                        configuration.SelectedIndices[i],
                        plan,
                        outputKeys,
                        steamDepots,
                        steamPayload,
                        result,
                        ref wantsItch,
                        ref wantsSteam);
                }
            }

            if (configuration.Upload && !wantsItch && !wantsSteam)
            {
                AddError(
                    result,
                    "publish.none",
                    "publishers",
                    null,
                    "Turn on itch.io or Steam for a selected build target before uploading.");
            }

            if (wantsItch)
                ValidateItch(configuration, result);
            if (wantsSteam)
                AddSteamJob(configuration, steamPayload, plan, result);

            if (!result.HasErrors)
                result.Plan = plan;
            return result;
        }

        static void AddTarget(
            BuildConfiguration configuration,
            BuildEnvironmentSnapshot environment,
            int sourceIndex,
            BuildPlan plan,
            HashSet<string> outputKeys,
            HashSet<string> steamDepots,
            SteamPublishPayload steamPayload,
            BuildPlanResult result,
            ref bool wantsItch,
            ref bool wantsSteam)
        {
            if (configuration.Targets == null
                || sourceIndex < 0
                || sourceIndex >= configuration.Targets.Count
                || configuration.Targets[sourceIndex] == null)
            {
                AddError(result, "target.missing", "targets", null, "A selected build target was missing.");
                return;
            }

            BuildTargetEntry entry = configuration.Targets[sourceIndex];
            BuildTarget target = (BuildTarget)entry.TargetValue;
            if (!Enum.IsDefined(typeof(BuildTarget), entry.TargetValue) || target == BuildTarget.NoTarget)
            {
                AddError(result, "target.invalid", "buildTarget", entry.Id, "Select a valid Unity build target.");
                return;
            }

            BuildTargetDescriptor descriptor = BuildTargetCatalog.Find(target);
            if (environment.UnsupportedTargetValues.Contains(entry.TargetValue))
            {
                AddError(
                    result,
                    "target.unsupported",
                    "buildTarget",
                    entry.Id,
                    descriptor.Label + " build support is not installed in this Editor.");
            }

            if (descriptor.IsGeneric)
            {
                AddWarning(
                    result,
                    "target.generic",
                    "buildTarget",
                    entry.Id,
                    descriptor.Label + " uses the generic directory output strategy. Verify this platform's output requirements.");
            }

            string outputKey = BuildPaths.SanitizePathSegment(entry.FolderKey);
            if (string.IsNullOrWhiteSpace(entry.FolderKey)
                || !string.Equals(outputKey, entry.FolderKey, StringComparison.Ordinal))
            {
                AddError(
                    result,
                    "target.outputKey",
                    "outputKey",
                    entry.Id,
                    descriptor.Label + " output key must be one portable path segment.");
            }
            else if (!outputKeys.Add(outputKey))
            {
                AddError(
                    result,
                    "target.outputDuplicate",
                    "outputKey",
                    entry.Id,
                    "Output key '" + outputKey + "' is used by more than one selected target.");
            }

            string label = string.IsNullOrWhiteSpace(entry.Name) ? descriptor.Label : entry.Name.Trim();
            if (ContainsControl(label))
            {
                AddError(
                    result,
                    "target.label",
                    "name",
                    entry.Id,
                    descriptor.Label + " display name contains control characters.");
            }

            int targetIndex = plan.Targets.Count;
            plan.Targets.Add(new BuildTargetPlan
            {
                Id = entry.Id,
                TargetValue = entry.TargetValue,
                DescriptorId = descriptor.Id,
                Label = label,
                OutputKey = outputKey
            });

            if (!configuration.Upload)
                return;

            if (entry.PublishItch)
            {
                wantsItch = true;
                string channel = string.IsNullOrWhiteSpace(entry.Channel)
                    ? descriptor.DefaultChannel
                    : entry.Channel.Trim();
                if (!ButlerCommand.IsValidChannel(channel))
                {
                    AddError(
                        result,
                        "itch.channel",
                        "channel",
                        entry.Id,
                        descriptor.Label + " itch channel contains unsupported characters.");
                }

                var payload = new ItchPublishPayload
                {
                    Owner = BuildPaths.ToItchSlug(configuration.ItchOwner),
                    Project = BuildPaths.ToItchSlug(configuration.ItchProject),
                    Channel = channel,
                    ButlerPath = configuration.ButlerPath ?? string.Empty,
                    Version = plan.Version
                };
                plan.PublishJobs.Add(new PublishJob
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProviderId = PublisherIds.Itch,
                    Scope = PublishJobScope.Artifact,
                    TargetIndex = targetIndex,
                    Itch = payload
                });
            }

            if (!entry.PublishSteam)
                return;

            wantsSteam = true;
            if (!descriptor.SupportsSteam)
            {
                AddError(
                    result,
                    "steam.target",
                    "publishSteam",
                    entry.Id,
                    descriptor.Label + " cannot be uploaded as a Steam desktop depot.");
                return;
            }

            string depotId;
            if (!SteamCommand.TryParseSteamId(entry.SteamDepotId, out depotId))
            {
                AddError(
                    result,
                    "steam.depot",
                    "steamDepotId",
                    entry.Id,
                    descriptor.Label + " depot ID must be a positive integer.");
                return;
            }

            if (!steamDepots.Add(depotId))
            {
                AddError(
                    result,
                    "steam.depotDuplicate",
                    "steamDepotId",
                    entry.Id,
                    "Depot " + depotId + " is used by more than one platform.");
            }

            steamPayload.Targets.Add(new SteamTargetPayload
            {
                TargetIndex = targetIndex,
                DepotId = depotId
            });
        }

        static void ValidateItch(BuildConfiguration configuration, BuildPlanResult result)
        {
            string owner;
            string project;
            if (!BuildPaths.TryNormalizeItchSlug(configuration.ItchOwner, out owner))
                AddError(result, "itch.owner", "itchOwner", null, "Enter a valid itch owner slug.");
            if (!BuildPaths.TryNormalizeItchSlug(configuration.ItchProject, out project))
                AddError(result, "itch.project", "itchProject", null, "Enter a valid itch project slug.");
            if (string.IsNullOrEmpty(configuration.ButlerPath) || !File.Exists(configuration.ButlerPath))
                AddError(result, "itch.butler", "butlerPath", null, "Locate Butler before uploading to itch.io.");
        }

        static void AddSteamJob(
            BuildConfiguration configuration,
            SteamPublishPayload payload,
            BuildPlan plan,
            BuildPlanResult result)
        {
            if (string.IsNullOrWhiteSpace(payload.Username))
                AddError(result, "steam.username", "steamUser", null, "Enter a Steam username.");
            else if (ContainsControlOrQuote(payload.Username))
                AddError(
                    result,
                    "steam.username.unsafe",
                    "steamUser",
                    null,
                    "Steam username contains unsupported control or quote characters.");

            string appId;
            if (!SteamCommand.TryParseSteamId(configuration.SteamAppId, out appId))
                AddError(result, "steam.appId", "steamAppId", null, "Steam App ID must be a positive integer.");
            payload.AppId = appId ?? string.Empty;

            if (string.IsNullOrEmpty(payload.SteamCmdPath) || !File.Exists(payload.SteamCmdPath))
                AddError(result, "steam.steamcmd", "steamCmdPath", null, "Locate steamcmd before uploading to Steam.");

            if (ContainsControlOrQuote(payload.Branch))
                AddError(result, "steam.branch", "steamBranch", null, "Steam branch contains unsupported control or quote characters.");

            if (payload.Targets.Count == 0)
            {
                AddError(
                    result,
                    "steam.targets",
                    "publishSteam",
                    null,
                    "Steam publishing needs Windows, Mac, or Linux. WebGL is not uploaded to Steam.");
                return;
            }

            var job = new PublishJob
            {
                Id = Guid.NewGuid().ToString("N"),
                ProviderId = PublisherIds.Steam,
                Scope = PublishJobScope.Aggregate,
                Steam = payload
            };
            for (int i = 0; i < payload.Targets.Count; i++)
                job.TargetIndices.Add(payload.Targets[i].TargetIndex);
            plan.PublishJobs.Add(job);
        }

        static void ValidateEnvironment(BuildEnvironmentSnapshot environment, BuildPlanResult result)
        {
            if (environment.IsPlaying)
                AddError(result, "editor.playing", null, null, "Exit Play Mode before starting a build.");
            if (environment.IsCompiling)
                AddError(result, "editor.compiling", null, null, "Wait for script compilation to finish before starting a build.");
            if (environment.Scenes == null || environment.Scenes.Length == 0)
                AddError(result, "scenes.empty", "scenes", null, "Enable at least one scene in Build Settings.");
        }

        static string NormalizeVersion(string version, BuildPlanResult result)
        {
            int major;
            int minor;
            int patch;
            if (!GameVersion.TryParse(version, out major, out minor, out patch))
            {
                AddError(
                    result,
                    "version.invalid",
                    "version",
                    null,
                    "Version must be major.minor.patch using non-negative integers.");
                return string.Empty;
            }

            return major + "." + minor + "." + patch;
        }

        static string NormalizeGameName(string gameName, BuildPlanResult result)
        {
            if (string.IsNullOrWhiteSpace(gameName))
            {
                AddError(result, "gameName.empty", "gameName", null, "Enter a game title.");
                return string.Empty;
            }

            string value = gameName.Trim();
            if (ContainsControlOrSeparator(value))
            {
                AddError(
                    result,
                    "gameName.unsafe",
                    "gameName",
                    null,
                    "Game title cannot contain path separators or control characters.");
            }

            return value;
        }

        static string[] CloneScenes(string[] scenes)
        {
            if (scenes == null)
                return new string[0];

            var copy = new string[scenes.Length];
            Array.Copy(scenes, copy, scenes.Length);
            return copy;
        }

        static bool ContainsControlOrSeparator(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]) || value[i] == '/' || value[i] == '\\')
                    return true;
            }

            return false;
        }

        static bool ContainsControlOrQuote(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]) || value[i] == '"')
                    return true;
            }

            return false;
        }

        static bool ContainsControl(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                    return true;
            }

            return false;
        }

        static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        static void AddError(
            BuildPlanResult result,
            string code,
            string field,
            string targetId,
            string message)
        {
            AddIssue(result, ValidationSeverity.Error, code, field, targetId, message);
        }

        static void AddWarning(
            BuildPlanResult result,
            string code,
            string field,
            string targetId,
            string message)
        {
            AddIssue(result, ValidationSeverity.Warning, code, field, targetId, message);
        }

        static void AddIssue(
            BuildPlanResult result,
            ValidationSeverity severity,
            string code,
            string field,
            string targetId,
            string message)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = severity,
                Code = code,
                Field = field,
                TargetId = targetId,
                Message = message
            });
        }
    }
}
