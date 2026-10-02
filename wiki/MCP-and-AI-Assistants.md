# AI assistants (MCP)

Arcadia Studio can let an AI assistant such as **Claude**, **ChatGPT** or another app that supports **MCP** look at your open project and change it: add controls, write scripts, run Preview, export, and more. You can watch every change appear on the canvas, and **Undo** works on them like your own edits.

MCP (Model Context Protocol) is a standard way for AI apps to use tools. Arcadia Studio includes an MCP server, so there's nothing else to install.

## Before you start

You need an AI app that:

- runs **on this same computer** (a desktop app or command-line tool, not a website only), and
- supports **MCP servers over HTTP** with an **Authorization header**.

Examples include Claude Desktop, Claude Code and other MCP-capable desktop tools. Web-only chat pages can't reach it.

## Step by step

1. In Arcadia Studio, click the blue **Start MCP server** button in the toolbar.
2. A window opens with the connection details. Click **Copy connection config**.
3. Open your AI app's MCP or connector settings and paste the configuration as a new server. The exact place differs between apps; look for "MCP servers", "Connectors" or "Tools". The pasted text looks like this, with your own port and token:

   ```json
   {
     "mcpServers": {
       "arcadia-studio": {
         "type": "http",
         "url": "http://127.0.0.1:PORT/mcp",
         "headers": { "Authorization": "Bearer YOUR_TOKEN" }
       }
     }
   }
   ```

   The server's name is `arcadia-studio`, so an assistant shows its tools as Arcadia Studio's. It used to be `wysicraft`: a config you pasted before still works under that name, and you can rename it there whenever you like.

4. Back in Arcadia Studio, click **Check connection** to confirm the server responds. Once your AI app connects, its name appears in the window under recently connected clients.
5. Ask your assistant for something, for example:
   - "Look at my Arcadia Studio project and add a Close button in the top-right corner that closes the screen."
   - "Make a shop screen with an item list and a Buy button that runs a server script."
   - "Validate the project and fix any errors."
   - "Draw a 16×16 pixel-art coin with a 4-frame spin, and add it to the screen as an animated sprite."
   - "Make a web game screen: a ball that falls onto a curved floor, a jump input on Space and the A button, and a start sound." (web & desktop)

When it's running, the toolbar button turns green and says **MCP running – connect**. Click it any time to see the connection details again.

## Things to know

- **The assistant is told how to use Arcadia Studio.** On connecting it receives a short briefing, and a **guide** tool explains the rest in topics: making a first screen, editing, scripting, art, sound, games, components, preview, exporting and what the errors mean. You don't have to explain any of it. If an assistant seems to be guessing, ask it to call the guide first.

- **Same address every time.** The port (4730 unless you change it in Set up) and the access token stay the same across restarts, so you paste the configuration once. **Regenerate token** in Set up makes a new one if you ever need to.
- **Closing the connection window keeps the server running.** Use **Stop MCP server** in that window to disconnect. Closing Arcadia Studio also stops it.
- **Keep the token private.** Anyone with it can read and edit your open project, including server scripts. It only works on your computer, and web pages can't use it.
- **Your project, your call.** The assistant edits the project that's open in Arcadia Studio. It saves only when asked (or when you press Save). Every change can be undone.
- **Finish what you're doing first.** If you're mid-drag or typing in a field when the assistant makes a change, it asks the assistant to wait.

## Ask Agent (requires a CLI)

Under the connection details is an **Ask Agent  (Requires CLI)** row. With it set up, Arcadia Studio can ask an AI to do things for you from inside the editor, without you switching to a chat window:

- **Ask Agent** at the top of Properties (with a control selected, or the screen) — describe what you want done to it.
- **Advanced → Input creator** — pick a device and a free button, say what it should do, and the script and input are written for you.
- **Test** in Set up — sends a real prompt through the whole path to prove it works.

It uses a command-line AI tool you already have — on your own account and subscription, never an API key. The editor starts the tool headlessly with the MCP server attached, the tool does the work through the same tools any assistant has, answers and exits. Each ask is its own run; the status line shows which tools it is calling and the time so far.

1. Click **Start MCP server**, then **Set up** on the Ask Agent row.
2. Choose the tool: **Claude Code**, **Codex**, **Gemini CLI**, **Qwen Code**, **Kimi Code**, **opencode**, **Copilot CLI**, or **Custom** (your own command and arguments). The window says whether it is installed, its version and whether you are signed in. If it isn't installed, **Copy** the install command and run it in a terminal.
3. Click **Sign in** if needed — the tool's own login opens, and your browser does the rest.
4. Click **Test**. When it says *Works*, click **Done**.

