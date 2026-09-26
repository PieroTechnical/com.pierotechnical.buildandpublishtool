# Build and Publish

Build and Publish is a Unity Editor package for recoverable, sequential builds
and explicit publishing to itch.io and Steam.

![Build and Publish window](https://github.com/user-attachments/assets/fafe03cd-a4f8-4001-8d8e-820f00dcb532)

## Highlights

- Project-owned settings under `ProjectSettings`, separate from local executable
  paths and the local Steam username.
- Immutable preflight plans that snapshot scenes, version, Android bundle mode,
  targets, output paths, and publisher jobs.
- A persisted queue under `Library/BuildAndPublishTool` that resumes after
  compilation, domain reload, or an Editor restart.
- Sequential builds for every non-obsolete Unity `BuildTarget`; common desktop,
  WebGL, Android, and iOS targets have explicit output strategies.
- itch.io publishing through Butler and aggregate SteamPipe publishing through
  steamcmd. Failed builds are never published.
- Asynchronous external tools with bounded output, timeouts, cancellation,
  credential-safe logs, and interrupted-upload reconciliation.
- Per-run logs and JSON records under
  `Builds/.build-and-publish/runs/<queue-id>/`.
- Crash-safe artifact replacement. The prior successful artifact remains in a
  `.previous` folder until explicitly cleaned.

## Requirements

- Unity 2020.3 or newer
- [Butler](https://itchio.itch.io/butler) for itch.io publishing
- [steamcmd](https://developer.valvesoftware.com/wiki/SteamCMD) for Steam
  publishing

The package never asks for, stores, or passes a Steam password, Steam Guard
code, itch API key, or access token. Complete interactive login in the tool's
own terminal window.

## Install

Pin a release tag in Package Manager:

```text
https://github.com/PieroTechnical/com.pierotechnical.buildandpublishtool.git#v2.0.0
```

Open the window from **Tools > Build and Publish**.

## First setup

1. Review the project profile migration notice. Import legacy settings only
   after confirming they belong to this project.
2. Set the local game title and version.
3. Add targets, choose **Include in batch**, and configure each stable output
   key under **Advanced**.
4. Configure itch.io and Steam even before enabling them on a target.
5. Locate Butler or steamcmd, complete interactive login, then use the explicit
   **Verify / Refresh** action.
6. Review inline preflight results and start a build.

Project-owned values are versioned with the project:

- game/artifact name
- itch owner and project slug
- Steam App ID and branch
- target list, output keys, channels, depots, and publish choices

Machine-local values are not written to project settings:

- Butler path
- steamcmd path
- Steam username

## Output and recovery

Successful artifacts are written to:

```text
Builds/<game>/<version>/<output-key>/
```

The active queue journal is stored at:

```text
Library/BuildAndPublishTool/queue.json
```

An Editor reload during a build records that build as interrupted. An Editor
reload during a non-idempotent upload records an interrupted publisher result
and requires checking the backend; it never silently retries the upload.

Use **Tools > Build and Publish > Cleanup** to explicitly remove failed work,
Steam chunk caches, run history, or `.previous` artifacts. Nothing is removed
automatically.

## Steam behavior

- Windows, macOS, and Linux artifacts can be mapped to depots.
- A single aggregate app build contains only successful mapped artifacts.
- A configured branch is set live only when every mapped Steam target built
  successfully.
- Blank branches upload without setting live.
- The `default` branch must be set live in Steamworks.
- Exit code 0 is insufficient: the upload must also report positive SteamPipe
  build completion evidence.

## Supported Editor API

The supported API is intentionally small:

- `BuildAndPublish.TryCreatePlan`
- `BuildAndPublish.TrySubmit`
- `BuildAndPublish.CurrentRun`
- `BuildAndPublish.HasActiveRun`
- `BuildAndPublish.Cancel`
- `BuildAndPublish.RunChanged`

All other package types are implementation details. See
[`Documentation~/api.md`](Documentation~/api.md).

## Tests

Add the package to `testables` in the host project's `Packages/manifest.json`:

```json
{
  "testables": [
    "com.pierotechnical.buildandpublishtool"
  ]
}
```

Run Edit Mode tests. Tests use fake/local process fixtures only and never contact
itch.io or Steam.

More detail is available in [`Documentation~`](Documentation~/index.md), and
release changes are listed in [`CHANGELOG.md`](CHANGELOG.md).

## License

[MIT](LICENSE)
