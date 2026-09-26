# Supported Editor API

The public API is in `Pierotechnical.BuildAndUploadTool.Editor` and is available
only in the Unity Editor.

## Create and submit a plan

```csharp
var options = new BuildPlanOptions
{
    Publish = false,
    Version = "2.1.0"
};
options.TargetIds.Add("windows");

ValidatedBuildPlan plan;
BuildValidationMessage[] issues;
if (BuildAndPublish.TryCreatePlan(options, out plan, out issues))
{
    string error;
    BuildAndPublish.TrySubmit(plan, out error);
}
```

An empty `TargetIds` collection selects targets marked **Include in batch**.
`TryCreatePlan` snapshots current project and machine settings. A
`ValidatedBuildPlan` cannot be constructed directly or mutated.

## Observe and cancel

```csharp
BuildAndPublish.RunChanged += OnRunChanged;

void OnRunChanged()
{
    BuildRunSnapshot run = BuildAndPublish.CurrentRun;
    if (run != null)
        UnityEngine.Debug.Log(run.Phase + ": " + run.LatestMessage);
}
```

Use `BuildAndPublish.HasActiveRun`, `CurrentRun`, `RunChanged`, and `Cancel`.
During `BuildPipeline.BuildPlayer`, cancellation means stop after the current
build because Unity does not provide safe immediate interruption.

Snapshots contain copied target and publisher outcomes. Internal persistence,
command, filesystem, uploader, and IMGUI types are not supported API.

## Publisher extensions

Publisher registration is intentionally internal in 2.0. The provider contract
will be made public only after third-party compatibility, schema migration, and
versioning tests exist. Do not reflect into `PublisherRegistry`.
