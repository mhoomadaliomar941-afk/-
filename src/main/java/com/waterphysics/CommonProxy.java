package com.waterphysics;

import com.waterphysics.net.MsgEffect;
import com.waterphysics.net.MsgGaugeReply;
import com.waterphysics.net.MsgSectionSync;
import net.minecraftforge.fml.common.event.FMLInitializationEvent;
import net.minecraftforge.fml.common.event.FMLPostInitializationEvent;
import net.minecraftforge.fml.common.event.FMLPreInitializationEvent;

/** Server (and common) side proxy. */
public class CommonProxy {
    public void preInit(FMLPreInitializationEvent e) {}

    public void init(FMLInitializationEvent e) {}

    public void postInit(FMLPostInitializationEvent e) {}

    public void handleSync(MsgSectionSync m) {}

    public void handleEffect(MsgEffect m) {}

    public void handleGauge(MsgGaugeReply m) {}
}
