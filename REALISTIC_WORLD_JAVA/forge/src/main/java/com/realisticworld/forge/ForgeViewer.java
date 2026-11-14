package com.realisticworld.forge;

import com.realisticworld.ui.ControlCenter;
import com.realisticworld.ui.Form;
import net.minecraft.entity.player.EntityPlayerMP;

import java.util.function.Consumer;

/** A connected player looking at the control center. */
final class ForgeViewer implements ControlCenter.Viewer {
    private final EntityPlayerMP p;

    ForgeViewer(EntityPlayerMP p) {
        this.p = p;
    }

    @Override public String name() { return p.getName(); }
    @Override public boolean isOperator() { return PlayerPrefs.isOperator(p); }
    @Override public boolean perfHud() { return PlayerPrefs.perfHud(p); }
    @Override public void setPerfHud(boolean on) { PlayerPrefs.setPerfHud(p, on); }
    @Override public boolean weatherHud() { return PlayerPrefs.weatherHud(p); }
    @Override public void setWeatherHud(boolean on) { PlayerPrefs.setWeatherHud(p, on); }
    @Override public int dim() { return p.dimension; }
    @Override public double x() { return p.posX; }
    @Override public double y() { return p.posY; }
    @Override public double z() { return p.posZ; }
    @Override public void message(String text) { RealisticWorldMod.say(p, text); }

    @Override
    public void show(Form form, Consumer<Form.Reply> onReply) {
        if (p.hasDisconnected()) return;
        RealisticWorldMod.showForm(p, form, onReply);
    }
}
