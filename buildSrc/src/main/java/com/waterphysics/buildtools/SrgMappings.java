package com.waterphysics.buildtools;

import org.tukaani.xz.LZMAInputStream;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

/**
 * Minecraft 1.12.2 name mappings.
 * <ul>
 *   <li>notch -> SRG: from Forge's deobfuscation_data-1.12.2.lzma (same data FML uses at runtime)</li>
 *   <li>SRG -> MCP: from an MCP mappings export (fields.csv / methods.csv), e.g. snapshot_20171003</li>
 * </ul>
 * From these it derives the obf->MCP mapping (to build the deobfuscated dev jar) and the owner-aware
 * MCP->SRG mapping used to reobfuscate the compiled mod, exactly like ForgeGradle's mcp-srg.srg.
 */
public final class SrgMappings {
    /** obf class -> srg class */
    public final Map<String, String> classes = new HashMap<>();
    /** "obfOwner obfName obfDesc" -> srg method name */
    public final Map<String, String> obfMethods = new HashMap<>();
    /** "obfOwner obfName" -> srg field name */
    public final Map<String, String> obfFields = new HashMap<>();
    /** srg member name (func_/field_) -> MCP name */
    public final Map<String, String> srgToMcp = new HashMap<>();
    /** srg method entries: srgOwner, srgName, srgDesc */
    public final List<String[]> srgMethods = new ArrayList<>();
    /** srg field entries: srgOwner, srgName */
    public final List<String[]> srgFields = new ArrayList<>();

    public static List<String> readDeobfData(File universalJar) throws IOException {
        try (ZipFile zf = new ZipFile(universalJar)) {
            ZipEntry e = zf.getEntry("deobfuscation_data-1.12.2.lzma");
            if (e == null) throw new IOException("deobfuscation_data-1.12.2.lzma missing from " + universalJar);
            try (BufferedReader r = new BufferedReader(new InputStreamReader(
                    new LZMAInputStream(new BufferedInputStream(zf.getInputStream(e))), StandardCharsets.UTF_8))) {
                List<String> lines = new ArrayList<>();
                String l;
                while ((l = r.readLine()) != null) lines.add(l);
                return lines;
            }
        }
    }

    public void loadNotchSrg(List<String> lines) {
        for (String line : lines) {
            String[] p = line.trim().split(" ");
            switch (p[0]) {
                case "CL:":
                    classes.put(p[1], p[2]);
                    break;
                case "FD:": {
                    int a = p[1].lastIndexOf('/'), b = p[2].lastIndexOf('/');
                    obfFields.put(p[1].substring(0, a) + " " + p[1].substring(a + 1), p[2].substring(b + 1));
                    srgFields.add(new String[]{p[2].substring(0, b), p[2].substring(b + 1)});
                    break;
                }
                case "MD:": {
                    int a = p[1].lastIndexOf('/'), b = p[3].lastIndexOf('/');
                    obfMethods.put(p[1].substring(0, a) + " " + p[1].substring(a + 1) + " " + p[2], p[3].substring(b + 1));
                    srgMethods.add(new String[]{p[3].substring(0, b), p[3].substring(b + 1), p[4]});
                    break;
                }
                default:
                    break;
            }
        }
    }

    public void loadMcpCsv(File mcpZip) throws IOException {
        try (ZipFile zf = new ZipFile(mcpZip)) {
            for (String csv : new String[]{"fields.csv", "methods.csv"}) {
                ZipEntry e = zf.getEntry(csv);
                if (e == null) throw new IOException(csv + " missing from " + mcpZip);
                try (BufferedReader r = new BufferedReader(new InputStreamReader(zf.getInputStream(e), StandardCharsets.UTF_8))) {
                    String l = r.readLine(); // header: searge,name,side,desc
                    while ((l = r.readLine()) != null) {
                        int c1 = l.indexOf(','), c2 = l.indexOf(',', c1 + 1);
                        if (c1 < 0 || c2 < 0) continue;
                        srgToMcp.put(l.substring(0, c1), l.substring(c1 + 1, c2));
                    }
                }
            }
        }
    }

    public String mcp(String srgName) {
        String m = srgToMcp.get(srgName);
        return m == null ? srgName : m;
    }

    /** obf -> MCP (class names to SRG/MCP class names, members to MCP names). */
    public MappingRemapper.Data obfToMcp() {
        MappingRemapper.Data d = new MappingRemapper.Data();
        d.classes.putAll(classes);
        for (Map.Entry<String, String> e : obfMethods.entrySet()) d.methods.put(e.getKey(), mcp(e.getValue()));
        for (Map.Entry<String, String> e : obfFields.entrySet()) d.fields.put(e.getKey(), mcp(e.getValue()));
        return d;
    }

    /** MCP -> SRG, owner aware (class names are identical in both namespaces). */
    public MappingRemapper.Data mcpToSrg() {
        MappingRemapper.Data d = new MappingRemapper.Data();
        for (String[] m : srgMethods) {
            String mcpName = mcp(m[1]);
            if (!mcpName.equals(m[1])) d.methods.put(m[0] + " " + mcpName + " " + m[2], m[1]);
        }
        for (String[] f : srgFields) {
            String mcpName = mcp(f[1]);
            if (!mcpName.equals(f[1])) d.fields.put(f[0] + " " + mcpName, f[1]);
        }
        return d;
    }

    public void writeMcpToSrg(File out) throws IOException {
        MappingRemapper.Data d = mcpToSrg();
        try (PrintWriter w = new PrintWriter(new OutputStreamWriter(new FileOutputStream(out), StandardCharsets.UTF_8))) {
            List<String> lines = new ArrayList<>();
            for (Map.Entry<String, String> e : d.methods.entrySet()) {
                String[] k = e.getKey().split(" ");
                lines.add("MD: " + k[0] + "/" + k[1] + " " + k[2] + " " + k[0] + "/" + e.getValue() + " " + k[2]);
            }
            for (Map.Entry<String, String> e : d.fields.entrySet()) {
                String[] k = e.getKey().split(" ");
                lines.add("FD: " + k[0] + "/" + k[1] + " " + k[0] + "/" + e.getValue());
            }
            Collections.sort(lines);
            for (String l : lines) w.println(l);
        }
    }

    /** Reads a file produced by {@link #writeMcpToSrg(File)}. */
    public static MappingRemapper.Data readSrgFile(File f) throws IOException {
        MappingRemapper.Data d = new MappingRemapper.Data();
        try (BufferedReader r = new BufferedReader(new InputStreamReader(new FileInputStream(f), StandardCharsets.UTF_8))) {
            String l;
            while ((l = r.readLine()) != null) {
                String[] p = l.split(" ");
                if (p[0].equals("MD:")) {
                    int a = p[1].lastIndexOf('/'), b = p[3].lastIndexOf('/');
                    d.methods.put(p[1].substring(0, a) + " " + p[1].substring(a + 1) + " " + p[2], p[3].substring(b + 1));
                } else if (p[0].equals("FD:")) {
                    int a = p[1].lastIndexOf('/'), b = p[2].lastIndexOf('/');
                    d.fields.put(p[1].substring(0, a) + " " + p[1].substring(a + 1), p[2].substring(b + 1));
                }
            }
        }
        return d;
    }
}
