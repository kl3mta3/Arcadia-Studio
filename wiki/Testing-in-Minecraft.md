# Testing in Minecraft

The **Minecraft test** (toolbar, Ctrl+F5, or **Project → Test in Minecraft**) launches a real Minecraft 1.21.1 with NeoForge, KubeJS and your project installed, straight from the editor.

## First run

1. Click **Minecraft test**.
2. If Java 21 isn't found automatically, choose its folder. (Arcadia Studio checks the real Java version; other versions are ignored.)
3. Click **Start editor test**.

The first launch downloads Minecraft, NeoForge and dependencies and compiles the runtime. Allow **several minutes** and **several GB** of disk space. Later launches reuse everything and start much faster.

Minecraft opens in its own window with a small creative superflat world called **Arcadia Studio Test** (no mobs, weather or day/night cycle). Your screen doesn't open by itself.

If you're using the portable ZIP, keep the `TestEnvironment` folder beside `Designer`.

## Controls in the game

| Key | Does |
| --- | --- |
| **F6** | Runs your project's `.open` command |
| **F7** | Runs `.close` |
| **F8** | Opens a test-control screen with Open and Close buttons. On your project screen, F8 shows or hides **Reset** and **.close** in the corner. |

Remap them under **Options → Controls → Key Binds → Arcadia Studio Test**. These controls belong to the test only and aren't exported.

## The test window

| Control | Does |
| --- | --- |
| **.open / .close** | Runs your project's real commands as the test player. |
| **Globals** | Lists other commands that start with `your_project.` (or are linked to it). Pick one, add arguments, **Run**. |
| **Apply changes** | Sends your latest editor changes into the running game and reloads scripts. Reopen the screen afterwards. (Changed KubeJS startup scripts need Stop and Start.) |
| **Stop** | Closes the game (forced after 15 seconds if needed). |
| **Reset world** | While stopped, restores the saved starting world and keeps the old one as a backup. |
| **Logs** | Setup and game output. Script errors are shown separately, with the script and event they came from. |

## Testing an export

**Test export…** runs the game with an exported file instead of the editor's live project, so you test exactly what players get:

1. Stop any running test.
2. Choose **Test export…** and pick a project JAR or an Installation ZIP (it uses the server JAR inside and any KubeJS files).
3. F6, F7, F8 and the buttons target the project inside the JAR. Apply changes is disabled; export again and reselect to retest.

**Start editor test** switches back to the live project.

## Using other mods' commands

A KubeJS server script can link an existing command to your project so it shows under **Globals**:

```js
const Arcadia Studio = Java.loadClass('com.wysicraft.runtime.api.WysicraftApi');
Wysicraft.registerProjectCommand('my_project', 'portal');
```

The mod that provides the command must be in the test instance's `run/mods` folder.

## Where things are kept

The test instance lives in `%LOCALAPPDATA%\Arcadia Studio\MinecraftTest\1.21.1`. The world is reused across projects; only your project's files are replaced when you switch. Stop the test before switching projects.

## Script support in the test

Built-in actions, Standard Client and Standard Server scripts, and KubeJS Server scripts all run. If a project uses an unsupported combination (such as KubeJS Client scripts), the window lists it and offers to skip just those for the test; your project isn't changed.
