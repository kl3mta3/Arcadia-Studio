# Installing Arcadia Studio

## Choose a download

| Download | Use it when |
| --- | --- |
| `ArcadiaStudio-1.3.0-Setup.exe` | You want a normal Windows install with a Start-menu shortcut and `.arcadia` project files that open on double-click. |
| `ArcadiaStudio-1.3.0-win-x64.zip` | You want a portable copy you can run from any folder, with no installation. |

Both contain the same editor. The editor bundles its own .NET runtime, so you don't need to install .NET.

## Installer

1. Run `ArcadiaStudio-1.3.0-Setup.exe`. It installs for your Windows user only and doesn't need administrator rights.
2. Choose your options: **Create a desktop shortcut** (off by default) and **Open Arcadia Studio projects (.arcadia, and older .wysicraftproj) with this app** (on by default, so project files open in Arcadia Studio on double-click).
3. Launch **Arcadia Studio** from the Start menu.

To upgrade, run a newer installer over the old one. Uninstalling removes the application but keeps your projects, recovery drafts, workspace layout, keyboard shortcuts and downloaded Minecraft test files. Keep your projects outside the installation folder.

## Portable ZIP

1. Extract the ZIP anywhere, for example `C:\Tools\Arcadia Studio`.
2. Run `Designer\ArcadiaStudio.exe`.
3. Keep the folders together: `Designer`, `Runtime`, `TestEnvironment` and `Docs`. The Minecraft test uses `TestEnvironment`, and exports use the runtime in `Runtime`.

## Optional: Java 21 for Minecraft testing

The editor itself doesn't need Java. The [[Minecraft test|Testing-in-Minecraft]] launches a real Minecraft instance, which needs **Java 21**. Any Java 21 distribution works (for example Microsoft Build of OpenJDK, Eclipse Temurin or JetBrains Runtime). Arcadia Studio checks the Java version itself, so a wrong `JAVA_HOME` doesn't matter; you can also pick the folder in the test window.

## First launch

- The window opens with an empty project called **Untitled** and a screen called `main`.
- Panels are arranged in the default layout. You can move them; see [[The workspace|The-Workspace]].
- If Arcadia Studio closed unexpectedly last time, the Output panel tells you a recovery draft is available. See [[Projects and screens|Projects-and-Screens#recovering-unsaved-work]].

## Updates

Arcadia Studio checks for a newer version when it starts (at most once a day) and asks before doing anything:

- **Update now** downloads the new version from the [Arcadia Studio releases on GitHub](https://github.com/kl3mta3/arcadia-studio/releases) and checks it against its published checksum; a file that doesn't match is deleted and nothing is installed. Arcadia Studio then asks you to save any unsaved work, closes, updates, and opens again.
  - An **installed** copy updates with the release's installer (quietly, into the same place).
  - A **portable** copy updates from the release's ZIP: the new version replaces the app's own folders (`Designer`, `Runtime`, `Docs`) and leaves anything else you keep in that folder alone.
- **Later** asks again next time. **Skip this version** doesn't mention that version again, only newer ones.

Your projects and settings aren't touched by an update: projects are wherever you saved them, and settings are in `%LOCALAPPDATA%\Arcadia Studio`.

**Help → Check for updates…** checks straight away. **Help → Check for updates automatically** turns the startup check off. The check only asks GitHub for the latest release, sending nothing but Arcadia Studio's version; offline, nothing is shown.

Next: [[Quick start tutorial|Quick-Start-Tutorial]].
