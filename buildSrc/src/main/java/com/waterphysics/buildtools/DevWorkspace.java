package com.waterphysics.buildtools;

import java.io.*;
import java.nio.file.Files;
import java.util.*;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;
import java.util.zip.ZipOutputStream;

/**
 * Builds the MCP-named "forgeBin" style jar used as compile classpath:
 * vanilla client jar + Forge binpatches, deobfuscated notch -> SRG -> MCP, plus Forge's own
 * classes (also notch-named in 1.12.2) remapped the same way, with forge_at.cfg applied. Also writes the MCP -> SRG mapping used by
 * {@link Reobfuscator}.
 */
public final class DevWorkspace {
    private DevWorkspace() {}

    public static String build(File clientJar, File universalJar, File mcpZip, File workDir,
                               File devJar, File srgJar, File reobfSrg) throws IOException {
        StringBuilder log = new StringBuilder();
        if (!workDir.isDirectory() && !workDir.mkdirs()) throw new IOException("mkdir " + workDir);
        File patched = new File(workDir, "client-patched-obf.jar");
        int n = BinPatcher.apply(universalJar, "client", clientJar, patched);
        log.append("Applied ").append(n).append(" Forge binpatches to the client jar\n");

        SrgMappings maps = new SrgMappings();
        maps.loadNotchSrg(SrgMappings.readDeobfData(universalJar));
        maps.loadMcpCsv(mcpZip);
        log.append("Mappings: ").append(maps.classes.size()).append(" classes, ")
           .append(maps.obfMethods.size()).append(" methods, ").append(maps.obfFields.size())
           .append(" fields, ").append(maps.srgToMcp.size()).append(" MCP names\n");

        // forge_at.cfg (SRG member names -> MCP)
        AccessTransformer at = new AccessTransformer();
        try (ZipFile zf = new ZipFile(universalJar)) {
            ZipEntry e = zf.getEntry("forge_at.cfg");
            try (InputStream in = zf.getInputStream(e)) { at.load(in, maps.srgToMcp); }
        }

        // 1) patched vanilla: obf -> MCP
        MappingRemapper.Inheritance obfInh = new MappingRemapper.Inheritance();
        obfInh.addJar(patched);
        obfInh.addJar(universalJar);
        MappingRemapper.Data obfMcpData = maps.obfToMcp();
        obfMcpData.memberNames = maps.srgToMcp; // any SRG-named member left in Forge code -> MCP
        MappingRemapper obfToMcp = new MappingRemapper(obfMcpData, obfInh);
        // 2) Forge's own classes are, like vanilla, in the obfuscated (notch) namespace in 1.12.2:
        //    FML's DeobfuscationTransformer remaps everything to SRG at runtime.

        File tmp = new File(devJar.getPath() + ".tmp");
        Set<String> written = new HashSet<>();
        try (ZipOutputStream zos = new ZipOutputStream(new BufferedOutputStream(new FileOutputStream(tmp)))) {
            try (ZipFile zf = new ZipFile(patched)) {
                MappingRemapper.writeRemapped(zf, zos, obfToMcp, null,
                        res -> !res.startsWith("META-INF/"), at::transform, written);
            }
            try (ZipFile zf = new ZipFile(universalJar)) {
                MappingRemapper.writeRemapped(zf, zos, obfToMcp,
                        cls -> cls.indexOf('/') > 0, // root package classes are obf binpatch outputs, already present
                        res -> !res.startsWith("META-INF/") && !res.endsWith(".lzma"),
                        at::transform, written);
            }
        }
        Files.move(tmp.toPath(), devJar.toPath(), java.nio.file.StandardCopyOption.REPLACE_EXISTING);
        log.append("Wrote ").append(devJar.getName()).append(" (").append(written.size()).append(" entries)\n");
        // SRG-named jar (what the game really looks like at runtime) for link verification of the reobf mod
        MappingRemapper.Data obfToSrg = new MappingRemapper.Data();
        obfToSrg.classes.putAll(maps.classes);
        obfToSrg.methods.putAll(maps.obfMethods);
        obfToSrg.fields.putAll(maps.obfFields);
        MappingRemapper toSrg = new MappingRemapper(obfToSrg, obfInh);
        Set<String> written2 = new HashSet<>();
        try (ZipOutputStream zos = new ZipOutputStream(new BufferedOutputStream(new FileOutputStream(srgJar)))) {
            try (ZipFile zf = new ZipFile(patched)) {
                MappingRemapper.writeRemapped(zf, zos, toSrg, null, res -> false, null, written2);
            }
            try (ZipFile zf = new ZipFile(universalJar)) {
                MappingRemapper.writeRemapped(zf, zos, toSrg, cls -> cls.indexOf('/') > 0, res -> false, null, written2);
            }
        }
        log.append("Wrote ").append(srgJar.getName()).append(" (runtime SRG namespace, for link checks)\n");
        maps.writeMcpToSrg(reobfSrg);
        log.append("Wrote reobfuscation mapping ").append(reobfSrg.getName()).append('\n');
        if (!patched.delete()) patched.deleteOnExit();
        return log.toString();
    }
}
