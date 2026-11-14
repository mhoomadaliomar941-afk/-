package com.realisticworld.ui;

import com.realisticworld.FakeWorld;
import com.realisticworld.core.DataStore;
import com.realisticworld.core.Engine;
import com.realisticworld.sim.Regions;
import org.junit.Test;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.function.Consumer;
import java.util.function.Function;

import static org.junit.Assert.*;

/** Control center flows (ported from tests/integration.test.mjs UI steps). */
public class ControlCenterTest {
    /** A player who answers forms from a script; an empty script closes the form. */
    static final class Viewer implements ControlCenter.Viewer {
        final boolean op;
        boolean perf, weatherHud;
        final List<Form> shown = new ArrayList<>();
        final List<String> messages = new ArrayList<>();
        final Deque<Function<Form, Form.Reply>> answers = new ArrayDeque<>();

        Viewer(boolean op) {
            this.op = op;
        }

        public String name() { return "Steve"; }
        public boolean isOperator() { return op; }
        public boolean perfHud() { return perf; }
        public void setPerfHud(boolean on) { perf = on; }
        public boolean weatherHud() { return weatherHud; }
        public void setWeatherHud(boolean on) { weatherHud = on; }
        public int dim() { return 0; }
        public double x() { return 8; }
        public double y() { return 71; }
        public double z() { return 8; }
        public void message(String text) { messages.add(text); }

        public void show(Form form, Consumer<Form.Reply> onReply) {
            // Every form goes through the same map encoding as the network packet.
            Form sent = Form.fromMap(DataStore.deepCopy(form.toMap()));
            shown.add(sent);
            Function<Form, Form.Reply> a = answers.poll();
            Form.Reply r = a == null ? Form.Reply.cancel(sent.id) : a.apply(sent);
            onReply.accept(Form.Reply.fromMap(DataStore.deepCopy(r.toMap())));
        }

        Form last() {
            return shown.get(shown.size() - 1);
        }
    }

    static Function<Form, Form.Reply> pick(String startsWith) {
        return f -> {
            List<String> b = f.buttons();
            for (int i = 0; i < b.size(); i++) if (b.get(i).startsWith(startsWith)) return Form.Reply.pick(f.id, i);
            throw new AssertionError("no button " + startsWith + " in " + b);
        };
    }

    /** Submit a modal with its defaults, overriding by control name. */
    static Function<Form, Form.Reply> submit(Map<String, Object> overrides) {
        return f -> {
            Map<String, Object> v = new LinkedHashMap<>();
            for (Form.Element e : f.elements) {
                if (!e.isControl()) continue;
                Object def = e.type.equals("toggle") ? e.bool : e.type.equals("dropdown") ? (Object) e.index : (Object) e.value;
                v.put(e.name, overrides.containsKey(e.name) ? overrides.get(e.name) : def);
            }
            return Form.Reply.submit(f.id, v);
        };
    }

    static Map<String, Object> kv(Object... pairs) {
        Map<String, Object> m = new LinkedHashMap<>();
        for (int i = 0; i < pairs.length; i += 2) m.put((String) pairs[i], pairs[i + 1]);
        return m;
    }

    static Engine engine() {
        FakeWorld w = new FakeWorld();
        w.players.add(new Regions.PlayerPos(0, 8, 8));
        Engine e = new Engine(w, new DataStore());
        for (int i = 0; i < 60; i++) e.tick();
        return e;
    }

    @Test
    public void mainMenuButtonsAndPlannedText() {
        Engine e = engine();
        Viewer v = new Viewer(true);
        new ControlCenter(e).open(v);
        assertEquals(ControlCenter.TITLE, v.last().title);
        assertEquals("[Dashboard, Weather, Water Physics, Systems, Performance, General Settings, Error Log (0), Run Self-Test]", v.last().buttons().toString());
        boolean planned = false;
        for (Form.Element el : v.last().elements) planned |= el.type.equals("label") && el.text.contains("Vegetation & Seasons (phase 4)");
        assertTrue("planned sections are text, not buttons", planned);
    }

    @Test
    public void dashboardShowsLiveData() {
        Engine e = engine();
        Viewer v = new Viewer(true);
        v.answers.add(pick("Dashboard"));
        new ControlCenter(e).open(v);
        assertEquals("Dashboard", v.last().title);
        assertTrue(v.last().body.contains("NEAR 25   MEDIUM 56   FAR 88"));
        assertTrue(v.last().body.contains("not FPS"));
    }

    @Test
    public void operatorChangesProfileAndHud() {
        Engine e = engine();
        Viewer v = new Viewer(true);
        v.answers.add(pick("Performance"));
        v.answers.add(submit(kv("profile", 0, "hud", true)));
        new ControlCenter(e).open(v);
        assertEquals("LOW", e.perf.profile().name);
        assertTrue(v.perf);
        assertTrue(v.messages.get(v.messages.size() - 1).contains("Performance settings applied"));
    }

