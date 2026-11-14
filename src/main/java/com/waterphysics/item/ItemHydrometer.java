package com.waterphysics.item;

import com.waterphysics.WaterPhysics;
import com.waterphysics.net.GaugeReader;
import net.minecraft.client.util.ITooltipFlag;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.item.Item;
import net.minecraft.item.ItemStack;
import net.minecraft.util.ActionResult;
import net.minecraft.util.EnumActionResult;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.EnumHand;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.RayTraceResult;
import net.minecraft.util.text.translation.I18n;
import net.minecraft.world.World;
import net.minecraftforge.fml.relauncher.Side;
import net.minecraftforge.fml.relauncher.SideOnly;

import javax.annotation.Nullable;
import java.util.List;

/**
 * Hydrometer: right click water, a tank, a pipe, pump, valve or spigot to read volume, level, pressure,
 * flow speed and tank capacity. While held, the readings are also shown on the HUD.
 */
public class ItemHydrometer extends Item {
    public ItemHydrometer() {
        setRegistryName(WaterPhysics.MODID, "hydrometer");
        setUnlocalizedName(WaterPhysics.MODID + ".hydrometer");
        setMaxStackSize(1);
    }

    @Override
    public ActionResult<ItemStack> onItemRightClick(World w, EntityPlayer player, EnumHand hand) {
        RayTraceResult r = rayTrace(w, player, true);
        if (r != null && r.typeOfHit == RayTraceResult.Type.BLOCK) {
            if (!w.isRemote) GaugeReader.toChat(w, r.getBlockPos(), player, true);
            return new ActionResult<>(EnumActionResult.SUCCESS, player.getHeldItem(hand));
        }
        return new ActionResult<>(EnumActionResult.PASS, player.getHeldItem(hand));
    }

    @Override
    public EnumActionResult onItemUse(EntityPlayer player, World w, BlockPos pos, EnumHand hand, EnumFacing side, float hx, float hy, float hz) {
        if (!w.isRemote) GaugeReader.toChat(w, pos, player, false);
        return EnumActionResult.SUCCESS;
    }

    @SideOnly(Side.CLIENT)
    @Override
    public void addInformation(ItemStack stack, @Nullable World world, List<String> tooltip, ITooltipFlag flag) {
        tooltip.add(I18n.translateToLocal("waterphysics.tooltip.hydrometer"));
    }
}
