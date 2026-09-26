# Recovery, logs, and cleanup

## Queue journal

The current queue is atomically journaled at:

```text
Library/BuildAndPublishTool/queue.json
```

Its previous revision is retained as `queue.json.previous`. Corrupt journals
are timestamped and preserved before recovery is attempted.

After a domain reload or Editor restart:

- readiness checks may safely run again
- target switching resumes
- an interrupted Unity build is recorded as failed and the prior artifact stays
  in place
- an interrupted upload is recorded as `Interrupted` and must be reconciled in
  itch.io or Steamworks before another upload
- the original active build target and Android bundle mode are restored during
  finalization when possible

## Artifacts

New builds are written under a queue-specific `.in-progress` folder, then moved
into place. Promotion has a small journal and queue-specific replacement folder.
The prior successful artifact remains as `<artifact>.previous`.

If the final artifact is absent after a crash, the replacement or `.previous`
artifact is restored before another promotion.

## Run diagnostics

Each run writes:

```text
Builds/.build-and-publish/runs/<queue-id>/run.log
Builds/.build-and-publish/runs/<queue-id>/run.json
```

`run.json` contains package and Unity versions, timestamps, durations, plan
inputs, artifact paths and sizes, process outcome flags, and independent
publisher results. Sensitive-looking process output is redacted before
persistence, and retained process output is bounded.

`Builds/last-build.log` is a convenience summary and pointer to the
authoritative per-run files. `history.json` indexes the latest 50 records but
does not delete older run folders.

## Cleanup policy

Nothing is removed automatically. Explicit commands are under
**Tools > Build and Publish > Cleanup**:

- failed/interrupted `.in-progress` work
- Steam chunk cache
- run logs and history
- `.previous` artifact backups

Cleanup commands are disabled while a queue is active and ask before deleting.
