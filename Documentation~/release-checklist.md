# Release checklist

## Automated gates

- Package metadata is valid JSON.
- Package version, changelog heading, and Git tag match.
- The package imports cleanly in Unity 2020.3 and Unity 6.
- Edit Mode tests pass on Windows, macOS, and Linux.
- Package validation reports no errors.
- Migration, queue recovery, path containment, promotion, process output
  bounds, quoting, redaction, and publisher selection tests pass.
- Tests use fake/local processes and make no live itch.io or Steam calls.
- The test host has no unexpected tracked changes after import and tests.

## Manual Editor checks

- Open **Tools > Build and Publish** without creating `Assets/version.txt`,
  changing project settings, or launching a lookup.
- Check light and dark Editor themes.
- Dock the window narrowly and verify rows stack without clipping.
- Navigate foldouts, target overflow actions, fields, and buttons by keyboard.
- Confirm tooltips, selectable paths, inline errors, run progress, and
  cancellation wording.
- Verify publisher setup remains reachable when no target enables it.
- Confirm target removal and Steam branch set-live require explicit
  confirmation.
- Run a local fake-process build flow through completion, cancellation,
  timeout, reload recovery, and cleanup.

## Publish

1. Update `package.json` and `CHANGELOG.md`.
2. Update the README screenshot when the window changes materially.
3. Run every automated and manual gate.
4. Create an annotated `v<version>` tag.
5. Verify the tag-pinned Package Manager URL in a clean project.
