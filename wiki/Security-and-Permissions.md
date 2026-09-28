# Security and permissions

This page is for **server owners** installing Wysicraft projects and for authors who want to build safe screens.

## The short version

- Wysicraft **never grants extra permissions**. Commands run as the clicking player by default.
- Players' game clients can only say "I clicked this control". What happens next is decided by the server, from the project installed **on the server**.
- Treat installed projects like server configuration: they can run commands and scripts. Only install projects you trust.

## What the server checks on every click

1. The player has a Wysicraft screen open, and the message carries that screen's session token.
2. The control exists, supports that event, and is visible and enabled (including its panels and any conditions), according to the **server's** state.
3. The input is the right type and size (text up to 1024 characters, numbers in range, valid row index…).
4. The player meets the handler's **permission level**, and its **cooldown** has passed.
5. The player hasn't sent more than 20 events this tick.

Only then are the server actions run. Commands are fixed text from the installed project; player input is never inserted into them.

## Commands and permissions

- Built-in `command` actions run **as the player** by default, so a button can't do anything the player couldn't type.
- The server config option **`runCommandsAsServer`** (off by default) makes built-in command actions run with server authority. Enabling it means every permitted button in every installed project can run its command as the server. Only enable it if you've reviewed those projects.
- `ctx.server.runCommand` in Standard Server scripts **always** uses the player's permissions, even with that option on, because scripts can build commands from player input.
- KubeJS handlers are trusted server code; KubeJS's own `runCommandSilent` uses server authority.

## Designing safe screens (for authors)

- Use **Permission level** (Events → Server) for admin-only buttons.
- In server scripts, double-check with `ctx.player.hasPermission(level)` before doing anything powerful.
- For Item List rows, look up the row index in **your own** data; never trust an item ID or amount from the client.
- Keep the default cooldown (4 ticks) or raise it for expensive actions.
- For distribution to players, prefer the **Installation ZIP**: the client JARs don't contain your server code.

## Scripts

- Standard scripts run in Wysicraft's bundled JavaScript engine, with no access to Java classes, files, the network or processes, and with statement, time and output limits.
- Server scripts also have a **per-player time budget**: a player who triggers scripts too quickly has their scripts paused, and the server log says so.
- The engine runs inside Minecraft without its own memory cap. These limits stop accidents and casual abuse, but they aren't a hostile-code sandbox. Install trusted projects.
- The editor's Preview runs scripts in a separate, restricted process.

## Pack loading

Projects are read directly from their archives without extracting. Unsafe paths, oversized files, too many files and symbolic links are rejected. A broken or unsafe project is skipped and logged; other projects still load.

## The editor's AI connection (MCP)

The MCP server only listens on your own computer (`127.0.0.1`) and requires a random access token that changes every time it starts. Browser pages can't connect. Treat the copied connection settings like a password: anyone with them can read and edit the open project. See [[AI assistants (MCP)|MCP-and-AI-Assistants]].
