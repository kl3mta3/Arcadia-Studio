# KubeJS export and Minecraft API

Target: Minecraft 1.21.1, NeoForge, KubeJS 2101. The Wysicraft runtime remains usable without KubeJS for built-in actions. KubeJS exports require KubeJS on the server. Use the updated runtime JAR on BOTH server and clients: the UI update packet changes the network protocol.

## First Minecraft test

1. Install the updated Wysicraft JAR and your matching KubeJS/dependencies in your test modpack. Do not leave an older Wysicraft JAR beside it.
2. Extract `samples/kube_bridge-kubejs.zip` into the server directory. It contains `wysicraft/kube_bridge.wysicraft` plus generated files under `kubejs/server_scripts/wysicraft/` and `kubejs/startup_scripts/wysicraft/`.
3. Copy the pack into the client's `wysicraft/` folder too. Never distribute the server scripts as client scripts.
4. Restart for initial installation. The KubeJS console should report `Wysicraft: registered 1 handlers for kube_bridge`.
5. Join and run `/kube_bridge.open`. Click TEST SERVER.
6. Expected: player message, status label becomes `Server clicks: 1`, and the normal `/me` command runs as the player. Click again for 2. CLOSE closes the interface.
7. After edits/re-export, replace the generated JS and pack; run `/reload`, then `/wysicraft reload`, and reopen.

To run your portal command instead, change `event.runCommand('me tested the Wysicraft bridge')` to `event.runCommand('portal')` before export. It must exist on the server and allow the clicking player to use it.

## Authoring

Use a Server event: New Script -> KubeJS example (or select `kubejs` in the script engine selector) -> Save & Assign -> Export for KubeJS. The toolbar and File/Project menus expose this export. It produces an installation ZIP, not a replacement project format. Save your source project separately.

Only assigned KubeJS Server script/function pairs are generated. Unused files are excluded. The pack contains server-function action references; raw JS is installed separately in KubeJS's server_scripts folder. Re-export preserves the original authoring project. The function is called once per event; its file-level code runs during KubeJS loading/reloading, so put button work INSIDE the function.

This integration supports KubeJS Server scripts, Standard Client scripts, and built-in actions. Export rejects Standard Server scripts and KubeJS Client scripts. KubeJS scripts are not run in desktop Preview; Test Event reports that an in-game test is needed. Standard Client JS runs in Wysicraft's bundled GraalJS engine; Standard Server JS still needs a provider.

```js
function on_click(event) {
    event.message('Hello from the Minecraft server');
    event.ui.setText('status', 'Updated from KubeJS');
    event.runCommand('portal'); // Clicking player's permissions.
}
```

The argument exposes `player` (ServerPlayer), `server` (MinecraftServer), `value` (event input), `message(text)`, `runCommand(command)`, `state.get(name)`, `state.set(name,value)`, and `ui` methods below. KubeJS's `Java.loadClass` and server APIs are available. `event.server.runCommandSilent(...)` uses server authority; it is distinct from our player-permission `event.runCommand(...)`.

UI methods: `setText(id,text)`, `setValue(id,value)`, `setVisible(id,boolean)`, `setEnabled(id,boolean)`, `setVariable(name,value)`, `getVariable(name)`, `open(screenId)`, `close()`. Updates target the player's active Wysicraft session and travel to the client through a session-checked packet. Changes to visibility/enabled/value also update server state. This API updates existing elements; it does not dynamically create them.

## Calling from existing KubeJS scripts

The generated startup file creates a helper in KubeJS's global object. New or changed startup helpers require a restart; server handlers can use `/reload`. Call it inside a server event after scripts have loaded:

```js
global.kube_bridge.open(event.player);
global.kube_bridge.close(event.player);
```

The public Java API is available without a generated helper:

```js
const Wysicraft = Java.loadClass('com.wysicraft.runtime.api.WysicraftApi');
// Inside a server event:
Wysicraft.openProject(event.player, 'kube_bridge'); // Resolves Main.
Wysicraft.closeProject(event.player, 'kube_bridge');
Wysicraft.openUi(event.player, 'kube_main');
Wysicraft.setText(event.player, 'status', 'Hello');
```

Other methods: `closeUi(player)`, `setValue(player,id,value)`, `setVisible(player,id,boolean)`, `setEnabled(player,id,boolean)`, `setVariable(player,name,value)`, `message(player,text)`, `runCommand(player,command)`. Call from the Minecraft server thread. Installed server scripts are trusted server code; they have KubeJS's capabilities, not the desktop preview sandbox limits. Clients send event identities and bounded input, never JavaScript source.

## Validation performed

C# export tests cover handler conversion, omission of unused files, preserving the authoring project, and generated pack contents. A Rhino 2101.2.8-build.91 check loads the generated JS, converts its function into a Java callback, executes it against simulated Minecraft endpoints, and repeats after reload. Java and WPF builds pass. A real Minecraft 1.21.1 / NeoForge 21.1.250 / KubeJS 2101.7.2-build.377 test also verified project open/close, command discovery, and a KubeJS screen-open callback sending a player message and executing a server command. Follow the first-test steps above to validate your own installed modpack. See [the built-in Minecraft test runner](MINECRAFT_TEST.md).
