package com.waterphysics.pipe;

import com.waterphysics.WaterPhysics;
import com.waterphysics.config.WPConfig;
import com.waterphysics.net.MsgEffect;
import com.waterphysics.net.WaterNet;
import com.waterphysics.sim.CellType;
import com.waterphysics.sim.WaterManager;
import com.waterphysics.sim.WaterSim;
import com.waterphysics.tile.TileFluidNode;
import com.waterphysics.tile.TilePipe;
import com.waterphysics.tile.TilePump;
import com.waterphysics.tile.TileValve;
import it.unimi.dsi.fastutil.ints.Int2ObjectOpenHashMap;
import net.minecraft.block.state.IBlockState;
import net.minecraft.init.SoundEvents;
import net.minecraft.tileentity.TileEntity;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.SoundCategory;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;
import net.minecraft.world.WorldServer;
import net.minecraftforge.event.world.ExplosionEvent;
import net.minecraftforge.event.world.WorldEvent;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;
import net.minecraftforge.fml.common.gameevent.TickEvent;

import java.util.*;

/**
 * Hydraulic network of pipes, valves and pumps of one dimension.
 * <ul>
 *   <li>Each node is a 1 m long chamber of the pipe's cross section (capacity = pi D^2 / 4).</li>
 *   <li>Partially filled nodes have a gravity head (water depth inside the pipe); full nodes are
 *       pressurised and get their head from an iterative pressure relaxation over the network.</li>
 *   <li>Flow between nodes follows Darcy-Weisbach: Q = A * sqrt(2 g dH / (f L/D + K)), the smaller
 *       diameter of the two nodes limits the flow, a valve scales the open area.</li>
 *   <li>Pumps add head along the pump curve H(Q) = Hmax (1 - (Q/Qmax)^2).</li>
 *   <li>Free pipe ends exchange water with the world cell they open into (Torricelli).</li>
 *   <li>Above the rated pressure a pipe takes damage: cracked pipes leak, burst pipes leak heavily, and
 *       fragile (glass) pipes shatter.</li>
 * </ul>
 */
@Mod.EventBusSubscriber(modid = WaterPhysics.MODID)
public final class PipeManager {
    private static final Int2ObjectOpenHashMap<PipeManager> MANAGERS = new Int2ObjectOpenHashMap<>();

    private final World world;
    private final Set<TileFluidNode> nodes = new LinkedHashSet<>();
    private final Set<com.waterphysics.tile.TileSpigot> spigots = new LinkedHashSet<>();
    private boolean dirty = true;
    private int rebuildTimer;
    private final List<Link> links = new ArrayList<>();
    private final List<Port> ports = new ArrayList<>();
    private final Map<TileFluidNode, List<Link>> nodeLinks = new HashMap<>();

    static final class Link {
        final TileFluidNode a, b;
        final EnumFacing dirAB;
        float q; // m^3/s from a to b (smoothed)
        Link(TileFluidNode a, TileFluidNode b, EnumFacing dirAB) {
            this.a = a;
            this.b = b;
            this.dirAB = dirAB;
        }
    }

    static final class Port {
        final TileFluidNode node;
        final EnumFacing side;
        final BlockPos cell;
        final float areaFactor; // 1 = open pipe end, < 1 = leak
        float q; // m^3/s, + = node -> world
        float qs; // smoothed
        Port(TileFluidNode node, EnumFacing side, float areaFactor) {
            this.node = node;
            this.side = side;
            this.cell = node.getPos().offset(side);
            this.areaFactor = areaFactor;
        }
    }

    private PipeManager(World world) {
        this.world = world;
    }

    public static PipeManager get(World w) {
        int dim = w.provider.getDimension();
        PipeManager m = MANAGERS.get(dim);
        if (m == null || m.world != w) {
            m = new PipeManager(w);
            MANAGERS.put(dim, m);
        }
        return m;
    }

