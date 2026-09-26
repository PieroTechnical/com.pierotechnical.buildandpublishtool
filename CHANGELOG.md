# Changelog

All notable changes follow [Keep a Changelog](https://keepachangelog.com/) and
this package follows [Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-09-25

### Added

- Versioned project-scoped build profiles with reviewed legacy migration.
- Immutable preflight plans for scenes, targets, outputs, and publisher jobs.
- A persisted, cancellable queue state machine with reload and restart recovery.
- Independent itch.io and Steam publish jobs with typed results.
- Bounded asynchronous process execution, output redaction, and truthful
  cancellation reporting.
- Crash-recoverable artifact promotion with retained `.previous` artifacts.
- Per-run text logs, JSON records, history, timings, and explicit cleanup tools.
- Responsive run status, inline validation, publisher refresh actions, and
  accessible target controls.
- A small supported Editor API through `BuildAndPublish`.
- Unity 2020.3 and Unity 6 package test projects and CI coverage.

### Changed

- Renamed the window and menu to **Build and Publish**.
- Project identity, destinations, targets, and publisher choices are now stored
  under `ProjectSettings`; executable paths and Steam username remain local.
- Butler publishes directly from successful artifacts with explicit exclusion
  arguments.
- Steam requires positive SteamPipe completion evidence, not only exit code 0.
- Accidental helper and persistence APIs are now internal.

### Removed

- The implicit list-count queue and publisher flag abstraction.
- Global `EditorPrefs` ownership of project publishing destinations.
- Blocking upload execution on the Unity Editor thread.

### Migration

- Version 2 is a major release because previously public implementation helpers
  are no longer supported API.
- Open **Tools > Build and Publish**, review the migration warning, then choose
  **Import legacy settings** or **Use project defaults**.

## [1.0.0]

- Initial versioned multi-platform build, itch.io, and Steam publishing tool.