**Start** opens a terminal with the tool running interactively and Arcadia Studio attached, for when you want to talk to it directly; **Stop** closes it. Tick **Auto-connect with MCP** to start it whenever MCP starts.

Without a CLI set up, the same buttons still work but queue the request: an assistant connected the ordinary way picks it up with the `pending_requests` tool on its next turn and answers with `answer_request`. The button tells you which is happening.

## Tutorials from your assistant

Ask your assistant to show you how to do something, and it can teach you inside Arcadia Studio instead of only describing it:

- **Pointing at things.** A pulsing outline with a short caption appears over the part of the editor it means: a menu command (it opens the menu first), a toolbar button, a panel, a Toolbox entry, a control on the canvas or a field in Properties. Clicks go straight through it. It goes away when you click what it points at, press **Escape**, or after a while.
- **Opening things for you.** It can open any window or panel the editor has (the Music maker, Animations, Shaders, Project settings, Speech settings, the Layers panel…) to show you where something is, select a control so its Properties show, and close what it opened. It scrolls panels so what it points at is in view. Opening a window never saves, exports or publishes anything: those stay your click.
- **Notices.** A small message in the corner that never takes the keyboard or blocks anything, and closes by itself. It's also written to Output. An assistant can show at most one every two seconds, and three at once.
- **The tutorial window.** A step-by-step guide that never locks the editor. Drag it anywhere, and tick **Always on top** to keep it above other apps too:

| In the window | What it does |
| --- | --- |
| **Step 3 of 8** | Where you are. **◀ Back** and **Next ▶** move between steps |
| **Show me** | Points at the part of the editor the step is about |
| **✔ Done** / **○ To do** | Some steps tick themselves off when you've done them, for example "a control called play exists" or "play's click event does something" |
| **Move on when done** | On (the default), a step that ticks off moves you to the next one |
| **References** | Extra notes and links the assistant added; click to open the list |
| **Save…** | The whole tutorial as one web page, pictures included, to keep or share |

Steps can include pictures: a part of the editor, captured when the tutorial opens, or a picture the assistant drew with the pixel art tools. Steps can also have narration, read aloud in the assistant's voice as each step is shown: untick **🔊 Read aloud** in the tutorial window (or in **Advanced → Speech settings…**) for silent tutorials. Tutorials are kept in `%LOCALAPPDATA%\Arcadia Studio\Tutorials`, never in your project.

## What the assistant can do (reference)

