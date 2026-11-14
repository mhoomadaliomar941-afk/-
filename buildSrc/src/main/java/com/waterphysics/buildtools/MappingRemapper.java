package com.waterphysics.buildtools;

import org.objectweb.asm.*;
import org.objectweb.asm.commons.ClassRemapper;
import org.objectweb.asm.commons.MethodRemapper;
import org.objectweb.asm.commons.Remapper;

import java.io.*;
import java.util.*;
import java.util.function.Function;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;
import java.util.zip.ZipOutputStream;

/**
 * ASM remapper with inheritance-aware member lookup (a reference to {@code Sub.foo()} resolves the
 * mapping declared on a superclass or interface), plus correct handling of lambda
 * (invokedynamic/LambdaMetafactory) method names whose functional interface is a mapped class.
 */
public class MappingRemapper extends Remapper {

    public static final class Data {
        public final Map<String, String> classes = new HashMap<>();
        /** "owner name desc" -> new name (owner/desc in source namespace) */
        public final Map<String, String> methods = new HashMap<>();
        /** "owner name" -> new name */
        public final Map<String, String> fields = new HashMap<>();
        /** optional name-only fallback (e.g. SRG -> MCP for func_/field_ names) */
        public Map<String, String> memberNames;
    }

    /** Supplies superclass + interfaces for a class in the SOURCE namespace (null when unknown). */
    public static final class Inheritance {
        private final Map<String, String[]> parents = new HashMap<>();

        public void addJar(File jar) throws IOException {
            try (ZipFile zf = new ZipFile(jar)) {
                Enumeration<? extends ZipEntry> en = zf.entries();
                while (en.hasMoreElements()) {
                    ZipEntry e = en.nextElement();
                    if (!e.getName().endsWith(".class")) continue;
                    try (InputStream in = zf.getInputStream(e)) {
                        addClass(BinPatcher.readAll(in));
                    }
                }
            }
        }

        public void addClass(byte[] bytes) {
            ClassReader cr = new ClassReader(bytes);
            String[] itf = cr.getInterfaces();
            String[] p = new String[itf.length + 1];
            p[0] = cr.getSuperName();
            System.arraycopy(itf, 0, p, 1, itf.length);
            parents.put(cr.getClassName(), p);
        }

        public String[] parents(String cls) {
            return parents.get(cls);
        }

        public boolean contains(String cls) {
            return parents.containsKey(cls);
        }
    }

    private final Data data;
    private final Inheritance inh;
    private final Map<String, String> methodCache = new HashMap<>();
    private final Map<String, String> fieldCache = new HashMap<>();

    public MappingRemapper(Data data, Inheritance inh) {
        this.data = data;
        this.inh = inh;
    }

    @Override
    public String map(String internalName) {
        String m = data.classes.get(internalName);
        if (m != null) return m;
        int d = internalName.lastIndexOf('$');
        if (d > 0) {
            String outer = map(internalName.substring(0, d));
            if (outer != null && !outer.equals(internalName.substring(0, d))) {
                return outer + internalName.substring(d);
            }
        }
        return internalName;
    }

    @Override
    public String mapMethodName(String owner, String name, String descriptor) {
        if (name.startsWith("<") || owner.startsWith("[")) return name;
        String key = owner + " " + name + " " + descriptor;
        String cached = methodCache.get(key);
        if (cached != null) return cached;
        String r = findMethod(owner, name, descriptor, new HashSet<String>());
        if (r == null) r = fallback(name);
        methodCache.put(key, r);
        return r;
    }

    private String findMethod(String owner, String name, String desc, Set<String> seen) {
        if (owner == null || !seen.add(owner)) return null;
        String m = data.methods.get(owner + " " + name + " " + desc);
        if (m != null) return m;
        String[] ps = inh.parents(owner);
        if (ps == null) return null;
        for (String p : ps) {
            String r = findMethod(p, name, desc, seen);
            if (r != null) return r;
        }
        return null;
    }

