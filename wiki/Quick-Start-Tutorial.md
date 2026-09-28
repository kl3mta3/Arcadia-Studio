# Quick start tutorial

You'll build a small **Airship Controls** screen with a Start button that updates a status label and sends a chat message, then try it in Preview and export it for Minecraft. It takes about ten minutes.

## 1. Create the project

1. Choose **File → New project** (Ctrl+N) and type the name `Airship Controls`.
2. **Project settings** opens next. Check that **Id** is `airship_controls` (Wysicraft derives it from the name), then click **Apply**.
   The Id is lowercase letters, numbers and underscores. It becomes the in-game command: `/airship_controls.open`. You can change it later under **Project → Project settings…**.
3. Click **Screen settings** above the canvas. In Properties, change **Screen ID** from `main` to `cockpit` and press Enter. Change **Title** to `Airship`.

## 2. Add controls

1. In the **Toolbox** (top left), drag **Panel** onto the canvas. Resize it by dragging its corner handles until it covers most of the screen. This is your background.
2. Drag a **Label** onto the panel. In Properties, set **Id** to `status` and **Text** to `Engine offline`.
3. Drag a **Button** onto the panel. Set **Id** to `engine_start` and **Text** to `START ENGINE`.
4. Select the label and the button together (Shift-click), plus the panel, then right-click and choose **Attach to 'panel'**. Now they move with the panel. See [[Panels and parenting|Panels-and-Parenting]].

## 3. Make the button work

1. Select the button and open the **Events** tab (next to Properties).
2. The `click` event is shown. Under **Client**, click **+ Add action**:
   - Type: `set_text`
   - Target: `status`
   - Value: `Starting...`
3. Under **Server**, add another action:
   - Type: `command`
   - Value / Command: `say Engine start requested`

Client actions run instantly on the player's screen. Server actions run on the Minecraft server with the player's own permissions. See [[Events and actions|Events-and-Actions]].

## 4. Try it in Preview

1. Click **Preview** in the toolbar (F5).
2. Click **START ENGINE**. The label changes to `Starting...`, and the console shows the server command as `SIMULATED SERVER`. Preview never runs real commands.
3. Close Preview.

## 5. Save

Choose **File → Save** (Ctrl+S) and save as `airship_controls.wysicraftproj`. This single file holds your screens, scripts and images. If the editor ever closes unexpectedly, Wysicraft keeps a recovery draft every 30 seconds.

## 6. Export and play

1. Click **Validate** (F7). The Output panel should say **Validation passed**.
2. Click **Export…** (Ctrl+E), keep **Project JAR** selected, and save `airship_controls.jar`.
3. Copy the JAR into your Minecraft instance's `mods` folder (Minecraft 1.21.1 with NeoForge 21.1.250+). Nothing else needs to be installed: the Wysicraft runtime is bundled inside the JAR.
4. Start Minecraft, open a world with cheats enabled, and type `/airship_controls.open`.

Your screen opens. Clicking START ENGINE updates the label, and `say` runs because you have cheats on. Players without permission for a command can't run it through your screen either.

## Want to skip ahead?

Instead of copying JARs by hand, the [[Minecraft test|Testing-in-Minecraft]] window launches a real game straight from the editor, with keys to open and close your screen.

## Where next

- Make it look good: [[Properties and appearance|Properties-and-Appearance]]
- Make it adapt to window size: [[Anchors and responsive layouts|Anchors-and-Responsive-Layouts]]
- Write logic in JavaScript: [[Scripting|Scripting]]