    @Test
    public void nonOperatorGetsReadOnlyPages() {
        Engine e = engine();
        Viewer v = new Viewer(false);
        v.answers.add(pick("Performance"));
        v.answers.add(submit(kv("hud", true)));
        new ControlCenter(e).open(v);
        Form perf = v.shown.get(1);
        assertNull("no profile control for non-operators", perf.control("profile"));
        assertEquals("MEDIUM", e.perf.profile().name);
        assertTrue("own HUD still allowed", v.perf);
        Viewer v2 = new Viewer(false);
        v2.answers.add(pick("General Settings"));
        new ControlCenter(e).open(v2);
        assertTrue(v2.messages.get(0).contains("Only operators"));
    }

    @Test
    public void systemsPageSwitchesSystems() {
        Engine e = engine();
        Viewer v = new Viewer(true);
        v.answers.add(pick("Systems"));
        v.answers.add(submit(kv("water", false)));
        new ControlCenter(e).open(v);
        assertEquals("OFF", e.scheduler.info("water").state);
        assertFalse(e.config.isSystemEnabled("water"));
    }

    @Test
    public void weatherForceForecastAndSettings() {
        Engine e = engine();
        ControlCenter cc = new ControlCenter(e);
        Viewer v = new Viewer(true);
        v.answers.add(pick("Weather"));
        v.answers.add(pick("Force weather"));
        v.answers.add(submit(kv("type", ControlCenter.FORCE_TYPES.indexOf("THUNDERSTORM"), "minutes", 5.0)));
        v.answers.add(f -> Form.Reply.cancel(f.id)); // the weather page shown again
        cc.open(v);
        assertEquals("THUNDERSTORM", e.weather.state().forceType);
        assertTrue(v.messages.get(0).contains("thunderstorm for 5 min"));
        Form page = v.last();
        assertTrue(page.body.contains("Forced: thunderstorm"));
        assertTrue(page.body.matches("(?s).*Temperature: §f-?\\d+\\.\\d°C.*"));

        Viewer v2 = new Viewer(true);
        v2.answers.add(pick("Forecast"));
        cc.weather(v2);
        int days = v2.last().body.split("\nDay ").length - 1;
        assertEquals(12, days);
    }

    @Test
    public void waterPageListsTanksNearby() {
        FakeWorld w = new FakeWorld();
        w.players.add(new Regions.PlayerPos(0, 2, 12));
        for (int y = 71; y <= 75; y++) {
            for (int x = -1; x <= 5; x++) for (int z = -1; z <= 5; z++) {
                boolean wall = x == -1 || x == 5 || z == -1 || z == 5;
                if (wall) w.set(x, y, z, FakeWorld.STONE);
                else if (y <= 74) w.set(x, y, z, FakeWorld.WATER);
            }
        }
        Engine e = new Engine(w, new DataStore());
        for (int i = 0; i < 40; i++) e.tick();
        e.water.requestScan(0, 2, 72, 2, "gauge");
        for (int i = 0; i < 10; i++) e.tick();
        Viewer v = new Viewer(true);
        v.answers.add(pick("Water Physics"));
        new ControlCenter(e).open(v);
        assertEquals("Water Physics", v.last().title);
        assertTrue(v.last().body, v.last().body.contains("1 tank(s), 0 pool(s)"));
        assertTrue(v.last().body.contains("100,000 L of 125,000 L"));
        assertTrue(v.last().body.contains("pressure at the bottom 39.2 kPa"));
    }

    @Test
    public void selfTestAndResetFromTheMenu() {
        Engine e = engine();
        ControlCenter cc = new ControlCenter(e);
        Viewer v = new Viewer(true);
        v.answers.add(pick("Run Self-Test"));
        cc.open(v);
        assertTrue(v.last().body, v.last().body.startsWith("5/5 checks passed"));
        for (int i = 0; i < 5; i++) e.tick(); // time passes before the player presses "Run again"
        cc.selfTest(v);
        assertTrue(v.last().body, v.last().body.startsWith("5/5 checks passed"));
        cc.selfTest(v); // no tick in between: the engine-loop check notices
        assertTrue(v.last().body.startsWith("4/5 checks passed"));

        e.config.set("profile", "ULTRA");
        Viewer v2 = new Viewer(true);
        v2.answers.add(pick("General Settings"));
        v2.answers.add(pick("Reset all settings"));
        v2.answers.add(f -> Form.Reply.pick(f.id, 0));
        cc.open(v2);
        assertTrue(v2.messages.get(0).contains("reset to defaults"));
        assertEquals("MEDIUM", e.config.string("profile"));
    }

    @Test
    public void statusLine() {
        assertTrue(new ControlCenter(engine()).statusLine().matches("(?s)Realistic World 0\\.3\\.0 \\| RUNNING \\| tick 60\n.*NEAR 25 MEDIUM 56 FAR 88.*"));
    }
}
