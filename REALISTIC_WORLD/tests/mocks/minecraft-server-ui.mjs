// Test double for @minecraft/server-ui 2.2.0. Each form records its elements;
// show() resolves with the next scripted answer from __ui.answers.
// Modal answers follow the layout in which labels take an `undefined` slot
// (set __ui.labelsTakeSlot = false to test the other layout).

export const FormCancelationReason = Object.freeze({ UserBusy: "UserBusy", UserClosed: "UserClosed" });

export const __ui = {
  /** forms shown, in order: { kind, title, body, elements } */
  shown: [],
  /** queue of answer functions: (form) => response */
  answers: [],
  labelsTakeSlot: true,
  busyTimes: 0,
  answer(fn) {
    this.answers.push(fn);
  },
  last() {
    return this.shown[this.shown.length - 1];
  },
};

class BaseForm {
  constructor(kind) {
    this.kind = kind;
    this.titleText = "";
    this.bodyText = "";
    this.elements = [];
  }
  title(t) {
    this.titleText = t;
    return this;
  }
  body(t) {
    this.bodyText = t;
    return this;
  }
  label(t) {
    this.elements.push({ type: "label", text: t });
    return this;
  }
  divider() {
    this.elements.push({ type: "divider" });
    return this;
  }
  header(t) {
    this.elements.push({ type: "header", text: t });
    return this;
  }
  async show(player) {
    if (__ui.busyTimes > 0) {
      __ui.busyTimes--;
      return { canceled: true, cancelationReason: FormCancelationReason.UserBusy };
    }
    const record = { kind: this.kind, title: this.titleText, body: this.bodyText, elements: this.elements, player: player.name };
    __ui.shown.push(record);
    const next = __ui.answers.shift();
    if (!next) return { canceled: true, cancelationReason: FormCancelationReason.UserClosed };
    return next(this, record);
  }
}

export class ActionFormData extends BaseForm {
  constructor() {
    super("action");
  }
  button(text) {
    this.elements.push({ type: "button", text });
    return this;
  }
  buttons() {
    return this.elements.filter((e) => e.type === "button").map((e) => e.text);
  }
}

export class MessageFormData extends BaseForm {
  constructor() {
    super("message");
  }
  button1(t) {
    this.b1 = t;
    return this;
  }
  button2(t) {
    this.b2 = t;
    return this;
  }
}

export class ModalFormData extends BaseForm {
  constructor() {
    super("modal");
  }
  toggle(text, opts = {}) {
    this.elements.push({ type: "toggle", text, value: opts.defaultValue ?? false });
    return this;
  }
  dropdown(text, items, opts = {}) {
    this.elements.push({ type: "dropdown", text, items, value: opts.defaultValueIndex ?? 0 });
    return this;
  }
  slider(text, min, max, opts = {}) {
    this.elements.push({ type: "slider", text, value: opts.defaultValue ?? min });
    return this;
  }
  textField(text, placeholder, opts = {}) {
    this.elements.push({ type: "textField", text, value: opts.defaultValue ?? "" });
    return this;
  }
  submitButton(t) {
    this.submit = t;
    return this;
  }
  /** Build formValues from defaults, with overrides by element text match. */
  values(overrides = {}) {
    const out = [];
    for (const e of this.elements) {
      const isInput = ["toggle", "dropdown", "slider", "textField"].includes(e.type);
      if (!isInput) {
        if (__ui.labelsTakeSlot) out.push(undefined);
        continue;
      }
      const key = Object.keys(overrides).find((k) => e.text.includes(k));
      out.push(key !== undefined ? overrides[key] : e.value);
    }
    return out;
  }
}

/** Helpers for scripted answers. */
export const pick = (buttonText) => (form) => {
  const idx = form.buttons().findIndex((b) => b.startsWith(buttonText));
  if (idx < 0) throw new Error(`button "${buttonText}" not found in [${form.buttons().join(", ")}]`);
  return { canceled: false, selection: idx };
};
export const submit = (overrides) => (form) => ({ canceled: false, formValues: form.values(overrides) });
export const close = () => () => ({ canceled: true, cancelationReason: FormCancelationReason.UserClosed });
export const button1 = () => () => ({ canceled: false, selection: 0 });
