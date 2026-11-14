package com.waterphysics.buildtools;

import com.nothome.delta.GDiffPatcher;
import org.apache.commons.compress.java.util.jar.Pack200;
import org.tukaani.xz.LZMAInputStream;

import java.io.*;
import java.util.*;
import java.util.jar.JarOutputStream;
import java.util.zip.Adler32;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;
import java.util.zip.ZipInputStream;
import java.util.zip.ZipOutputStream;

/**
 * Applies Forge's binary class patches (binpatches.pack.lzma inside the Forge universal jar) to a
 * vanilla (obfuscated) Minecraft jar. This is the same operation Forge performs at runtime in
 * ClassPatchManager, done ahead of time so that the patched classes can be deobfuscated and used
 * as a compile classpath.
 */
public final class BinPatcher {
    private BinPatcher() {}

    static final class Patch {
        String name, source, target;
        boolean exists;
        int checksum;
        byte[] data;
    }

    public static Map<String, Patch> readPatches(File universalJar, String side) throws IOException {
        byte[] packed;
        try (ZipFile zf = new ZipFile(universalJar)) {
            ZipEntry e = zf.getEntry("binpatches.pack.lzma");
            if (e == null) throw new IOException("binpatches.pack.lzma not found in " + universalJar);
            try (InputStream in = new LZMAInputStream(new BufferedInputStream(zf.getInputStream(e)))) {
                packed = readAll(in);
            }
        }
        ByteArrayOutputStream jarBytes = new ByteArrayOutputStream();
        try (JarOutputStream jos = new JarOutputStream(jarBytes)) {
            Pack200.newUnpacker().unpack(new ByteArrayInputStream(packed), jos);
        }
        Map<String, Patch> patches = new HashMap<>();
        String prefix = "binpatch/" + side + "/";
        try (ZipInputStream zis = new ZipInputStream(new ByteArrayInputStream(jarBytes.toByteArray()))) {
            ZipEntry e;
            while ((e = zis.getNextEntry()) != null) {
                if (!e.getName().startsWith(prefix) || !e.getName().endsWith(".binpatch")) continue;
                DataInputStream in = new DataInputStream(new ByteArrayInputStream(readAll(zis)));
                Patch p = new Patch();
                p.name = in.readUTF();
                p.source = in.readUTF();
                p.target = in.readUTF();
                p.exists = in.readBoolean();
                if (p.exists) p.checksum = in.readInt();
                int len = in.readInt();
                p.data = new byte[len];
                in.readFully(p.data);
                patches.put(p.source.replace('.', '/'), p);
            }
        }
        return patches;
    }

    /** @return number of patched classes */
    public static int apply(File universalJar, String side, File inJar, File outJar) throws IOException {
        Map<String, Patch> patches = readPatches(universalJar, side);
        Set<String> applied = new HashSet<>();
        GDiffPatcher patcher = new GDiffPatcher();
        try (ZipFile in = new ZipFile(inJar);
             ZipOutputStream out = new ZipOutputStream(new BufferedOutputStream(new FileOutputStream(outJar)))) {
            Enumeration<? extends ZipEntry> en = in.entries();
            while (en.hasMoreElements()) {
                ZipEntry e = en.nextElement();
                String n = e.getName();
                if (e.isDirectory()) continue;
                if (n.startsWith("META-INF/") && (n.endsWith(".SF") || n.endsWith(".RSA") || n.endsWith(".DSA") || n.equals("META-INF/MANIFEST.MF"))) {
                    continue; // signatures are invalidated by patching
                }
                byte[] data;
                try (InputStream is = in.getInputStream(e)) { data = readAll(is); }
                if (n.endsWith(".class")) {
                    String cls = n.substring(0, n.length() - 6);
                    Patch p = patches.get(cls);
                    if (p != null) {
                        if (p.exists) {
                            Adler32 a = new Adler32();
                            a.update(data);
                            if ((int) a.getValue() != p.checksum) {
                                throw new IOException("Binpatch checksum mismatch for " + cls + " (wrong Minecraft jar?)");
                            }
                        }
                        data = patcher.patch(data, p.data);
                        applied.add(cls);
                    }
                }
                ZipEntry ne = new ZipEntry(n);
                ne.setTime(0L);
                out.putNextEntry(ne);
                out.write(data);
                out.closeEntry();
            }
            // classes that Forge adds (exists == false)
            for (Map.Entry<String, Patch> me : patches.entrySet()) {
                Patch p = me.getValue();
                if (applied.contains(me.getKey())) continue;
                if (p.exists) throw new IOException("Patch target missing in jar: " + me.getKey());
                byte[] data = patcher.patch(new byte[0], p.data);
                ZipEntry ne = new ZipEntry(me.getKey() + ".class");
                ne.setTime(0L);
                out.putNextEntry(ne);
                out.write(data);
                out.closeEntry();
                applied.add(me.getKey());
            }
        }
        return applied.size();
    }

    static byte[] readAll(InputStream in) throws IOException {
        ByteArrayOutputStream bos = new ByteArrayOutputStream();
        byte[] buf = new byte[65536];
        int n;
        while ((n = in.read(buf)) > 0) bos.write(buf, 0, n);
        return bos.toByteArray();
    }
}
