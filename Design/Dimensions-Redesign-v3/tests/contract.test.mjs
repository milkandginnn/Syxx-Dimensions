import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, existsSync } from "node:fs";
import { resolve } from "node:path";
import { hasElementWithAttribute } from "./structural.mjs";
import { createStore } from "../assets/js/state.js";
import { focusOverlay, keepCompactTutorialInOverlay } from "../assets/js/overlays.js";
import { bindApplication, createFocusRestorer } from "../assets/js/app.js";

const root = resolve(import.meta.dirname, "..");
const pages = [
  "index.html",
  "syxx-macos.html",
  "syxx-windows.html",
  "syxx-linux.html",
  "syxx-server.html",
  "copy-catalogue.html"
];

class BindingNode {
  constructor(dataset = {}) {
    this.dataset = dataset;
    this.attributes = new Map();
    this.hidden = false;
    this.isConnected = true;
    this.focusCount = 0;
  }

  setAttribute(name, value) {
    this.attributes.set(name, String(value));
  }

  getAttribute(name) {
    return this.attributes.get(name) ?? null;
  }

  hasAttribute(name) {
    return this.attributes.has(name);
  }

  focus() {
    this.focusCount += 1;
  }

  closest(selector) {
    const selectorsByDataset = [
      ["toggleDrawer", "[data-toggle-drawer]"],
      ["toggleStatusPopover", "[data-toggle-status-popover]"],
      ["statusPopover", "[data-status-popover]"],
      ["openOverlay", "[data-open-overlay]"],
      ["menuCommand", "[data-menu-command]"],
      ["dialogCommand", "[data-dialog-command]"]
    ];
    return selectorsByDataset.some(([key, token]) => this.dataset[key] !== undefined && selector.includes(token))
      ? this
      : null;
  }

  matches(selector) {
    return this.dataset.closeDrawer !== undefined && selector.includes("[data-close-drawer]");
  }
}

class BindingRoot extends BindingNode {
  constructor(nodes) {
    super();
    this.nodes = nodes;
    this.listeners = new Map();
    this.ownerDocument = { defaultView: null };
  }

  querySelectorAll(selector) {
    if (selector === "[data-overlay]") return [this.nodes.overlay].filter(Boolean);
    if (selector === "[data-drawer], .app-drawer") return [this.nodes.drawer].filter(Boolean);
    if (selector === "[data-toggle-drawer]") return [this.nodes.drawerTrigger].filter(Boolean);
    if (selector === '[data-status-popover], [data-popover="status"]') {
      return [this.nodes.statusPopover].filter(Boolean);
    }
    if (selector === "[data-toggle-status-popover], [data-status-popover-toggle]") {
      return [this.nodes.statusTrigger].filter(Boolean);
    }
    return [];
  }

  querySelector(selector) {
    return this.querySelectorAll(selector)[0] ?? null;
  }

  addEventListener(type, listener) {
    this.listeners.set(type, listener);
  }

  removeEventListener(type) {
    this.listeners.delete(type);
  }

  emitClick(target) {
    this.listeners.get("click")({ target, preventDefault() {} });
  }
}

function createTrackedRestorer() {
  return {
    recorded: null,
    clearCount: 0,
    restoreCount: 0,
    record(trigger) {
      this.recorded = trigger;
    },
    clear() {
      this.recorded = null;
      this.clearCount += 1;
    },
    hasRecordedTrigger() {
      return this.recorded !== null;
    },
    restore(fallback) {
      this.restoreCount += 1;
      const target = this.recorded?.isConnected ? this.recorded : fallback;
      this.recorded = null;
      target?.focus();
      return target ?? null;
    }
  };
}

test("all required pages exist", () => {
  for (const page of pages) assert.equal(existsSync(resolve(root, page)), true, page);
});

test("desktop pages expose the shared application landmarks", () => {
  for (const page of pages.slice(1, 4)) {
    const html = readFileSync(resolve(root, page), "utf8");
    assert.equal(hasElementWithAttribute(html, "data-app-root"), true, page);
    assert.equal(hasElementWithAttribute(html, "data-status-strip"), true, page);
    assert.equal(hasElementWithAttribute(html, "data-route", "whats-new"), true, page);
    assert.equal(hasElementWithAttribute(html, "data-open-overlay", "settings"), true, page);
  }
});

