# Target support

Builds remain sequential because Unity target switching and
`BuildPipeline.BuildPlayer` mutate global Editor state.

## Explicit output strategies

- Windows 64-bit and 32-bit: executable output
- macOS: `.app` output
- Linux 64-bit: executable output
- WebGL: directory output
- Android: `.apk` or `.aab`, snapshotted when the plan is created
- iOS: directory output

## Generic targets

Every non-obsolete `BuildTarget` can be selected. Targets without an explicit
descriptor use a generic directory strategy and produce a preflight warning.
Verify their platform-specific output requirements before relying on that
artifact in automation.

Preflight reports missing Editor module support, unsafe or duplicate output
keys, empty scene lists, and unsupported publisher mappings across all selected
targets at once.

## Publisher support

- itch.io accepts any successfully built artifact with a valid channel.
- Steam accepts descriptors mapped to Windows, macOS, or Linux.
- WebGL, Android, iOS, and generic targets cannot be Steam depots.
- Steam branch set-live is withheld if any mapped target fails.
