package com.waterphysics.command;

import com.waterphysics.WaterPhysics;
import com.waterphysics.net.GaugeReader;
import com.waterphysics.pipe.PipeManager;
import com.waterphysics.sim.*;
import com.waterphysics.tile.TileFluidNode;
import com.waterphysics.tile.TilePipe;
import com.waterphysics.tile.TilePump;
import com.waterphysics.tile.TileSpigot;
import net.minecraft.block.state.IBlockState;
import net.minecraft.command.CommandBase;
import net.minecraft.command.CommandException;
import net.minecraft.command.ICommandSender;
import net.minecraft.command.WrongUsageException;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.init.Blocks;
import net.minecraft.init.Items;
import net.minecraft.item.ItemStack;
import net.minecraft.server.MinecraftServer;
import net.minecraft.tileentity.TileEntity;
import net.minecraft.util.ActionResult;
import net.minecraft.util.EnumHand;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Vec3d;
import net.minecraft.util.text.TextComponentString;
import net.minecraft.world.World;
import net.minecraft.world.WorldServer;
import net.minecraftforge.common.util.FakePlayer;
import net.minecraftforge.common.util.FakePlayerFactory;

import javax.annotation.Nullable;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

/**
 * /water - inspection and testing tools. Output lines prefixed with [WP] are machine readable
 * (key=value) and used by the automated test suite.
 */
public class CommandWater extends CommandBase {
    private static final List<String> SUBS = Arrays.asList("info", "body", "tank", "stats", "perf", "add", "remove", "fill",
            "clear", "bucket", "pipe", "pipedamage", "spigot", "region", "spawn", "wake", "tovanilla", "help");

    @Override
    public String getName() {
        return "water";
    }

    @Override
    public String getUsage(ICommandSender sender) {
        return "/water <info|body|tank|stats|perf|add|remove|fill|clear|bucket|pipe|pipedamage|spigot|wake|tovanilla|help>";
    }

    @Override
    public int getRequiredPermissionLevel() {
        return 2;
    }

    private static void out(ICommandSender s, String msg) {
        s.sendMessage(new TextComponentString(msg));
        WaterPhysics.LOG.info(msg);
    }

    private static String f(double v) {
        return String.format(Locale.ROOT, "%.3f", v);
    }

    private BlockPos pos(MinecraftServer server, ICommandSender sender, String[] a, int i) throws CommandException {
        if (a.length < i + 3) throw new WrongUsageException(getUsage(sender));
        return parseBlockPos(sender, a, i, false);
    }

