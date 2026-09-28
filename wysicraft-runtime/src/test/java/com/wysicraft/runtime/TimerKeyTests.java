package com.wysicraft.runtime;

import com.wysicraft.runtime.model.KeyNames;
import com.wysicraft.runtime.pack.PackRepository;
import org.junit.jupiter.api.Test;
import java.nio.file.Files;
import java.nio.file.Path;
import static org.junit.jupiter.api.Assertions.*;

class TimerKeyTests {
    @Test void keyNamesMatchTheEditorPreview() {
        assertEquals("a",KeyNames.name(65)); assertEquals("z",KeyNames.name(90));
        assertEquals("0",KeyNames.name(48)); assertEquals("7",KeyNames.name(327));
        assertEquals("f1",KeyNames.name(290)); assertEquals("f12",KeyNames.name(301));
        assertEquals("space",KeyNames.name(32)); assertEquals("enter",KeyNames.name(257)); assertEquals("enter",KeyNames.name(335));
        assertEquals("left",KeyNames.name(263)); assertEquals("right",KeyNames.name(262));
        assertEquals("up",KeyNames.name(265)); assertEquals("down",KeyNames.name(264));
        assertNull(KeyNames.name(256)); // Escape always closes the screen
    }
    private static void load(String ui) throws Exception {
        Path root=Files.createTempDirectory("wysicraft-tick"), folder=root.resolve("dev/tick");
        Files.createDirectories(folder.resolve("ui"));
        Files.writeString(folder.resolve("project.json"),"{\"id\":\"tick\",\"defaultUi\":\"main\",\"ui\":[\"main\"]}");
        Files.writeString(folder.resolve("ui/main.json"),ui);
        new PackRepository().reload(root,(m,e)->{if(e!=null)throw new RuntimeException(e);});
    }
    @Test void tickAndKeyAreClientOnlyWithABoundedInterval() throws Exception {
        load("{\"id\":\"main\",\"tickInterval\":250,\"elements\":[{\"id\":\"a\",\"type\":\"label\"}],\"events\":{\"tick\":{\"client\":{\"actions\":[{\"type\":\"set_text\",\"target\":\"a\",\"value\":\"x\"}]}},\"key\":{\"client\":{\"actions\":[]}}}}");
        assertThrows(RuntimeException.class,()->load("{\"id\":\"main\",\"tickInterval\":10}"));
        assertThrows(RuntimeException.class,()->load("{\"id\":\"main\",\"tickInterval\":250,\"events\":{\"tick\":{\"server\":{\"actions\":[{\"type\":\"command\",\"value\":\"say hi\"}]}}}}"));
        assertThrows(RuntimeException.class,()->load("{\"id\":\"main\",\"events\":{\"key\":{\"server\":{\"actions\":[{\"type\":\"command\",\"value\":\"say hi\"}]}}}}"));
    }
}
