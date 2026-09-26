# Settings ownership and migration

## Project-owned settings

`ProjectSettings/BuildAndPublishToolSettings.asset` is intended for source
control. It contains:

- local game/artifact name
- itch owner and project slug
- Steam App ID and optional branch
- build targets, stable output keys, channels, depot mappings, inclusion, and
  publisher choices

Changing one Unity project cannot change these values in another project.

## Machine-local settings

The following remain in `EditorPrefs` because they identify a workstation or
user account:

- Butler executable path
- steamcmd executable path
- Steam username

Passwords, Steam Guard codes, itch API keys, and access tokens are neither
requested nor stored.

## Legacy migration

Version 2 does not silently import old machine-wide project destinations.
Opening **Tools > Build and Publish** shows a warning when legacy keys exist and
the project has no profile.

- **Import legacy settings** copies the values after review.
- **Use project defaults** creates a clean profile from Unity Player Settings.

The existing project settings file is backed up before each persisted change.
Profiles written by a newer schema are read-only until the package is updated.

## Stable output keys

Each target has a stable output key under **Advanced**. It controls the artifact
folder and publisher identity. Changing Unity's build target resets
platform-derived channel/depot bindings but does not silently rename this key.