    public void add(TileFluidNode n) {
        nodes.add(n);
        dirty = true;
    }

    public void remove(TileFluidNode n) {
        nodes.remove(n);
        dirty = true;
    }

    public void addSpigot(com.waterphysics.tile.TileSpigot s) {
        spigots.add(s);
    }

    public void removeSpigot(com.waterphysics.tile.TileSpigot s) {
        spigots.remove(s);
    }

    public void markDirty() {
        dirty = true;
    }

    public int nodeCount() {
        return nodes.size();
    }

    public double totalStored() {
        double t = 0;
        for (TileFluidNode n : nodes) t += n.volume;
        return t;
    }

    @SubscribeEvent
    public static void onWorldTick(TickEvent.WorldTickEvent e) {
        // runs before the water simulation (registered first on the same phase is not guaranteed, but
        // order does not matter: both only move water between stores)
        if (e.phase != TickEvent.Phase.END || e.world.isRemote) return;
        PipeManager m = MANAGERS.get(e.world.provider.getDimension());
        if (m != null && m.world == e.world) m.tick();
    }

    @SubscribeEvent
    public static void onWorldUnload(WorldEvent.Unload e) {
        if (e.getWorld().isRemote) return;
        PipeManager m = MANAGERS.get(e.getWorld().provider.getDimension());
        if (m != null && m.world == e.getWorld()) MANAGERS.remove(e.getWorld().provider.getDimension());
    }

