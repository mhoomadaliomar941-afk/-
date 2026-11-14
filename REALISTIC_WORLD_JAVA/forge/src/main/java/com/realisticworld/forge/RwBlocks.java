package com.realisticworld.forge;

import net.minecraft.block.Block;
import net.minecraft.block.SoundType;
import net.minecraft.block.material.Material;
import net.minecraft.creativetab.CreativeTabs;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.entity.player.EntityPlayerMP;
import net.minecraft.item.Item;
import net.minecraft.item.ItemBlock;
import net.minecraft.item.ItemStack;
import net.minecraft.util.ActionResult;
import net.minecraft.util.EnumActionResult;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.EnumHand;
import net.minecraft.util.ResourceLocation;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;
import net.minecraftforge.event.RegistryEvent;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;

/** Blocks and items of the mod, registered through Forge's registry events. */
@Mod.EventBusSubscriber(modid = RealisticWorldMod.MODID)
public final class RwBlocks {
    public static final BlockValve VALVE = named(new BlockValve(), "valve");
    public static final Block PIPE = named(new Block(Material.IRON) {
        { setHardness(1.5F); setResistance(6F); setSoundType(SoundType.METAL); setCreativeTab(CreativeTabs.REDSTONE); }
    }, "pipe");
    public static final Block DRAIN = named(new Block(Material.IRON) {
        { setHardness(1.5F); setResistance(6F); setSoundType(SoundType.METAL); setCreativeTab(CreativeTabs.REDSTONE); }
    }, "drain");
    public static final BlockPuddle PUDDLE = named(new BlockPuddle(), "puddle");

    public static final Item CONTROL_CENTER = named(new Item() {
        { setMaxStackSize(1); setCreativeTab(CreativeTabs.TOOLS); }

        @Override
        public ActionResult<ItemStack> onItemRightClick(World world, EntityPlayer player, EnumHand hand) {
            if (!world.isRemote && player instanceof EntityPlayerMP) RealisticWorldMod.openControlCenter((EntityPlayerMP) player);
            return new ActionResult<>(EnumActionResult.SUCCESS, player.getHeldItem(hand));
        }
    }, "control_center");

    public static final Item WATER_GAUGE = named(new Item() {
        { setMaxStackSize(1); setCreativeTab(CreativeTabs.TOOLS); }

        @Override
        public EnumActionResult onItemUse(EntityPlayer player, World world, BlockPos pos, EnumHand hand, EnumFacing facing, float hx, float hy, float hz) {
            if (!world.isRemote && player instanceof EntityPlayerMP) RealisticWorldMod.gauge((EntityPlayerMP) player, world.provider.getDimension(), pos);
            return EnumActionResult.SUCCESS;
        }
    }, "water_gauge");

    private RwBlocks() {}

    private static <T extends Block> T named(T b, String name) {
        b.setRegistryName(new ResourceLocation(RealisticWorldMod.MODID, name));
        b.setUnlocalizedName(RealisticWorldMod.MODID + "." + name);
        return b;
    }

    private static <T extends Item> T named(T i, String name) {
        i.setRegistryName(new ResourceLocation(RealisticWorldMod.MODID, name));
        i.setUnlocalizedName(RealisticWorldMod.MODID + "." + name);
        return i;
    }

    static ItemBlock itemOf(Block b) {
        ItemBlock ib = new ItemBlock(b);
        ib.setRegistryName(b.getRegistryName());
        return ib;
    }

    @SubscribeEvent
    public static void registerBlocks(RegistryEvent.Register<Block> event) {
        event.getRegistry().registerAll(VALVE, PIPE, DRAIN, PUDDLE);
    }

    @SubscribeEvent
    public static void registerItems(RegistryEvent.Register<Item> event) {
        event.getRegistry().registerAll(itemOf(VALVE), itemOf(PIPE), itemOf(DRAIN), CONTROL_CENTER, WATER_GAUGE);
    }
}
