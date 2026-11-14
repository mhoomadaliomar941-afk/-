package com.waterphysics.client;

import com.waterphysics.CommonProxy;
import com.waterphysics.net.MsgEffect;
import com.waterphysics.net.MsgGaugeReply;
import com.waterphysics.net.MsgSectionSync;
import net.minecraft.client.Minecraft;
import net.minecraftforge.fml.common.event.FMLInitializationEvent;
import net.minecraftforge.fml.common.event.FMLPreInitializationEvent;

/** Client side proxy: rendering, effects and HUD. */
public class ClientProxy extends CommonProxy {
    @Override
    public void preInit(FMLPreInitializationEvent e) {
        ClientSetup.preInit();
    }

    @Override
    public void init(FMLInitializationEvent e) {
        ClientSetup.init();
    }

    @Override
    public void handleSync(MsgSectionSync m) {
        Minecraft.getMinecraft().addScheduledTask(() -> ClientWaterData.apply(m));
    }

    @Override
    public void handleEffect(MsgEffect m) {
        Minecraft.getMinecraft().addScheduledTask(() -> WaterEffects.oneShot(m));
    }

    @Override
    public void handleGauge(MsgGaugeReply m) {
        Minecraft.getMinecraft().addScheduledTask(() -> HydrometerHud.update(m));
    }
}
