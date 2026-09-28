# Exporting and installing

Your `.wysicraftproj` is for editing. Players get an **export**. Click **Validate** (F7) first, then **Export…** (Ctrl+E).

## Export formats

| Format | File | Best for |
| --- | --- | --- |
| **Project JAR** (default) | `<id>.jar` | Most projects. One mod JAR with your screens, images, attached scripts **and the Wysicraft runtime** built in. Install it on the server and every client. |
| **Installation ZIP** | `<id>.zip` | Servers where you don't want players to receive server code. Contains separate `server/` and `client/` JARs (each with the runtime) and install instructions. The client JAR leaves out server scripts and server actions. |
| **Portable .wysicraft pack** | `<id>.wysicraft` | Development: drop into a `wysicraft/` folder and reload without restarting. Needs the Wysicraft runtime installed separately. |
| **KubeJS loose files (advanced)** | `.zip` | The older KubeJS layout with separate script files. Most KubeJS projects should use **Export for KubeJS** instead. |
| **Web page**, **Windows app**, **Electron apps** | folder, `.html`, `.zip`, `.tar.gz` | Using your screens outside Minecraft: in a browser or as a desktop program. See [[Web and desktop apps|Web-and-Desktop-Apps]]. |

**Export for KubeJS** (toolbar) makes a bundled JAR for projects with KubeJS server scripts. See [[KubeJS integration|KubeJS-Integration]].

Exports include only scripts attached to events. Validation errors block export; they're listed in Output.

## Installing a Project JAR

1. Install **NeoForge 21.1.250 or newer** for **Minecraft 1.21.1**.
2. Put `<id>.jar` in the `mods` folder of the **server** and **every client**. Clients need it to display the screens.
3. Remove older copies of the same project, and don't also install the same project as a loose `.wysicraft` pack.
4. Restart Minecraft or the server. JAR changes always need a restart.
5. In game: `/<id>.open`.

Several Wysicraft projects can be installed together; they share one copy of the runtime automatically.

A Project JAR contains **both** sides, so anyone who receives it can read your server script code. If that matters, use the Installation ZIP.

## Installing from the Installation ZIP

- **Server:** copy the contents of `server/` into the server folder (the project JAR goes into `mods`, plus a `kubejs` folder if your project uses KubeJS).
- **Clients:** copy the contents of `client/` into each player's instance.
- **Single player:** use the `server/` files in your own game.

## Installing a portable pack (development)

1. Install the Wysicraft runtime JAR (from the `Runtime` folder of the Wysicraft download) in `mods`, on the server and clients.
2. Put `<id>.wysicraft` in `<game folder>/wysicraft/`. On a dedicated server that's `<server folder>/wysicraft/`. Clients also need it if you use custom images or client scripts.
3. Run `/wysicraft reload` (needs permission level 2). This closes open screens and rescans packs.

Unpacked project folders can go in `wysicraft/dev/<project>/`.

## Commands in Minecraft

| Command | Does |
| --- | --- |
| `/<id>.open` | Opens the project's Main screen for you |
| `/<id>.close` | Closes it |
| `/<id>.open <player>` / `/<id>.close <player>` | Targets another player (permission level 2, for command blocks and console) |
| `/wysicraft list` | Lists loaded screens |
| `/wysicraft open <screen> [player]` | Opens a specific screen; use `project:screen` when names clash |
| `/wysicraft reload` | Reloads portable packs (level 2) |
| `/wui …` | Short alias for `/wysicraft …` |

If a project's command name clashes with another mod's command, Wysicraft logs it and never replaces the other command.

## Things players should know

- Wysicraft **never gives players extra permissions**. A button that runs `/give` only works for players who could type `/give` themselves.
- Server owners can review any project's server behavior; see [[Security and permissions|Security-and-Permissions]].
- KubeJS projects also need **KubeJS and Rhino** installed; exports don't include them.
