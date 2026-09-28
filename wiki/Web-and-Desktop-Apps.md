# Web and desktop apps

Your screens don't have to live in Minecraft. **File → Export** can also turn a project into a web page or a desktop program. They look and behave like the game: the same layout and anchors, conditions, actions, client scripts, Tick and Key events, animated textures and navigation between screens.

| Format | You get | Runs on |
| --- | --- | --- |
| **Web page — folder** | `index.html`, `host.js`, a `wysicraft` folder and your images | Any modern browser. Open `index.html`, or upload the folder to a website. |
| **Web page — one HTML file** | One `.html` file with everything inside, images included | Any modern browser. Easy to email or attach. |
| **Windows app** | A `.zip` with `Name.exe`, `app.ini` and an `app` folder | Windows 10 and 11. Unzip and run the `.exe`: nothing to install. |
| **Electron apps** | One download per platform you tick | Windows, macOS (Apple silicon and Intel) and Linux. |

## What's different outside Minecraft

The Export dialog lists anything in your project that won't work the same way:

- **Minecraft textures and the Minecraft font** belong to Mojang and can't be included. `minecraft:` textures show nothing, and text uses the system font. Import your own PNGs for backgrounds.
- **Item icons** show a placeholder, unless the project has an image for that item under `textures/item`. For example, `assets/<namespace>/textures/item/diamond.png` is used for `minecraft:diamond`.
- **Server side:** `message`, `set_variable`, `toggle_variable`, `open_ui` and `close_ui` work as usual, and server scripts run too. Things that need a Minecraft server go to **host.js** instead: `command` actions, `ctx.server.runCommand`, `server_function` and `play_sound`.
- **KubeJS** scripts don't run.
- **Escape** doesn't close the screen, unless you turn that on in host.js.

## host.js: connect your own code

The folder export includes `host.js`. It's plain JavaScript that you edit, and re-exporting keeps your copy. Every hook is optional:

| Hook | Called when |
| --- | --- |
| `onCommand(command, info)` | A Server `command` action runs, or a server script calls `ctx.server.runCommand`. |
| `onServerFunction(name, value, info)` | A `server_function` action runs. |
| `onServerAction(action, info)` | Any other Minecraft-only server action runs. |
| `onSound(sound)` | A `play_sound` action runs. |
| `onMessage(text)` | A `message` action or `ctx.message` runs. Return `false` to hide the built-in message line. |
| `onClose(screen)` | The screen is closed. Desktop apps close their window here. |

`info.app`, which is also `window.Wysicraft.app`, lets your code change the UI, for example after fetching data:

```js
onCommand(command, info) {
  if (command === 'refresh') fetch('/api/status').then(r => r.json()).then(s => info.app.setText('status', s.text));
}
```

`app` has `open(screen)`, `close()`, `setText(id, text)`, `setValue(id, value)`, `setVisible(id, bool)`, `setEnabled(id, bool)`, `getVariable(name)`, `setVariable(name, value)`, `message(text)` and `fire(elementId, eventName, value)`. host.js also sets `closeOnEscape`, `guiScale` (the size, like Minecraft's GUI scale), `fontFamily`, and the `player` details that server scripts see.

## Windows app

The Windows app is a small program (under 1 MB) plus your web page. It shows the page in its own window using Microsoft Edge WebView2, which comes with Windows 10 and 11. It only ever shows your own files.

- Edit `app.ini` to change the window title and starting size.
- The `.zip` includes `THIRD-PARTY-NOTICES.txt` for the WebView2 loader inside the program. Keep it with the app if you share it.

## Electron apps

Electron apps run on Windows, macOS and Linux, and are about 100 MB each.

- **Download:** the first time you export for a platform, Wysicraft downloads Electron's official build from its GitHub releases, checks it against Electron's published checksums, and keeps it in `%LOCALAPPDATA%\Wysicraft\Electron` for next time.
- **You can build all three from Windows:** Electron isn't compiled, so no Mac or Linux PC is needed.
- **Windows** is unticked by default: the **Windows app** format does the same job in under 1 MB instead of about 100 MB. Tick it only if you want your Windows build to match your Mac and Linux ones.
- **macOS:** the app isn't signed by Apple, so macOS says it's damaged when it's downloaded. Run `xattr -cr "Your App.app"` once in Terminal, or sign it on a Mac with your Apple developer account.
- **Linux:** extract the `.tar.gz` and run the program inside. If it complains about the sandbox, run it with `--no-sandbox`.


## How big should a game be?

Every player downloads a web game over their own connection, so Wysicraft warns when one gets heavy. Nothing is refused; the warning just says what the wait will be.

| Export | Warns past | Why |
| --- | --- | --- |
| Web page (folder) | 100 MB | Each player downloads it. At 10 Mbit/s, 100 MB is about a minute and a half. |
| Web page (one HTML file) | 100 MB | The file carries everything as text, which makes it about a third bigger than the folder. |
| Windows app, Electron apps | 500 MB | Downloaded once and kept. Electron adds about 100 MB to each app. |

The Export dialog shows about how big the chosen format comes out, and the warning in yellow when it is over. The warning names the three biggest files. It stays in the log after the dialog closes, and **Validate** notes it too. Sounds are usually the biggest part: shorter or quieter songs, or lower Ogg quality, help most.

How files load in a web game:

- **Pictures** start loading as soon as the page opens.
- **Sound effects** are fetched and decoded when the page opens, so the first play of each is instant.
- **Music** played by a Sound control streams: it downloads while it plays, and it is not held in memory whole.
- A *Play sound* action or `ctx.client.playSound` on a long sound (over 512 KB) streams too, instead of being decoded whole.
