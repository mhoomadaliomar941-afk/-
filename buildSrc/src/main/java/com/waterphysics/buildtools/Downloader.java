package com.waterphysics.buildtools;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.security.MessageDigest;
import java.util.List;

/**
 * Downloads a file from an ordered list of URLs (official source first, mirrors after) and
 * verifies its SHA-1. A file is only accepted if its hash matches, so mirrors cannot inject
 * modified content.
 */
public final class Downloader {
    private Downloader() {}

    public static String sha1(byte[] data) {
        try {
            MessageDigest md = MessageDigest.getInstance("SHA-1");
            byte[] d = md.digest(data);
            StringBuilder sb = new StringBuilder();
            for (byte b : d) sb.append(String.format("%02x", b));
            return sb.toString();
        } catch (Exception e) {
            throw new RuntimeException(e);
        }
    }

    public static String sha1(File f) throws IOException {
        return sha1(Files.readAllBytes(f.toPath()));
    }

    /** @return true when the file was downloaded or was already present with the right hash. */
    public static void fetch(File target, String expectedSha1, List<String> urls, StringBuilder log) throws IOException {
        if (target.isFile() && (expectedSha1 == null || expectedSha1.equalsIgnoreCase(sha1(target)))) {
            return;
        }
        File parent = target.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) {
            throw new IOException("Cannot create " + parent);
        }
        StringBuilder errors = new StringBuilder();
        for (String u : urls) {
            try {
                byte[] data = get(u);
                String got = sha1(data);
                if (expectedSha1 != null && !expectedSha1.equalsIgnoreCase(got)) {
                    errors.append("\n  ").append(u).append(" -> SHA-1 mismatch (").append(got).append(")");
                    continue;
                }
                File tmp = new File(target.getPath() + ".part");
                Files.write(tmp.toPath(), data);
                Files.move(tmp.toPath(), target.toPath(), StandardCopyOption.REPLACE_EXISTING);
                if (log != null) log.append("Downloaded ").append(target.getName()).append(" from ").append(u).append('\n');
                return;
            } catch (IOException e) {
                errors.append("\n  ").append(u).append(" -> ").append(e.getClass().getSimpleName()).append(": ").append(e.getMessage());
            }
        }
        throw new IOException("Could not download " + target.getName() + " (sha1 " + expectedSha1 + "):" + errors);
    }

    private static byte[] get(String url) throws IOException {
        IOException last = null;
        for (int attempt = 0; attempt < 3; attempt++) {
            HttpURLConnection c = null;
            try {
                c = (HttpURLConnection) new URL(url).openConnection();
                c.setConnectTimeout(30000);
                c.setReadTimeout(300000);
                c.setInstanceFollowRedirects(true);
                c.setRequestProperty("User-Agent", "waterphysics-build/1.0");
                int code = c.getResponseCode();
                if (code == 429 || code >= 500) {
                    last = new IOException("HTTP " + code);
                    sleep(2000L * (attempt + 1));
                    continue;
                }
                if (code != 200) throw new IOException("HTTP " + code);
                try (InputStream in = c.getInputStream()) {
                    ByteArrayOutputStream out = new ByteArrayOutputStream();
                    byte[] buf = new byte[65536];
                    int n;
                    while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
                    return out.toByteArray();
                }
            } catch (IOException e) {
                last = e;
                if (e.getMessage() != null && e.getMessage().startsWith("HTTP 4")) throw e;
            } finally {
                if (c != null) c.disconnect();
            }
        }
        throw last;
    }

    private static void sleep(long ms) {
        try { Thread.sleep(ms); } catch (InterruptedException ignored) { Thread.currentThread().interrupt(); }
    }
}
