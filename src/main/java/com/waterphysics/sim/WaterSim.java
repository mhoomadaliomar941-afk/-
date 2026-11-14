package com.waterphysics.sim;

import com.waterphysics.WaterPhysics;
import com.waterphysics.block.BlockPhysWater;
import com.waterphysics.config.WPConfig;
import it.unimi.dsi.fastutil.longs.*;
import it.unimi.dsi.fastutil.objects.ObjectArrayList;
import net.minecraft.block.BlockLiquid;
import net.minecraft.block.state.IBlockState;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.init.Blocks;
import net.minecraft.nbt.NBTTagCompound;
import net.minecraft.nbt.NBTTagList;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.ChunkPos;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.WorldServer;
import net.minecraft.world.chunk.Chunk;
import net.minecraft.world.chunk.storage.ExtendedBlockStorage;

import java.util.*;
import java.util.concurrent.ConcurrentLinkedQueue;

/**
 * Physical water simulation of one dimension (server side).
 *
 * <h3>Model</h3>
 * Every block cell holds a water volume v (m^3, 1.0 = full block = 1000 L). Each cell keeps an outflow
 * flux for each of its 6 faces ("virtual pipes" with inertia). Fluxes are accelerated by the difference of
 * hydraulic head H between neighbouring cells:
 * <pre>
 *   H = y + min(v,1) + D + (v-1)^+ * K + H_impact
 *   D = depth of the contiguous water above (hydrostatic pressure, p = rho*g*D)
 *   df/dt = g * A/L * (H_i - H_j) - viscous damping - friction - turbulent drag (Cd, Torricelli)
 * </pre>
 * Enclosed water (columns capped by a solid block) gets its head from an iterative pressure relaxation,
 * so pressure propagates through sealed channels (U-tubes, siphon bottoms, closed tanks). Water hitting a
 * wall or a floor converts its kinetic energy into stagnation head (u^2/2g), which redirects / bounces the
 * flow and produces splashes. Thin films below a threshold do not spread (surface tension approximation).
 * The outflow of a cell per step never exceeds its content, and every transfer is applied to both cells,
 * so mass is conserved exactly (up to float rounding which is tracked).
 *
 * <h3>Performance</h3>
 * Only "active" 16x16x16 sections are simulated. A section sleeps once its water has settled and is woken
 * by block changes, buckets, pipes or by water flowing in. Only sections near players are simulated, with a
 * hard cap per tick, and block (visual) updates are throttled.
 */
public final class WaterSim {
    public static final int[] DX = {0, 0, 0, 0, -1, 1};
    public static final int[] DY = {-1, 1, 0, 0, 0, 0};
    public static final int[] DZ = {0, 0, -1, 1, 0, 0};
    public static final int[] OPP = {1, 0, 3, 2, 5, 4};
    public static final int DOWN = 0, UP = 1;
    public static final float FULL = 0.999f;
    public static final float RHO_G = 9.81f; // kPa per meter of water head (rho * g / 1000)

    public static final byte FX_IMPACT = 1, FX_FALLING = 2, FX_VORTEX = 4, FX_FAST = 8, FX_JET = 16,
            FX_SURFACE = 32, FX_CONFTOP = 64;

    public final WorldServer world;
    public WaterStats stats;
    private final Long2ObjectOpenHashMap<WaterChunk> chunks = new Long2ObjectOpenHashMap<>();
    private final LongLinkedOpenHashSet active = new LongLinkedOpenHashSet();
    private final LongLinkedOpenHashSet visualQueue = new LongLinkedOpenHashSet();
    private final ConcurrentLinkedQueue<Object[]> pendingLoads = new ConcurrentLinkedQueue<>();
    private final ArrayDeque<BlockPos> conversions = new ArrayDeque<>();
    /** true while the simulation itself modifies the world (suppresses feedback from block callbacks) */
    public boolean internalUpdate;

    // per tick state
    private Sec[] sim = new Sec[0];
    private int simCount;
    private final LongOpenHashSet simKeys = new LongOpenHashSet();
    private long mcChunkKey = Long.MIN_VALUE;
    private Chunk mcChunk;
    private WaterChunk lastWc;
    private long lastWcKey = Long.MIN_VALUE;
    // deferred reservoir exchanges of the current sub-step (applied after the outflows)
    private Sec[] exSec = new Sec[64];
    private int[] exIdx = new int[64];
    private float[] exAmt = new float[64];
    private int exCount;
    public long lastTickNanos;
    public int lastSimulated, lastBlockUpdates;
    /** per-section sync list for the network layer: sections changed this tick */
    public final LongOpenHashSet changedThisTick = new LongOpenHashSet();

    public WaterSim(WorldServer world) {
        this.world = world;
        WaterStats s = (WaterStats) world.getPerWorldStorage().getOrLoadData(WaterStats.class, WaterStats.NAME);
        if (s == null) {
            s = new WaterStats();
            world.getPerWorldStorage().setData(WaterStats.NAME, s);
        }
        this.stats = s;
    }

    // ------------------------------------------------------------------------------------------------
    // keys & access
    // ------------------------------------------------------------------------------------------------
    public static long secKey(int cx, int sy, int cz) {
        return ((long) (cx & 0x3FFFFF) << 26) | ((long) (cz & 0x3FFFFF) << 4) | (sy & 15);
    }

    public static int keyX(long k) { return ((int) (k >>> 26)) << 10 >> 10; }
    public static int keyZ(long k) { return ((int) ((k >>> 4) & 0x3FFFFF)) << 10 >> 10; }
    public static int keyY(long k) { return (int) (k & 15); }

    public WaterChunk wc(int cx, int cz) {
        long k = ChunkPos.asLong(cx, cz);
        if (k == lastWcKey && lastWc != null) return lastWc;
        WaterChunk w = chunks.get(k);
        if (w != null) {
            lastWc = w;
            lastWcKey = k;
        }
        return w;
    }

    private WaterChunk wcCreate(int cx, int cz) {
        WaterChunk w = wc(cx, cz);
        if (w == null) {
            w = new WaterChunk(cx, cz);
            w.chunk = mc(cx, cz);
            chunks.put(ChunkPos.asLong(cx, cz), w);
            lastWc = w;
            lastWcKey = ChunkPos.asLong(cx, cz);
        }
        return w;
    }

    public Chunk mc(int cx, int cz) {
        long k = ChunkPos.asLong(cx, cz);
        if (k == mcChunkKey && mcChunk != null && mcChunk.isLoaded()) return mcChunk;
        Chunk c = world.getChunkProvider().getLoadedChunk(cx, cz);
        mcChunk = c;
        mcChunkKey = k;
        return c;
    }

    public Sec sec(int x, int y, int z) {
        if (y < 0 || y > 255) return null;
        WaterChunk w = wc(x >> 4, z >> 4);
        return w == null ? null : w.secs[y >> 4];
    }

    public Sec secByKey(long key) {
        WaterChunk w = wc(keyX(key), keyZ(key));
        return w == null ? null : w.secs[keyY(key)];
    }

    public float vol(int x, int y, int z) {
        Sec s = sec(x, y, z);
        return s == null ? 0f : s.vol[Sec.idx(x, y, z)];
    }

    public float getVolume(BlockPos p) {
        return vol(p.getX(), p.getY(), p.getZ());
    }

    public IBlockState stateAt(int x, int y, int z) {
        if (y < 0 || y > 255) return Blocks.BEDROCK.getDefaultState();
        Chunk c = mc(x >> 4, z >> 4);
        if (c == null) return null;
        ExtendedBlockStorage e = c.getBlockStorageArray()[y >> 4];
        return e == Chunk.NULL_BLOCK_STORAGE ? Blocks.AIR.getDefaultState() : e.get(x & 15, y & 15, z & 15);
    }

    public byte type(int x, int y, int z) {
        if (y < 0 || y > 255) return CellType.SOLID;
        Sec s = sec(x, y, z);
        if (s != null && s.type != null && s.simulatedThisTick) return s.type[Sec.idx(x, y, z)];
        IBlockState st = stateAt(x, y, z);
        return st == null ? CellType.UNLOADED : CellType.of(st);
    }

    public boolean isLoaded(int x, int z) {
        return mc(x >> 4, z >> 4) != null;
    }

