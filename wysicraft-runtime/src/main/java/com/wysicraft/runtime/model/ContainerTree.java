package com.wysicraft.runtime.model;
import com.wysicraft.runtime.model.Models.*;
import java.util.*;
public final class ContainerTree {
    public static List<Element> ancestors(Ui screen,Element child) {
        var result=new ArrayList<Element>();var seen=new HashSet<String>();seen.add(child.id);String id=child.parent;
        while(!id.isEmpty()) {
            if(!seen.add(id) || seen.size()>33)throw new IllegalArgumentException("Container cycle or nesting exceeds 32 levels");
            var parent=screen.element(id);
            if(parent==null || !Set.of("panel","scroll_panel","camera").contains(parent.type))throw new IllegalArgumentException("Parent must identify a panel, scroll panel or camera");
            result.add(parent);id=parent.parent;
        }
        return result;
    }
}
