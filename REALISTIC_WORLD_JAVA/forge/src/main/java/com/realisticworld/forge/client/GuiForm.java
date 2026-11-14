package com.realisticworld.forge.client;

import com.realisticworld.forge.net.RwNetwork;
import com.realisticworld.ui.Form;
import net.minecraft.client.gui.GuiButton;
import net.minecraft.client.gui.GuiScreen;
import net.minecraftforge.fml.client.config.GuiSlider;
import org.lwjgl.input.Mouse;

import java.io.IOException;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Draws any control-center form sent by the server: title, scrollable text,
 * and buttons / toggles / dropdowns / sliders. The answer goes back to the
 * server, which decides what comes next.
 */
public class GuiForm extends GuiScreen {
    private static final int ROW = 24;
    private final Form form;
    private boolean replied;
    private int scroll;
    private int contentHeight;

    /** Something placed in the scrolling column: wrapped text or a widget. */
    private static final class Row {
        List<String> lines;
        GuiButton widget;
        Form.Element element;
        int y;
        int height;
    }

    private final List<Row> rows = new ArrayList<>();
    private final Map<GuiButton, Integer> buttonIndex = new LinkedHashMap<>();
    private GuiButton submit;
    private GuiButton cancel;

    public GuiForm(Form form) {
        this.form = form;
    }

    private int panelWidth() {
        return Math.min(340, width - 20);
    }

    private int left() {
        return (width - panelWidth()) / 2;
    }

    private int viewTop() {
        return 30;
    }

    private int viewBottom() {
        return form.kind == Form.Kind.MODAL ? height - 34 : height - 8;
    }

    @Override
    public void initGui() {
        buttonList.clear();
        rows.clear();
        buttonIndex.clear();
        int w = panelWidth();
        int y = 0;
        int id = 0;
        if (!form.body.isEmpty()) y = addText(form.body, y, w);
        int buttonNo = 0;
        for (Form.Element e : form.elements) {
            switch (e.type) {
                case "button": {
                    GuiButton b = new GuiButton(id++, left(), 0, w, 20, e.text);
                    buttonIndex.put(b, buttonNo++);
                    y = addWidget(b, e, y);
                    break;
                }
                case "label":
                    y = addText(e.text, y, w);
                    break;
                case "toggle":
                    y = addText(e.text, y, w);
                    y = addWidget(new GuiButton(id++, left(), 0, w, 20, onOff(e.bool)), e, y);
                    break;
                case "dropdown":
                    y = addText(e.text, y, w);
                    y = addWidget(new GuiButton(id++, left(), 0, w, 20, dropdownText(e)), e, y);
                    break;
                case "slider":
                    y = addText(e.text, y, w);
                    y = addWidget(new GuiSlider(id++, left(), 0, w, 20, "", "", e.min, e.max, e.value, e.step < 1, true), e, y);
                    break;
                default:
                    break;
            }
        }
        contentHeight = y;
        if (form.kind == Form.Kind.MODAL) {
            submit = new GuiButton(1000, left(), height - 28, w / 2 - 2, 20, form.submit);
            cancel = new GuiButton(1001, left() + w / 2 + 2, height - 28, w / 2 - 2, 20, "Cancel");
            buttonList.add(submit);
            buttonList.add(cancel);
        }
        clampScroll();
        layout();
    }

    private int addText(String text, int y, int w) {
        Row r = new Row();
        r.lines = fontRenderer.listFormattedStringToWidth(text, w);
        r.y = y;
        r.height = r.lines.size() * (fontRenderer.FONT_HEIGHT + 1) + 4;
        rows.add(r);
        return y + r.height;
    }

    private int addWidget(GuiButton b, Form.Element e, int y) {
        Row r = new Row();
        r.widget = b;
        r.element = e;
        r.y = y;
        r.height = ROW;
        rows.add(r);
        buttonList.add(b);
        return y + ROW;
    }

