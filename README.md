![image](https://github.com/user-attachments/assets/fafe03cd-a4f8-4001-8d8e-820f00dcb532)

# Build Automation and Publish Tool

The Build Automation and Publish Tool is a Unity Editor extension that streamlines the process of building and uploading your Unity game to itch.io. This tool provides an easy-to-use interface to manage versioning, build for multiple platforms, and upload your game builds seamlessly.

## Features

- **Version Management:** Automatically update and save version numbers with options to increment minor and fix versions.
- **Excludes Unwanted Folders:** `_DoNotShip` folders are excluded from the zipping process, meaning they do not get uploaded to Itch.io.
- **Multi-Platform Build:** Currently supports building for Windows, with Mac and Linux on the immediate horizon.
- **Automated Uploads:** Integrates with itch.io's Butler tool to automate your game build upload.
- **Versioned Backups:** Stores backups of each version in your build directory so you can go back and experience your game at previous stages.
- **User-Friendly Interface:** Provides a clean and intuitive GUI within the Unity Editor to manage builds and uploads efficiently.

## Installation

1. Download or clone the repository into your Unity project's `Packages` folder.

Alternatively, in unity editor inside package manager:
1. Hit `(+)` and select `Add package from Git URL` 
2. Paste the `git URL` for this package: https://github.com/PieroTechnical/com.pierotechnical.buildandpublishtool.git and hit `Add`

After installation, navigate to `Tools > Build Automation and Publish Tool` to open the tool window.

*The first time you use the tool, it will ask you to locate the **butler.exe** executable.*
## Usage

- **Set Up:** Ensure the Butler executable path is set up correctly. If not, you will be prompted to locate it. Currently, you'll need to make sure that the organization and game name in your Unity Project Settings matches those of your itch.io project (ie. [organization].itch.io/[game name]).
- **Manage Version:** Use the version controls to update the version number as needed. The version is automatically saved to `Assets/version.txt`.
- **Select Platforms:** Choose the platforms you want to build for (Windows, Mac, Linux).
- **Build and Upload:** Click the build buttons to start building and uploading your game to itch.io. The tool will handle the rest for you.

## Requirements

- Unity 2018.4 or higher
- [Butler (itch.io command-line tool)](https://itchio.itch.io/butler)

## Contribution

Contributions are welcome! If you have suggestions for improvements or find any issues, please create a new issue or submit a pull request.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

## Contact

For any questions or feedback, please contact [daniel@pierotechnical.com](mailto:daniel@pierotechnical.com).