    // ------------------------------------------------------------------------------------------------
    // activation
    // ------------------------------------------------------------------------------------------------
    public void activate(Sec s) {
        if (s == null) return;
        s.quiet = 0;
        if (!s.active) {
            s.active = true;
            active.add(s.key);
        }
    }

    public void wake(BlockPos p, int radius) {
        int x0 = (p.getX() - radius) >> 4, x1 = (p.getX() + radius) >> 4;
        int y0 = Math.max(0, p.getY() - radius) >> 4, y1 = Math.min(255, p.getY() + radius) >> 4;
        int z0 = (p.getZ() - radius) >> 4, z1 = (p.getZ() + radius) >> 4;
        for (int cx = x0; cx <= x1; cx++)
            for (int cz = z0; cz <= z1; cz++) {
                WaterChunk w = wc(cx, cz);
                if (w == null) continue;
                for (int sy = y0; sy <= y1; sy++) {
                    Sec s = w.secs[sy];
                    if (s != null && s.waterCells > 0) {
                        activate(s);
                        s.typeAge = 0;
                    }
                }
            }
    }

    /** A block changed at p (wall broken, hole closed, ...): refresh caches and wake nearby water. */
    public void blockChanged(BlockPos p) {
        if (internalUpdate) return;
        int x = p.getX(), y = p.getY(), z = p.getZ();
        boolean water = vol(x, y, z) > 0;
        for (int d = 0; d < 6 && !water; d++) water = vol(x + DX[d], y + DY[d], z + DZ[d]) > 0;
        if (!water) return;
        for (int d = -1; d < 6; d++) {
            int nx = d < 0 ? x : x + DX[d], ny = d < 0 ? y : y + DY[d], nz = d < 0 ? z : z + DZ[d];
            Sec s = sec(nx, ny, nz);
            if (s != null) {
                s.typeAge = 0;
                if (s.waterCells > 0) activate(s);
            }
        }
        // a cell that became solid while holding water: push the water out
        Sec s = sec(x, y, z);
        if (s != null) {
            int i = Sec.idx(x, y, z);
            if (s.vol[i] > 0) {
                IBlockState st = stateAt(x, y, z);
                if (st != null && CellType.of(st) != CellType.OPEN) displace(p, s);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------
    // public mutation API (buckets, pipes, commands)
    // ------------------------------------------------------------------------------------------------
    /** Adds water to a cell (may over-fill; pressure pushes the excess out). Returns the amount added. */
    public float addWater(BlockPos p, float amount) {
        int x = p.getX(), y = p.getY(), z = p.getZ();
        if (amount <= 0 || y < 0 || y > 255 || !isLoaded(x, z)) return 0f;
        IBlockState st = stateAt(x, y, z);
        if (st == null) return 0f;
        byte t = CellType.of(st);
        if (t == CellType.RESERVOIR) {
            convertVanilla(p, st);
            t = CellType.OPEN;
        }
        if (t != CellType.OPEN) return 0f;
        Sec s = wcCreate(x >> 4, z >> 4).getOrCreate(y >> 4);
        int i = Sec.idx(x, y, z);
        if (s.vol[i] <= 0) s.waterCells++;
        s.vol[i] += amount;
        activate(s);
        s.visualDirty = true;
        visualQueue.add(s.key);
        ensureBlock(p, s.vol[i]);
        return amount;
    }

    /**
     * Pours water at p like a bucket does: it fills the column from p upward (water lands on the
     * existing surface) instead of over-pressurising one block. Returns the amount placed.
     */
    public float pour(BlockPos p, float amount) {
        int x = p.getX(), z = p.getZ();
        float remaining = amount;
        int y = p.getY();
        // climb to the first block of the column that is not full (the water surface)
        while (y < 255) {
            IBlockState st = stateAt(x, y, z);
            if (st == null) return 0f;
            byte t = CellType.of(st);
            if (t != CellType.OPEN && t != CellType.RESERVOIR) break;
            if (vol(x, y, z) < FULL) break;
            IBlockState up = stateAt(x, y + 1, z);
            if (up == null || CellType.of(up) != CellType.OPEN) break;
            y++;
        }
        BlockPos start = new BlockPos(x, y, z);
        IBlockState s0 = stateAt(x, y, z);
        if (s0 == null) return 0f;
        byte t0 = CellType.of(s0);
        if (t0 != CellType.OPEN && t0 != CellType.RESERVOIR) return 0f;
        // poured water spreads over the surface layer (only over supported blocks), layer by layer
        for (int layer = 0; layer < 64 && remaining > 1e-6f; layer++) {
            int ly = y + layer;
            if (ly > 255) break;
            ArrayDeque<BlockPos> q = new ArrayDeque<>();
            LongOpenHashSet seen = new LongOpenHashSet();
            BlockPos c0 = new BlockPos(x, ly, z);
            IBlockState cs = stateAt(x, ly, z);
            if (cs == null || (CellType.of(cs) != CellType.OPEN && CellType.of(cs) != CellType.RESERVOIR)) break;
            q.add(c0);
            seen.add(c0.toLong());
            int visited = 0;
            while (!q.isEmpty() && remaining > 1e-6f && visited < 96) {
                BlockPos c = q.poll();
                visited++;
                float room = Math.max(0f, 1f - getVolume(c));
                if (room > 0f) remaining -= addWater(c, Math.min(room, remaining));
                for (int d = 2; d < 6; d++) {
                    BlockPos n = c.add(DX[d], 0, DZ[d]);
                    if (!seen.add(n.toLong())) continue;
                    IBlockState ns = stateAt(n.getX(), n.getY(), n.getZ());
                    if (ns == null || CellType.of(ns) != CellType.OPEN) continue;
                    // supported: water poured here would stay at this level
                    IBlockState below = stateAt(n.getX(), n.getY() - 1, n.getZ());
                    boolean supported = below == null || CellType.of(below) != CellType.OPEN || getVolume(n.down()) >= FULL;
                    if (supported) q.add(n);
                }
            }
            IBlockState up = stateAt(x, ly + 1, z);
            if (up == null || CellType.of(up) != CellType.OPEN) break;
        }
        if (remaining > 1e-6f) remaining -= addWater(start, remaining);
        return amount - Math.max(0f, remaining);
    }

    /** Puts water back into the world near p (e.g. contents of a removed pipe). Returns what could not be placed. */
    public float release(BlockPos p, float amount) {
        if (amount <= 0f) return 0f;
        IBlockState st = stateAt(p.getX(), p.getY(), p.getZ());
        if (st != null && CellType.of(st) == CellType.OPEN) return amount - pour(p, amount);
        int[] order = {UP, 2, 3, 4, 5, DOWN};
        for (int d : order) {
            BlockPos n = p.add(DX[d], DY[d], DZ[d]);
            IBlockState ns = stateAt(n.getX(), n.getY(), n.getZ());
            if (ns != null && CellType.of(ns) == CellType.OPEN) return amount - pour(n, amount);
        }
        return amount;
    }

    /** Removes up to maxAmount from a single cell. */
    public float takeWater(BlockPos p, float maxAmount) {
        Sec s = sec(p.getX(), p.getY(), p.getZ());
        if (s == null) return 0f;
        int i = Sec.idx(p.getX(), p.getY(), p.getZ());
        float v = s.vol[i];
        if (v <= 0) return 0f;
        float t = Math.min(v, maxAmount);
        s.vol[i] = v - t;
        if (s.vol[i] <= 1e-7f) {
            stats.rounding += s.vol[i];
            s.vol[i] = 0f;
            s.waterCells--;
        }
        activate(s);
        s.visualDirty = true;
        visualQueue.add(s.key);
        return t;
    }

    /**
     * Collects exactly {@code amount} m^3 from the water body connected to {@code p} (top layer first).
     * Nothing is removed if the body holds less. Used by buckets.
     */
    public boolean drainExact(BlockPos p, float amount, int searchLimit) {
        List<long[]> cells = new ArrayList<>();
        LongOpenHashSet seen = new LongOpenHashSet();
        ArrayDeque<BlockPos> q = new ArrayDeque<>();
        q.add(p);
        seen.add(p.toLong());
        double total = 0;
        while (!q.isEmpty() && cells.size() < searchLimit) {
            BlockPos c = q.poll();
            float v = getVolume(c);
            if (v <= 0) continue;
            cells.add(new long[]{c.toLong(), Float.floatToIntBits(c.getY() + Math.min(v, 1f))});
            total += v;
            for (int d = 0; d < 6; d++) {
                BlockPos n = c.add(DX[d], DY[d], DZ[d]);
                if (seen.add(n.toLong()) && getVolume(n) > 0) q.add(n);
            }
        }
        if (total + 1e-4 < amount) return false;
        cells.sort((a, b) -> Float.compare(Float.intBitsToFloat((int) b[1]), Float.intBitsToFloat((int) a[1])));
        float need = amount;
        for (long[] c : cells) {
            if (need <= 0) break;
            need -= takeWater(BlockPos.fromLong(c[0]), need);
        }
        if (need > 0) stats.rounding -= need; // within tolerance
        return true;
    }

    public void ensureBlock(BlockPos p, float v) {
        IBlockState cur = stateAt(p.getX(), p.getY(), p.getZ());
        if (cur == null || cur.getBlock() == WaterPhysics.WATER_BLOCK) return;
        if (CellType.of(cur) != CellType.OPEN) return;
        boolean prev = internalUpdate;
        internalUpdate = true;
        try {
            if (CellType.needsWashAway(cur)) cur.getBlock().dropBlockAsItem(world, p, cur, 0);
            world.setBlockState(p, WaterPhysics.WATER_BLOCK.getDefaultState().withProperty(BlockPhysWater.LEVEL, level(v)), 3);
        } finally {
            internalUpdate = prev;
        }
    }

    /** Called when one of our water blocks was replaced by something else (block placed into water etc.). */
    public void onWaterBlockReplaced(BlockPos p, IBlockState newState) {
        if (internalUpdate) return;
        Sec s = sec(p.getX(), p.getY(), p.getZ());
        if (s == null) return;
        int i = Sec.idx(p.getX(), p.getY(), p.getZ());
        float v = s.vol[i];
        if (v <= 0) return;
        if (newState.getMaterial() == net.minecraft.block.material.Material.AIR
                || newState.getMaterial() == net.minecraft.block.material.Material.WATER) {
            // removed (sponge, command, another fluid)
            s.vol[i] = 0f;
            s.waterCells--;
            stats.destroyedOut += v;
            stats.markDirty();
            wake(p, 1);
            return;
        }
        displace(p, s);
    }

    /** Our water block appeared without data (placed by a command / another mod): give it its volume. */
    public void onWaterBlockAdded(BlockPos p, IBlockState state) {
        if (internalUpdate) return;
        Sec s = wcCreate(p.getX() >> 4, p.getZ() >> 4).getOrCreate(p.getY() >> 4);
        int i = Sec.idx(p.getX(), p.getY(), p.getZ());
        if (s.vol[i] <= 0) {
            float v = (state.getValue(BlockPhysWater.LEVEL) + 1) / 16f;
            s.vol[i] = v;
            s.waterCells++;
            stats.commandIn += v;
            stats.markDirty();
        }
        activate(s);
    }

    /** Pushes the water of a cell that became solid into the neighbouring open cells. */
    private void displace(BlockPos p, Sec s) {
        int x = p.getX(), y = p.getY(), z = p.getZ();
        int i = Sec.idx(x, y, z);
        float v = s.vol[i];
        s.vol[i] = 0f;
        s.waterCells = Math.max(0, s.waterCells - 1);
        int[] order = {UP, 2, 3, 4, 5, DOWN};
        List<BlockPos> targets = new ArrayList<>();
        for (int d : order) {
            int nx = x + DX[d], ny = y + DY[d], nz = z + DZ[d];
            if (ny < 0 || ny > 255) continue;
            IBlockState st = stateAt(nx, ny, nz);
            if (st != null && CellType.of(st) == CellType.OPEN) targets.add(new BlockPos(nx, ny, nz));
            if (d == UP && !targets.isEmpty()) break;
        }
        if (targets.isEmpty()) {
            stats.displacedOut += v;
            stats.markDirty();
            return;
        }
        float each = v / targets.size();
        for (BlockPos t : targets) {
            Sec ts = wcCreate(t.getX() >> 4, t.getZ() >> 4).getOrCreate(t.getY() >> 4);
            int ti = Sec.idx(t.getX(), t.getY(), t.getZ());
            if (ts.vol[ti] <= 0) ts.waterCells++;
            ts.vol[ti] += each;
            activate(ts);
            ts.visualDirty = true;
            visualQueue.add(ts.key);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // vanilla water
    // ------------------------------------------------------------------------------------------------
    public void queueConversion(BlockPos p) {
        if (conversions.size() < 4096) conversions.add(p);
    }

    /** Converts a vanilla water block into physical water with the equivalent volume. */
    public void convertVanilla(BlockPos p, IBlockState st) {
        if (!(st.getBlock() == Blocks.WATER || st.getBlock() == Blocks.FLOWING_WATER)) return;
        int lvl = st.getValue(BlockLiquid.LEVEL);
        float v = (lvl == 0 || lvl >= 8) ? 1.0f : (8 - lvl) / 9f;
        int x = p.getX(), y = p.getY(), z = p.getZ();
        Sec s = wcCreate(x >> 4, z >> 4).getOrCreate(y >> 4);
        int i = Sec.idx(x, y, z);
        boolean prev = internalUpdate;
        internalUpdate = true;
        try {
            world.setBlockState(p, WaterPhysics.WATER_BLOCK.getDefaultState().withProperty(BlockPhysWater.LEVEL, level(v)), 2 | 16);
        } finally {
            internalUpdate = prev;
        }
        if (s.vol[i] <= 0) s.waterCells++;
        s.vol[i] += v;
        stats.convertedIn += v;
        stats.markDirty();
        s.typeAge = 0;
        activate(s);
        // neighbours' type caches changed (reservoir -> open)
        for (int d = 0; d < 6; d++) {
            Sec n = sec(x + DX[d], y + DY[d], z + DZ[d]);
            if (n != null) n.typeAge = 0;
        }
    }

    private void processConversions() {
        int n = 0;
        while (!conversions.isEmpty() && n++ < 512) {
            BlockPos p = conversions.poll();
            if (!isLoaded(p.getX(), p.getZ())) continue;
            IBlockState st = stateAt(p.getX(), p.getY(), p.getZ());
            if (st != null) convertVanilla(p, st);
        }
    }

    /** Head of a vanilla water reservoir cell (its surface level). */
    private float reservoirHead(int x, int y, int z) {
        int top = y;
        for (int k = 0; k < 64; k++) {
            IBlockState st = stateAt(x, top + 1, z);
            if (st == null || CellType.of(st) != CellType.RESERVOIR) break;
            top++;
        }
        IBlockState st = stateAt(x, top, z);
        float h = 8f / 9f;
        if (st != null && st.getBlock() instanceof BlockLiquid) {
            int lvl = st.getValue(BlockLiquid.LEVEL);
            if (lvl > 0 && lvl < 8) h = (8 - lvl) / 9f;
        }
        return top + h;
    }

    // ------------------------------------------------------------------------------------------------
    // tick
    // ------------------------------------------------------------------------------------------------
    public void tick() {
        long t0 = System.nanoTime();
        mcChunk = null;
        mcChunkKey = Long.MIN_VALUE;
        changedThisTick.clear();
        drainPendingLoads();
        processConversions();
        lastSimulated = 0;
        if (WPConfig.simulation.enabled) {
            selectSections();
            if (simCount > 0) {
                for (int i = 0; i < simCount; i++) prepare(sim[i]);
                int sub = Math.max(1, WPConfig.simulation.substepsPerTick);
                float dt = (float) (0.05 * WPConfig.simulation.timeScale / sub);
                for (int k = 0; k < sub; k++) {
                    computeHeads();
                    relaxPressure();
                    computeOutflows(dt);
                    applyOutflows(dt);
                    applyExchanges();
                }
                postProcess();
                lastSimulated = simCount;
                for (int i = 0; i < simCount; i++) sim[i].simulatedThisTick = false;
            }
        }
        applyVisuals();
        lastTickNanos = System.nanoTime() - t0;
    }

    private void selectSections() {
        simCount = 0;
        simKeys.clear();
        List<long[]> players = new ArrayList<>();
        for (EntityPlayer p : world.playerEntities) players.add(new long[]{((int) Math.floor(p.posX)) >> 4, ((int) Math.floor(p.posZ)) >> 4});
        int radius = WPConfig.performance.activeChunkRadius;
        ObjectArrayList<Sec> list = new ObjectArrayList<>();
        LongIterator it = active.iterator();
        while (it.hasNext()) {
            long k = it.nextLong();
            Sec s = secByKey(k);
            if (s == null || !s.active) {
                it.remove();
                continue;
            }
            if (radius > 0) {
                int best = Integer.MAX_VALUE;
                for (long[] p : players) best = (int) Math.min(best, Math.max(Math.abs(p[0] - s.owner.cx), Math.abs(p[1] - s.owner.cz)));
                if (best > radius) continue; // frozen until a player comes near
                s.quiet = Math.min(s.quiet, Integer.MAX_VALUE - 1);
            }
            if (s.owner.chunk == null || !s.owner.chunk.isLoaded()) {
                s.owner.chunk = mc(s.owner.cx, s.owner.cz);
                if (s.owner.chunk == null) continue;
            }
            list.add(s);
        }
        int max = WPConfig.performance.maxActiveSections;
        if (list.size() > max && !players.isEmpty()) {
            list.sort(Comparator.comparingInt(s -> {
                int best = Integer.MAX_VALUE;
                for (long[] p : players) best = (int) Math.min(best, Math.abs(p[0] - s.owner.cx) + Math.abs(p[1] - s.owner.cz));
                return best;
            }));
        }
        int n = Math.min(list.size(), max);
        if (sim.length < n) sim = new Sec[Math.max(n, sim.length * 2)];
        for (int i = 0; i < n; i++) sim[i] = list.get(i);
        // top-down order so hydrostatic depth can be carried across stacked sections
        Arrays.sort(sim, 0, n, (a, b) -> Integer.compare(b.sy, a.sy));
        simCount = n;
        for (int i = 0; i < n; i++) {
            sim[i].simulatedThisTick = true;
            simKeys.add(sim[i].key);
        }
    }

    private void prepare(Sec s) {
        s.ensureSimArrays();
        if (s.typeAge <= 0) refreshTypes(s);
        else s.typeAge--;
        System.arraycopy(s.vol, 0, s.prev, 0, Sec.SIZE);
        Arrays.fill(s.fx, (byte) 0);
    }

    private void refreshTypes(Sec s) {
        Chunk c = s.owner.chunk;
        ExtendedBlockStorage e = c == null ? null : c.getBlockStorageArray()[s.sy];
        for (int i = 0; i < Sec.SIZE; i++) {
            if (e == null || e == Chunk.NULL_BLOCK_STORAGE) {
                s.type[i] = CellType.OPEN;
                continue;
            }
            IBlockState st = e.get(i & 15, i >> 8, (i >> 4) & 15);
            byte t = CellType.of(st);
            s.type[i] = t;
            if (st.getBlock() == WaterPhysics.WATER_BLOCK && s.vol[i] <= 0 && !s.owner.fromSave) {
                // block without any saved data (world edited externally): adopt it.
                // (in saved chunks a water block with 0 volume is only a not yet updated visual)
                float v = (st.getValue(BlockPhysWater.LEVEL) + 1) / 16f;
                s.vol[i] = v;
                s.waterCells++;
                stats.commandIn += v;
            } else if (t != CellType.OPEN && s.vol[i] > 0) {
                displace(new BlockPos(s.worldX(i), s.worldY(i), s.worldZ(i)), s);
            }
        }
        // does this section touch vanilla water (inside or across its faces)?
        boolean res = false;
        for (int i = 0; i < Sec.SIZE && !res; i++) res = s.type[i] == CellType.RESERVOIR;
        int wx0 = s.owner.cx << 4, wy0 = s.sy << 4, wz0 = s.owner.cz << 4;
        for (int a = 0; a < 16 && !res; a++)
            for (int b = 0; b < 16 && !res; b++) {
                res = type(wx0 - 1, wy0 + a, wz0 + b) == CellType.RESERVOIR || type(wx0 + 16, wy0 + a, wz0 + b) == CellType.RESERVOIR
                        || type(wx0 + a, wy0 + b, wz0 - 1) == CellType.RESERVOIR || type(wx0 + a, wy0 + b, wz0 + 16) == CellType.RESERVOIR
                        || type(wx0 + a, wy0 - 1, wz0 + b) == CellType.RESERVOIR || type(wx0 + a, wy0 + 16, wz0 + b) == CellType.RESERVOIR;
            }
        s.nearReservoir = res;
        s.typeAge = 10;
    }

    /** cell type straight from the world (no cache) */
    private byte realType(int x, int y, int z) {
        IBlockState st = stateAt(x, y, z);
        return st == null ? CellType.UNLOADED : CellType.of(st);
    }

    private Sec simSec(int x, int y, int z) {
        Sec s = sec(x, y, z);
        return (s != null && s.simulatedThisTick) ? s : null;
    }

    /** base head of an arbitrary cell (slow path for cells outside the simulated set) */
    private float baseHeadSlow(int x, int y, int z) {
        Sec s = simSec(x, y, z);
        if (s != null) return s.baseHead[Sec.idx(x, y, z)];
        float v = vol(x, y, z);
        return y + Math.min(v, 1f) + carrySlow(x, y + 1, z);
    }

    private float headSlow(int x, int y, int z) {
        Sec s = simSec(x, y, z);
        if (s != null) return s.head[Sec.idx(x, y, z)];
        float v = vol(x, y, z);
        float h = y + Math.min(v, 1f) + carrySlow(x, y + 1, z);
        if (v > 1f) h += (v - 1f) * (float) WPConfig.physics.compressionStiffness;
        return h;
    }

    /** hydrostatic depth carried down into the cell below (x, y-1, z), computed from cell (x,y,z) upward */
    private float carrySlow(int x, int y, int z) {
        float d = 0f;
        for (int k = 0; k < 64 && y + k <= 255; k++) {
            Sec s = simSec(x, y + k, z);
            if (s != null) {
                int i = Sec.idx(x, y + k, z);
                if (s.type[i] != CellType.OPEN || s.vol[i] <= 0) return d;
                return d + (s.baseHead[i] - (y + k));
            }
            float v = vol(x, y + k, z);
            if (v <= 0) return d;
            d += Math.min(v, 1f);
        }
        return d;
    }

    private void computeHeads() {
        final float kc = (float) WPConfig.physics.compressionStiffness;
        final float g2 = (float) (2.0 * WPConfig.physics.gravity);
        final float impact = (float) WPConfig.physics.impactPressure;
        for (int n = 0; n < simCount; n++) {
            Sec s = sim[n];
            float[] vol = s.vol, head = s.head, base = s.baseHead, depth = s.depth, conf = s.confined;
            byte[] type = s.type, fx = s.fx;
            int baseY = s.sy << 4;
            int wx0 = s.owner.cx << 4, wz0 = s.owner.cz << 4;
            for (int z = 0; z < 16; z++) {
                for (int x = 0; x < 16; x++) {
                    int wx = wx0 + x, wz = wz0 + z;
                    // state above the section top
                    float carry;
                    boolean aboveBlocked;
                    int ya = baseY + 16;
                    if (ya > 255) {
                        carry = 0f;
                        aboveBlocked = false;
                    } else {
                        Sec a = simSec(wx, ya, wz);
                        if (a != null) {
                            int ia = Sec.idx(x, 0, z);
                            byte ta = a.type[ia];
                            aboveBlocked = ta != CellType.OPEN && ta != CellType.RESERVOIR;
                            carry = (ta == CellType.OPEN && a.vol[ia] > 0) ? a.baseHead[ia] - ya : 0f;
                        } else {
                            byte ta = type(wx, ya, wz);
                            aboveBlocked = ta != CellType.OPEN && ta != CellType.RESERVOIR;
                            carry = ta == CellType.OPEN ? carrySlow(wx, ya, wz) : 0f;
                        }
                    }
                    for (int y = 15; y >= 0; y--) {
                        int i = (y << 8) | (z << 4) | x;
                        byte t = type[i];
                        if (t != CellType.OPEN) {
                            carry = 0f;
                            aboveBlocked = t != CellType.RESERVOIR;
                            head[i] = base[i] = baseY + y;
                            continue;
                        }
                        float v = vol[i];
                        int wy = baseY + y;
                        float hb = wy + Math.min(v, 1f) + carry;
                        if (v >= FULL && aboveBlocked) {
                            // top of an enclosed water column: pressure comes from the relaxation
                            float c = conf[i];
                            if (c > hb) hb = c;
                            else conf[i] = hb;
                            fx[i] |= FX_CONFTOP;
                        } else {
                            fx[i] &= ~FX_CONFTOP;
                            conf[i] = 0f;
                        }
                        base[i] = hb;
                        depth[i] = carry;
                        float h = hb;
                        if (v > 1f) h += (v - 1f) * kc;
                        if (v > 0f && impact > 0f) {
                            float dyn = impactHead(s, i, wx, wy, wz, g2) * impact;
                            if (dyn > 0.02f) {
                                fx[i] |= FX_IMPACT;
                                h += Math.min(dyn, 4f);
                            }
                        }
                        head[i] = h;
                        carry = v > 0f ? hb - wy : 0f;
                        aboveBlocked = false;
                    }
                }
            }
        }
    }

    /** stagnation head of water flowing into a blocked face: u^2 / 2g */
    private float impactHead(Sec s, int i, int wx, int wy, int wz, float g2) {
        float sum = 0f;
        for (int d = 0; d < 6; d++) {
            if (d == UP) continue;
            int nx = wx + DX[d], ny = wy + DY[d], nz = wz + DZ[d];
            boolean blocked;
            byte tn = cellTypeFast(s, i, d, nx, ny, nz);
            if (tn != CellType.OPEN) blocked = tn != CellType.RESERVOIR;
            else blocked = d == DOWN && volFast(s, i, d, nx, ny, nz) >= FULL;
            if (!blocked) continue;
            // inflow moving in direction d comes from the cell on the opposite side
            int bx = wx - DX[d], by = wy - DY[d], bz = wz - DZ[d];
            float u = outFast(s, i, OPP[d], bx, by, bz, d);
            if (u > 0f) sum += u * u / g2;
        }
        return sum;
    }

    private byte cellTypeFast(Sec s, int i, int d, int nx, int ny, int nz) {
        int j = inner(i, d);
        if (j >= 0) return s.type[j];
        return type(nx, ny, nz);
    }

    private float volFast(Sec s, int i, int d, int nx, int ny, int nz) {
        int j = inner(i, d);
        if (j >= 0) return s.vol[j];
        return vol(nx, ny, nz);
    }

    /** outflow in direction dir of the neighbour of cell i in direction nd */
    private float outFast(Sec s, int i, int nd, int bx, int by, int bz, int dir) {
        int j = inner(i, nd);
        if (j >= 0) return s.out[j * 6 + dir];
        Sec o = simSec(bx, by, bz);
        if (o == null) return 0f;
        return o.out[Sec.idx(bx, by, bz) * 6 + dir];
    }

    /** index of the neighbour inside the same section, or -1 */
    public static int inner(int i, int d) {
        switch (d) {
            case 0: return (i >> 8) > 0 ? i - 256 : -1;
            case 1: return (i >> 8) < 15 ? i + 256 : -1;
            case 2: return ((i >> 4) & 15) > 0 ? i - 16 : -1;
            case 3: return ((i >> 4) & 15) < 15 ? i + 16 : -1;
            case 4: return (i & 15) > 0 ? i - 1 : -1;
            default: return (i & 15) < 15 ? i + 1 : -1;
        }
    }

    private void relaxPressure() {
        int iters = Math.max(1, WPConfig.simulation.pressureIterations);
        for (int it = 0; it < iters; it++) {
            for (int n = 0; n < simCount; n++) {
                Sec s = sim[n];
                byte[] fx = s.fx;
                int baseY = s.sy << 4;
                int wx0 = s.owner.cx << 4, wz0 = s.owner.cz << 4;
                for (int i = 0; i < Sec.SIZE; i++) {
                    if ((fx[i] & FX_CONFTOP) == 0) continue;
                    int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
                    int wx = wx0 + x, wy = baseY + y, wz = wz0 + z;
                    float sum = 0f;
                    int cnt = 0;
                    for (int d = 2; d < 6; d++) {
                        int nx = wx + DX[d], nz = wz + DZ[d];
                        int j = inner(i, d);
                        float nv;
                        byte tn;
                        float nh;
                        if (j >= 0) {
                            tn = s.type[j];
                            nv = s.vol[j];
                            nh = s.baseHead[j];
                        } else {
                            tn = type(nx, wy, nz);
                            nv = vol(nx, wy, nz);
                            nh = tn == CellType.OPEN ? baseHeadSlow(nx, wy, nz) : 0f;
                        }
                        if (tn == CellType.OPEN && nv > 0f) {
                            sum += nh;
                            cnt++;
                        } else if (tn == CellType.RESERVOIR) {
                            sum += reservoirHead(nx, wy, nz);
                            cnt++;
                        }
                    }
                    float target = cnt > 0 ? Math.max(wy + 1f, sum / cnt) : wy + 1f;
                    float c = s.confined[i];
                    if (c < wy + 1f) c = wy + 1f;
                    c = 0.5f * c + 0.5f * target;
                    s.confined[i] = c;
                    s.baseHead[i] = c;
                }
            }
        }
    }

    private void computeOutflows(float dt) {
        final float g = (float) WPConfig.physics.gravity;
        final float damp = (float) Math.exp(-WPConfig.physics.viscosity * dt);
        final float cd = (float) WPConfig.physics.dischargeCoefficient;
        final float cq = 1f / (2f * cd * cd);
        final float fr = (float) WPConfig.physics.surfaceFriction;
        final float minSpread = (float) (WPConfig.physics.minSpreadLiters / 1000.0);
        final boolean infinite = WPConfig.gameplay.naturalWaterIsInfinite;
        final float adv = (float) (WPConfig.physics.momentum * 20.0);
        for (int n = 0; n < simCount; n++) {
            Sec s = sim[n];
            float[] vol = s.vol, head = s.head, out = s.out, depth = s.depth;
            byte[] type = s.type;
            int baseY = s.sy << 4;
            int wx0 = s.owner.cx << 4, wz0 = s.owner.cz << 4;
            for (int i = 0; i < Sec.SIZE; i++) {
                if (type[i] != CellType.OPEN) continue;
                float v = vol[i];
                int o = i * 6;
                int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
                int wx = wx0 + x, wy = baseY + y, wz = wz0 + z;
                if (v <= 0f) {
                    // dry cell: only check for reservoirs that can fill it
                    out[o] = out[o + 1] = out[o + 2] = out[o + 3] = out[o + 4] = out[o + 5] = 0f;
                    if (!s.nearReservoir) continue;
                    for (int d = 0; d < 6; d++) {
                        int j = inner(i, d);
                        byte tn = j >= 0 ? type[j] : type(wx + DX[d], wy + DY[d], wz + DZ[d]);
                        if (tn == CellType.RESERVOIR) reservoirExchange(s, i, wx, wy, wz, wx + DX[d], wy + DY[d], wz + DZ[d], dt, cd, g, infinite);
                    }
                    continue;
                }
                float h = head[i];
                float sum = 0f;
                for (int d = 0; d < 6; d++) {
                    int j = inner(i, d);
                    int nx = wx + DX[d], ny = wy + DY[d], nz = wz + DZ[d];
                    byte tn;
                    float hn, vn;
                    if (j >= 0) {
                        tn = type[j];
                        hn = head[j];
                        vn = vol[j];
                    } else {
                        tn = (ny < 0 || ny > 255) ? CellType.SOLID : type(nx, ny, nz);
                        if (tn == CellType.OPEN) {
                            hn = headSlow(nx, ny, nz);
                            vn = vol(nx, ny, nz);
                        } else {
                            hn = 0f;
                            vn = 0f;
                        }
                    }
                    if (tn != CellType.OPEN) {
                        out[o + d] = 0f;
                        if (tn == CellType.RESERVOIR) reservoirExchange(s, i, wx, wy, wz, nx, ny, nz, dt, cd, g, infinite);
                        continue;
                    }
                    // wetted cross-section of the face: a shallow layer only flows through its depth
                    float area = 1f;
                    if (d >= 2 && depth[i] <= 0f) area = Math.max(0.02f, Math.min(1f, Math.max(v, vn)));
                    float f = out[o + d] + dt * g * area * (h - hn);
                    if (d >= 2) {
                        // momentum carry-over: water arriving from behind keeps moving in its direction (jets, waves)
                        int jb = inner(i, OPP[d]);
                        float in = jb >= 0 ? out[jb * 6 + d] : 0f;
                        if (in > f) f += (in - f) * Math.min(1f, adv * dt);
                        if (vn <= 0f && v < minSpread && depth[i] <= 0f) {
                            f = 0f; // surface tension: thin film does not wet dry ground
                        } else {
                            f /= (1f + fr * dt / Math.max(Math.min(v, 1f), 0.02f));
                        }
                    }
                    f *= damp;
                    if (f > 0f) f /= (1f + cq * f * dt / area);
                    else f = 0f;
                    out[o + d] = f;
                    sum += f;
                }
                if (sum > 0f) {
                    float max = v / dt;
                    if (sum > max) {
                        float k = max / sum;
                        for (int d = 0; d < 6; d++) out[o + d] *= k;
                    }
                }
            }
        }
    }

    private void reservoirExchange(Sec s, int i, int wx, int wy, int wz, int rx, int ry, int rz, float dt,
                                   float cd, float g, boolean infinite) {
        if (!infinite) {
            queueConversion(new BlockPos(rx, ry, rz));
            return;
        }
        float hr = reservoirHead(rx, ry, rz);
        float v = s.vol[i];
        float hi = wy + Math.min(v, 1f) + s.depth[i];
        float dh = hr - hi;
        if (Math.abs(dh) < 1e-4f) return;
        float q = cd * (float) Math.sqrt(2f * g * Math.abs(dh)) * dt;
        if (exCount == exSec.length) {
            exSec = Arrays.copyOf(exSec, exCount * 2);
            exIdx = Arrays.copyOf(exIdx, exCount * 2);
            exAmt = Arrays.copyOf(exAmt, exCount * 2);
        }
        exSec[exCount] = s;
        exIdx[exCount] = i;
        exAmt[exCount] = dh > 0f ? q : -q;
        exCount++;
    }

    private void applyExchanges() {
        for (int k = 0; k < exCount; k++) {
            Sec s = exSec[k];
            int i = exIdx[k];
            float v = s.vol[i];
            float q = exAmt[k];
            if (q > 0f) {
                float a = Math.min(q, Math.max(0f, 1f - v));
                if (a <= 0f) continue;
                if (v <= 0f) s.waterCells++;
                s.vol[i] = v + a;
                stats.reservoirIn += a;
            } else {
                float a = Math.min(-q, v);
                if (a <= 0f) continue;
                s.vol[i] = v - a;
                stats.reservoirOut += a;
            }
            exSec[k] = null;
        }
        exCount = 0;
    }

    private void applyOutflows(float dt) {
        for (int n = 0; n < simCount; n++) {
            Sec s = sim[n];
            float[] vol = s.vol, out = s.out;
            byte[] type = s.type;
            int baseY = s.sy << 4;
            int wx0 = s.owner.cx << 4, wz0 = s.owner.cz << 4;
            for (int i = 0; i < Sec.SIZE; i++) {
                if (type[i] != CellType.OPEN || vol[i] <= 0f) continue;
                int o = i * 6;
                for (int d = 0; d < 6; d++) {
                    float f = out[o + d];
                    if (f <= 0f) continue;
                    float a = f * dt;
                    int j = inner(i, d);
                    if (j >= 0) {
                        if (vol[j] <= 0f) {
                            byte real = realType(wx0 + (j & 15), baseY + (j >> 8), wz0 + ((j >> 4) & 15));
                            if (real != CellType.OPEN) {
                                type[j] = real;
                                out[o + d] = 0f;
                                continue;
                            }
                            s.waterCells++;
                        }
                        vol[j] += a;
                    } else {
                        int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
                        int nx = wx0 + x + DX[d], ny = baseY + y + DY[d], nz = wz0 + z + DZ[d];
                        if (ny < 0 || ny > 255) {
                            out[o + d] = 0f;
                            continue;
                        }
                        Sec t0 = sec(nx, ny, nz);
                        if ((t0 == null || t0.vol[Sec.idx(nx, ny, nz)] <= 0f) && realType(nx, ny, nz) != CellType.OPEN) {
                            out[o + d] = 0f;
                            if (t0 != null && t0.type != null) t0.typeAge = 0;
                            continue;
                        }
                        Sec t = wcCreate(nx >> 4, nz >> 4).getOrCreate(ny >> 4);
                        int ti = Sec.idx(nx, ny, nz);
                        if (t.vol[ti] <= 0f) t.waterCells++;
                        t.vol[ti] += a;
                        if (!t.simulatedThisTick) {
                            activate(t);
                            t.visualDirty = true;
                            visualQueue.add(t.key);
                            changedThisTick.add(t.key);
                        }
                    }
                    vol[i] -= a;
                }
            }
        }
    }

    private void postProcess() {
        final float eps = (float) (WPConfig.performance.sleepEpsilonLiters / 1000.0);
        final float minVol = (float) (WPConfig.physics.minVolumeLiters / 1000.0);
        final int sleepTicks = WPConfig.performance.sleepTicks;
        final long now = world.getTotalWorldTime();
        for (int n = 0; n < simCount; n++) {
            Sec s = sim[n];
            float[] vol = s.vol, prev = s.prev, out = s.out;
            byte[] type = s.type, fx = s.fx;
            boolean changed = false;
            boolean[] faceChanged = new boolean[6];
            float maxFlux = 0f;
            int cells = 0;
            int baseY = s.sy << 4;
            int wx0 = s.owner.cx << 4, wz0 = s.owner.cz << 4;
            for (int i = 0; i < Sec.SIZE; i++) {
                float v = vol[i];
                if (v > 0f && v < 1e-6f) {
                    stats.rounding += v;
                    v = vol[i] = 0f;
                } else if (v > 0f && v < minVol && type[i] == CellType.OPEN) {
                    v = mergeDroplet(s, i, wx0, baseY, wz0);
                }
                if (v < 0f) {
                    stats.rounding += v;
                    v = vol[i] = 0f;
                }
                if (v > 0f) cells++;
                float dv = Math.abs(v - prev[i]);
                if (dv > eps) {
                    changed = true;
                    int x = i & 15, y = i >> 8, z = (i >> 4) & 15;
                    if (y == 0) faceChanged[0] = true;
                    if (y == 15) faceChanged[1] = true;
                    if (z == 0) faceChanged[2] = true;
                    if (z == 15) faceChanged[3] = true;
                    if (x == 0) faceChanged[4] = true;
                    if (x == 15) faceChanged[5] = true;
                }
                if (v > 0f) {
                    int o = i * 6;
                    float fsum = out[o] + out[o + 1] + out[o + 2] + out[o + 3] + out[o + 4] + out[o + 5];
                    if (fsum > maxFlux) maxFlux = fsum;
                    // effect flags for the client
                    byte f = fx[i];
                    if (out[o] > 0.02f) {
                        int jb = inner(i, DOWN);
                        float vb = jb >= 0 ? vol[jb] : vol(wx0 + (i & 15), baseY + (i >> 8) - 1, wz0 + ((i >> 4) & 15));
                        if (vb < FULL) f |= FX_FALLING;
                    }
                    float horiz = Math.max(Math.max(out[o + 2], out[o + 3]), Math.max(out[o + 4], out[o + 5]));
                    float speed = horiz / Math.max(Math.min(v, 1f), 0.05f);
                    if (speed > 2.5f) f |= FX_FAST;
                    if (horiz > 0.3f && v < 0.6f) f |= FX_JET;
                    int ju = inner(i, UP);
                    float va = ju >= 0 ? vol[ju] : vol(wx0 + (i & 15), baseY + (i >> 8) + 1, wz0 + ((i >> 4) & 15));
                    if (va <= 0f) {
                        f |= FX_SURFACE;
                        if (out[o] > 0.25f && v > 0.5f) f |= FX_VORTEX;
                    }
                    fx[i] = f;
                }
            }
            s.waterCells = cells;
            if (changed) {
                s.quiet = 0;
                s.rev++;
                s.visualDirty = true;
                visualQueue.add(s.key);
                changedThisTick.add(s.key);
                for (int d = 0; d < 6; d++) {
                    if (!faceChanged[d]) continue;
                    int ny = s.sy + DY[d];
                    if (ny < 0 || ny > 15) continue;
                    WaterChunk w = wc(s.owner.cx + DX[d], s.owner.cz + DZ[d]);
                    Sec ns = w == null ? null : w.secs[ny];
                    if (ns != null && ns.waterCells > 0) activate(ns);
                }
            } else {
                s.quiet++;
            }
            if (cells == 0 || (s.quiet >= sleepTicks && maxFlux < 0.02f)) {
                sleep(s, now);
            }
        }
        if (stats != null) stats.markDirty();
    }

    /** a droplet below the minimum volume joins neighbouring water (prefers below) or evaporates */
    private float mergeDroplet(Sec s, int i, int wx0, int baseY, int wz0) {
        float v = s.vol[i];
        int wx = wx0 + (i & 15), wy = baseY + (i >> 8), wz = wz0 + ((i >> 4) & 15);
        int best = -1;
        float bestV = 0f;
        for (int d = 0; d < 6; d++) {
            if (d == UP) continue;
            int nx = wx + DX[d], ny = wy + DY[d], nz = wz + DZ[d];
            if (ny < 0 || ny > 255) continue;
            float nv = vol(nx, ny, nz);
            if (nv <= 0f || type(nx, ny, nz) != CellType.OPEN) continue;
            float score = d == DOWN ? 10f + nv : nv;
            if (score > bestV) {
                bestV = score;
                best = d;
            }
        }
        if (best >= 0) {
            int nx = wx + DX[best], ny = wy + DY[best], nz = wz + DZ[best];
            Sec t = sec(nx, ny, nz);
            t.vol[Sec.idx(nx, ny, nz)] += v;
            if (t != s && !t.simulatedThisTick) activate(t);
            s.vol[i] = 0f;
            return 0f;
        }
        if (WPConfig.physics.evaporateDroplets) {
            stats.evaporated += v;
            s.vol[i] = 0f;
            return 0f;
        }
        return v;
    }

    private void sleep(Sec s, long now) {
        s.active = false;
        active.remove(s.key);
        s.releaseSimArrays();
        s.sleptAt = now;
        s.quiet = 0;
        s.rev++;
        changedThisTick.add(s.key);
        s.visualDirty = true;
        visualQueue.add(s.key);
    }

    // ------------------------------------------------------------------------------------------------
    // visuals: block states
    // ------------------------------------------------------------------------------------------------
    public static int level(float v) {
        int levels = WPConfig.performance.visualLevels >= 16 ? 16 : 8;
        int l = (int) Math.ceil(Math.min(v, 1f) * levels - 1e-4f) - 1;
        if (l < 0) l = 0;
        if (l > levels - 1) l = levels - 1;
        return levels == 16 ? l : Math.min(15, l * 2 + 1);
    }

    private void applyVisuals() {
        int budget = WPConfig.performance.maxBlockUpdatesPerTick;
        int used = 0;
        internalUpdate = true;
        try {
            LongIterator it = visualQueue.iterator();
            while (it.hasNext() && used < budget) {
                long k = it.nextLong();
                Sec s = secByKey(k);
                if (s == null) {
                    it.remove();
                    continue;
                }
                Chunk c = mc(s.owner.cx, s.owner.cz);
                if (c == null) {
                    it.remove();
                    continue;
                }
                boolean done = true;
                ExtendedBlockStorage e = c.getBlockStorageArray()[s.sy];
                BlockPos.MutableBlockPos mp = new BlockPos.MutableBlockPos();
                for (int i = 0; i < Sec.SIZE; i++) {
                    float v = s.vol[i];
                    IBlockState cur = (e == null || e == Chunk.NULL_BLOCK_STORAGE) ? Blocks.AIR.getDefaultState()
                            : e.get(i & 15, i >> 8, (i >> 4) & 15);
                    boolean mine = cur.getBlock() == WaterPhysics.WATER_BLOCK;
                    if (v <= 0f && !mine) continue;
                    if (used >= budget) {
                        done = false;
                        break;
                    }
                    mp.setPos(s.worldX(i), s.worldY(i), s.worldZ(i));
                    if (v <= 0f) {
                        world.setBlockState(mp, Blocks.AIR.getDefaultState(), 3);
                        used++;
                    } else if (mine) {
                        int l = level(v);
                        if (cur.getValue(BlockPhysWater.LEVEL) != l) {
                            world.setBlockState(mp, cur.withProperty(BlockPhysWater.LEVEL, l), 2 | 16);
                            used++;
                        }
                    } else {
                        byte t = CellType.of(cur);
                        if (t == CellType.OPEN) {
                            if (CellType.needsWashAway(cur)) cur.getBlock().dropBlockAsItem(world, mp, cur, 0);
                            world.setBlockState(mp, WaterPhysics.WATER_BLOCK.getDefaultState().withProperty(BlockPhysWater.LEVEL, level(v)), 3);
                            used++;
                        } else {
                            displace(mp.toImmutable(), s);
                        }
                    }
                    e = c.getBlockStorageArray()[s.sy];
                }
                if (done) {
                    s.visualDirty = false;
                    it.remove();
                }
            }
        } finally {
            internalUpdate = false;
        }
        lastBlockUpdates = used;
    }

    // ------------------------------------------------------------------------------------------------
    // queries
    // ------------------------------------------------------------------------------------------------
    /** absolute hydraulic head (m) of a cell */
    public float headAt(BlockPos p) {
        Sec s = sec(p.getX(), p.getY(), p.getZ());
        if (s != null && s.head != null && s.active) return s.baseHead[Sec.idx(p.getX(), p.getY(), p.getZ())];
        return baseHeadSlow(p.getX(), p.getY(), p.getZ());
    }

    /** gauge pressure at the bottom of the cell in kPa */
    public float pressureKPa(BlockPos p) {
        float v = getVolume(p);
        if (v <= 0f) return 0f;
        return Math.max(0f, headAt(p) - p.getY()) * RHO_G;
    }

    /** mean flow velocity of a cell (m/s) */
    public Vec3d velocityAt(BlockPos p) {
        Sec s = sec(p.getX(), p.getY(), p.getZ());
        if (s == null || s.out == null) return Vec3d.ZERO;
        int x = p.getX(), y = p.getY(), z = p.getZ();
        int i = Sec.idx(x, y, z);
        float v = s.vol[i];
        if (v <= 0f) return Vec3d.ZERO;
        float[] q = new float[6];
        for (int d = 0; d < 6; d++) {
            float outd = s.out[i * 6 + d];
            float in = 0f;
            Sec o = sec(x - DX[d], y - DY[d], z - DZ[d]);
            if (o != null && o.out != null) in = o.out[Sec.idx(x - DX[d], y - DY[d], z - DZ[d]) * 6 + d];
            q[d] = (outd + in) * 0.5f;
        }
        float a = Math.max(Math.min(v, 1f), 0.05f);
        return new Vec3d((q[5] - q[4]) / a, (q[1] - q[0]) / a, (q[3] - q[2]) / a);
    }

    /** velocity (m/s) of cell i of an active section, written to out[0..2] = x,y,z */
    public void cellVelocity(Sec s, int i, float[] res) {
        res[0] = res[1] = res[2] = 0f;
        if (s.out == null) return;
        float v = s.vol[i];
        if (v <= 0f) return;
        int wx = s.worldX(i), wy = s.worldY(i), wz = s.worldZ(i);
        float[] q = new float[6];
        for (int d = 0; d < 6; d++) {
            float in;
            int j = inner(i, OPP[d]);
            if (j >= 0) in = s.out[j * 6 + d];
            else {
                Sec o = sec(wx - DX[d], wy - DY[d], wz - DZ[d]);
                in = (o != null && o.out != null) ? o.out[Sec.idx(wx - DX[d], wy - DY[d], wz - DZ[d]) * 6 + d] : 0f;
            }
            q[d] = (s.out[i * 6 + d] + in) * 0.5f;
        }
        float a = Math.max(Math.min(v, 1f), 0.05f);
        res[0] = (q[5] - q[4]) / a;
        res[1] = (q[1] - q[0]) / a;
        res[2] = (q[3] - q[2]) / a;
    }

    public static final class BodyInfo {
        public int cells;
        public double volume;
        public int minY = Integer.MAX_VALUE, maxY = Integer.MIN_VALUE;
        public double surface = -1;
        public double maxPressureKPa;
        public double maxSpeed;
        public boolean truncated;
        public int activeCells;
    }

    /** Connected body of water containing p (BFS through water cells). */
    public BodyInfo scanBody(BlockPos p, int limit) {
        BodyInfo b = new BodyInfo();
        if (getVolume(p) <= 0f) return b;
        LongOpenHashSet seen = new LongOpenHashSet();
        ArrayDeque<BlockPos> q = new ArrayDeque<>();
        q.add(p);
        seen.add(p.toLong());
        while (!q.isEmpty()) {
            if (b.cells >= limit) {
                b.truncated = true;
                break;
            }
            BlockPos c = q.poll();
            float v = getVolume(c);
            if (v <= 0f) continue;
            b.cells++;
            b.volume += v;
            b.minY = Math.min(b.minY, c.getY());
            b.maxY = Math.max(b.maxY, c.getY());
            if (getVolume(c.up()) <= 0f) b.surface = Math.max(b.surface, c.getY() + Math.min(v, 1f));
            b.maxPressureKPa = Math.max(b.maxPressureKPa, pressureKPa(c));
            Sec s = sec(c.getX(), c.getY(), c.getZ());
            if (s != null && s.active) {
                b.activeCells++;
                b.maxSpeed = Math.max(b.maxSpeed, velocityAt(c).lengthVector());
            }
            for (int d = 0; d < 6; d++) {
                BlockPos n = c.add(DX[d], DY[d], DZ[d]);
                if (seen.add(n.toLong()) && getVolume(n) > 0f) q.add(n);
            }
        }
        return b;
    }

    public static final class TankInfo {
        public int capacityCells;
        public boolean closed;
        public int spillLevel;
        public int floorY;
    }

    /**
     * Capacity of the container around p: "priority flood" from the floor upward; the container ends at the
     * level where the flood escapes (spills over the rim or through a hole).
     */
    public TankInfo scanTank(BlockPos p, int limit) {
        TankInfo t = new TankInfo();
        BlockPos start = p;
        while (start.getY() > 0) {
            IBlockState below = stateAt(start.getX(), start.getY() - 1, start.getZ());
            if (below == null || CellType.of(below) != CellType.OPEN) break;
            start = start.down();
        }
        t.floorY = start.getY();
        PriorityQueue<long[]> heap = new PriorityQueue<>((a, b) -> Long.compare(a[1], b[1]));
        LongOpenHashSet seen = new LongOpenHashSet();
        heap.add(new long[]{start.toLong(), start.getY()});
        seen.add(start.toLong());
        int level = start.getY();
        Int2IntOpenHashMapLite counts = new Int2IntOpenHashMapLite();
        int visited = 0;
        while (!heap.isEmpty()) {
            long[] e = heap.poll();
            BlockPos c = BlockPos.fromLong(e[0]);
            if (c.getY() > level) level = c.getY();
            visited++;
            if (visited > limit) {
                t.spillLevel = level;
                int cap = 0;
                for (int y = t.floorY; y < level; y++) cap += counts.get(y);
                t.capacityCells = cap;
                t.closed = false;
                return t;
            }
            counts.add(level, 1);
            for (int d = 0; d < 6; d++) {
                BlockPos n = c.add(DX[d], DY[d], DZ[d]);
                if (n.getY() < 0 || n.getY() > 255 || !seen.add(n.toLong())) continue;
                IBlockState st = stateAt(n.getX(), n.getY(), n.getZ());
                if (st == null) continue;
                byte ty = CellType.of(st);
                if (ty == CellType.OPEN) heap.add(new long[]{n.toLong(), n.getY()});
            }
        }
        t.closed = true;
        t.capacityCells = visited;
        t.spillLevel = level + 1;
        return t;
    }

    /** minimal int->int counter (avoids depending on more fastutil classes) */
    private static final class Int2IntOpenHashMapLite {
        private final Map<Integer, Integer> m = new HashMap<>();
        void add(int k, int v) { m.merge(k, v, Integer::sum); }
        int get(int k) { Integer v = m.get(k); return v == null ? 0 : v; }
    }

    /** total physical water (m^3) currently stored in loaded chunks */
    public double totalStored() {
        double t = 0;
        for (WaterChunk w : chunks.values())
            for (Sec s : w.secs) if (s != null) t += s.total();
        return t;
    }

    public int activeCount() { return active.size(); }
    public int chunkCount() { return chunks.size(); }

    public Collection<WaterChunk> allChunks() { return chunks.values(); }

    // ------------------------------------------------------------------------------------------------
    // persistence (chunk NBT)
    // ------------------------------------------------------------------------------------------------
    public static final String NBT_KEY = "WaterPhysics";

    public NBTTagCompound writeChunk(int cx, int cz) {
        WaterChunk w = chunks.get(ChunkPos.asLong(cx, cz));
        if (w == null) return null;
        NBTTagList list = new NBTTagList();
        for (Sec s : w.secs) {
            if (s == null) continue;
            int n = 0;
            for (float v : s.vol) if (v > 0f) n++;
            if (n == 0) continue;
            int[] idx = new int[n];
            int[] vals = new int[n];
            int k = 0;
            for (int i = 0; i < Sec.SIZE; i++) {
                if (s.vol[i] > 0f) {
                    idx[k] = i;
                    vals[k] = Float.floatToIntBits(s.vol[i]);
                    k++;
                }
            }
            NBTTagCompound t = new NBTTagCompound();
            t.setByte("y", (byte) s.sy);
            t.setIntArray("i", idx);
            t.setIntArray("v", vals);
            t.setBoolean("a", s.active);
            list.appendTag(t);
        }
        if (list.hasNoTags()) return null;
        NBTTagCompound root = new NBTTagCompound();
        root.setInteger("ver", 1);
        root.setTag("s", list);
        return root;
    }

    /** May be called from the chunk IO thread: data is applied on the server thread at the next tick. */
    public void queueChunkLoad(Chunk chunk, NBTTagCompound tag) {
        pendingLoads.add(new Object[]{chunk, tag.copy()});
    }

    private void drainPendingLoads() {
        Object[] e;
        while ((e = pendingLoads.poll()) != null) {
            Chunk c = (Chunk) e[0];
            NBTTagCompound root = (NBTTagCompound) e[1];
            WaterChunk w = new WaterChunk(c.x, c.z);
            w.chunk = c;
            w.fromSave = true;
            NBTTagList list = root.getTagList("s", 10);
            for (int k = 0; k < list.tagCount(); k++) {
                NBTTagCompound t = list.getCompoundTagAt(k);
                Sec s = w.getOrCreate(t.getByte("y") & 15);
                int[] idx = t.getIntArray("i");
                int[] vals = t.getIntArray("v");
                for (int j = 0; j < Math.min(idx.length, vals.length); j++) {
                    if (idx[j] >= 0 && idx[j] < Sec.SIZE) s.vol[idx[j]] = Float.intBitsToFloat(vals[j]);
                }
                s.recount();
                s.visualDirty = true;
                if (t.getBoolean("a")) {
                    s.active = true;
                    active.add(s.key);
                }
            }
            chunks.put(ChunkPos.asLong(c.x, c.z), w);
            lastWc = null;
            lastWcKey = Long.MIN_VALUE;
            for (Sec s : w.secs) if (s != null) visualQueue.add(s.key);
        }
    }

    public void onChunkLoaded(Chunk c) {
        WaterChunk w = chunks.get(ChunkPos.asLong(c.x, c.z));
        if (w != null) w.chunk = c;
    }

    public void onChunkUnloaded(Chunk c) {
        WaterChunk w = chunks.remove(ChunkPos.asLong(c.x, c.z));
        lastWc = null;
        lastWcKey = Long.MIN_VALUE;
        mcChunk = null;
        mcChunkKey = Long.MIN_VALUE;
        if (w == null) return;
        for (Sec s : w.secs) {
            if (s == null) continue;
            active.remove(s.key);
            visualQueue.remove(s.key);
        }
    }

    public boolean hasPendingLoads() {
        return !pendingLoads.isEmpty();
    }
}
