using System;
using System.IO;
using System.Text.RegularExpressions;

internal static class Program
{
    static int Main(string[] args)
    {
        string root = args.Length == 0 ? Directory.GetCurrentDirectory() : args[0];
        string packageText = File.ReadAllText(Path.Combine(root, "package.json"));
        string version = Match(packageText, "\"version\"\\s*:\\s*\"([^\"]+)\"");
        if (!Regex.IsMatch(version, "^\\d+\\.\\d+\\.\\d+$"))
            return Fail("package.json version must be SemVer.");

        string changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        if (changelog.IndexOf("## [" + version + "]", StringComparison.Ordinal) < 0)
            return Fail("CHANGELOG.md is missing version " + version + ".");

        string readme = File.ReadAllText(Path.Combine(root, "README.md"));
        if (readme.IndexOf("#v" + version, StringComparison.Ordinal) < 0)
            return Fail("README.md does not pin tag v" + version + ".");

        if (!File.Exists(Path.Combine(root, "Documentation~", "index.md")))
            return Fail("Documentation~ is missing.");
        if (!File.Exists(Path.Combine(root, "TestProject~", "Packages", "manifest.json")))
            return Fail("Unity test host is missing.");

        Console.WriteLine("Package metadata is consistent for " + version + ".");
        return 0;
    }

    static string Match(string text, string pattern)
    {
        Match match = Regex.Match(text, pattern);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
