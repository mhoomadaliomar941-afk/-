package com.waterphysics;

import com.waterphysics.block.BlockPhysWater;
import com.waterphysics.command.CommandWater;
import com.waterphysics.net.WaterNet;
import net.minecraft.util.ResourceLocation;
import net.minecraftforge.fluids.Fluid;
import net.minecraftforge.fluids.FluidRegistry;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.common.SidedProxy;
import net.minecraftforge.fml.common.event.FMLInitializationEvent;
import net.minecraftforge.fml.common.event.FMLPostInitializationEvent;
import net.minecraftforge.fml.common.event.FMLPreInitializationEvent;
import net.minecraftforge.fml.common.event.FMLServerStartingEvent;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

/**
 * Ultra Realistic Water Physics for Minecraft 1.12.2.
 * Replaces block-spreading water with a physically simulated, finite, pressurised fluid.
 */
@Mod(modid = WaterPhysics.MODID, name = WaterPhysics.NAME, version = WaterPhysics.VERSION,
        acceptedMinecraftVersions = "[1.12.2]", dependencies = "required-after:forge@[14.23.5.2768,)")
public class WaterPhysics {
    public static final String MODID = "waterphysics";
    public static final String NAME = "Ultra Realistic Water Physics";
    public static final String VERSION = "1.0.0";
    public static final Logger LOG = LogManager.getLogger(NAME);

    @Mod.Instance(MODID)
    public static WaterPhysics INSTANCE;

    @SidedProxy(clientSide = "com.waterphysics.client.ClientProxy", serverSide = "com.waterphysics.CommonProxy")
    public static CommonProxy proxy;

    public static Fluid FLUID;
    public static BlockPhysWater WATER_BLOCK;

    @Mod.EventHandler
    public void preInit(FMLPreInitializationEvent e) {
        FLUID = new Fluid("physical_water", new ResourceLocation("minecraft", "blocks/water_still"),
                new ResourceLocation("minecraft", "blocks/water_flow")).setColor(0xFF3F76E4).setUnlocalizedName(MODID + ".physical_water");
        FluidRegistry.registerFluid(FLUID);
        WaterNet.init();
        proxy.preInit(e);
    }

    @Mod.EventHandler
    public void init(FMLInitializationEvent e) {
        ModRegistry.registerDispenserBehaviors();
        proxy.init(e);
    }

    @Mod.EventHandler
    public void postInit(FMLPostInitializationEvent e) {
        proxy.postInit(e);
        LOG.info("Ultra Realistic Water Physics {} loaded", VERSION);
    }

    @Mod.EventHandler
    public void serverStarting(FMLServerStartingEvent e) {
        e.registerServerCommand(new CommandWater());
    }
}
