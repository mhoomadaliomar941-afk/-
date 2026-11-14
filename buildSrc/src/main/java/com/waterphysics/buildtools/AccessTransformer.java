package com.waterphysics.buildtools;

import org.objectweb.asm.*;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.*;

/**
 * Minimal implementation of FML access transformer files (forge_at.cfg). Applied to the dev jar so
 * that the compile classpath has the same member visibility that Forge creates at runtime.
 */
public final class AccessTransformer {
    static final class Mod {
        int access; // Opcodes.ACC_PUBLIC / PROTECTED / PRIVATE / 0 (package)
        boolean changeFinal, setFinal;
    }

    /** class(internal) -> member key ("name" for fields, "name+desc" for methods, "*", "*()", "") -> modifier */
    private final Map<String, Map<String, Mod>> mods = new HashMap<>();

    public void load(InputStream in, Map<String, String> memberRename) throws IOException {
        BufferedReader r = new BufferedReader(new InputStreamReader(in, StandardCharsets.UTF_8));
        String line;
        while ((line = r.readLine()) != null) {
            int h = line.indexOf('#');
            if (h >= 0) line = line.substring(0, h);
            line = line.trim();
            if (line.isEmpty()) continue;
            String[] p = line.split("\\s+");
            Mod m = new Mod();
            String acc = p[0];
            if (acc.endsWith("-f")) { m.changeFinal = true; m.setFinal = false; acc = acc.substring(0, acc.length() - 2); }
            else if (acc.endsWith("+f")) { m.changeFinal = true; m.setFinal = true; acc = acc.substring(0, acc.length() - 2); }
            switch (acc) {
                case "public": m.access = Opcodes.ACC_PUBLIC; break;
                case "protected": m.access = Opcodes.ACC_PROTECTED; break;
                case "private": m.access = Opcodes.ACC_PRIVATE; break;
                default: m.access = 0; break;
            }
            String cls = p[1].replace('.', '/');
            String key = "";
            if (p.length > 2) {
                String mem = p[2];
                int paren = mem.indexOf('(');
                if (paren > 0) {
                    String name = mem.substring(0, paren);
                    String desc = mem.substring(paren).replace('.', '/');
                    if (memberRename != null && memberRename.containsKey(name)) name = memberRename.get(name);
                    key = name.equals("*") ? "*()" : name + desc;
                } else {
                    String name = mem;
                    if (memberRename != null && memberRename.containsKey(name)) name = memberRename.get(name);
                    key = name;
                }
            }
            mods.computeIfAbsent(cls, k -> new HashMap<>()).put(key, m);
        }
    }

    private static int apply(int access, Mod m) {
        int vis = Opcodes.ACC_PUBLIC | Opcodes.ACC_PROTECTED | Opcodes.ACC_PRIVATE;
        int cur = access & vis;
        int target = cur;
        if (m.access == Opcodes.ACC_PUBLIC) target = Opcodes.ACC_PUBLIC;
        else if (m.access == Opcodes.ACC_PROTECTED && cur != Opcodes.ACC_PUBLIC) target = Opcodes.ACC_PROTECTED;
        else if (m.access == 0 && cur == Opcodes.ACC_PRIVATE) target = 0;
        int r = (access & ~vis) | target;
        if (m.changeFinal) r = m.setFinal ? (r | Opcodes.ACC_FINAL) : (r & ~Opcodes.ACC_FINAL);
        return r;
    }

    public byte[] transform(byte[] bytes) {
        ClassReader cr = new ClassReader(bytes);
        final Map<String, Mod> cm = mods.get(cr.getClassName());
        if (cm == null) return bytes;
        ClassWriter cw = new ClassWriter(0);
        cr.accept(new ClassVisitor(Opcodes.ASM9, cw) {
            @Override
            public void visit(int version, int access, String name, String signature, String superName, String[] interfaces) {
                Mod m = cm.get("");
                super.visit(version, m != null ? apply(access, m) : access, name, signature, superName, interfaces);
            }

            @Override
            public FieldVisitor visitField(int access, String name, String descriptor, String signature, Object value) {
                Mod m = cm.get(name);
                if (m == null) m = cm.get("*");
                return super.visitField(m != null ? apply(access, m) : access, name, descriptor, signature, value);
            }

            @Override
            public MethodVisitor visitMethod(int access, String name, String descriptor, String signature, String[] exceptions) {
                Mod m = cm.get(name + descriptor);
                if (m == null && !name.startsWith("<")) m = cm.get("*()");
                return super.visitMethod(m != null ? apply(access, m) : access, name, descriptor, signature, exceptions);
            }

            @Override
            public void visitInnerClass(String name, String outerName, String innerName, int access) {
                Mod m = mods.containsKey(name) ? mods.get(name).get("") : null;
                super.visitInnerClass(name, outerName, innerName, m != null ? apply(access, m) : access);
            }
        }, 0);
        return cw.toByteArray();
    }
}
