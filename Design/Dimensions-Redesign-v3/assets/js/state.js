export const routes = Object.freeze([
  "whats-new",
  "dashboard",
  "prophet",
  "mirror",
  "portal",
  "glutton",
  "cage",
  "witness",
  "conclusion",
  "settings"
]);

export const modes = Object.freeze(["storybook", "lame"]);
export const overlays = Object.freeze([
  "eula",
  "settings",
  "save",
  "license",
  "feedback",
  "about",
  "setup",
  "tutorial"
]);
export const scans = Object.freeze(["never", "running", "incomplete", "stale", "complete"]);

export const initialState = Object.freeze({
  page: "whats-new",
  mode: "storybook",
  overlay: null,
  drawerOpen: false,
  setupStep: 0,
  tutorialStep: 0,
  scan: "never",
  encryption: "unknown",
  protocol: "unknown",
  unbound: "unknown"
});

const has = (values, value) => values.includes(value);
const boundedNext = (value) => (value < 3 ? value + 1 : value);
const boundedPrevious = (value) => (value > 0 ? value - 1 : value);
const update = (state, changes) => ({ ...state, ...changes });

export function reduce(state, action = {}) {
  if (!action || typeof action.type !== "string") return state;

  switch (action.type) {
    case "navigate":
      return has(routes, action.route) && action.route !== state.page ? update(state, { page: action.route }) : state;
    case "setMode":
      return has(modes, action.mode) && action.mode !== state.mode ? update(state, { mode: action.mode }) : state;
    case "openOverlay":
      return has(overlays, action.overlay) && action.overlay !== state.overlay
        ? update(state, { overlay: action.overlay, drawerOpen: false })
        : state;
    case "closeOverlay":
      return state.overlay === null ? state : update(state, { overlay: null });
    case "setScan":
      return has(scans, action.scan) && action.scan !== state.scan ? update(state, { scan: action.scan }) : state;
    case "setDrawer":
      return typeof action.open === "boolean" && action.open !== state.drawerOpen
        ? update(state, { drawerOpen: action.open })
        : state;
    case "toggleDrawer":
      return update(state, { drawerOpen: !state.drawerOpen });
    case "nextSetup": {
      const next = boundedNext(state.setupStep);
      return next === state.setupStep ? state : update(state, { setupStep: next });
    }
    case "previousSetup": {
      const next = boundedPrevious(state.setupStep);
      return next === state.setupStep ? state : update(state, { setupStep: next });
    }
    case "nextTutorial": {
      const next = boundedNext(state.tutorialStep);
      return next === state.tutorialStep ? state : update(state, { tutorialStep: next });
    }
    case "previousTutorial": {
      const next = boundedPrevious(state.tutorialStep);
      return next === state.tutorialStep ? state : update(state, { tutorialStep: next });
    }
    case "resetTutorial":
      return state.tutorialStep === 0 ? state : update(state, { tutorialStep: 0 });
    default:
      return state;
  }
}

export function createStore(seed = initialState) {
  let state = Object.freeze({ ...initialState, ...seed });
  const listeners = new Set();

  return {
    getState: () => state,
    dispatch(action) {
      const next = reduce(state, action);
      if (next !== state) {
        state = Object.freeze(next);
        listeners.forEach((listener) => listener(state));
      }
      return state;
    },
    subscribe(listener) {
      if (typeof listener !== "function") return () => {};
      listeners.add(listener);
      return () => listeners.delete(listener);
    }
  };
}
