# Installing Wysicraft

## Choose a download

| Download | Use it when |
| --- | --- |
| `Wysicraft-1.2.0-Setup.exe` | You want a normal Windows install with a Start-menu shortcut and `.wysicraftproj` files that open on double-click. |
| `Wysicraft-1.2.0-win-x64.zip` | You want a portable copy you can run from any folder, with no installation. |

Both contain the same editor. The editor bundles its own .NET runtime, so you don't need to install .NET.

## Installer

1. Run `Wysicraft-1.2.0-Setup.exe`. It installs for your Windows user only and doesn't need administrator rights.
2. Choose your options: **Create a desktop shortcut** (off by default) and **Register Wysicraft project files with this app** (on by default, so `.wysicraftproj` files open in Wysicraft).
3. Launch **Wysicraft** from the Start menu.

To upgrade, run a newer installer over the old one. Uninstalling removes the application but keeps your projects, recovery drafts, workspace layout, keyboard shortcuts and downloaded Minecraft test files. Keep your projects outside the installation folder.

## Portable ZIP

1. Extract the ZIP anywhere, for example `C:\Tools\Wysicraft`.
2. Run `Designer\Wysicraft.Designer.exe`.
3. Keep the folders together: `Designer`, `Runtime`, `TestEnvironment` and `Docs`. The Minecraft test uses `TestEnvironment`, and exports use the runtime in `Runtime`.

## Optional: Java 21 for Minecraft testing

The editor itself doesn't need Java. The [[Minecraft test|Testing-in-Minecraft]] launches a real Minecraft instance, which needs **Java 21**. Any Java 21 distribution works (for example Microsoft Build of OpenJDK, Eclipse Temurin or JetBrains Runtime). Wysicraft checks the Java version itself, so a wrong `JAVA_HOME` doesn't matter; you can also pick the folder in the test window.

## First launch

- The window opens with an empty project called **Untitled** and a screen called `main`.
- Panels are arranged in the default layout. You can move them; see [[The workspace|The-Workspace]].
- If Wysicraft closed unexpectedly last time, the Output panel tells you a recovery draft is available. See [[Projects and screens|Projects-and-Screens#recovering-unsaved-work]].

Next: [[Quick start tutorial|Quick-Start-Tutorial]].