    @SubscribeEvent
    public static void onExplosion(ExplosionEvent.Detonate e) {
        if (e.getWorld().isRemote) return;
        PipeManager m = MANAGERS.get(e.getWorld().provider.getDimension());
        if (m == null) return;
        net.minecraft.util.math.Vec3d c = e.getExplosion().getPosition();
        for (TileFluidNode n : new ArrayList<>(m.nodes)) {
            if (!(n instanceof TilePipe)) continue;
            double d = n.getPos().distanceSqToCenter(c.x, c.y, c.z);
            if (d < 36) {
                ((TilePipe) n).damage = Math.min(1f, ((TilePipe) n).damage + (float) (0.9 - d / 40.0));
                n.syncIfChanged(true);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    private void rebuild() {
        Map<String, Float> oldPortQ = new HashMap<>();
        for (Port p : ports) oldPortQ.put(p.node.getPos().toLong() + ":" + p.side.getIndex(), p.qs);
        Map<String, Float> oldLinkQ = new HashMap<>();
        for (Link l : links) oldLinkQ.put(l.a.getPos().toLong() + ":" + l.b.getPos().toLong(), l.q);
        links.clear();
        ports.clear();
        nodeLinks.clear();
        nodes.removeIf(TileEntity::isInvalid);
        for (TileFluidNode n : nodes) nodeLinks.put(n, new ArrayList<>());
        for (TileFluidNode n : nodes) {
            if (!world.isBlockLoaded(n.getPos())) continue;
            int linked = 0;
            EnumFacing onlyLink = null;
            List<EnumFacing> openSides = new ArrayList<>();
            for (EnumFacing f : EnumFacing.VALUES) {
                if (!n.connectsTo(f)) continue;
                BlockPos np = n.getPos().offset(f);
                if (!world.isBlockLoaded(np)) continue;
                TileEntity te = world.getTileEntity(np);
                if (te instanceof TileFluidNode && !te.isInvalid() && ((TileFluidNode) te).connectsTo(f.getOpposite())) {
                    linked++;
                    onlyLink = f;
                    if (n.getPos().compareTo(np) < 0) {
                        Link l = new Link(n, (TileFluidNode) te, f);
                        links.add(l);
                        nodeLinks.get(n).add(l);
                        List<Link> other = nodeLinks.get(te);
                        if (other != null) other.add(l);
                    }
                } else if (n.canPort(f) && CellType.of(world.getBlockState(np)) != CellType.SOLID
                        && CellType.of(world.getBlockState(np)) != CellType.UNLOADED && CellType.of(world.getBlockState(np)) != CellType.LAVA) {
                    openSides.add(f);
                }
            }
            if (n instanceof TilePipe) {
                TilePipe p = (TilePipe) n;
                if (p.cracked()) {
                    for (EnumFacing f : openSides) ports.add(new Port(n, f, p.burst() ? 0.6f : 0.04f));
                    continue;
                }
                // free ends only: a pipe with one link opens on the far side (or into water it touches),
                // a pipe without links opens towards water
                if (linked == 1) {
                    EnumFacing far = onlyLink.getOpposite();
                    if (openSides.contains(far)) ports.add(new Port(n, far, 1f));
                    else for (EnumFacing f : openSides) if (hasWater(n.getPos().offset(f))) ports.add(new Port(n, f, 1f));
                } else if (linked == 0) {
                    for (EnumFacing f : openSides) if (hasWater(n.getPos().offset(f))) ports.add(new Port(n, f, 1f));
                    if (ports.isEmpty() || ports.get(ports.size() - 1).node != n) {
                        for (EnumFacing f : openSides) ports.add(new Port(n, f, 1f));
                    }
                }
            } else {
                // pumps and valves: their axis sides open into the world when not linked
                for (EnumFacing f : openSides) ports.add(new Port(n, f, 1f));
            }
        }
        for (Port p : ports) {
            Float q = oldPortQ.get(p.node.getPos().toLong() + ":" + p.side.getIndex());
            if (q != null) p.qs = q;
        }
        for (Link l : links) {
            Float q = oldLinkQ.get(l.a.getPos().toLong() + ":" + l.b.getPos().toLong());
            if (q != null) l.q = q;
        }
        dirty = false;
        rebuildTimer = 40;
    }

    private boolean hasWater(BlockPos p) {
        IBlockState s = world.getBlockState(p);
        return s.getBlock() == WaterPhysics.WATER_BLOCK || CellType.of(s) == CellType.RESERVOIR;
    }

    /** water of removed pipe blocks, put back into the world at the next tick */
    private final List<Object[]> releases = new ArrayList<>();

    public void release(BlockPos pos, float volume) {
        releases.add(new Object[]{pos, volume});
    }

    private void tick() {
        WaterSim sim = WaterManager.get(world);
        if (sim == null) return;
        if (!releases.isEmpty()) {
            for (Object[] r : releases) {
                float left = sim.release((BlockPos) r[0], (Float) r[1]);
                if (left > 0f) sim.stats.destroyedOut += left;
            }
            releases.clear();
        }
        if (!spigots.isEmpty() && WPConfig.simulation.enabled) {
            for (com.waterphysics.tile.TileSpigot sp : new ArrayList<>(spigots)) {
                if (sp.isInvalid()) spigots.remove(sp);
                else if (world.isBlockLoaded(sp.getPos())) sp.update();
            }
        }
        if (nodes.isEmpty() || !WPConfig.simulation.enabled) return;
        if (dirty || --rebuildTimer <= 0) rebuild();
        final float g = (float) WPConfig.physics.gravity;
        final float cd = (float) WPConfig.physics.dischargeCoefficient;
        final float fD = (float) WPConfig.pipes.frictionFactor;
        final int sub = 6;
        final float dt = (float) (0.05 * WPConfig.simulation.timeScale / sub);
        gcache = g;
        cdcache = cd;
        fDcache = fD;
        for (TileFluidNode n : nodes) n.throughput = 0f;
        for (int k = 0; k < sub; k++) {
            computeHeads(sim);
            relax(sim);
            computeHeads(sim);
            step(sim, g, cd, fD, dt);
        }
        damageAndSync(sim, 0.05f * (float) WPConfig.simulation.timeScale);
    }

    private static float bottom(TileFluidNode n) {
        return n.getPos().getY() + 0.5f - n.diameter() / 2f;
    }

    private static final float STIFF = 4f; // m of head per 100 % over-fill: weak correction, pressure comes from the relaxation
    private float gcache = 9.81f, cdcache = 0.62f, fDcache = 0.025f;

    private void computeHeads(WaterSim sim) {
        for (TileFluidNode n : nodes) {
            float cap = n.capacity();
            float f = n.volume / cap;
            float bot = bottom(n);
            if (f < 0.995f) {
                n.head = bot + Math.max(0f, f) * n.diameter();
                n.pressureHead = 0f;
            } else {
                float top = bot + n.diameter();
                n.head = Math.max(top, n.pressureHead) + Math.max(0f, f - 1f) * STIFF;
            }
        }
    }

    private float pumpBoost(TilePump p) {
        return pumpHeadAt(p, p.lastFlow);
    }

    /** pump whose outlet is this link, or null */
    private static TilePump outletPump(Link l) {
        if (l.a instanceof TilePump && ((TilePump) l.a).facing() == l.dirAB) return (TilePump) l.a;
        if (l.b instanceof TilePump && ((TilePump) l.b).facing() == l.dirAB.getOpposite()) return (TilePump) l.b;
        return null;
    }

    /**
     * Pump operating point: the flow at which the pump curve H(Q) = Hmax (1 - (Q/Qmax)^2) equals the head
     * the network asks for (discharge - suction). A built-in check valve prevents back-flow.
     */
    private static float pumpFlow(TilePump p, float suction, float discharge) {
        if (!p.running()) return 0f;
        float hmax = (float) WPConfig.pipes.pumpMaxHead;
        float qmax = (float) (WPConfig.pipes.pumpMaxFlow / 1000.0);
        float lift = discharge - suction;
        if (lift >= hmax) return 0f;
        return qmax * (float) Math.sqrt(Math.max(0.0, 1.0 - lift / hmax));
    }

    /** head added by a pump that currently delivers flow q */
    private static float pumpHeadAt(TilePump p, float q) {
        if (!p.running()) return 0f;
        float hmax = (float) WPConfig.pipes.pumpMaxHead;
        float qmax = (float) (WPConfig.pipes.pumpMaxFlow / 1000.0);
        float r = Math.max(0f, q) / qmax;
        return hmax * Math.max(0f, 1f - r * r);
    }

    /** head difference driving flow a -> b including pump head */
    private float drivingHead(Link l) {
        float dh = l.a.head - l.b.head;
        if (l.a instanceof TilePump && ((TilePump) l.a).facing() == l.dirAB) dh += pumpBoost((TilePump) l.a);
        if (l.b instanceof TilePump && ((TilePump) l.b).facing() == l.dirAB.getOpposite()) dh -= pumpBoost((TilePump) l.b);
        return dh;
    }

    /** pump head seen by a port: the pump node holds the suction-side head, the head is added on its outlet */
    private float portBoost(Port p) {
        if (!(p.node instanceof TilePump)) return 0f;
        TilePump pu = (TilePump) p.node;
        return pu.facing() == p.side ? pumpBoost(pu) : 0f;
    }

    /** linearised hydraulic conductance (m^2/s) of an element with quadratic loss: Q = C * dH */
    private static float conductance(float area, float loss, float dh, float g) {
        float h = Math.max(0.02f, Math.abs(dh));
        return area * (float) Math.sqrt(2.0 * g / (loss * h));
    }

    private void relax(WaterSim sim) {
        for (int it = 0; it < 12; it++) {
            for (TileFluidNode n : nodes) {
                if (n.volume / n.capacity() < 0.995f) continue;
                float sum = 0f, w = 0f;
                for (Link l : nodeLinks.getOrDefault(n, Collections.emptyList())) {
                    TileFluidNode o = l.a == n ? l.b : l.a;
                    if (n instanceof TilePump && outletPump(l) == n) continue; // suction side defines the pump head
                    float pump = drivingHead(l) - (l.a.head - l.b.head);
                    float seen = l.a == n ? o.head - pump : o.head + pump;
                    float open = linkOpening(l);
                    if (open <= 0f) continue;
                    float dmin = Math.min(n.diameter(), o.diameter());
                    float ow = (float) (Math.PI * dmin * dmin / 4.0) * open;
                    sum += seen * ow;
                    w += ow;
                }
                for (Port p : ports) {
                    if (p.node != n) continue;
                    if (n instanceof TilePump && ((TilePump) n).facing() == p.side) continue;
                    float hc = cellHead(sim, p) - portBoost(p);
                    float pa = n.area() * p.areaFactor * (n instanceof TileValve ? ((TileValve) n).opening() : 1f);
                    if (pa <= 0f) continue;
                    float pw = pa * cdcache;
                    sum += hc * pw;
                    w += pw;
                }
                float top = bottom(n) + n.diameter();
                float target = w > 0f ? Math.max(top, sum / w) : top;
                float ph = Math.max(top, n.pressureHead);
                n.pressureHead = 0.5f * ph + 0.5f * target;
                n.head = n.pressureHead + Math.max(0f, n.volume / n.capacity() - 1f) * STIFF;
            }
        }
    }

    private float linkOpening(Link l) {
        float o = 1f;
        if (l.a instanceof TileValve) o = Math.min(o, ((TileValve) l.a).opening());
        if (l.b instanceof TileValve) o = Math.min(o, ((TileValve) l.b).opening());
        return o;
    }

    private float cellHead(WaterSim sim, Port p) {
        BlockPos c = p.cell;
        IBlockState s = world.getBlockState(c);
        byte t = CellType.of(s);
        if (t == CellType.RESERVOIR) {
            int top = c.getY();
            while (top < 255 && CellType.of(world.getBlockState(new BlockPos(c.getX(), top + 1, c.getZ()))) == CellType.RESERVOIR) top++;
            return top + 0.89f;
        }
        if (sim.getVolume(c) > 0f) return sim.headAt(c);
        // air: atmospheric at the bottom of the pipe opening
        return bottom(p.node);
    }

    /** one sub-step: all link and port flows are computed from the same heads and applied together */
    private void step(WaterSim sim, float g, float cd, float fD, float dt) {
        Map<TileFluidNode, float[]> io = new HashMap<>(); // [out, in] m^3/s
        for (TileFluidNode n : nodes) io.put(n, new float[2]);
        for (Link l : links) {
            TilePump pump = outletPump(l);
            if (pump != null) {
                TileFluidNode other = l.a == pump ? l.b : l.a;
                float q = pumpFlow(pump, pump.head, other.head);
                float smooth = 0.8f * Math.abs(l.q) + 0.2f * q;
                l.q = l.a == pump ? smooth : -smooth;
                TileFluidNode src = l.q >= 0f ? l.a : l.b, dst = l.q >= 0f ? l.b : l.a;
                io.get(src)[0] += Math.abs(l.q);
                io.get(dst)[1] += Math.abs(l.q);
                continue;
            }
            float open = linkOpening(l);
            float dh = drivingHead(l);
            float dmin = Math.min(l.a.diameter(), l.b.diameter());
            float area = (float) (Math.PI * dmin * dmin / 4.0) * open;
            float qc = 0f;
            if (area > 0f && Math.abs(dh) > 1e-6f) {
                float loss = fD * 1f / dmin + 0.5f + (open < 1f ? (1f / (open * open) - 1f) : 0f);
                qc = Math.signum(dh) * area * (float) Math.sqrt(2.0 * g * Math.abs(dh) / loss);
            }
            l.q = area <= 0f ? 0f : 0.7f * l.q + 0.3f * qc;
            TileFluidNode src = l.q >= 0f ? l.a : l.b, dst = l.q >= 0f ? l.b : l.a;
            io.get(src)[0] += Math.abs(l.q);
            io.get(dst)[1] += Math.abs(l.q);
        }
        for (Port p : ports) {
            TileFluidNode n = p.node;
            IBlockState s = world.getBlockState(p.cell);
            byte t = CellType.of(s);
            p.q = 0f;
            if (t != CellType.OPEN && t != CellType.RESERVOIR) continue;
            float area = n.area() * p.areaFactor;
            if (n instanceof TileValve) area *= ((TileValve) n).opening();
            if (area <= 0f) continue;
            float q;
            if (n instanceof TilePump && ((TilePump) n).facing() == p.side) {
                q = 0.8f * Math.max(0f, p.qs) + 0.2f * pumpFlow((TilePump) n, n.head, cellHead(sim, p));
            } else {
                float dh = n.head + portBoost(p) - cellHead(sim, p);
                if (Math.abs(dh) < 1e-5f) continue;
                q = Math.signum(dh) * cd * area * (float) Math.sqrt(2.0 * g * Math.abs(dh));
                q = 0.7f * p.qs + 0.3f * q;
            }
            if (q < 0f && t == CellType.OPEN) q = Math.max(q, -sim.getVolume(p.cell) / dt); // only what the cell holds
            p.q = q;
            p.qs = q;
            if (q > 0f) io.get(n)[0] += q;
            else io.get(n)[1] -= q;
        }
        // a node can send what it holds plus what flows into it during this sub-step; iterate because
        // scaling a node's outflow reduces the inflow of its neighbours
        Map<TileFluidNode, Float> scale = new HashMap<>();
        for (int pass = 0; pass < 4; pass++) {
            for (float[] v : io.values()) v[0] = v[1] = 0f;
            for (Link l : links) {
                TileFluidNode src = l.q >= 0f ? l.a : l.b, dst = l.q >= 0f ? l.b : l.a;
                Float k = scale.get(src);
                float q = Math.abs(l.q) * (k != null ? k : 1f);
                io.get(src)[0] += q;
                io.get(dst)[1] += q;
            }
            for (Port p : ports) {
                if (p.q > 0f) {
                    Float k = scale.get(p.node);
                    io.get(p.node)[0] += p.q * (k != null ? k : 1f);
                } else if (p.q < 0f) {
                    io.get(p.node)[1] -= p.q;
                }
            }
            boolean changed = false;
            for (Map.Entry<TileFluidNode, float[]> e : io.entrySet()) {
                float out = e.getValue()[0];
                float avail = Math.max(0f, e.getKey().volume) / dt + e.getValue()[1];
                Float old = scale.get(e.getKey());
                float k = old != null ? old : 1f;
                if (out > avail * 1.0001f && out > 0f) {
                    scale.put(e.getKey(), k * avail / out);
                    changed = true;
                }
            }
            if (!changed) break;
        }
        for (Link l : links) {
            TileFluidNode src = l.q >= 0f ? l.a : l.b;
            Float k = scale.get(src);
            if (k != null) l.q *= k;
            float a = l.q * dt;
            l.a.volume -= a;
            l.b.volume += a;
            l.a.throughput = Math.max(l.a.throughput, Math.abs(l.q));
            l.b.throughput = Math.max(l.b.throughput, Math.abs(l.q));
            TilePump op = outletPump(l);
            if (op != null) op.lastFlow = Math.abs(l.q);
        }
        for (Port p : ports) {
            if (p.q == 0f) continue;
            TileFluidNode n = p.node;
            byte t = CellType.of(world.getBlockState(p.cell));
            if (p.q > 0f) {
                Float k = scale.get(n);
                float amt = p.q * (k != null ? k : 1f) * dt;
                amt = Math.min(amt, Math.max(0f, n.volume));
                if (amt <= 0f) continue;
                if (t == CellType.RESERVOIR) {
                    n.volume -= amt;
                    sim.stats.reservoirOut += amt;
                } else {
                    n.volume -= sim.addWater(p.cell, amt);
                }
            } else {
                float amt = -p.q * dt;
                if (t == CellType.RESERVOIR) {
                    n.volume += amt;
                    sim.stats.pumpedIn += amt;
                } else {
                    n.volume += sim.takeWater(p.cell, amt);
                }
            }
            n.throughput = Math.max(n.throughput, Math.abs(p.q));
            if (n instanceof TilePump && ((TilePump) n).facing() == p.side) ((TilePump) n).lastFlow = Math.max(0f, p.q);
        }
    }

    private void damageAndSync(WaterSim sim, float dtTick) {
        List<TileFluidNode> broken = new ArrayList<>();
        for (TileFluidNode n : nodes) {
            if (n.volume < 0f) {
                sim.stats.rounding += n.volume;
                n.volume = 0f;
            }
            if (n instanceof TilePipe && WPConfig.pipes.pipeDamage) {
                TilePipe p = (TilePipe) n;
                p.smoothedKPa = 0.8f * p.smoothedKPa + 0.2f * p.gaugePressureKPa();
                float pkpa = p.smoothedKPa;
                float rated = p.ratedPressure();
                if (pkpa > rated) {
                    boolean wasCracked = p.cracked();
                    p.damage = Math.min(1f, p.damage + (float) WPConfig.pipes.damageRate * (pkpa / rated - 1f) * dtTick);
                    if (!wasCracked && p.cracked()) {
                        dirty = true;
                        WaterNet.effect(world, MsgEffect.CRACK, p.getPos(), pkpa / rated);
                        world.playSound(null, p.getPos(), SoundEvents.BLOCK_METAL_BREAK, SoundCategory.BLOCKS, 0.8f, 0.6f);
                    }
                    if (p.burst()) {
                        broken.add(p);
                    }
                }
            }
            n.syncIfChanged(false);
            // contents change every tick without block updates: make sure the chunk (tile entity) is saved
            world.markChunkDirty(n.getPos(), n);
        }
        for (TileFluidNode n : broken) burst((TilePipe) n, sim);
    }

    private final Set<BlockPos> burstAnnounced = new HashSet<>();

    private void burst(TilePipe p, WaterSim sim) {
        BlockPos pos = p.getPos();
        if (!burstAnnounced.add(pos)) return;
        WaterNet.effect(world, MsgEffect.BURST, pos, p.gaugePressureKPa() / p.ratedPressure());
        if (p.isGlass()) {
            // fragile pipe shatters: its water is released into the world
            float v = p.volume;
            p.volume = 0f;
            world.playSound(null, pos, SoundEvents.BLOCK_GLASS_BREAK, SoundCategory.BLOCKS, 1.2f, 0.9f);
            world.setBlockToAir(pos);
            if (v > 0f) sim.addWater(pos, v);
        } else {
            world.playSound(null, pos, SoundEvents.ENTITY_GENERIC_EXPLODE, SoundCategory.BLOCKS, 0.5f, 1.6f);
        }
        burstAnnounced.remove(pos);
        dirty = true;
    }

    // ---------------------------------------------------------------------------------------------
    /** info for the hydrometer */
    public float linkFlow(TileFluidNode n) {
        float m = 0f;
        for (Link l : nodeLinks.getOrDefault(n, Collections.emptyList())) m = Math.max(m, Math.abs(l.q));
        return Math.max(m, n.throughput);
    }

    public static void blockChangedNear(World w, BlockPos p) {
        if (w.isRemote) return;
        PipeManager m = MANAGERS.get(w.provider.getDimension());
        if (m != null) m.dirty = true;
    }

    public static Iterable<PipeManager> all() {
        return MANAGERS.values();
    }

    public World world() {
        return world;
    }

    public static boolean isServer(World w) {
        return w instanceof WorldServer;
    }
}
