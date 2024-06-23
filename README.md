#Build and Upload Automation Tool
Build and Upload Automation Tool is a Unity Editor extension that streamlines the process of building and uploading your Unity game to itch.io. This tool provides an easy-to-use interface to manage versioning, build for multiple platforms, and upload your game builds seamlessly.

#Features
Version Management: Automatically update and save version numbers with options to increment minor and fix versions.
Multi-Platform Build: Support for building and uploading for Windows, Mac, and Linux platforms.
Automated Uploads: Integrates with itch.io's Butler tool to automate the upload of your game builds.
Versioned Backups: Stores backup zip files with version numbers in a dedicated _versions subfolder for better organization.
User-Friendly Interface: Provides a clean and intuitive GUI within the Unity Editor to manage builds and uploads efficiently.
Installation
Download or clone the repository into your Unity project's Assets folder.
Open Unity and navigate to Tools > Build and Upload Automation Tool to open the tool window.
Usage
Set Up: Ensure the Butler executable path is set up correctly. If not, you will be prompted to locate it.
Manage Version: Use the version controls to update the version number as needed. The version is automatically saved to Assets/version.txt.
Select Platforms: Choose the platforms you want to build for (Windows, Mac, Linux).
Build and Upload: Click the build buttons to start building and uploading your game to itch.io. The tool will handle the rest, including creating a zip file and uploading it.
Requirements
Unity 2018.4 or higher
Butler (itch.io command-line tool)

#Contribution
Contributions are welcome! If you have suggestions for improvements or find any issues, please create a new issue or submit a pull request.

#License
This project is licensed under the MIT License. See the LICENSE file for details.

#Contact
For any questions or feedback, please contact daniel@pierotechnical.com