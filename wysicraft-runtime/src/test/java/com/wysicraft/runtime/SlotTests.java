package com.wysicraft.runtime;

import com.wysicraft.runtime.pack.PackRepository;
import org.junit.jupiter.api.Test;
import java.nio.file.*;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

/** Item slots in packs: what a screen may ask for, checked before any container opens. */
class SlotTests {
    static String slots(String id, String kind, int x, int y, int columns, int rows, int start) {
        return "{\"id\":\"" + id + "\",\"type\":\"slots\",\"slotKind\":\"" + kind + "\",\"columns\":" + columns + ",\"rows\":" + rows + ",\"slotStart\":" + start
            + ",\"bounds\":{\"x\":" + x + ",\"y\":" + y + ",\"width\":" + Math.max(18, columns * 18) + ",\"height\":" + Math.max(18, rows * 18) + "}}";
    }
    /** Loads one pack with one screen and returns the errors the repository reported (empty: it loaded). */
    static List<String> load(String screenExtra, String... elements) throws Exception {
        Path root = Files.createTempDirectory("wysicraft-slots"), folder = root.resolve("dev").resolve("crafty");
        Files.createDirectories(folder.resolve("ui"));
        Files.writeString(folder.resolve("project.json"), "{\"id\":\"crafty\",\"defaultUi\":\"main\",\"ui\":[\"main\"]}");
        Files.writeString(folder.resolve("ui/main.json"), "{\"id\":\"main\",\"size\":{\"width\":176,\"height\":166}" + screenExtra + ",\"elements\":[" + String.join(",", elements) + "]}");
        var errors = new ArrayList<String>(); var repository = new PackRepository();
        repository.reload(root, (message, error) -> { if (error != null) errors.add(error.getMessage()); });
        if (errors.isEmpty()) assertNotNull(repository.get("crafty:main"));
        return errors;
    }
    static final String GRID = slots("grid", "crafting", 30, 17, 3, 3, 0), RESULT = slots("output", "result", 120, 31, 1, 1, 0),
        MAIN = slots("inventory", "player", 8, 84, 9, 3, 9), HOTBAR = slots("hotbar", "player", 8, 142, 9, 1, 0);

    @Test void aCraftingTableLoads() throws Exception { assertEquals(List.of(), load("", GRID, RESULT, MAIN, HOTBAR)); }
    @Test void storageAndPlayerSlotsLoad() throws Exception { assertEquals(List.of(), load("", slots("chest", "storage", 8, 18, 9, 3, 0), MAIN, HOTBAR)); }
    @Test void playerSlotsStayInsideTheInventory() throws Exception {
        assertTrue(load("", slots("inventory", "player", 8, 84, 9, 1, 30)).getFirst().contains("0-35"));
        assertTrue(load("", slots("inventory", "player", 8, 84, 9, 1, -1)).getFirst().contains("0-35"));
    }
    @Test void aCraftingGridIsAtMostThreeByThree() throws Exception { assertTrue(load("", slots("grid", "crafting", 30, 17, 4, 3, 0)).getFirst().contains("3 × 3")); }
    @Test void aResultIsOneSlotAndNeedsAGrid() throws Exception {
        assertTrue(load("", RESULT).getFirst().contains("needs a crafting grid"));
        assertTrue(load("", GRID, slots("output", "result", 120, 31, 2, 1, 0)).getFirst().contains("one slot"));
        assertTrue(load("", GRID, RESULT, slots("output2", "result", 150, 31, 1, 1, 0)).getFirst().contains("at most one"));
    }
    @Test void unknownKindsAndSizesAreRefused() throws Exception {
        assertTrue(load("", slots("odd", "furnace", 8, 8, 1, 1, 0)).getFirst().contains("player, storage, crafting or result"));
        assertTrue(load("", slots("wide", "storage", 8, 8, 10, 1, 0)).getFirst().contains("1-9 columns"));
    }
    @Test void slotScreensUseAFixedLayout() throws Exception { assertTrue(load(",\"responsive\":true", GRID, RESULT, MAIN).getFirst().contains("fixed layout")); }
    @Test void slotsCantScroll() throws Exception {
        String panel = "{\"id\":\"scroller\",\"type\":\"scroll_panel\",\"bounds\":{\"x\":0,\"y\":0,\"width\":176,\"height\":80}}";
        String inside = slots("chest", "storage", 8, 8, 9, 3, 0).replace("\"type\":\"slots\"", "\"type\":\"slots\",\"parent\":\"scroller\"");
        assertTrue(load("", panel, inside).getFirst().contains("scroll panel"));
    }
    /** The pack the editor exports after its Crafting table stamp (--smoke-slots writes it) loads here, as a real container layout. */
    @Test void theEditorsCraftingTableLoads() throws Exception {
        Path pack = Path.of("../artifacts/slots-demo.wysicraft");
        org.junit.jupiter.api.Assumptions.assumeTrue(Files.exists(pack), "run the editor's --smoke-slots first");
        var screen = PackRepository.load(pack).screens().get("crafting");
        var slots = screen.elements.stream().filter(e -> e.type.equals("slots")).toList();
        assertEquals(4, slots.size());
        var grid = slots.stream().filter(e -> e.slotKind.equals("crafting")).findFirst().orElseThrow();
        assertEquals(3, grid.columns); assertEquals(3, grid.rows); assertEquals(54, (int) grid.bounds.width);
        assertEquals(List.of(0, 9), slots.stream().filter(e -> e.slotKind.equals("player")).map(e -> e.slotStart).sorted().toList());
        assertFalse(screen.responsive);
    }
}