    @Override
    public void execute(MinecraftServer server, ICommandSender sender, String[] a) throws CommandException {
        if (a.length == 0) throw new WrongUsageException(getUsage(sender));
        World w = sender.getEntityWorld();
        WaterSim sim = WaterManager.get(w);
        if (sim == null) throw new CommandException("No simulation for this world");
        switch (a[0]) {
            case "info": {
                BlockPos p = pos(server, sender, a, 1);
                Vec3d v = sim.velocityAt(p);
                out(sender, "[WP] info pos=" + p.getX() + "," + p.getY() + "," + p.getZ() + " volume_l=" + f(sim.getVolume(p) * 1000)
                        + " head=" + f(sim.headAt(p)) + " pressure_kpa=" + f(sim.pressureKPa(p)) + " speed=" + f(v.lengthVector())
                        + " block=" + w.getBlockState(p).getBlock().getRegistryName());
                break;
            }
            case "body": {
                BlockPos p = pos(server, sender, a, 1);
                WaterSim.BodyInfo b = sim.scanBody(p, 200000);
                out(sender, "[WP] body pos=" + p.getX() + "," + p.getY() + "," + p.getZ() + " cells=" + b.cells + " volume_l=" + f(b.volume * 1000)
                        + " surface=" + f(b.surface) + " miny=" + (b.cells > 0 ? b.minY : 0) + " maxp_kpa=" + f(b.maxPressureKPa)
                        + " maxspeed=" + f(b.maxSpeed) + " active=" + b.activeCells + " truncated=" + b.truncated
                        + " tick=" + w.getTotalWorldTime());
                break;
            }
            case "tank": {
                BlockPos p = pos(server, sender, a, 1);
                WaterSim.TankInfo t = sim.scanTank(p, 60000);
                WaterSim.BodyInfo b = sim.scanBody(p, 200000);
                out(sender, "[WP] tank pos=" + p.getX() + "," + p.getY() + "," + p.getZ() + " capacity_l=" + t.capacityCells * 1000
                        + " closed=" + t.closed + " spill=" + t.spillLevel + " floor=" + t.floorY + " volume_l=" + f(b.volume * 1000));
                break;
            }
            case "stats": {
                WaterStats st = sim.stats;
                double loaded = sim.totalStored();
                double unloaded = sim.totalUnloaded();
                double stored = loaded + unloaded;
                double pipes = PipeManager.get(w).totalStored();
                double expected = st.expectedStored();
                out(sender, "[WP] stats stored_l=" + f(stored * 1000) + " loaded_l=" + f(loaded * 1000) + " unloaded_l=" + f(unloaded * 1000) + " pipes_l=" + f(pipes * 1000) + " total_l=" + f((stored + pipes) * 1000)
                        + " expected_l=" + f(expected * 1000) + " drift_l=" + f((stored + pipes - expected) * 1000)
                        + " in_buckets=" + f(st.bucketsIn * 1000) + " out_buckets=" + f(st.bucketsOut * 1000)
                        + " in_reservoir=" + f(st.reservoirIn * 1000) + " out_reservoir=" + f(st.reservoirOut * 1000)
                        + " in_converted=" + f(st.convertedIn * 1000) + " in_command=" + f(st.commandIn * 1000)
                        + " out_command=" + f(st.commandOut * 1000) + " in_pumped=" + f(st.pumpedIn * 1000)
                        + " out_displaced=" + f(st.displacedOut * 1000) + " out_destroyed=" + f(st.destroyedOut * 1000)
                        + " out_other=" + f(st.otherOut * 1000) + " evaporated=" + f(st.evaporated * 1000)
                        + " rounding_l=" + f(st.rounding * 1000));
                break;
            }
            case "perf": {
                out(sender, "[WP] perf active_sections=" + sim.activeCount() + " simulated=" + sim.lastSimulated + " tick_ms="
                        + f(sim.lastTickNanos / 1e6) + " block_updates=" + sim.lastBlockUpdates + " chunks=" + sim.chunkCount()
                        + " pipe_nodes=" + PipeManager.get(w).nodeCount() + " net_msgs=" + com.waterphysics.net.WaterNet.messagesSent);
                break;
            }
            case "add":
            case "remove": {
                BlockPos p = pos(server, sender, a, 1);
                float liters = (float) parseDouble(a.length > 4 ? a[4] : "1000", 0, 1e6);
                float m3 = liters / 1000f;
                float done;
                if (a[0].equals("add")) {
                    done = sim.addWater(p, m3);
                    sim.stats.commandIn += done;
                } else {
                    done = sim.takeWater(p, m3);
                    sim.stats.commandOut += done;
                }
                out(sender, "[WP] " + a[0] + " pos=" + p.getX() + "," + p.getY() + "," + p.getZ() + " liters=" + f(done * 1000));
                break;
            }
            case "fill":
            case "clear": {
                BlockPos p1 = pos(server, sender, a, 1), p2 = pos(server, sender, a, 4);
                float per = a[0].equals("fill") ? (float) parseDouble(a.length > 7 ? a[7] : "1000", 0, 1e5) / 1000f : 0f;
                double total = 0;
                for (BlockPos p : BlockPos.getAllInBox(p1, p2)) {
                    if (a[0].equals("fill")) {
                        if (CellType.of(w.getBlockState(p)) != CellType.OPEN) continue;
                        float d = sim.addWater(p, per);
                        sim.stats.commandIn += d;
                        total += d;
                    } else {
                        float d = sim.takeWater(p, 100f);
                        sim.stats.commandOut += d;
                        total += d;
                    }
                }
                out(sender, "[WP] " + a[0] + " liters=" + f(total * 1000));
                break;
            }
            case "bucket": {
                // simulates a player using a bucket from above the target (real vanilla bucket code path)
                BlockPos p = pos(server, sender, a, 1);
                boolean fill = a.length > 4 && a[4].equals("fill");
                FakePlayer fp = FakePlayerFactory.getMinecraft((WorldServer) w);
                fp.setPositionAndRotation(p.getX() + 0.5, p.getY() + 1.05, p.getZ() + 0.5, 0f, 90f);
                fp.rotationPitch = 90f;
                fp.rotationYaw = 0f;
                fp.capabilities.isCreativeMode = false;
                ItemStack stack = new ItemStack(fill ? Items.BUCKET : Items.WATER_BUCKET);
                fp.setHeldItem(EnumHand.MAIN_HAND, stack);
                ActionResult<ItemStack> r = stack.useItemRightClick(w, fp, EnumHand.MAIN_HAND);
                ItemStack res = r.getResult();
                out(sender, "[WP] bucket mode=" + (fill ? "fill" : "empty") + " result=" + r.getType() + " item="
                        + res.getItem().getRegistryName());
                fp.setHeldItem(EnumHand.MAIN_HAND, ItemStack.EMPTY);
                break;
            }
            case "pipe": {
                BlockPos p = pos(server, sender, a, 1);
                TileEntity te = w.getTileEntity(p);
                if (!(te instanceof TileFluidNode)) throw new CommandException("No pipe at " + p);
                TileFluidNode n = (TileFluidNode) te;
                String extra = te instanceof TilePipe ? " damage=" + f(((TilePipe) te).damage) : te instanceof TilePump ? " pump_flow_lps=" + f(((TilePump) te).lastFlow * 1000) : "";
                out(sender, "[WP] pipe pos=" + p.getX() + "," + p.getY() + "," + p.getZ() + " volume_l=" + f(n.volume * 1000) + " capacity_l="
                        + f(n.capacity() * 1000) + " head=" + f(n.head) + " pressure_kpa=" + f(n.gaugePressureKPa()) + " flow_lps="
                        + f(PipeManager.get(w).linkFlow(n) * 1000) + extra);
                break;
            }
            case "pipedamage": {
                BlockPos p = pos(server, sender, a, 1);
                TileEntity te = w.getTileEntity(p);
                if (!(te instanceof TilePipe)) throw new CommandException("No pipe at " + p);
                ((TilePipe) te).damage = (float) parseDouble(a.length > 4 ? a[4] : "0.6", 0, 1);
                ((TilePipe) te).syncIfChanged(true);
                PipeManager.get(w).markDirty();
                out(sender, "[WP] pipedamage pos=" + p.getX() + "," + p.getY() + "," + p.getZ() + " damage=" + f(((TilePipe) te).damage));
                break;
            }
            case "spigot": {
                BlockPos p = pos(server, sender, a, 1);
                TileEntity te = w.getTileEntity(p);
                if (!(te instanceof TileSpigot)) throw new CommandException("No spigot at " + p);
                TileSpigot s = (TileSpigot) te;
                if (a.length > 4) {
                    s.aperture = parseInt(a[4], 0, TileSpigot.APERTURES_CM2.length - 1);
                    s.markDirty();
                }
                out(sender, "[WP] spigot pos=" + p.getX() + "," + p.getY() + "," + p.getZ() + " aperture_cm2=" + TileSpigot.APERTURES_CM2[s.aperture]
                        + " flow_lps=" + f(s.lastFlow * 1000));
                break;
            }
            case "region": {
                // total water inside a box, number of wet blocks and highest free surface
                BlockPos p1 = pos(server, sender, a, 1), p2 = pos(server, sender, a, 4);
                double vol = 0, surf = -1;
                int cells = 0;
                for (BlockPos p : BlockPos.getAllInBox(p1, p2)) {
                    float v = sim.getVolume(p);
                    if (v <= 0f) continue;
                    vol += v;
                    cells++;
                    if (sim.getVolume(p.up()) <= 0f) surf = Math.max(surf, p.getY() + Math.min(1f, v));
                }
                out(sender, "[WP] region volume_l=" + f(vol * 1000) + " cells=" + cells + " surface=" + f(surf) + " tick=" + w.getTotalWorldTime());
                break;
            }
            case "spawn": {
                BlockPos sp = w.getSpawnPoint();
                BlockPos top = w.getHeight(sp);
                out(sender, "[WP] spawn x=" + sp.getX() + " y=" + top.getY() + " z=" + sp.getZ());
                break;
            }
            case "wake": {
                int n = 0;
                for (WaterChunk wc : sim.allChunks())
                    for (Sec s : wc.secs)
                        if (s != null && s.waterCells > 0) {
                            sim.activate(s);
                            n++;
                        }
                out(sender, "[WP] wake sections=" + n);
                break;
            }
            case "tovanilla": {
                // converts physical water back to vanilla water (e.g. before uninstalling the mod)
                BlockPos p1 = pos(server, sender, a, 1), p2 = pos(server, sender, a, 4);
                int n = 0;
                for (BlockPos p : BlockPos.getAllInBox(p1, p2)) {
                    IBlockState st = w.getBlockState(p);
                    if (st.getBlock() != WaterPhysics.WATER_BLOCK) continue;
                    float v = sim.getVolume(p);
                    sim.internalUpdate = true;
                    try {
                        sim.takeWater(p, 100f);
                        w.setBlockState(p, v >= 0.5f ? Blocks.WATER.getDefaultState() : Blocks.AIR.getDefaultState(), 2);
                    } finally {
                        sim.internalUpdate = false;
                    }
                    sim.stats.otherOut += v;
                    n++;
                }
                out(sender, "[WP] tovanilla blocks=" + n);
                break;
            }
            default:
                out(sender, "Ultra Realistic Water Physics: " + getUsage(sender));
                out(sender, "body/tank/info x y z | fill x1 y1 z1 x2 y2 z2 [L] | bucket x y z fill|empty | stats | perf");
        }
    }

    @Override
    public List<String> getTabCompletions(MinecraftServer server, ICommandSender sender, String[] args, @Nullable BlockPos target) {
        if (args.length == 1) return getListOfStringsMatchingLastWord(args, SUBS);
        if (args.length >= 2 && args.length <= 4) return getTabCompletionCoordinate(args, 1, target);
        return Collections.emptyList();
    }

    @Override
    public boolean checkPermission(MinecraftServer server, ICommandSender sender) {
        return !(sender instanceof EntityPlayer) || super.checkPermission(server, sender);
    }

    static GaugeReader unused() {
        return null;
    }
}
