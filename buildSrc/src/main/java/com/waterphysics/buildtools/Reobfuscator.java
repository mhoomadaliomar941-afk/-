package com.waterphysics.buildtools;

import org.objectweb.asm.*;

import java.io.*;
import java.util.*;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;
import java.util.zip.ZipOutputStream;

/**
 * Reobfuscates the compiled (MCP-named) mod jar to SRG names, which is what Forge 1.12.2 uses at
 * runtime, then verifies that every reference to a Minecraft/Forge member resolves in the runtime
 * (SRG) namespace. A failed check aborts the build instead of producing a jar that crashes in game.
 */
public final class Reobfuscator {
    private Reobfuscator() {}

    public static String reobf(File devModJar, File devJar, File reobfSrg, File srgJar, File outJar) throws IOException {
        MappingRemapper.Data data = SrgMappings.readSrgFile(reobfSrg);
        MappingRemapper.Inheritance inh = new MappingRemapper.Inheritance();
        inh.addJar(devJar);
        inh.addJar(devModJar);
        MappingRemapper remapper = new MappingRemapper(data, inh);
        File tmp = new File(outJar.getPath() + ".tmp");
        try (ZipFile zf = new ZipFile(devModJar);
             ZipOutputStream zos = new ZipOutputStream(new BufferedOutputStream(new FileOutputStream(tmp)))) {
            MappingRemapper.writeRemapped(zf, zos, remapper, null, null, null, new HashSet<String>());
        }
        String report = verify(tmp, srgJar);
        java.nio.file.Files.move(tmp.toPath(), outJar.toPath(), java.nio.file.StandardCopyOption.REPLACE_EXISTING);
        return report;
    }

    private static final class ClassInfo {
        String superName;
        String[] interfaces;
        Set<String> methods = new HashSet<>();
        Set<String> fields = new HashSet<>();
    }

    private static void index(File jar, Map<String, ClassInfo> into) throws IOException {
        try (ZipFile zf = new ZipFile(jar)) {
            Enumeration<? extends ZipEntry> en = zf.entries();
            while (en.hasMoreElements()) {
                ZipEntry e = en.nextElement();
                if (!e.getName().endsWith(".class")) continue;
                byte[] b;
                try (InputStream in = zf.getInputStream(e)) { b = BinPatcher.readAll(in); }
                final ClassInfo ci = new ClassInfo();
                ClassReader cr = new ClassReader(b);
                ci.superName = cr.getSuperName();
                ci.interfaces = cr.getInterfaces();
                cr.accept(new ClassVisitor(Opcodes.ASM9) {
                    @Override
                    public FieldVisitor visitField(int access, String name, String desc, String sig, Object v) {
                        ci.fields.add(name);
                        return null;
                    }

                    @Override
                    public MethodVisitor visitMethod(int access, String name, String desc, String sig, String[] ex) {
                        ci.methods.add(name + desc);
                        return null;
                    }
                }, ClassReader.SKIP_CODE);
                into.put(cr.getClassName(), ci);
            }
        }
    }

    /** @return 0 = found, 1 = not found (all ancestors known), 2 = unknown ancestor (library/JDK) */
    private static int resolve(Map<String, ClassInfo> classes, String owner, String member, boolean method, Set<String> seen) {
        if (owner == null || !seen.add(owner)) return 1;
        ClassInfo ci = classes.get(owner);
        if (ci == null) return 2;
        if (method ? ci.methods.contains(member) : ci.fields.contains(member)) return 0;
        int best = 1;
        List<String> ps = new ArrayList<>();
        ps.add(ci.superName);
        ps.addAll(Arrays.asList(ci.interfaces));
        for (String p : ps) {
            int r = resolve(classes, p, member, method, seen);
            if (r == 0) return 0;
            if (r == 2) best = 2;
        }
        return best;
    }

    public static String verify(File modJar, File srgJar) throws IOException {
        final Map<String, ClassInfo> classes = new HashMap<>();
        index(srgJar, classes);
        index(modJar, classes);
        final List<String> problems = new ArrayList<>();
        final int[] checked = {0};
        try (ZipFile zf = new ZipFile(modJar)) {
            Enumeration<? extends ZipEntry> en = zf.entries();
            while (en.hasMoreElements()) {
                ZipEntry e = en.nextElement();
                if (!e.getName().endsWith(".class")) continue;
                byte[] b;
                try (InputStream in = zf.getInputStream(e)) { b = BinPatcher.readAll(in); }
                final String cls = e.getName();
                new ClassReader(b).accept(new ClassVisitor(Opcodes.ASM9) {
                    @Override
                    public MethodVisitor visitMethod(int access, String name, String desc, String sig, String[] ex) {
                        return new MethodVisitor(Opcodes.ASM9) {
                            void check(String owner, String member, boolean method) {
                                if (!(owner.startsWith("net/minecraft/") || owner.startsWith("net/minecraftforge/"))
                                        && !classes.containsKey(owner)) return;
                                if (owner.startsWith("[")) return;
                                checked[0]++;
                                int r = resolve(classes, owner, member, method, new HashSet<String>());
                                if (r == 1) problems.add(cls + ": unresolved " + (method ? "method " : "field ") + owner + "." + member);
                            }

                            @Override
                            public void visitMethodInsn(int op, String owner, String n, String d, boolean itf) {
                                check(owner, n + d, true);
                            }

                            @Override
                            public void visitFieldInsn(int op, String owner, String n, String d) {
                                check(owner, n, false);
                            }
                        };
                    }
                }, 0);
            }
        }
        if (!problems.isEmpty()) {
            StringBuilder sb = new StringBuilder("Reobfuscated jar has unresolved references:\n");
            for (String p : problems) sb.append("  ").append(p).append('\n');
            throw new IOException(sb.toString());
        }
        return "Reobfuscation verified: " + checked[0] + " Minecraft/Forge member references resolve in the SRG runtime namespace";
    }
}
