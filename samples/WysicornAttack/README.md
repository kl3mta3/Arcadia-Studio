# Wysicorn Attack

An endless rainbow-unicorn runner over floating islands. It was built entirely through the Wysicraft MCP tools: every picture was drawn with `pixel_art`, sprites were set up with `sprite_sheet`, and the screens, events and scripts were made with `apply_edits`. Target: web and desktop.

## Play

| Action | Keyboard | Gamepad | Touch |
| --- | --- | --- | --- |
| Jump (press again in the air to double jump; let go early for a short hop) | Space, Z, Up, W | A, B | left of the screen |
| Rainbow dash (holds your height; gives the double jump back) | X, D, Right | X, Y, RB, RT | right of the screen |
| Start / play again | Enter or Jump | Start | Start button |

- The world speeds up the longer you survive.
- Stars can only be broken while dashing. Touch one without dashing and you explode, just as you do when you hit an island's cliff face or fall into the sea.
- Pixies caught close together build a chain worth more each time (25 × chain).
- Each star smashed in the same life is worth more (250 × smashes).
- You have three wishes (lives), and the score carries across them.

## How it's made

- **Screens.** `title` scrolls the world with looping animations. `game` is 640×360 with a 16 ms tick, gravity 1500, and a **camera** that follows the unicorn up and down. The sky, score, hearts, touch zones and menus are attached to the camera, so they stay in view.
- **Platforms.** The platforms are built from modular pixel-art pieces: left and right ends, three middles, and ramps up and down. They join seamlessly into runs of any length, with ramps, cliff steps and gaps. The path wanders between the sea and the sky, and a thin-ledge path appears above it now and then.
- **Pools.** The pieces, colliders, ledges, stars (`cry*`) and pixies (`pix*`) come from pools of hidden controls. `scripts/client/game.js` places them ahead of the unicorn and sets each piece's picture and size with `ui.changeTexture` and `ui.setSize`. It scrolls them left with `ui.setPosition` and hides them once they are past.
- **Colliders.** Each flat section has one kinematic box collider sized to fit, and each ramp has a sloped polygon collider. Stars and pixies are circle triggers, and the unicorn's `trigger_enter` event collects or smashes them. Only a cliff face above your feet is a crash; scraping a join is not.
- **Game state.** Everything the game remembers is JSON in the screen variable `g`. The variable `mode` (run, dead, wish, over) shows the wish and game-over overlays through `visibleIf`.
- **Sound.** All sounds and the music loop are original.
