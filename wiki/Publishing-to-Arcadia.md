# Publishing to Arcadia

**File → Publish to Arcadia…** puts your game on an Arcadia web arcade, where anyone can play it in their browser. Arcadia Studio packs the game, checks it, uploads it, and tells you when it's live. Later versions of the same project update the same game.

Arcadia runs web games, so publishing works for projects made for **web and desktop** (or both). Minecraft-only features don't run there, just as in a [[web export|Web-and-Desktop-Apps]].

## 1. Link Arcadia Studio to your account (once)

1. Click **Link to Arcadia…**. Your browser opens the arcade's approval page, and Arcadia Studio shows a code such as `QHTB-7K2M`.
2. Sign in on the arcade (if you aren't already), check the code matches, and click **Approve**.
3. Arcadia Studio says **Publishing to … as *your creator name***.

You never type your password into Arcadia Studio. If you don't make games on Arcadia yet, the page first asks you to set up a creator profile (Profile → Make games for Arcadia); Arcadia Studio keeps waiting while you do.

The link is saved for your Windows user, encrypted, and only for that arcade. **Unlink** ends it; you can also remove it on the arcade's website (Profile → Arcadia Studio on your devices), and Arcadia Studio then asks you to link again.

**Arcade address…** chooses another arcade (the default is `https://arcadia.arcadiastudio.games`). Each arcade has its own link and its own copy of your game.

## 2. Describe the game

| Field | What it's for |
| --- | --- |
| **Title** | 1 to 60 characters, shown everywhere. |
| **Description** | Up to 600 characters of plain text. |
| **Genre** | Up to 3. The first is the label on the game's card. Common words (Action, Arcade, Puzzle, Platformer, Runner, Shooter, Strategy, Survival, Roguelike, Racing, Sports, Casual, Classic) help players filter. |
| **Version** | Raise it every time you publish. Arcadia Studio offers the last one + 1. |
| **Controls** | How to play, for example `WASD to move · Space to dash`. Up to 200 characters. |
| **Aspect ratio** | The shape the arcade fits the game to. **From the screen** uses your main screen's size. |
| **Plays on phones and tablets** | Tick it when the game has touch controls (on-screen buttons or tapping) and fits a small screen. The arcade shows a phone icon, lists it under **Mobile friendly** and features it for players on phones. Unticked, phone players are told it may need a keyboard. |

## 3. Add a cover, screenshots and videos

Every picture is **1280 × 800** (16:10) at its best, real gameplay, no borders: a PNG, JPEG or WebP of up to 2 MB. Each one can come from either button:

- **Capture from Preview**: the first click opens Preview. Play to a good moment, then click it again. Arcadia Studio takes the game screen (without collider outlines), crops the middle to 16:10 and scales it to 1280 × 800, keeping pixel art sharp.
- **Choose file…**: a picture from your computer. A PNG or JPEG that isn't already 1280 × 800 is fitted for you (the middle at 16:10, like a capture); a WebP is used as it is.

| | What it's for |
| --- | --- |
| **Cover** (required) | The game's card in the arcade and the big picture on its page. |
| **Screenshots** (optional) | The gallery on the game's page, up to **8**, shown in the order you set: **◀** and **▶** move one earlier or later, **Remove** takes it out. 3 or more is best. |
| **Videos** (optional) | Up to **3 YouTube links**: `youtube.com/watch?v=…`, `youtu.be/…` or `youtube.com/shorts/…`. They play on the game's page before the screenshots. Only the link is sent, never a video file. A link that isn't YouTube is marked as you type, and Check and Publish refuse it. |

A moderator looks at the pictures and videos along with the game when an upload is reviewed.

## 4. A leaderboard (optional)

Tick **Keep a leaderboard for this game** and Arcadia records each player's scores without any code in your game: it reads your game's variables while it runs.

Leave it unticked and the game is published with no leaderboard, on purpose: Arcadia doesn't set one up by itself, and a leaderboard page isn't sent.

**Scan…** (beside the tick box) reads your game the way Arcadia does when a game is uploaded, and suggests the answers for the settings below:

| It suggests | From |
| --- | --- |
| **Score comes from** | A variable named like `score`, `points`, `coins` or `distance` that goes up, as a screen variable, a `ctx.state` name, or a field of a variable that holds JSON (`g.score`). |
| **Run ends when** | A variable set to a word like `over` or `dead`, or a flag like `gameOver`, `dead` or `won` set to true. |
| **Extra columns** | Other figures worth showing: level, wave, lines, lives, time and the like. |

Each suggestion shows the line of script it was found on. The ones Arcadia would use by itself are already picked, and the window says whether Arcadia would be sure enough to set the board up alone. **Use these** puts your choices into the fields (and ticks **Keep a leaderboard**), where you can still change them; they're saved with the rest of the window. **Cancel** changes nothing. Ticked conditions replace the ones in the form; ticked columns are added to the ones already there.

The scan only sees what Arcadia can read while the game runs: screen variables and `ctx.state` names. A score kept only in a script's own variable isn't found; save it with `ctx.state.set`.

| Setting | Meaning |
| --- | --- |
| **Column name** | Score, Distance, Time… |
| **Format** | Points or Number (whole numbers), or Time (in **milliseconds**, shown as m:ss.cc). |
| **Better scores** | Higher is better, or lower is better (a fastest time). |
| **Ranks** | Each player's best run, or the total of all their runs. |
| **Lowest / Highest score** | Set the highest score that's really possible: anything above it is refused as a cheat. |
| **Shortest run** | Seconds. A shorter run isn't counted. |
| **Score comes from** | A screen variable or a `ctx.state` name. If it holds JSON (a game that keeps everything in one variable with `ctx.state.set('g', JSON.stringify(g))`), put the part to read in **field**, for example `score` or `stats.kills`. |
| **Run ends when** | Up to 6 conditions; the run has ended when any of them holds: a variable (and field) **is true**, or **equals** a value such as `over`. |
| **Extra columns** | Up to 8 more figures per run (level reached, crystals…), each with a key (lowercase letters, digits and `_`) and whether to keep the highest, lowest or total. Tick **Doesn't grow over time** for a figure like accuracy % or a character number (see below). |

**Score checks.** Arcadia compares each run's pace (its score, and each extra column, per second of play) with the game's usual runs, and a run far ahead of it waits for a moderator before it reaches the board. That suits figures that build up as you play. For one that doesn't (an accuracy %, which character was played), tick **Doesn't grow over time** so it's left out of that comparison.

A score is sent **each time "run ends" turns from false to true**. So it has to be false while playing, true at game over, and false again when a new run starts (for example `mode` going `play` → `over` → `play`). A game that starts in the ended state sends nothing until it has been false once.

**Leaderboard page.** What players see when they open the leaderboard: the arcade's standard board, or a page of your own. A project with a page uses it unless you choose the standard board. See [[Leaderboard pages|Leaderboard-Pages]].

| Button | What it does |
| --- | --- |
| **Create leaderboard…** | Opens the **leaderboard creator**, a window of its own, on a new page with a starter layout (a podium, a top 10, the player's own rank and more, filled with the game's real scores once it's published). |
| **Import leaderboard…** | Brings in a `.lb` file or a page. |
| **Edit** | Opens the chosen page in the leaderboard creator. |
| **Remove** | Stops using a page of your own: the choice goes back to the arcade's standard board, and the next publish takes your page off the game. A page you designed stays in the project, so you can choose it again; an imported page is taken out of the project (after asking). Off while the standard board is chosen. |

Closing the leaderboard creator (or its **Done** button) comes back to this window with the page chosen.

## 5. Check, then publish

**Check** packs the game and runs the arcade's rules on it here first, then asks the arcade for a dry run (nothing is stored and no upload is used up). Findings are listed by colour:

| Colour | Meaning |
| --- | --- |
| **Red, Refused** | Must be fixed; the arcade won't take the upload. |
| **Amber, Review** | A moderator has to look before it goes live. |
| **Blue, Warning** | Worth fixing; it doesn't stop anything. |
| **Grey, Info** | Facts such as the number of files and the size. |

Double-click a finding in one of your scripts to open that script.

**Publish** uploads the game. Before you click, the window says whether clean uploads **go live straight away** or **wait for a moderator** (new creators start with every upload reviewed), and how many uploads you have left. After uploading it waits for the result:

| Result | What happens |
| --- | --- |
| **Live** | It's on the arcade. **Open in browser** shows it. |
| **Waiting for review** | A moderator looks first; the reason is shown, and you get an email when they approve or decline it. |
| **Not published** | A moderator sent it back, with their reason. An earlier live version stays live. |
| **Something went wrong** | A problem on the arcade's side. It doesn't use up an upload: try again. |

Everything in the window is saved with the project, including the game's permanent ID on each arcade, so the next publish is one click and updates the same game. The previous version stays live until the new one is.

**If the game can't be updated**, because you deleted it on the arcade's website, a moderator removed it, or it isn't one of your games there anymore, Check and Publish say so and ask whether to publish it as a new game. **Yes** forgets the old game and starts the version again at 1.0.0; the title, description, genres, controls, screenshot and leaderboard stay as they are. **No** changes nothing. Arcadia Studio never does this without asking: you may have deleted the game on purpose.

**If you already have a game with this title** (titles are unique per creator, ignoring case and punctuation), Arcadia Studio asks whether it's this project, which happens when a project has lost its saved game. **Yes** updates that game; **No** lets you give this one a different title.

**If the game's details were changed on Arcadia's website** since you last published (its title, description, genres, controls, videos, cover, screenshots or leaderboard, by you or a moderator), **Publish** checks first and asks:

| Choice | What happens |
| --- | --- |
| **Keep Arcadia's** (the default) | The game is updated, its details stay as they are on Arcadia, and they're copied into your project (pictures included), so you aren't asked again. |
| **Use mine** | The game is updated with the details in this window, replacing the website's. |
| **Cancel** | Nothing is published. |

When the difference is whether the game has a leaderboard at all, the question says so:

| The question | Use mine | Keep Arcadia's |
| --- | --- | --- |
| **The arcade has a leaderboard set up on the website. Remove it?** (the website has one; **Keep a leaderboard** is unticked here) | Removes the game's leaderboard. | Keeps it, and ticks **Keep a leaderboard** here with the website's setup. |
| **The leaderboard was turned off on the website. Turn it back on?** (the website has none; **Keep a leaderboard** is ticked here) | Turns it back on with this window's setup. | Leaves it off, and unticks **Keep a leaderboard** here. Your setup stays filled in for later. |

**Load details from Arcadia** copies the game's current details from Arcadia into the window at any time, for example after editing them on the website. If the leaderboard was turned off there, it unticks **Keep a leaderboard**.

**Publishing history…** lists the last 20 uploads and can download any stored version (or what's live) as a zip.

## Limits

- **New games:** by default one every 4 hours. **Updates:** one per game every 24 hours, saving up to 3. When none are left, the window says when the next one is available.
- **Size:** at most 1,000 files, 25 MB each, and the arcade's total (usually 50 MB unzipped). See [[How big should a game be?|Web-and-Desktop-Apps]].

## What the arcade allows

Games on Arcadia run in a locked-down page:

- **No network.** Everything the game needs is in the package, which Arcadia Studio always does.
- **Sound** plays as in a page opened from disk: effects use the browser's audio players rather than Web Audio. They play normally.
- **Your own code in host.js isn't sent.** The package uses the standard host.js, so hooks you added to an exported host.js don't go to Arcadia.
- The Arcadia Studio runtime goes exactly as Arcadia Studio ships it. The arcade recognises official builds; the first game that uses a new Arcadia Studio version may wait for a moderator once.

## For AI assistants

Over [[MCP|MCP-and-AI-Assistants]], an assistant can fill in these settings, capture the cover and screenshots from Preview, add video links, build the package and run the arcade's check (`arcadia_publish`). It can't publish: that's always your click.
