using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace Pierotechnical.BuildAndUploadTool.Editor
{
    internal enum BuildOutputKind
    {
        Directory,
        WindowsExecutable,
        MacApplication,
        LinuxExecutable,
        WebDirectory,
        AndroidPackage
    }

    internal sealed class BuildTargetDescriptor
    {
        internal BuildTargetDescriptor(
            string id,
            string label,
            BuildTarget target,
            BuildOutputKind outputKind,
            string defaultChannel,
            string steamPlatformId)
        {
            Id = id;
            Label = label;
            Target = target;
            OutputKind = outputKind;
            DefaultChannel = defaultChannel;
            SteamPlatformId = steamPlatformId;
        }

        internal string Id { get; private set; }
        internal string Label { get; private set; }
        internal BuildTarget Target { get; private set; }
        internal BuildOutputKind OutputKind { get; private set; }
        internal string DefaultChannel { get; private set; }
        internal string SteamPlatformId { get; private set; }
        internal bool SupportsSteam { get { return !string.IsNullOrEmpty(SteamPlatformId); } }
        internal bool IsGeneric { get { return OutputKind == BuildOutputKind.Directory && !IsKnownDirectoryTarget(Target); } }

        static bool IsKnownDirectoryTarget(BuildTarget target)
        {
            return target == BuildTarget.iOS;
        }
    }

    internal static class BuildTargetCatalog
    {
        static readonly BuildTargetDescriptor[] Known =
        {
            new BuildTargetDescriptor("windows", "Windows", BuildTarget.StandaloneWindows64, BuildOutputKind.WindowsExecutable, "windows", "windows"),
            new BuildTargetDescriptor("windows-32", "Windows 32-bit", BuildTarget.StandaloneWindows, BuildOutputKind.WindowsExecutable, "windows-32", "windows"),
            new BuildTargetDescriptor("mac", "Mac", BuildTarget.StandaloneOSX, BuildOutputKind.MacApplication, "mac", "mac"),
            new BuildTargetDescriptor("linux", "Linux", BuildTarget.StandaloneLinux64, BuildOutputKind.LinuxExecutable, "linux", "linux"),
            new BuildTargetDescriptor("webgl", "WebGL", BuildTarget.WebGL, BuildOutputKind.WebDirectory, "webgl", null),
            new BuildTargetDescriptor("android", "Android", BuildTarget.Android, BuildOutputKind.AndroidPackage, "android", null),
            new BuildTargetDescriptor("ios", "iOS", BuildTarget.iOS, BuildOutputKind.Directory, "ios", null)
        };

        static BuildTarget[] allTargets;

        internal static BuildTargetDescriptor Find(BuildTarget target)
        {
            for (int i = 0; i < Known.Length; i++)
            {
                if (Known[i].Target == target)
                    return Known[i];
            }

            string label = target.ToString();
            return new BuildTargetDescriptor(
                BuildPaths.ToPortableKey(label),
                label,
                target,
                BuildOutputKind.Directory,
                BuildPaths.ToItchSlug(label),
                null);
        }

        internal static BuildTargetDescriptor Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            for (int i = 0; i < Known.Length; i++)
            {
                if (string.Equals(Known[i].Id, id, StringComparison.Ordinal))
                    return Known[i];
            }

            return null;
        }

        internal static BuildTarget[] AllTargets()
        {
            if (allTargets == null)
            {
                Array values = Enum.GetValues(typeof(BuildTarget));
                var targets = new List<BuildTarget>();
                var seen = new HashSet<int>();
                for (int i = 0; i < values.Length; i++)
                {
                    BuildTarget target = (BuildTarget)values.GetValue(i);
                    int value = (int)target;
                    if (target == BuildTarget.NoTarget || seen.Contains(value) || IsObsolete(target))
                        continue;

                    seen.Add(value);
                    targets.Add(target);
                }

                targets.Sort((left, right) =>
                    string.Compare(Find(left).Label, Find(right).Label, StringComparison.OrdinalIgnoreCase));
                allTargets = targets.ToArray();
            }

            var copy = new BuildTarget[allTargets.Length];
            Array.Copy(allTargets, copy, allTargets.Length);
            return copy;
        }

        internal static BuildTargetDescriptor[] SeedDescriptors()
        {
            var descriptors = new BuildTargetDescriptor[4];
            descriptors[0] = Find(BuildTarget.StandaloneWindows64);
            descriptors[1] = Find(BuildTarget.StandaloneOSX);
            descriptors[2] = Find(BuildTarget.StandaloneLinux64);
            descriptors[3] = Find(BuildTarget.WebGL);
            return descriptors;
        }

        static bool IsObsolete(BuildTarget target)
        {
            FieldInfo field = typeof(BuildTarget).GetField(target.ToString());
            return field != null
                && field.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0;
        }
    }
}
