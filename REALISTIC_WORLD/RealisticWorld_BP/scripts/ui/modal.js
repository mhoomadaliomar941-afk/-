// ModalFormData wrapper that returns values by name.
//
// The official docs say formValues is "an ordered set of values based on the
// order of controls" but do not say whether label/divider elements take a slot
// (the type allows `undefined`, which suggests they do). This builder records
// both layouts and picks the one whose length matches the response, so values
// are read correctly either way.

import { ModalFormData } from "@minecraft/server-ui";

export class ModalBuilder {
  /** @param {string} title */
  constructor(title) {
    this.form = new ModalFormData().title(title);
    /** @type {{ name: string | null }[]} every element in order */
    this.elements = [];
  }

  /** @param {string} text */
  label(text) {
    this.form.label(text);
    this.elements.push({ name: null });
    return this;
  }

  /** @param {string} name @param {string} text @param {boolean} value */
  toggle(name, text, value) {
    this.form.toggle(text, { defaultValue: value });
    this.elements.push({ name });
    return this;
  }

  /** @param {string} name @param {string} text @param {string[]} options @param {number} index */
  dropdown(name, text, options, index) {
    this.form.dropdown(text, options, { defaultValueIndex: index });
    this.elements.push({ name });
    return this;
  }

  /** @param {string} name @param {string} text @param {number} min @param {number} max @param {number} value @param {number} [stepSize] */
  slider(name, text, min, max, value, stepSize = 1) {
    this.form.slider(text, min, max, { defaultValue: value, valueStep: stepSize });
    this.elements.push({ name });
    return this;
  }

  /** @param {string} text */
  submit(text) {
    this.form.submitButton(text);
    return this;
  }

  /**
   * Map a formValues array to { name: value }.
   * @param {(boolean | number | string | undefined)[]} formValues
   * @returns {Record<string, boolean | number | string | undefined> | null} null if the layout is unrecognised
   */
  read(formValues) {
    const inputs = this.elements.filter((e) => e.name !== null);
    /** @type {Record<string, boolean | number | string | undefined>} */
    const out = {};
    if (formValues.length === this.elements.length) {
      this.elements.forEach((e, i) => {
        if (e.name !== null) out[e.name] = formValues[i];
      });
      return out;
    }
    if (formValues.length === inputs.length) {
      inputs.forEach((e, i) => {
        out[/** @type {string} */ (e.name)] = formValues[i];
      });
      return out;
    }
    return null;
  }
}