| Tool | Purpose |
| --- | --- |
| `get_project` | Read screens, controls, scripts, images, the current selection and a revision number. For a big project it can read less: one screen (`screen`), or without the scripts (`scripts`: none, or just the ones named) |
| `guide` | How to use this server, in topics: the edit loop, every edit kind, scripting, pixel art and sprites, sound and music, game features, components, preview, export and what the errors mean |
| `get_schema` | Controls (including sprites, shapes, Sound controls and the web & desktop ones), properties, events, actions, components, limits per target, gamepad buttons, sound formats, starters and edit formats |
| `apply_edits` | Make up to 128 changes at once, applied only if the whole batch is valid (one Undo step). It also makes, changes and deletes [[leaderboard pages|Leaderboard-Pages]] (`new_leaderboard`, `upsert_leaderboard`, `delete_leaderboard`, and control edits with the page's ID as the screen). Besides screens, controls, scripts and events it can add and remove inputs, animations, state graphs, shaders and particle effects, add and remove components (a Collider, a Rigidbody, a Character controller and the rest — the same cards you see in Properties), rename controls (updating what refers to them), change layer order and make empty groups. |
| `pending_requests`, `answer_request` | Read what you asked for with Ask Agent or the Input creator when no CLI is set up, and answer it |
| `undo`, `redo` | Undo or redo |
| `validate_project` | List validation errors, which ones only block Minecraft exports, and everything Minecraft can't run with where it is |
| `save_project`, `save_project_as` | Save the project file |
| `export_project` | Export a JAR, installation ZIP, portable pack, KubeJS files, web page (folder or single file) or Windows app (to `%LOCALAPPDATA%\Arcadia Studio\McpExports`, where exports are kept for 14 days: copy one you want to keep). Minecraft formats are refused while the project uses things Minecraft can't run. |
| `arcadia_publish` | [[Publish to Arcadia|Publishing-to-Arcadia]]: read the link and account, fill in the game details (phones and tablets, video links), the leaderboard, the cover and screenshots (captured from Preview or chosen; fitted to 1280 × 800), import a leaderboard page from a file and which [[leaderboard page|Leaderboard-Pages]] to use, build the package and run the arcade's check. It never publishes: that's your click in File → Publish to Arcadia |
| `itch_publish` | [[Publish to itch.io|Publishing-to-itch.io]]: read the sign-in and settings, choose the itch.io game and what to upload (web version, Windows app, channels), build the uploads and ask itch.io what they'd change. It never uploads and can't sign in: both are your clicks in File → Publish to itch.io |
| `export_electron_apps` | Export Electron apps for macOS and Linux (and Windows if asked); downloads Electron the first time |
| `import_asset` | Import a PNG, a sound (`.ogg`, `.mp3`, `.wav`, `.m4a`) or a `.png.mcmeta` animation file from your computer |
| `pixel_art` | Draw pixel art (layers, groups, frames, transparency) and save it as a project image, exactly like the pixel editor, as one Undo step. It has the soft brush and edge smoothing too, for art that isn't pixelated. Returns the texture ID, a text grid and a preview picture. With **live** it draws in the real pixel editor where you can watch, one pixel at a time, each new frame starting as a copy of the last, then plays the animation in the editor's Preview; it can keep the editor open while it points at its tools, frames, Duplicate button and Preview |
| `sound_effect` | Make or change a retro sound effect from a preset (coin, jump, laser, explosion, powerup, hurt, blip, random) and settings, like the Sound effect maker. It's saved as a project sound, and can be opened in the Sound effect maker and played for you to hear |
| `particles` | Make or change a particle effect from a preset (sparkle, sparks, confetti, explosion…) and settings, like the Particle maker, and show it to you there. Use it on a Pickup, a Particles control, or `ui.burst` from a script |
| `compose_music` | Write or change an 8-bit song, like the Music maker. Give layers with an instrument and notes as text, e.g. `C4 q E4 e G4 e | C5 h`. It's saved as a project sound the Music maker can open |
| `song_from_audio` | Turn a recording (a file on this computer or a project sound) into an 8-bit song: notes, chords, bass and drums on chip instruments |
| `read_music` | Read a song's tempo, key and every layer's notes as text, a sound effect's settings, or a recording's length |
| `read_pixel_art` | Look at any image as a text grid (one character per pixel) and a preview picture, with its layers and frames |
| `sprite_sheet`, `read_sprite_sheet` | Set up or read a Sprite control's sheet, frame size and clips, like the sprite sheet editor. Every frame is checked against the sheet |
| `preview_control` | Open Preview, fire an event (including key, tick and input events), hold one of the game's inputs for a while (so it can walk a character around), tap a point on the screen, run JavaScript on the screen, let time pass for animations and physics, read the live state, capture a screenshot (the whole window, or just the game), close |
| `tutorial` | Teach you the editor, inside the app: point at a menu command, toolbar button, panel, Toolbox entry, control or Properties field with an outline and a caption; show a notice in the corner; open a step-by-step tutorial window, with steps it can read aloud; talk you through things out loud. See [Tutorials from your assistant](#tutorials-from-your-assistant) |
| `speech` | Speak aloud in its voice (real time: it starts within a fraction of a second), in any of the voices or a mix of them, at any speed; change its own voice; save speech as Ogg, WAV or MP3; add voiced lines to your project as sounds, for characters, a narrator or menus; write down what's said in any recording or video; listen to your microphone for an answer. See [[Speech and video|Speech-and-Video]] |
| `video` | Record your game in Preview (only the game, with its sound and the assistant's narration), the editor, a window or a whole monitor; then add narration over a video, trim it, make a GIF or a still, or anything else FFmpeg does. See [[Videos with your assistant|Speech-and-Video#videos-with-your-assistant]] |
| `minecraft_test_control`, `get_test_status` | Start, apply, open/close, run commands in, and stop the Minecraft test, and read its logs |
| `get_templates`, `apply_template` | Use the script templates |
| `project_control` | Create or open a project (after saving the current one) |

Edits include adding and changing screens, controls, scripts and events, the Main screen, project settings, components, starters and align/distribute. Every edit request includes the revision number the assistant last read. If you changed something in the meantime, the edit is refused and the assistant rereads the project, so it never overwrites your work blindly.

Server actions in Preview stay simulated even when an assistant triggers them. Project text and scripts are treated as data, not instructions to the assistant.
