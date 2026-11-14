package com.waterphysics.client;

import com.waterphysics.WaterPhysics;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiMainMenu;
import net.minecraft.client.gui.GuiScreen;
import net.minecraft.util.EnumHand;
import net.minecraft.util.ScreenShotHelper;
import net.minecraft.world.GameType;
import net.minecraft.world.WorldSettings;
import net.minecraft.world.WorldType;
import net.minecraftforge.client.event.GuiOpenEvent;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;
import net.minecraftforge.fml.common.gameevent.TickEvent;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;

/**
 * Automated client test driver, only active with -Dwaterphysics.autotest=&lt;script file&gt;.
 * With -Dwaterphysics.autotest.world=&lt;name&gt; it creates/opens a flat singleplayer world by itself.
 * Script commands (one per line): wait N | cmd /... | look yaw pitch | use | screenshot name | config |
 * closegui | stats | log text | quit
 */
public final class ClientAutoTest {
    private static List<String> script;
    private static int pc, waitTicks, worldWait;
    private static boolean worldStarted, done;
    private static double ox, oy, oz;

    private ClientAutoTest() {}

    public static boolean enabled() {
        return System.getProperty("waterphysics.autotest") != null;
    }

    public static void load() {
        try {
            script = new ArrayList<>(Files.readAllLines(new File(System.getProperty("waterphysics.autotest")).toPath(), StandardCharsets.UTF_8));
            WaterPhysics.LOG.info("[WP-test] loaded {} script lines", script.size());
        } catch (Exception e) {
            WaterPhysics.LOG.error("[WP-test] cannot read script", e);
            script = new ArrayList<>();
        }
    }

    @SubscribeEvent
    public static void onGui(GuiOpenEvent e) {
        String world = System.getProperty("waterphysics.autotest.world");
        if (world == null || worldStarted || !(e.getGui() instanceof GuiMainMenu)) return;
        worldStarted = true;
        Minecraft mc = Minecraft.getMinecraft();
        mc.addScheduledTask(() -> {
            WorldSettings ws = new WorldSettings(12345L, GameType.SURVIVAL, false, false, WorldType.FLAT);
            ws.enableCommands();
            WaterPhysics.LOG.info("[WP-test] launching singleplayer world {}", world);
            mc.launchIntegratedServer(world, world, ws);
        });
    }

    @SubscribeEvent
    public static void onTick(TickEvent.ClientTickEvent e) {
        if (e.phase != TickEvent.Phase.END || done || script == null) return;
        Minecraft mc = Minecraft.getMinecraft();
        if (mc.player == null || mc.world == null) return;
        if (worldWait < 60) { // let the world settle after joining
            worldWait++;
            return;
        }
        if (waitTicks > 0) {
            waitTicks--;
            return;
        }
        while (pc < script.size() && waitTicks == 0) {
            String line = script.get(pc++).trim();
            if (line.isEmpty() || line.startsWith("#")) continue;
            run(mc, line);
        }
        if (pc >= script.size() && waitTicks == 0) done = true;
    }

    private static void run(Minecraft mc, String line) {
        String[] p = line.split(" ", 2);
        String arg = p.length > 1 ? p[1] : "";
        WaterPhysics.LOG.info("[WP-test] > {}", line);
        try {
            switch (p[0]) {
                case "wait":
                    waitTicks = Integer.parseInt(arg.trim());
                    break;
                case "cmd":
                    mc.player.sendChatMessage(resolve(arg));
                    break;
                case "look": {
                    String[] a = arg.split(" ");
                    mc.player.rotationYaw = Float.parseFloat(a[0]);
                    mc.player.rotationPitch = Float.parseFloat(a[1]);
                    mc.player.prevRotationYaw = mc.player.rotationYaw;
                    mc.player.prevRotationPitch = mc.player.rotationPitch;
                    break;
                }
                case "use":
                    WaterPhysics.LOG.info("[WP-test] use -> {}", mc.playerController.processRightClick(mc.player, mc.world, EnumHand.MAIN_HAND));
                    break;
                case "screenshot": {
                    File dir = new File(mc.mcDataDir, "screenshots");
                    WaterPhysics.LOG.info("[WP-test] {}", ScreenShotHelper.saveScreenshot(mc.mcDataDir, arg + ".png",
                            mc.displayWidth, mc.displayHeight, mc.getFramebuffer()).getUnformattedText() + " dir=" + dir);
                    break;
                }
                case "config":
                    ClientSetup.openConfig();
                    break;
                case "closegui":
                    mc.displayGuiScreen((GuiScreen) null);
                    break;
                case "stats":
                    WaterPhysics.LOG.info("[WP-test] client stats packets={} sections={} activeSections={} overlayCells={} particles={} fps={} entityParticles={}",
                            ClientWaterData.packets, ClientWaterData.SECS.size(), activeSections(), WaterEffects.lastOverlayCells,
                            WaterEffects.lastParticles, Minecraft.getDebugFPS(), mc.effectRenderer.getStatistics());
                    break;
                case "origin":
                    ox = Math.floor(mc.player.posX) + 0.5;
                    oy = Math.floor(mc.player.posY);
                    oz = Math.floor(mc.player.posZ) + 0.5;
                    WaterPhysics.LOG.info("[WP-test] origin {} {} {}", ox, oy, oz);
                    break;
                case "view": {
                    // teleport relative to the recorded origin: view dx dy dz yaw pitch
                    String[] a = arg.split(" ");
                    mc.player.sendChatMessage(String.format(java.util.Locale.ROOT, "/tp @p %.2f %.2f %.2f %s %s",
                            ox + Double.parseDouble(a[0]), oy + Double.parseDouble(a[1]), oz + Double.parseDouble(a[2]), a[3], a[4]));
                    break;
                }
                case "hidegui":
                    mc.gameSettings.hideGUI = Boolean.parseBoolean(arg.trim());
                    break;
                case "clearchat":
                    mc.ingameGUI.getChatGUI().clearChatMessages(false);
                    break;
                case "held":
                    WaterPhysics.LOG.info("[WP-test] held item={} count={}", mc.player.getHeldItemMainhand().getItem().getRegistryName(),
                            mc.player.getHeldItemMainhand().getCount());
                    break;
                case "log":
                    WaterPhysics.LOG.info("[WP-test] {}", arg);
                    break;
                case "quit":
                    WaterPhysics.LOG.info("[WP-test] done");
                    done = true;
                    mc.shutdown();
                    break;
                default:
                    WaterPhysics.LOG.warn("[WP-test] unknown command {}", line);
            }
        } catch (Exception ex) {
            WaterPhysics.LOG.error("[WP-test] command failed: " + line, ex);
        }
    }

    private static final java.util.regex.Pattern AT = java.util.regex.Pattern.compile("@\\((-?\\d+),(-?\\d+),(-?\\d+)\\)");

    /** replaces @(dx,dy,dz) with absolute block coordinates relative to the recorded origin */
    private static String resolve(String c) {
        java.util.regex.Matcher m = AT.matcher(c);
        StringBuffer sb = new StringBuffer();
        while (m.find()) {
            m.appendReplacement(sb, ((int) Math.floor(ox) + Integer.parseInt(m.group(1))) + " " + ((int) Math.floor(oy) + Integer.parseInt(m.group(2)))
                    + " " + ((int) Math.floor(oz) + Integer.parseInt(m.group(3))));
        }
        m.appendTail(sb);
        return sb.toString();
    }

    private static int activeSections() {
        int n = 0;
        for (ClientWaterData.CSec s : ClientWaterData.SECS.values()) if (s.active) n++;
        return n;
    }
}
