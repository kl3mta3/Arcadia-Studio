# Live Inventory

Open project.json and Save As a .wysicraftproj, or use the supplied editable project file. Choose Export → Installation ZIP. Install the server/client folders as instructed, then run `/live_inventory.open`.

The server reads the opening player's actual main inventory (36 slots), sends occupied stacks to the Item List, and updates the summary. Refresh reads a new snapshot. Clicking a row sends its index; the server verifies that index and reports the item from its own snapshot. No inventory items are changed. It is a read-only browser, not a replacement container or item-transfer system.

This sample uses Standard Server JavaScript and requires Wysicraft 1.1. KubeJS is not required. The optional example in examples/kubejs-block.js shows how an existing KubeJS server can open it from a block interaction; install that file separately only if wanted.

Desktop Preview has an empty simulated player inventory. Use Minecraft test to see actual game items. Empty player inventory means an empty list. After changing inventory, reopen the UI or press Refresh; this sample does not subscribe to continuous inventory updates.
