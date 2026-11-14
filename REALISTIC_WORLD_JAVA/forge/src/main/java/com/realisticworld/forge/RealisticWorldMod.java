package com.realisticworld.forge;

import com.realisticworld.core.Engine;
import com.realisticworld.forge.net.RwNetwork;
import com.realisticworld.ui.ControlCenter;
import com.realisticworld.ui.Form;
import com.realisticworld.water.Bodies;
import com.realisticworld.water.TankScan;
import net.minecraft.entity.player.EntityPlayerMP;
import net.minecraft.server.MinecraftServer;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.text.TextComponentString;
import net.minecraft.world.WorldServer;
import net.minecraft.world.storage.MapStorage;
import net.minecraftforge.common.DimensionManager;
import net.minecraftforge.common.MinecraftForge;
import net.minecraftforge.event.world.BlockEvent;
import net.minecraftforge.event.world.ExplosionEvent;
import net.minecraftforge.fml.common.FMLCommonHandler;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.common.SidedProxy;
import net.minecraftforge.fml.common.event.FMLPreInitializationEvent;
import net.minecraftforge.fml.common.event.FMLServerStartedEvent;
import net.minecraftforge.fml.common.event.FMLServerStartingEvent;
import net.minecraftforge.fml.common.event.FMLServerStoppingEvent;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;
import net.minecraftforge.fml.common.gameevent.PlayerEvent;
import net.minecraftforge.fml.common.gameevent.TickEvent;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.function.Consumer;

/**
 * Realistic World for Minecraft 1.12.2 (Forge). One engine runs on the server
 * for the overworld; it starts with the server and saves with the world.
 */
@Mod(modid = RealisticWorldMod.MODID, name = "Realistic World", version = Engine.VERSION, acceptedMinecraftVersions = "[1.12.2]")
public class RealisticWorldMod {
    public static final String MODID = "realisticworld";
    public static final Logger LOG = LogManager.getLogger("RealisticWorld");

    @SidedProxy(clientSide = "com.realisticworld.forge.client.ClientProxy", serverSide = "com.realisticworld.forge.CommonProxy")
    public static CommonProxy proxy;

    private static Engine engine;
    private static RwSavedData data;
    private static ControlCenter controlCenter;
    private static String lastVanillaWeather = "";
    /** Block changes seen this tick; handled after the game has finished them. */
    private static final List<long[]> removed = new ArrayList<>();
    private static final List<long[]> placed = new ArrayList<>();
    /** Open form per player: form id and what to do with the answer. */
    private static final Map<UUID, Object[]> pendingForms = new HashMap<>();
    private static int nextFormId = 1;
    /** Who is waiting for a gauge measurement, by scanned seed. */
    private static final Map<String, Consumer<String>> gaugeWaiting = new HashMap<>();

    public static Engine engine() {
        return engine;
    }

    @Mod.EventHandler
    public void preInit(FMLPreInitializationEvent event) {
        RwNetwork.register();
        MinecraftForge.EVENT_BUS.register(this);
        proxy.preInit();
    }

    @Mod.EventHandler
    public void serverStarting(FMLServerStartingEvent event) {
        event.registerServerCommand(new CommandRw());
    }

    @Mod.EventHandler
    public void serverStarted(FMLServerStartedEvent event) {
        MinecraftServer server = FMLCommonHandler.instance().getMinecraftServerInstance();
        WorldServer overworld = DimensionManager.getWorld(0);
        MapStorage storage = overworld.getMapStorage();
        data = (RwSavedData) storage.getOrLoadData(RwSavedData.class, RwSavedData.NAME);
        if (data == null) {
            data = new RwSavedData();
            storage.setData(RwSavedData.NAME, data);
        }
        loadAnchors(data.store.load("anchors"));
        engine = new Engine(new ForgeHost(server, LOG), data.store);
        controlCenter = new ControlCenter(engine);
        lastVanillaWeather = ForgeHost.vanillaWeatherNow();
        engine.water.onScan(r -> {
            Consumer<String> out = gaugeWaiting.remove(r.dim + "|" + r.x + "," + r.y + "," + r.z);
            if (out == null || engine == null) return;
            if (r.tankId != null) {
                for (String l : ControlCenter.bodyLines(engine.water.describeBody(engine.water.bodies().get(r.tankId)))) out.accept(l);
            } else if ("natural".equals(r.status)) {
                out.accept("\u00a77Natural water (lake, river or sea): not simulated, never drained.\u00a7r");
            } else {
                out.accept("\u00a77Could not measure this water (" + r.status + ").\u00a7r");
            }
        });
    }

