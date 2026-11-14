package com.realisticworld.ui;

import com.realisticworld.core.DataStore;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * A screen defined on the server and drawn by the client, like Bedrock's
 * ActionFormData / ModalFormData / MessageFormData:
 * ACTION: title, body text and a list of buttons;
 * MODAL: title and controls (label, toggle, dropdown, slider) with a submit button;
 * MESSAGE: title, body and two buttons.
 * Forms travel as small maps (NBT in a network packet) and come back as a Reply.
 */
public final class Form {
    public enum Kind { ACTION, MODAL, MESSAGE }

    /** One element of a form. */
    public static final class Element {
        public final String type; // button, label, toggle, dropdown, slider
        public final String name; // reply key for controls
        public final String text;
        public boolean bool;
        public int index;
        public List<String> options = new ArrayList<>();
        public double min, max, step, value;

        Element(String type, String name, String text) {
            this.type = type;
            this.name = name;
            this.text = text;
        }

        public boolean isControl() {
            return type.equals("toggle") || type.equals("dropdown") || type.equals("slider");
        }
    }

    public final Kind kind;
    public String title = "";
    public String body = "";
    public String submit = "Apply";
    public final List<Element> elements = new ArrayList<>();
    /** Set by the server when sent; the reply must carry it. */
    public int id;

    public Form(Kind kind) {
        this.kind = kind;
    }

    public static Form action(String title) {
        Form f = new Form(Kind.ACTION);
        f.title = title;
        return f;
    }

    public static Form modal(String title) {
        Form f = new Form(Kind.MODAL);
        f.title = title;
        return f;
    }

    public static Form message(String title, String body, String yes, String no) {
        Form f = new Form(Kind.MESSAGE);
        f.title = title;
        f.body = body;
        f.button(yes);
        f.button(no);
        return f;
    }

    public Form body(String text) {
        this.body = text;
        return this;
    }

    public Form button(String text) {
        elements.add(new Element("button", null, text));
        return this;
    }

    public Form label(String text) {
        elements.add(new Element("label", null, text));
        return this;
    }

    public Form toggle(String name, String text, boolean value) {
        Element e = new Element("toggle", name, text);
        e.bool = value;
        elements.add(e);
        return this;
    }

    public Form dropdown(String name, String text, List<String> options, int index) {
        Element e = new Element("dropdown", name, text);
        e.options = new ArrayList<>(options);
        e.index = Math.max(0, Math.min(options.size() - 1, index));
        elements.add(e);
        return this;
    }

    public Form slider(String name, String text, double min, double max, double step, double value) {
        Element e = new Element("slider", name, text);
        e.min = min;
        e.max = max;
        e.step = step;
        e.value = value;
        elements.add(e);
        return this;
    }

    public Form submit(String text) {
        this.submit = text;
        return this;
    }

    public List<String> buttons() {
        List<String> out = new ArrayList<>();
        for (Element e : elements) if (e.type.equals("button")) out.add(e.text);
        return out;
    }

    public Element control(String name) {
        for (Element e : elements) if (name.equals(e.name)) return e;
        return null;
    }

    public Map<String, Object> toMap() {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("kind", kind.name());
        m.put("id", id);
        m.put("title", title);
        m.put("body", body);
        m.put("submit", submit);
        List<Object> els = new ArrayList<>();
        for (Element e : elements) {
            Map<String, Object> em = new LinkedHashMap<>();
            em.put("type", e.type);
            if (e.name != null) em.put("name", e.name);
            em.put("text", e.text);
            switch (e.type) {
                case "toggle": em.put("bool", e.bool); break;
                case "dropdown": em.put("index", e.index); em.put("options", new ArrayList<Object>(e.options)); break;
                case "slider": em.put("range", new ArrayList<Object>(Arrays.asList(e.min, e.max, e.step, e.value))); break;
                default: break;
            }
            els.add(em);
        }
        m.put("elements", els);
        return m;
    }

    public static Form fromMap(Map<String, Object> m) {
        Form f = new Form(Kind.valueOf(DataStore.getString(m, "kind", "ACTION")));
        f.id = DataStore.getInt(m, "id", 0);
        f.title = DataStore.getString(m, "title", "");
        f.body = DataStore.getString(m, "body", "");
        f.submit = DataStore.getString(m, "submit", "Apply");
        for (Object o : DataStore.getList(m, "elements")) {
            if (!(o instanceof Map)) continue;
            @SuppressWarnings("unchecked")
            Map<String, Object> em = (Map<String, Object>) o;
            Element e = new Element(DataStore.getString(em, "type", "label"), DataStore.getString(em, "name", null), DataStore.getString(em, "text", ""));
            e.bool = DataStore.getBool(em, "bool", false);
            e.index = DataStore.getInt(em, "index", 0);
            for (Object op : DataStore.getList(em, "options")) e.options.add(String.valueOf(op));
            List<Object> r = DataStore.getList(em, "range");
            if (r.size() == 4) {
                e.min = ((Number) r.get(0)).doubleValue();
                e.max = ((Number) r.get(1)).doubleValue();
                e.step = ((Number) r.get(2)).doubleValue();
                e.value = ((Number) r.get(3)).doubleValue();
            }
            f.elements.add(e);
        }
        return f;
    }

    /** What the player answered. */
    public static final class Reply {
        public final int formId;
        public final boolean canceled;
        /** ACTION/MESSAGE: index of the pressed button. */
        public final int selection;
        /** MODAL: control values by name (Boolean, Integer index, Double). */
        public final Map<String, Object> values;

        public Reply(int formId, boolean canceled, int selection, Map<String, Object> values) {
            this.formId = formId;
            this.canceled = canceled;
            this.selection = selection;
            this.values = values == null ? new LinkedHashMap<>() : values;
        }

        public static Reply cancel(int formId) {
            return new Reply(formId, true, -1, null);
        }

        public static Reply pick(int formId, int selection) {
            return new Reply(formId, false, selection, null);
        }

        public static Reply submit(int formId, Map<String, Object> values) {
            return new Reply(formId, false, -1, values);
        }

        public Map<String, Object> toMap() {
            Map<String, Object> m = new LinkedHashMap<>();
            m.put("id", formId);
            m.put("canceled", canceled);
            m.put("selection", selection);
            m.put("values", new LinkedHashMap<>(values));
            return m;
        }

        public static Reply fromMap(Map<String, Object> m) {
            Map<String, Object> v = DataStore.getMap(m, "values");
            return new Reply(DataStore.getInt(m, "id", 0), DataStore.getBool(m, "canceled", true), DataStore.getInt(m, "selection", -1),
                v == null ? null : new LinkedHashMap<>(v));
        }

        public boolean bool(String name) {
            return Boolean.TRUE.equals(values.get(name));
        }

        public int index(String name) {
            Object v = values.get(name);
            return v instanceof Number ? ((Number) v).intValue() : -1;
        }

        public double number(String name) {
            Object v = values.get(name);
            return v instanceof Number ? ((Number) v).doubleValue() : Double.NaN;
        }
    }
}
