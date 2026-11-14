package com.waterphysics.net;

import com.waterphysics.pipe.PipeManager;
import com.waterphysics.sim.WaterManager;
import com.waterphysics.sim.WaterSim;
import com.waterphysics.tile.*;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.tileentity.TileEntity;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Vec3d;
import net.minecraft.util.text.TextComponentTranslation;
import net.minecraft.util.text.TextFormatting;
import net.minecraft.world.World;

import java.util.Locale;

/** Builds hydrometer / gauge readings (server side). Lines are "translationKey\u0001arg\u0001arg...". */
public final class GaugeReader {
    public static final char SEP = '\u0001';

    private GaugeReader() {}

    private static String fmt(double v, int decimals) {
        return String.format(Locale.ROOT, "%." + decimals + "f", v);
    }

    private static String line(String key, Object... args) {
        StringBuilder sb = new StringBuilder(key);
        for (Object a : args) sb.append(SEP).append(a);
        return sb.toString();
    }

    public static MsgGaugeReply read(World w, BlockPos p) {
        return read(w, p, false);
    }

    public static MsgGaugeReply read(World w, BlockPos p, boolean tank) {
        WaterSim sim = WaterManager.get(w);
        if (sim == null || !w.isBlockLoaded(p)) return null;
        MsgGaugeReply r = new MsgGaugeReply(p);
        TileEntity te = w.getTileEntity(p);
        if (te instanceof TileFluidNode) {
            TileFluidNode n = (TileFluidNode) te;
            PipeManager pm = PipeManager.get(w);
            float lps = pm.linkFlow(n) * 1000f;
            if (te instanceof TilePump) {
                TilePump pu = (TilePump) te;
                r.lines.add(line("waterphysics.gauge.pump", "waterphysics.msg.pump_mode." + pu.mode, fmt(Math.abs(pu.lastFlow) * 1000f, 1)));
            } else if (te instanceof TileValve) {
                r.lines.add(line("waterphysics.gauge.valve", Math.round(((TileValve) te).opening() * 100)));
            }
            r.lines.add(line("waterphysics.gauge.pipe", Math.round(n.diameter() * 100), fmt(n.volume * 1000f, 1),
                    fmt(n.capacity() * 1000f, 1), fmt(n.gaugePressureKPa(), 1), Math.round(n.ratedPressure())));
            r.lines.add(line("waterphysics.gauge.pipeflow", fmt(lps, 1), fmt(lps / 1000f / n.area(), 2)));
            if (te instanceof TilePipe) {
                TilePipe pp = (TilePipe) te;
                String cond = pp.burst() ? "waterphysics.gauge.cond.burst" : pp.cracked() ? "waterphysics.gauge.cond.cracked" : "waterphysics.gauge.cond.ok";
                r.lines.add(line("waterphysics.gauge.damage", cond, Math.round(pp.damage * 100)));
            }
            return r;
        }
        if (te instanceof TileSpigot) {
            TileSpigot s = (TileSpigot) te;
            r.lines.add(line("waterphysics.gauge.spigot", TileSpigot.APERTURES_CM2[s.aperture], fmt(s.lastFlow * 1000f, 2)));
            return r;
        }
        float v = sim.getVolume(p);
        if (v <= 0f) {
            if (tank) r.lines.add(line("waterphysics.gauge.empty"));
            else return null;
        } else {
            WaterSim.BodyInfo b = sim.scanBody(p, 20000);
            Vec3d vel = sim.velocityAt(p);
            r.lines.add(line("waterphysics.gauge.cell", fmt(v * 1000f, 1)));
            r.lines.add(line("waterphysics.gauge.body", fmt(b.volume * 1000.0, 0) + (b.truncated ? "+" : ""), b.cells));
            r.lines.add(line("waterphysics.gauge.level", fmt(b.surface, 2), fmt(b.surface - b.minY, 2)));
            float kpa = sim.pressureKPa(p);
            r.lines.add(line("waterphysics.gauge.pressure", fmt(kpa, 1), fmt(kpa / 9.81f, 2)));
            r.lines.add(line("waterphysics.gauge.flow", fmt(vel.lengthVector(), 2), fmt(b.maxSpeed, 2)));
        }
        if (tank) {
            WaterSim.TankInfo t = sim.scanTank(p, 40000);
            WaterSim.BodyInfo b = sim.scanBody(p, 40000);
            if (t.capacityCells > 0) {
                double pct = 100.0 * b.volume / t.capacityCells;
                r.lines.add(line("waterphysics.gauge.tank", t.capacityCells * 1000, fmt(Math.min(100.0, pct), 1), t.closed ? "waterphysics.gauge.closed" : "waterphysics.gauge.open"));
            }
        }
        return r;
    }

    public static void toChat(World w, BlockPos p, EntityPlayer player, boolean tank) {
        MsgGaugeReply r = read(w, p, tank);
        if (r == null || r.lines.isEmpty()) {
            player.sendMessage(new TextComponentTranslation("waterphysics.gauge.nothing"));
            return;
        }
        for (String l : r.lines) {
            String[] parts = l.split(String.valueOf(SEP));
            Object[] args = new Object[parts.length - 1];
            for (int i = 1; i < parts.length; i++) {
                args[i - 1] = parts[i].startsWith("waterphysics.") ? new TextComponentTranslation(parts[i]) : parts[i];
            }
            TextComponentTranslation c = new TextComponentTranslation(parts[0], args);
            c.getStyle().setColor(TextFormatting.AQUA);
            player.sendMessage(c);
        }
    }

    public static int comparator(World w, BlockPos cell) {
        WaterSim sim = WaterManager.get(w);
        if (sim == null) return 0;
        float v = sim.getVolume(cell);
        if (v <= 0f) return 0;
        WaterSim.TankInfo t = sim.scanTank(cell, 20000);
        if (t.capacityCells <= 0) return 0;
        WaterSim.BodyInfo b = sim.scanBody(cell, 20000);
        return (int) Math.max(1, Math.min(15, Math.round(15.0 * b.volume / t.capacityCells)));
    }
}
