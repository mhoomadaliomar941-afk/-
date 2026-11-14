package com.waterphysics;

import com.waterphysics.block.*;
import com.waterphysics.item.ItemHydrometer;
import com.waterphysics.item.ItemPipeWrench;
import com.waterphysics.sim.CellType;
import com.waterphysics.sim.WaterManager;
import com.waterphysics.sim.WaterSim;
import com.waterphysics.tile.*;
import net.minecraft.block.Block;
import net.minecraft.block.BlockDispenser;
import net.minecraft.block.state.IBlockState;
import net.minecraft.creativetab.CreativeTabs;
import net.minecraft.dispenser.BehaviorDefaultDispenseItem;
import net.minecraft.dispenser.IBehaviorDispenseItem;
import net.minecraft.dispenser.IBlockSource;
import net.minecraft.init.Items;
import net.minecraft.item.Item;
import net.minecraft.item.ItemBlock;
import net.minecraft.item.ItemStack;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.ResourceLocation;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;
import net.minecraftforge.event.RegistryEvent;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;
import net.minecraftforge.fml.common.registry.GameRegistry;

import java.util.ArrayList;
import java.util.List;

/** Blocks, items, tile entities and dispenser behaviours. */
@Mod.EventBusSubscriber(modid = WaterPhysics.MODID)
public final class ModRegistry {
    private ModRegistry() {}

    public static final CreativeTabs TAB = new CreativeTabs(WaterPhysics.MODID) {
        @Override
        public ItemStack getTabIconItem() {
            return new ItemStack(HYDROMETER);
        }
    };

    public static final List<Block> BLOCKS = new ArrayList<>();
    public static final List<Item> ITEMS = new ArrayList<>();

    public static Block TANK_WALL, TANK_GLASS;
    public static BlockSpigot SPIGOT;
    public static BlockPipe PIPE_SMALL, PIPE_MEDIUM, PIPE_LARGE, PIPE_GLASS;
    public static BlockPump PUMP;
    public static BlockValve VALVE;
    public static BlockTankGauge GAUGE;
    public static Item HYDROMETER, WRENCH;

    @SubscribeEvent
    public static void registerBlocks(RegistryEvent.Register<Block> e) {
        WaterPhysics.WATER_BLOCK = new BlockPhysWater(WaterPhysics.FLUID);
        e.getRegistry().register(WaterPhysics.WATER_BLOCK);

        TANK_WALL = add(e, new BlockTankWall("tank_wall", false));
        TANK_GLASS = add(e, new BlockTankWall("tank_glass", true));
        SPIGOT = add(e, new BlockSpigot());
        PIPE_SMALL = add(e, new BlockPipe("pipe_small", 0.10f, 1000f, false));
        PIPE_MEDIUM = add(e, new BlockPipe("pipe_medium", 0.25f, 800f, false));
        PIPE_LARGE = add(e, new BlockPipe("pipe_large", 0.50f, 600f, false));
        PIPE_GLASS = add(e, new BlockPipe("pipe_glass", 0.25f, 60f, true));
        PUMP = add(e, new BlockPump());
        VALVE = add(e, new BlockValve());
        GAUGE = add(e, new BlockTankGauge());

        GameRegistry.registerTileEntity(TileSpigot.class, new ResourceLocation(WaterPhysics.MODID, "spigot"));
        GameRegistry.registerTileEntity(TilePipe.class, new ResourceLocation(WaterPhysics.MODID, "pipe"));
        GameRegistry.registerTileEntity(TilePump.class, new ResourceLocation(WaterPhysics.MODID, "pump"));
        GameRegistry.registerTileEntity(TileValve.class, new ResourceLocation(WaterPhysics.MODID, "valve"));
    }

    private static <T extends Block> T add(RegistryEvent.Register<Block> e, T b) {
        b.setCreativeTab(TAB);
        e.getRegistry().register(b);
        BLOCKS.add(b);
        return b;
    }

    @SubscribeEvent
    public static void registerItems(RegistryEvent.Register<Item> e) {
        for (Block b : BLOCKS) {
            Item i = new ItemBlock(b).setRegistryName(b.getRegistryName());
            e.getRegistry().register(i);
            ITEMS.add(i);
        }
        HYDROMETER = new ItemHydrometer();
        WRENCH = new ItemPipeWrench();
        for (Item i : new Item[]{HYDROMETER, WRENCH}) {
            i.setCreativeTab(TAB);
            e.getRegistry().register(i);
            ITEMS.add(i);
        }
    }

    /** Dispensers move exactly 1000 L like buckets used by players. */
    public static void registerDispenserBehaviors() {
        final IBehaviorDispenseItem vanillaWater = BlockDispenser.DISPENSE_BEHAVIOR_REGISTRY.getObject(Items.WATER_BUCKET);
        final IBehaviorDispenseItem vanillaEmpty = BlockDispenser.DISPENSE_BEHAVIOR_REGISTRY.getObject(Items.BUCKET);
        BlockDispenser.DISPENSE_BEHAVIOR_REGISTRY.putObject(Items.WATER_BUCKET, new BehaviorDefaultDispenseItem() {
            @Override
            protected ItemStack dispenseStack(IBlockSource src, ItemStack stack) {
                if (!com.waterphysics.config.WPConfig.gameplay.physicalBuckets) return vanillaWater.dispense(src, stack);
                World w = src.getWorld();
                BlockPos p = src.getBlockPos().offset(src.getBlockState().getValue(BlockDispenser.FACING));
                if (WaterManager.pour(w, p, 1.0f)) return new ItemStack(Items.BUCKET);
                return super.dispenseStack(src, stack);
            }
        });
        BlockDispenser.DISPENSE_BEHAVIOR_REGISTRY.putObject(Items.BUCKET, new BehaviorDefaultDispenseItem() {
            @Override
            protected ItemStack dispenseStack(IBlockSource src, ItemStack stack) {
                World w = src.getWorld();
                EnumFacing f = src.getBlockState().getValue(BlockDispenser.FACING);
                BlockPos p = src.getBlockPos().offset(f);
                IBlockState st = w.getBlockState(p);
                if (st.getBlock() != WaterPhysics.WATER_BLOCK || !com.waterphysics.config.WPConfig.gameplay.physicalBuckets) {
                    return vanillaEmpty.dispense(src, stack);
                }
                WaterSim s = WaterManager.get(w);
                if (s == null || !s.drainExact(p, 1.0f, 512)) return stack;
                s.stats.bucketsOut += 1.0;
                stack.shrink(1);
                ItemStack full = new ItemStack(Items.WATER_BUCKET);
                if (stack.isEmpty()) return full;
                if (src.<net.minecraft.tileentity.TileEntityDispenser>getBlockTileEntity().addItemStack(full) < 0) {
                    super.dispense(src, full);
                }
                return stack;
            }
        });
    }

    public static boolean isOpen(World w, BlockPos p) {
        return CellType.of(w.getBlockState(p)) == CellType.OPEN;
    }
}
