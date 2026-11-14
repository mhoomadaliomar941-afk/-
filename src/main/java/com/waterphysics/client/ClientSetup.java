package com.waterphysics.client;

import com.waterphysics.ModRegistry;
import com.waterphysics.WaterPhysics;
import com.waterphysics.tile.TilePipe;
import com.waterphysics.tile.TileValve;
import net.minecraft.block.Block;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.block.model.ModelResourceLocation;
import net.minecraft.client.renderer.block.statemap.StateMapperBase;
import net.minecraft.client.settings.KeyBinding;
import net.minecraft.item.Item;
import net.minecraft.util.ResourceLocation;
import net.minecraft.world.biome.BiomeColorHelper;
import net.minecraftforge.client.event.ModelBakeEvent;
import net.minecraftforge.client.event.ModelRegistryEvent;
import net.minecraftforge.client.model.ModelLoader;
import net.minecraftforge.common.MinecraftForge;
import net.minecraftforge.common.config.ConfigElement;
import net.minecraftforge.fml.client.config.GuiConfig;
import net.minecraftforge.fml.client.registry.ClientRegistry;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;
import net.minecraftforge.fml.common.gameevent.InputEvent;
import net.minecraftforge.fml.relauncher.Side;
import org.lwjgl.input.Keyboard;

/** Client registration: models, textures, colours, renderers, key binding for the settings screen. */
@Mod.EventBusSubscriber(modid = WaterPhysics.MODID, value = Side.CLIENT)
public final class ClientSetup {
    public static final ModelResourceLocation WATER_MRL = new ModelResourceLocation(WaterPhysics.MODID + ":water", "normal");
    public static KeyBinding CONFIG_KEY;

    private ClientSetup() {}

    public static void preInit() {
        MinecraftForge.EVENT_BUS.register(WaterEffects.class);
        MinecraftForge.EVENT_BUS.register(HydrometerHud.class);
        if (ClientAutoTest.enabled()) {
            ClientAutoTest.load();
            MinecraftForge.EVENT_BUS.register(ClientAutoTest.class);
        }
    }

    /** the in-game settings screen (same as Mods > Ultra Realistic Water Physics > Config) */
    public static void openConfig() {
        Minecraft mc = Minecraft.getMinecraft();
        mc.displayGuiScreen(new GuiConfig(mc.currentScreen, ConfigElement.from(com.waterphysics.config.WPConfig.class).getChildElements(),
                WaterPhysics.MODID, false, false, WaterPhysics.NAME));
    }

    public static void init() {
        Minecraft.getMinecraft().getBlockColors().registerBlockColorHandler((state, world, pos, tint) ->
                world != null && pos != null ? BiomeColorHelper.getWaterColorAtPos(world, pos) : 0x3F76E4, WaterPhysics.WATER_BLOCK);
        ClientRegistry.bindTileEntitySpecialRenderer(TilePipe.class, new PipeFillRenderer());
        CONFIG_KEY = new KeyBinding("key.waterphysics.config", Keyboard.KEY_O, "key.categories.waterphysics");
        ClientRegistry.registerKeyBinding(CONFIG_KEY);
    }

    @SubscribeEvent
    public static void onModels(ModelRegistryEvent e) {
        ModelLoader.setCustomStateMapper(WaterPhysics.WATER_BLOCK, new StateMapperBase() {
            @Override
            protected ModelResourceLocation getModelResourceLocation(net.minecraft.block.state.IBlockState state) {
                return WATER_MRL;
            }
        });
        for (Item i : ModRegistry.ITEMS) {
            ModelLoader.setCustomModelResourceLocation(i, 0, new ModelResourceLocation(i.getRegistryName(), "inventory"));
        }
    }

    @SubscribeEvent
    public static void onBake(ModelBakeEvent e) {
        e.getModelRegistry().putObject(WATER_MRL, new WaterBakedModel());
    }

    @SubscribeEvent
    public static void onKey(InputEvent.KeyInputEvent e) {
        if (CONFIG_KEY != null && CONFIG_KEY.isPressed()) openConfig();
    }

    static Block water() {
        return WaterPhysics.WATER_BLOCK;
    }

    static Class<?> valve() {
        return TileValve.class;
    }
}