test("landmark helper rejects comment-only and inert landmark matches", () => {
  const commentOnly = "<!-- <main data-app-root data-status-strip> -->";
  const inertScript = '<script>const sample = "<main data-route=\\"whats-new\\">";</script>';

  assert.equal(hasElementWithAttribute(commentOnly, "data-app-root"), false);
  assert.equal(hasElementWithAttribute(inertScript, "data-route", "whats-new"), false);
});

test("state actions create frozen state for each approved interaction", () => {
  const store = createStore();
  const transitions = [
    [{ type: "navigate", route: "dashboard" }, "page", "dashboard"],
    [{ type: "setMode", mode: "lame" }, "mode", "lame"],
    [{ type: "openOverlay", overlay: "settings" }, "overlay", "settings"],
    [{ type: "setScan", scan: "incomplete" }, "scan", "incomplete"],
    [{ type: "nextSetup" }, "setupStep", 1],
    [{ type: "nextTutorial" }, "tutorialStep", 1]
  ];

  for (const [action, field, expected] of transitions) {
    const before = store.getState();
    const after = store.dispatch(action);
    assert.notEqual(after, before, action.type);
    assert.equal(Object.isFrozen(after), true, action.type);
    assert.equal(after[field], expected, action.type);
  }
});

test("invalid state values preserve the prior frozen state", () => {
  const store = createStore();
  const invalidActions = [
    { type: "navigate", route: "unmeasured-route" },
    { type: "setMode", mode: "optimistic" },
    { type: "openOverlay", overlay: "unmeasured-overlay" },
    { type: "setScan", scan: "secure" }
  ];

  for (const action of invalidActions) {
    const before = store.getState();
    assert.equal(store.dispatch(action), before, action.type);
    assert.equal(Object.isFrozen(before), true, action.type);
  }
});

test("compact tutorial cards remain inside the active overlay focus scope", () => {
  const overlay = {
    children: [],
    contains(node) {
      return this.children.includes(node);
    },
    append(node) {
      this.children.push(node);
      node.parentNode = this;
    }
  };
  const card = { parentNode: null, dataset: {} };

  keepCompactTutorialInOverlay(overlay, card);

  assert.equal(overlay.contains(card), true);
  assert.equal(card.parentNode, overlay);
  assert.equal(card.dataset.tutorialPlacement, "inline");
});

test("drawer and status popover closing restores the trigger or stable fallback", () => {
  const trigger = {
    isConnected: true,
    focusCount: 0,
    focus() {
      this.focusCount += 1;
    }
  };
  const fallback = {
    isConnected: true,
    focusCount: 0,
    focus() {
      this.focusCount += 1;
    }
  };
  const drawerFocus = createFocusRestorer();
  const popoverFocus = createFocusRestorer();

  drawerFocus.record(trigger);
  assert.equal(drawerFocus.restore(fallback), trigger);
  assert.equal(trigger.focusCount, 1);

  trigger.isConnected = false;
  popoverFocus.record(trigger);
  assert.equal(popoverFocus.restore(fallback), fallback);
  assert.equal(fallback.focusCount, 1);
});

test("drawer to overlay focus transfer clears restoration and focuses the modal", () => {
  const drawerTrigger = {
    isConnected: true,
    focusCount: 0,
    focus() {
      this.focusCount += 1;
    }
  };
  const hiddenDrawerControl = { focusCount: 0 };
  const modalControl = {
    focusCount: 0,
    hasAttribute() {
      return true;
    },
    focus() {
      this.focusCount += 1;
    }
  };
  const overlay = {
    querySelector() {
      return modalControl;
    }
  };
  const drawerFocus = createFocusRestorer();

  drawerFocus.record(drawerTrigger);
  drawerFocus.clear();
  focusOverlay(overlay);

  assert.equal(drawerFocus.hasRecordedTrigger(), false);
  assert.equal(drawerTrigger.focusCount, 0);
  assert.equal(hiddenDrawerControl.focusCount, 0);
  assert.equal(modalControl.focusCount, 1);
});

