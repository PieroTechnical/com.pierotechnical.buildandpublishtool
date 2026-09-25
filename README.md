![image](https://github.com/user-attachments/assets/fafe03cd-a4f8-4001-8d8e-820f00dcb532)

# Build Automation and Publish Tool

The Build Automation and Publish Tool is a Unity Editor extension that builds a project for Windows, Mac, Linux, and WebGL, keeps each successful build in a versioned folder, and can upload that build to itch.io with Butler or to Steam with steamcmd.

## Features

- **Version management:** Edit a `major.minor.patch` version, or increment the minor or patch number. The value is saved to `Assets/version.txt` and to Player Settings as the bundle version.
- **Versioned builds:** A successful build is stored at `Builds/<game>/<version>/<platform>/`. The previous folder for that version is replaced only after the new build succeeds.
- **Upload exclusions:** Folders whose names end with `_DoNotShip`, `BackUpThisFolder_ButDontShipItWithYourGame`, or `BurstDebugInformation_DoNotShip` stay in the local build and are left out of itch.io and Steam uploads.
- **Multi-platform builds:** Windows, Mac, Linux, and WebGL. The tool switches build targets and resumes after the Editor reloads, so a batch can finish more than the first platform.
- **Build, or build and upload:** Each platform can be built locally, or built and uploaded to the destinations that are turned on (itch.io, Steam, or both). Selected platforms can be run the same way. A failed build is not uploaded.
- **Butler uploads:** The itch.io upload sends the staged build folder to Butler (`butler push`), which keeps macOS and Linux executable permissions intact. The itch target is the username and game title shown in the window.
- **Steam uploads:** After every selected platform has finished, steamcmd uploads the successful Windows, Mac, and Linux builds as one SteamPipe app build. WebGL is not uploaded to Steam. Leave the branch blank to upload without setting the build live. The `default` branch cannot be set live from steamcmd. Later uploads of the same App ID reuse a chunk cache in `Builds/.steam-cache`. The tool does not store a Steam password or Steam Guard code, and it leaves `steam_appid.txt` out of the depot.

## Installation

1. Download or clone the repository into your Unity project's `Packages` folder.

Alternatively, in the Unity Editor Package Manager:

1. Hit `(+)` and select `Add package from Git URL`
2. Paste the git URL for this package: https://github.com/PieroTechnical/com.pierotechnical.buildandpublishtool.git and hit `Add`

After installation, navigate to `Tools > Build Automation and Publish Tool` to open the tool window.

Use **Locate Butler** to choose the Butler executable. On Windows that file is `butler.exe`. On macOS and Linux it has no extension.

Use **Locate steamcmd** to choose the steamcmd executable before a Steam upload.

## Usage

- **Butler:** Set the Butler executable before uploading to itch.io. The path is stored for this machine. Log in once with `butler login` before the first upload. If Butler is missing or not logged in, you can still build locally or upload to Steam.
- **Steam:** Set the steamcmd executable, Steam username, and App ID. Leaving the App ID field loads that app’s depots and branches into dropdowns beside each depot field and the branch field. A typed value is what the upload uses, including an ID or branch that is not in the list. An empty depot field is filled only when the app has exactly one depot for that OS. Log in once with **Login with steamcmd** and finish the password and Steam Guard prompt in that window. The upload check uses that saved login and does not type a password. A blank branch uploads the build without setting it live. `public` and `default` stay in the branch list, and `default` must be set live on the Steamworks builds page. Another branch is set live only when every desktop platform selected for Steam built successfully. Removing `Builds/.steam-cache` is safe and makes the next upload complete.
- **Version:** Use `major.minor.patch` only. Increment buttons and leaving the version field save `Assets/version.txt` and Player Settings `bundleVersion`.
- **Itch target:** Itch username and game title are the itch.io user and game slug (`lowercase`, spaces become hyphens), for example `my-studio/cool-game`. The game title also names the local build folder. The tool remembers them on this machine. Unity product name and company name stay in Player Settings.
- **Publish:** Turn on itch.io, Steam, or both. **Build** does not upload. **Build and Upload** uploads to each destination that is on.
- **Platforms:** Turn on the platforms to include in a batch, or use a platform's own Build / Build and Upload button. WebGL can upload to itch.io and is left out of the Steam app build.
- **Output:** Successful builds are written to `Builds/<game>/<version>/<platform>/`. A log for the latest run is written to `Builds/last-build.log`.

## Tests

Edit Mode tests cover version parsing, output paths, upload exclusions, Butler arguments, SteamPipe scripts, and the rule that a failed build is not uploaded. To run them, add this package to `testables` in the project `Packages/manifest.json`:

```json
"testables": [
  "com.pierotechnical.buildandpublishtool"
]
```

## Requirements

- Unity 2020.3 or higher
- [Butler (itch.io command-line tool)](https://itchio.itch.io/butler), for itch.io uploads
- [steamcmd](https://developer.valvesoftware.com/wiki/SteamCMD), for Steam uploads

## Contribution

Contributions are welcome! If you have suggestions for improvements or find any issues, please create a new issue or submit a pull request.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

## Contact

For any questions or feedback, please contact [daniel@pierotechnical.com](mailto:daniel@pierotechnical.com).
