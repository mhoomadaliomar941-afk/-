package com.waterphysics.item;

import com.waterphysics.WaterPhysics;
import com.waterphysics.pipe.PipeManager;
import com.waterphysics.tile.TilePipe;
import net.minecraft.client.util.ITooltipFlag;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.init.SoundEvents;
import net.minecraft.item.Item;
import net.minecraft.item.ItemStack;
import net.minecraft.tileentity.TileEntity;
import net.minecraft.util.EnumActionResult;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.EnumHand;
import net.minecraft.util.SoundCategory;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.text.TextComponentTranslation;
import net.minecraft.util.text.translation.I18n;
import net.minecraft.world.World;
import net.minecraftforge.fml.relauncher.Side;
import net.minecraftforge.fml.relauncher.SideOnly;

import javax.annotation.Nullable;
import java.util.List;

/** Pipe wrench: sneak + right click repairs a damaged pipe; right click on pumps/valves/spigots adjusts them. */
public class ItemPipeWrench extends Item {
    public ItemPipeWrench() {
        setRegistryName(WaterPhysics.MODID, "pipe_wrench");
        setUnlocalizedName(WaterPhysics.MODID + ".pipe_wrench");
        setMaxStackSize(1);
    }

    @Override
    public EnumActionResult onItemUseFirst(EntityPlayer player, World w, BlockPos pos, EnumFacing side, float hx, float hy, float hz, EnumHand hand) {
        TileEntity te = w.getTileEntity(pos);
        if (te instanceof TilePipe && player.isSneaking()) {
            if (!w.isRemote) {
                TilePipe p = (TilePipe) te;
                p.damage = 0f;
                p.syncIfChanged(true);
                PipeManager.get(w).markDirty();
                w.playSound(null, pos, SoundEvents.BLOCK_ANVIL_USE, SoundCategory.BLOCKS, 0.4f, 1.6f);
                player.sendStatusMessage(new TextComponentTranslation("waterphysics.msg.repaired"), true);
            }
            return EnumActionResult.SUCCESS;
        }
        return EnumActionResult.PASS;
    }

    @SideOnly(Side.CLIENT)
    @Override
    public void addInformation(ItemStack stack, @Nullable World world, List<String> tooltip, ITooltipFlag flag) {
        tooltip.add(I18n.translateToLocal("waterphysics.tooltip.wrench"));
    }
}