test("menu overlay command transfers drawer focus through the application binding", () => {
  const originalElement = globalThis.Element;
  globalThis.Element = BindingNode;
  try {
    const drawerTrigger = new BindingNode({ toggleDrawer: "" });
    const drawer = new BindingNode({ drawer: "" });
    const modalControl = new BindingNode();
    const overlay = new BindingNode({ overlay: "license" });
    overlay.querySelector = () => modalControl;
    const menuOverlay = new BindingNode({ menuCommand: "license" });
    const root = new BindingRoot({ drawerTrigger, drawer, overlay });
    const drawerRestorer = createTrackedRestorer();
    const popoverRestorer = createTrackedRestorer();
    const restorers = [drawerRestorer, popoverRestorer];
    const store = createStore();

    bindApplication(root, store, { createFocusRestorer: () => restorers.shift() });
    root.emitClick(drawerTrigger);
    root.emitClick(menuOverlay);

    assert.equal(store.getState().drawerOpen, false);
    assert.equal(store.getState().overlay, "license");
    assert.equal(drawerRestorer.clearCount, 1);
    assert.equal(drawerRestorer.restoreCount, 0);
    assert.equal(drawerRestorer.hasRecordedTrigger(), false);
    assert.equal(drawerTrigger.focusCount, 0);
    assert.equal(modalControl.focusCount, 1);
  } finally {
    globalThis.Element = originalElement;
  }
});

test("popover binding restores focus only for ordinary outside clicks", () => {
  const originalElement = globalThis.Element;
  globalThis.Element = BindingNode;
  try {
    const statusTrigger = new BindingNode({ toggleStatusPopover: "" });
    const statusPopover = new BindingNode({ statusPopover: "" });
    const modalControl = new BindingNode();
    const overlay = new BindingNode({ overlay: "license" });
    overlay.querySelector = () => modalControl;
    const overlayTrigger = new BindingNode({ openOverlay: "license" });
    const root = new BindingRoot({ statusTrigger, statusPopover, overlay });
    const drawerRestorer = createTrackedRestorer();
    const popoverRestorer = createTrackedRestorer();
    const restorers = [drawerRestorer, popoverRestorer];
    const store = createStore();

    bindApplication(root, store, { createFocusRestorer: () => restorers.shift() });
    root.emitClick(statusTrigger);
    root.emitClick(overlayTrigger);

    assert.equal(statusPopover.hidden, true);
    assert.equal(statusPopover.getAttribute("aria-hidden"), "true");
    assert.equal(statusTrigger.getAttribute("aria-expanded"), "false");
    assert.equal(popoverRestorer.clearCount, 1);
    assert.equal(popoverRestorer.restoreCount, 0);
    assert.equal(popoverRestorer.hasRecordedTrigger(), false);
    assert.equal(statusTrigger.focusCount, 0);
    assert.equal(store.getState().overlay, "license");
    assert.equal(modalControl.focusCount, 1);

    const outsideStatusTrigger = new BindingNode({ toggleStatusPopover: "" });
    const outsideStatusPopover = new BindingNode({ statusPopover: "" });
    const outsideRoot = new BindingRoot({
      statusTrigger: outsideStatusTrigger,
      statusPopover: outsideStatusPopover
    });
    const outsideDrawerRestorer = createTrackedRestorer();
    const outsidePopoverRestorer = createTrackedRestorer();
    const outsideRestorers = [outsideDrawerRestorer, outsidePopoverRestorer];
    const outsideStore = createStore();

    bindApplication(outsideRoot, outsideStore, {
      createFocusRestorer: () => outsideRestorers.shift()
    });
    outsideRoot.emitClick(outsideStatusTrigger);
    outsideRoot.emitClick(new BindingNode());

    assert.equal(outsideStatusPopover.hidden, true);
    assert.equal(outsidePopoverRestorer.clearCount, 0);
    assert.equal(outsidePopoverRestorer.restoreCount, 1);
    assert.equal(outsidePopoverRestorer.hasRecordedTrigger(), false);
    assert.equal(outsideStatusTrigger.focusCount, 1);
  } finally {
    globalThis.Element = originalElement;
  }
});
