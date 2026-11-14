package com.realisticworld.forge;

import com.realisticworld.core.Engine;
import com.realisticworld.core.SelfTest;
import net.minecraft.command.CommandBase;
import net.minecraft.command.CommandException;
import net.minecraft.command.ICommandSender;
import net.minecraft.command.WrongUsageException;
import net.minecraft.entity.player.EntityPlayerMP;
import net.minecraft.server.MinecraftServer;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.text.TextComponentString;

import javax.annotation.Nullable;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;

/**
 * /rw [menu|status|selftest|gauge x y z|anchor add x z|anchor clear|anchor list]
 * menu and status for everyone; the rest for operators and the server console.
 */
public class CommandRw extends CommandBase {
    @Override
    public String getName() {
        return "rw";
    }

    @Override
    public String getUsage(ICommandSender sender) {
        return "/rw <menu|status|selftest|gauge <x> <y> <z>|anchor <add <x> <z>|clear|list>>";
    }

    @Override
    public int getRequiredPermissionLevel() {
        return 0;
    }

    @Override
    public boolean checkPermission(MinecraftServer server, ICommandSender sender) {
        return true;
    }

    @Override
    public void execute(MinecraftServer server, ICommandSender sender, String[] args) throws CommandException {
        String sub = args.length == 0 ? "menu" : args[0];
        switch (sub) {
            case "menu":
                if (!(sender instanceof EntityPlayerMP)) throw new CommandException("Only a player can open the control center.");
                RealisticWorldMod.openControlCenter((EntityPlayerMP) sender);
                break;
            case "status":
                for (String line : RealisticWorldMod.statusLine().split("\n")) sender.sendMessage(new TextComponentString(line));
                break;
            case "gauge": {
                requireOperator(sender);
                if (args.length < 4) throw new WrongUsageException("/rw gauge <x> <y> <z>");
                BlockPos base = sender.getPosition();
                BlockPos pos = new BlockPos(parseDouble(base.getX(), args[1], true), parseDouble(base.getY(), args[2], false), parseDouble(base.getZ(), args[3], true));
                RealisticWorldMod.gauge(line -> sender.sendMessage(new TextComponentString(line)), sender.getEntityWorld().provider.getDimension(), pos);
                break;
            }
            case "anchor": {
                requireOperator(sender);
                String what = args.length > 1 ? args[1] : "list";
                if (what.equals("add")) {
                    if (args.length < 4) throw new WrongUsageException("/rw anchor add <x> <z>");
                    BlockPos base = sender.getPosition();
                    int x = (int) Math.floor(parseDouble(base.getX(), args[2], false));
                    int z = (int) Math.floor(parseDouble(base.getZ(), args[3], false));
                    if (!RealisticWorldMod.addAnchor(sender.getEntityWorld().provider.getDimension(), x, z)) throw new CommandException("Could not add the anchor (limit 8).");
                    sender.sendMessage(new TextComponentString("Area around " + x + ", " + z + " is now simulated without players."));
                } else if (what.equals("clear")) {
                    RealisticWorldMod.clearAnchors();
                    sender.sendMessage(new TextComponentString("Anchors removed."));
                } else {
                    for (int[] a : RealisticWorldMod.anchors()) sender.sendMessage(new TextComponentString("anchor dim " + a[0] + " at " + a[1] + ", " + a[2]));
                    if (RealisticWorldMod.anchors().isEmpty()) sender.sendMessage(new TextComponentString("No anchors."));
                }
                break;
            }
            case "selftest": {
                requireOperator(sender);
                Engine e = RealisticWorldMod.engine();
                if (e == null) throw new CommandException("Realistic World is still starting.");
                List<SelfTest.Result> results = e.selfTest();
                int passed = 0;
                for (SelfTest.Result r : results) if (r.ok) passed++;
                sender.sendMessage(new TextComponentString("§b[Realistic World]§r self-test: " + passed + "/" + results.size() + " passed"));
                for (SelfTest.Result r : results) {
                    sender.sendMessage(new TextComponentString((r.ok ? "§aPASS" : "§cFAIL") + "§r " + r.name + " - " + r.detail));
                }
                break;
            }
            default:
                throw new WrongUsageException(getUsage(sender));
        }
    }

    private static void requireOperator(ICommandSender sender) throws CommandException {
        if (sender instanceof EntityPlayerMP && !PlayerPrefs.isOperator((EntityPlayerMP) sender)) {
            throw new CommandException("Only operators can do this.");
        }
    }

    @Override
    public List<String> getTabCompletions(MinecraftServer server, ICommandSender sender, String[] args, @Nullable BlockPos targetPos) {
        return args.length == 1 ? getListOfStringsMatchingLastWord(args, Arrays.asList("menu", "status", "selftest", "gauge", "anchor")) : Collections.emptyList();
    }
}