    private static String onOff(boolean v) {
        return v ? "§aON" : "§cOFF";
    }

    private static String dropdownText(Form.Element e) {
        return e.options.isEmpty() ? "-" : "< " + e.options.get(e.index) + " >";
    }

    private void clampScroll() {
        int view = viewBottom() - viewTop();
        scroll = Math.max(0, Math.min(scroll, Math.max(0, contentHeight - view)));
    }

    private void layout() {
        for (Row r : rows) {
            if (r.widget == null) continue;
            int y = viewTop() + r.y - scroll;
            r.widget.y = y;
            r.widget.visible = y >= viewTop() - 2 && y + 20 <= viewBottom() + 2;
        }
    }

    @Override
    public void handleMouseInput() throws IOException {
        super.handleMouseInput();
        int wheel = Mouse.getEventDWheel();
        if (wheel != 0) {
            scroll += wheel > 0 ? -20 : 20;
            clampScroll();
            layout();
        }
    }

    @Override
    public void drawScreen(int mouseX, int mouseY, float partialTicks) {
        drawDefaultBackground();
        drawCenteredString(fontRenderer, form.title, width / 2, 12, 0xFFFFFF);
        int top = viewTop();
        int bottom = viewBottom();
        for (Row r : rows) {
            if (r.lines == null) continue;
            int y = top + r.y - scroll;
            for (String line : r.lines) {
                if (y >= top - 2 && y + fontRenderer.FONT_HEIGHT <= bottom + 2) fontRenderer.drawStringWithShadow(line, left(), y, 0xE0E0E0);
                y += fontRenderer.FONT_HEIGHT + 1;
            }
        }
        if (contentHeight > bottom - top) {
            int track = bottom - top;
            int knob = Math.max(12, track * track / contentHeight);
            int pos = top + (track - knob) * scroll / Math.max(1, contentHeight - track);
            drawRect(left() + panelWidth() + 4, pos, left() + panelWidth() + 6, pos + knob, 0xAAFFFFFF);
        }
        super.drawScreen(mouseX, mouseY, partialTicks);
    }

    @Override
    protected void actionPerformed(GuiButton b) throws IOException {
        if (b == submit) {
            Map<String, Object> values = new LinkedHashMap<>();
            for (Row r : rows) {
                if (r.element == null || !r.element.isControl()) continue;
                Form.Element e = r.element;
                switch (e.type) {
                    case "toggle": values.put(e.name, e.bool); break;
                    case "dropdown": values.put(e.name, e.index); break;
                    case "slider": {
                        double v = ((GuiSlider) r.widget).getValue();
                        if (e.step > 0) v = Math.round(v / e.step) * e.step;
                        values.put(e.name, Math.max(e.min, Math.min(e.max, v)));
                        break;
                    }
                    default: break;
                }
            }
            reply(Form.Reply.submit(form.id, values));
            return;
        }
        if (b == cancel) {
            reply(Form.Reply.cancel(form.id));
            return;
        }
        Integer index = buttonIndex.get(b);
        if (index != null) {
            reply(Form.Reply.pick(form.id, index));
            return;
        }
        for (Row r : rows) {
            if (r.widget != b || r.element == null) continue;
            if (r.element.type.equals("toggle")) {
                r.element.bool = !r.element.bool;
                b.displayString = onOff(r.element.bool);
            } else if (r.element.type.equals("dropdown") && !r.element.options.isEmpty()) {
                r.element.index = (r.element.index + 1) % r.element.options.size();
                b.displayString = dropdownText(r.element);
            }
        }
    }

    private void reply(Form.Reply r) {
        if (replied) return;
        replied = true;
        RwNetwork.sendReply(r);
        mc.displayGuiScreen(null);
    }

    @Override
    public void onGuiClosed() {
        if (!replied) {
            replied = true;
            RwNetwork.sendReply(Form.Reply.cancel(form.id));
        }
    }

    @Override
    public boolean doesGuiPauseGame() {
        return false;
    }
}
