# Build and Publish manual

Build and Publish turns the current Unity project configuration into a validated
immutable plan, runs one Unity build at a time, and publishes only successful
artifacts.

## Guides

- [Settings ownership and migration](settings.md)
- [Target support and output behavior](target-support.md)
- [Recovery, logs, and cleanup](recovery-and-logs.md)
- [Supported Editor API](api.md)
- [Troubleshooting](troubleshooting.md)
- [Release checklist](release-checklist.md)

## Safety model

- Project settings and machine settings have separate stores.
- The queue writes every phase transition before non-idempotent work.
- `BuildPipeline.BuildPlayer` cannot be interrupted; cancellation becomes
  **Stop After Current Build** during that phase.
- External tools are asynchronous, bounded, serialized per executable, and
  cancellable.
- Interrupted uploads are not retried automatically.
- Artifact promotion preserves a recoverable previous successful build.
- Cleanup is always explicit.