    @Mod.EventHandler
    public void serverStopping(FMLServerStoppingEvent event) {
        if (engine != null) {
            engine.saveNow(true);
            data.syncDirty();
        }
        engine = null;
        controlCenter = null;
        pendingForms.clear();
        gaugeWaiting.clear();
        removed.clear();
        placed.clear();
    }

    // ---------------------------------------------------------------- ticking and events

    @SubscribeEvent
    public void onWorldTick(TickEvent.WorldTickEvent event) {
        if (engine == null || event.phase != TickEvent.Phase.END || event.world.isRemote || event.world.provider.getDimension() != 0) return;
        try {
            for (long[] b : removed) engine.water.onBlockRemoved((int) b[0], (int) b[1], (int) b[2], (int) b[3]);
            for (long[] b : placed) engine.water.onBlockPlaced((int) b[0], (int) b[1], (int) b[2], (int) b[3]);
            removed.clear();
            placed.clear();
            engine.tick();
            String now = ForgeHost.vanillaWeatherNow();
            if (!now.equals(lastVanillaWeather)) {
                engine.weather.onVanillaChange(now, engine.scheduler.ticks());
                lastVanillaWeather = now;
            }
        } catch (RuntimeException e) {
            engine.log.error("core", "engine tick failed", e);
        }
        data.syncDirty();
    }

    @SubscribeEvent
    public void onBreak(BlockEvent.BreakEvent event) {
        if (engine == null || event.getWorld().isRemote) return;
        BlockPos p = event.getPos();
        removed.add(new long[] {event.getWorld().provider.getDimension(), p.getX(), p.getY(), p.getZ()});
    }

    @SubscribeEvent
    public void onExplosion(ExplosionEvent.Detonate event) {
        if (engine == null || event.getWorld().isRemote) return;
        int dim = event.getWorld().provider.getDimension();
        List<BlockPos> blocks = event.getAffectedBlocks();
        for (int i = 0; i < Math.min(64, blocks.size()); i++) {
            BlockPos p = blocks.get(i);
            removed.add(new long[] {dim, p.getX(), p.getY(), p.getZ()});
        }
    }

    @SubscribeEvent
    public void onPlace(BlockEvent.EntityPlaceEvent event) {
        if (engine == null || event.getWorld().isRemote) return;
        BlockPos p = event.getPos();
        placed.add(new long[] {event.getWorld().provider.getDimension(), p.getX(), p.getY(), p.getZ()});
    }

    @SubscribeEvent
    public void onLogout(PlayerEvent.PlayerLoggedOutEvent event) {
        pendingForms.remove(event.player.getUniqueID());
        if (engine != null) {
            engine.saveNow(true);
            data.syncDirty();
        }
    }

    // ---------------------------------------------------------------- control center and forms

    static void openControlCenter(EntityPlayerMP p) {
        if (controlCenter == null) {
            say(p, "§eRealistic World is still starting, try again in a moment.");
            return;
        }
        controlCenter.open(new ForgeViewer(p));
    }

    static String statusLine() {
        return controlCenter == null ? "Realistic World is still starting." : controlCenter.statusLine();
    }

    /** Send a form to a player; the callback runs with the answer. */
    static void showForm(EntityPlayerMP p, Form form, Consumer<Form.Reply> onReply) {
        form.id = nextFormId++;
        pendingForms.put(p.getUniqueID(), new Object[] {form.id, onReply});
        RwNetwork.sendForm(p, form);
    }

    @SuppressWarnings("unchecked")
    public static void onReply(EntityPlayerMP p, Form.Reply reply) {
        Object[] pending = pendingForms.get(p.getUniqueID());
        if (pending == null || (Integer) pending[0] != reply.formId) return; // stale or forged reply
        pendingForms.remove(p.getUniqueID());
        try {
            ((Consumer<Form.Reply>) pending[1]).accept(reply);
        } catch (RuntimeException e) {
            if (engine != null) engine.log.error("ui", "control center failed", e);
            say(p, "§c[Realistic World] The control center hit an error; see Error Log.");
        }
    }

