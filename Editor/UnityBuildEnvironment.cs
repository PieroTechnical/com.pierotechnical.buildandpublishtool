using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal static class UnityBuildEnvironment
    {
        internal static BuildEnvironmentSnapshot Capture(
            IList<BuildTargetEntry> targets,
            IList<int> selectedIndices)
        {
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            var snapshot = new BuildEnvironmentSnapshot
            {
                ProjectRoot = projectRoot == null ? null : projectRoot.FullName,
                Scenes = ProjectBuilder.GetEnabledScenes(),
                AndroidAppBundle = EditorUserBuildSettings.buildAppBundle,
                ActiveBuildTargetValue = (int)EditorUserBuildSettings.activeBuildTarget,
                IsPlaying = EditorApplication.isPlayingOrWillChangePlaymode,
                IsCompiling = EditorApplication.isCompiling || EditorApplication.isUpdating
            };

            if (targets == null || selectedIndices == null)
                return snapshot;

            for (int i = 0; i < selectedIndices.Count; i++)
            {
                int index = selectedIndices[i];
                if (index < 0 || index >= targets.Count || targets[index] == null)
                    continue;

                BuildTarget target = (BuildTarget)targets[index].TargetValue;
                BuildTargetGroup group = PlatformCatalog.Group(target);
                if (!BuildPipeline.IsBuildTargetSupported(group, target))
                    snapshot.UnsupportedTargetValues.Add((int)target);
            }

            return snapshot;
        }
    }
}
