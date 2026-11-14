package com.realisticworld.core;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.List;
import java.util.function.LongSupplier;

/**
 * Readable log with a ring buffer. Errors are also kept separately so the
 * control center can show them, and the Forge layer saves them with the world.
 */
public final class RwLog {
    public enum Level { DEBUG, INFO, WARN, ERROR }

    /** One log line. */
    public static final class Entry {
        public final long tick;
        public final Level level;
        public final String source;
        public final String message;

        public Entry(long tick, Level level, String source, String message) {
            this.tick = tick;
            this.level = level;
            this.source = source;
            this.message = message;
        }

        @Override
        public String toString() {
            return "[t" + tick + "] " + level + " " + source + ": " + message;
        }
    }

    /** Where lines go besides the buffer (the game log in Forge). */
    public interface Sink {
        void write(Level level, String line);
    }

    private final int maxEntries;
    private final int maxErrors;
    private final Deque<Entry> entries = new ArrayDeque<>();
    private final Deque<Entry> errors = new ArrayDeque<>();
    private final List<Runnable> errorListeners = new ArrayList<>();
    private Sink sink;
    private LongSupplier tickSource = () -> 0;
    private Level level = Level.INFO;

    public RwLog(Sink sink) {
        this(sink, 60, 20);
    }

    public RwLog(Sink sink, int maxEntries, int maxErrors) {
        this.sink = sink;
        this.maxEntries = maxEntries;
        this.maxErrors = maxErrors;
    }

    public void setTickSource(LongSupplier source) {
        this.tickSource = source;
    }

    public void setLevel(Level level) {
        this.level = level;
    }

    public Level level() {
        return level;
    }

    public void debug(String source, String message) {
        log(Level.DEBUG, source, message);
    }

    public void info(String source, String message) {
        log(Level.INFO, source, message);
    }

    public void warn(String source, String message) {
        log(Level.WARN, source, message);
    }

    public void error(String source, String message) {
        log(Level.ERROR, source, message);
    }

    public void error(String source, String message, Throwable t) {
        StringBuilder sb = new StringBuilder(message).append(": ").append(t);
        StackTraceElement[] st = t.getStackTrace();
        for (int i = 0; i < Math.min(3, st.length); i++) sb.append("\n  at ").append(st[i]);
        log(Level.ERROR, source, sb.toString());
    }

    public synchronized List<Entry> errors() {
        return new ArrayList<>(errors);
    }

    public synchronized List<Entry> entries() {
        return new ArrayList<>(entries);
    }

    public synchronized void clearErrors() {
        errors.clear();
    }

    /** Restore errors saved in an earlier session (oldest first). */
    public synchronized void restoreErrors(List<Entry> list) {
        errors.clear();
        int from = Math.max(0, list.size() - maxErrors);
        for (int i = from; i < list.size(); i++) errors.add(list.get(i));
    }

    public void onError(Runnable listener) {
        errorListeners.add(listener);
    }

    private void log(Level l, String source, String message) {
        if (l.ordinal() < level.ordinal()) return;
        long tick;
        try {
            tick = tickSource.getAsLong();
        } catch (RuntimeException e) {
            tick = 0;
        }
        Entry e = new Entry(tick, l, source, message);
        synchronized (this) {
            entries.addLast(e);
            if (entries.size() > maxEntries) entries.removeFirst();
            if (l == Level.ERROR) {
                errors.addLast(e);
                if (errors.size() > maxErrors) errors.removeFirst();
            }
        }
        if (sink != null) sink.write(l, "[RW] " + e);
        if (l == Level.ERROR) {
            for (Runnable r : errorListeners) {
                try {
                    r.run();
                } catch (RuntimeException ignored) {
                    // a failing listener must never break logging
                }
            }
        }
    }
}