    // ---------------------------------------------------------------- water tools

    static void onValve(int dim, BlockPos pos) {
        if (engine != null) engine.water.onValve(dim, pos.getX(), pos.getY(), pos.getZ());
    }

    private static final int[][] SEVEN = {{0, 0, 0}, {1, 0, 0}, {-1, 0, 0}, {0, 0, 1}, {0, 0, -1}, {0, 1, 0}, {0, -1, 0}};

    /** Water Gauge used on a block by a player. */
    static void gauge(EntityPlayerMP p, int dim, BlockPos pos) {
        gauge(line -> {
            if (!p.hasDisconnected()) say(p, line);
        }, dim, pos);
    }

    /**
     * Report the tank or pool at a block (or next to it), or measure the water
     * next to it. A known tank is also measured again, so changes the mod could
     * not see (commands, pistons, other mods) are picked up.
     */
    static void gauge(Consumer<String> out, int dim, BlockPos pos) {
        if (engine == null) {
            out.accept("Realistic World is still starting.");
            return;
        }
        for (int[] d : SEVEN) {
            Bodies.Tank t = engine.water.tankAt(dim, pos.getX() + d[0], pos.getY() + d[1], pos.getZ() + d[2]);
            if (t != null) {
                for (String l : ControlCenter.bodyLines(engine.water.describeBody(t))) out.accept(l);
                engine.water.rescanTank(t, "gauge");
                return;
            }
        }
        for (int[] d : SEVEN) {
            Bodies.Pool pool = engine.water.poolAt(dim, pos.getX() + d[0], pos.getZ() + d[2]);
            if (pool != null) {
                for (String l : ControlCenter.bodyLines(engine.water.describeBody(pool))) out.accept(l);
                return;
            }
        }
        ForgeHost host = new ForgeHost(FMLCommonHandler.instance().getMinecraftServerInstance(), LOG);
        for (int[] d : SEVEN) {
            int x = pos.getX() + d[0], y = pos.getY() + d[1], z = pos.getZ() + d[2];
            if (host.water().cellAt(dim, x, y, z) != TankScan.Cell.SOURCE) continue;
            if (engine.water.requestScan(dim, x, y, z, "gauge")) {
                gaugeWaiting.put(dim + "|" + x + "," + y + "," + z, out);
                out.accept("\u00a77Measuring the water...\u00a7r");
            } else {
                out.accept("\u00a77This water is natural (lake, river or sea) or already being measured.\u00a7r");
            }
            return;
        }
        out.accept("\u00a77No simulated water here. Use the gauge on a tank wall or next to water.\u00a7r");
    }

    // ---------------------------------------------------------------- anchors

    private static void loadAnchors(Map<String, Object> saved) {
        ForgeHost.ANCHORS.clear();
        if (saved == null) return;
        Object list = saved.get("list");
        if (!(list instanceof List)) return;
        for (Object o : (List<?>) list) {
            if (!(o instanceof List) || ((List<?>) o).size() != 3) continue;
            List<?> a = (List<?>) o;
            ForgeHost.ANCHORS.add(new int[] {((Number) a.get(0)).intValue(), ((Number) a.get(1)).intValue(), ((Number) a.get(2)).intValue()});
        }
    }

    private static void saveAnchors() {
        List<Object> list = new ArrayList<>();
        for (int[] a : ForgeHost.ANCHORS) list.add(new ArrayList<Object>(Arrays.asList(a[0], a[1], a[2])));
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("list", list);
        data.store.save("anchors", m);
    }

    /** Keep an area simulated without a player nearby (like a ticking area). */
    static boolean addAnchor(int dim, int x, int z) {
        if (engine == null || ForgeHost.ANCHORS.size() >= 8) return false;
        ForgeHost.ANCHORS.add(new int[] {dim, x, z});
        saveAnchors();
        return true;
    }

    static void clearAnchors() {
        ForgeHost.ANCHORS.clear();
        if (data != null) saveAnchors();
    }

    static List<int[]> anchors() {
        return ForgeHost.ANCHORS;
    }

    static void say(EntityPlayerMP p, String line) {
        p.sendMessage(new TextComponentString(line));
    }

    static void say(EntityPlayerMP p, List<String> lines) {
        for (String l : lines) say(p, l);
    }
}
