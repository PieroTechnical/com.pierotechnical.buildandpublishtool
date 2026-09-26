# Troubleshooting

## Preflight reports missing build support

Install the target module for the current Unity Editor version in Unity Hub,
then run preflight again.

## Butler or steamcmd is not logged in

Use the package's interactive login button. Complete password, two-factor, or
Steam Guard prompts only in that external terminal. Return to the window and
click **Verify / Refresh**.

## An upload is marked interrupted

Do not immediately retry. Open the backend link from the run status and check
whether the release or SteamPipe build completed. Reconcile or remove the
backend result before starting another upload.

## Cancellation says termination is unconfirmed

The package terminated the process it could identify, but the operating system
could not prove that every descendant stopped. Check Task Manager or the
platform process list and verify the publisher backend before retrying.

## The original build target was not restored

The run record and log explain whether switching failed or timed out. Restore
the target manually in Unity Build Settings. Built artifacts remain valid.

## A queue journal was corrupt

The package preserves timestamped corrupt files in
`Library/BuildAndPublishTool`, attempts the previous atomic revision, and shows
an actionable error in the window when recovery is impossible.

## Promotion left no final artifact

Starting another build invokes promotion recovery. It restores the
queue-specific replacement first, then `<artifact>.previous` when available.
Do not delete those folders until recovery is complete.

## Steam exited with code 0 but publishing failed

Exit code 0 is not accepted alone. The bounded output must contain a BuildID and
positive SteamPipe completion message. Scripts remain in `.in-progress/steam`
for diagnosis.