    @Override
    public String mapFieldName(String owner, String name, String descriptor) {
        String key = owner + " " + name;
        String cached = fieldCache.get(key);
        if (cached != null) return cached;
        String r = findField(owner, name, new HashSet<String>());
        if (r == null) r = fallback(name);
        fieldCache.put(key, r);
        return r;
    }

    private String findField(String owner, String name, Set<String> seen) {
        if (owner == null || !seen.add(owner)) return null;
        String m = data.fields.get(owner + " " + name);
        if (m != null) return m;
        String[] ps = inh.parents(owner);
        if (ps == null) return null;
        for (String p : ps) {
            String r = findField(p, name, seen);
            if (r != null) return r;
        }
        return null;
    }

    private String fallback(String name) {
        if (data.memberNames != null) {
            String m = data.memberNames.get(name);
            if (m != null) return m;
        }
        return name;
    }

    /** ClassRemapper that fixes the SAM method name of lambdas implementing mapped interfaces. */
    public static ClassVisitor visitor(ClassVisitor cv, MappingRemapper remapper) {
        return new ClassRemapper(Opcodes.ASM9, cv, remapper) {
            @Override
            protected MethodVisitor createMethodRemapper(MethodVisitor mv) {
                return new MethodRemapper(Opcodes.ASM9, mv, remapper) {
                    @Override
                    public void visitInvokeDynamicInsn(String name, String descriptor, Handle bsm, Object... args) {
                        if ("java/lang/invoke/LambdaMetafactory".equals(bsm.getOwner()) && args.length > 0 && args[0] instanceof Type) {
                            String itf = Type.getReturnType(descriptor).getInternalName();
                            name = remapper.mapMethodName(itf, name, ((Type) args[0]).getDescriptor());
                        }
                        super.visitInvokeDynamicInsn(name, descriptor, bsm, args);
                    }
                };
            }
        };
    }

    /**
     * Remaps every class of {@code in} into {@code out}. Resources are copied unless
     * {@code resourceFilter} rejects them. {@code classFilter} receives the source class name.
     */
    public static void remapJar(File in, File out, MappingRemapper remapper,
                                Function<String, Boolean> classFilter,
                                Function<String, Boolean> resourceFilter,
                                Function<byte[], byte[]> postProcess) throws IOException {
        try (ZipFile zf = new ZipFile(in);
             ZipOutputStream zos = new ZipOutputStream(new BufferedOutputStream(new FileOutputStream(out)))) {
            writeRemapped(zf, zos, remapper, classFilter, resourceFilter, postProcess, new HashSet<String>());
        }
    }

    public static void writeRemapped(ZipFile zf, ZipOutputStream zos, MappingRemapper remapper,
                                     Function<String, Boolean> classFilter,
                                     Function<String, Boolean> resourceFilter,
                                     Function<byte[], byte[]> postProcess,
                                     Set<String> written) throws IOException {
        Enumeration<? extends ZipEntry> en = zf.entries();
        while (en.hasMoreElements()) {
            ZipEntry e = en.nextElement();
            if (e.isDirectory()) continue;
            String n = e.getName();
            byte[] data;
            try (InputStream is = zf.getInputStream(e)) { data = BinPatcher.readAll(is); }
            String outName = n;
            if (n.endsWith(".class")) {
                String cls = n.substring(0, n.length() - 6);
                if (classFilter != null && !classFilter.apply(cls)) continue;
                ClassReader cr = new ClassReader(data);
                ClassWriter cw = new ClassWriter(0);
                cr.accept(visitor(cw, remapper), 0);
                data = cw.toByteArray();
                if (postProcess != null) data = postProcess.apply(data);
                outName = remapper.map(cls) + ".class";
            } else if (resourceFilter != null && !resourceFilter.apply(n)) {
                continue;
            }
            if (!written.add(outName)) continue;
            ZipEntry ne = new ZipEntry(outName);
            ne.setTime(0L);
            zos.putNextEntry(ne);
            zos.write(data);
            zos.closeEntry();
        }
    }
}
