import { overlays, routes } from './state.js';
import { clearTutorial, closeOverlay, openOverlay, renderTutorial, syncDialogSheets, trapOverlayFocus } from './overlays.js';

const pagePanels = '[data-page], [data-route-panel], [data-view]';
const drawerSnapshots = new WeakMap();

function closest(target, selector) {
  return target instanceof Element ? target.closest(selector) : null;
}

function setDrawerInert(root, open) {
  const regions = [...root.querySelectorAll('[data-inert-when-drawer], .app-main, .app-chrome')];
  const snapshots = drawerSnapshots.get(root) ?? new Map();
  drawerSnapshots.set(root, snapshots);

  for (const region of regions) {
    if (open) {
      if (!snapshots.has(region)) snapshots.set(region, { inert: region.inert, ariaHidden: region.getAttribute('aria-hidden') });
      region.inert = true;
      region.setAttribute('aria-hidden', 'true');
    } else if (snapshots.has(region)) {
      const previous = snapshots.get(region);
      region.inert = previous.inert;
      if (previous.ariaHidden === null) region.removeAttribute('aria-hidden');
      else region.setAttribute('aria-hidden', previous.ariaHidden);
      snapshots.delete(region);
    }
  }
}

function renderPopover(root, open) {
  for (const popover of root.querySelectorAll('[data-status-popover], [data-popover="status"]')) {
    popover.hidden = !open;
    popover.setAttribute('aria-hidden', String(!open));
  }
  for (const trigger of root.querySelectorAll('[data-toggle-status-popover], [data-status-popover-toggle]')) {
    trigger.setAttribute('aria-expanded', String(open));
  }
}

function renderState(root, state, previous, trigger) {
  const body = root.closest('body') ?? root;
  body.dataset.mode = state.mode;
  root.dataset.route = state.page;
  root.dataset.scan = state.scan;
  root.dataset.drawerOpen = String(state.drawerOpen);

  for (const route of root.querySelectorAll('[data-route]')) {
    route.setAttribute('aria-current', String(route.dataset.route === state.page ? 'page' : 'false'));
  }
  for (const panel of root.querySelectorAll(pagePanels)) {
    const route = panel.dataset.page ?? panel.dataset.routePanel ?? panel.dataset.view;
    const active = route === state.page;
    panel.hidden = !active;
    panel.setAttribute('aria-hidden', String(!active));
  }
  for (const option of root.querySelectorAll('[data-set-mode], [data-mode-option]')) {
    const mode = option.dataset.setMode ?? option.dataset.modeOption;
    option.setAttribute('aria-pressed', String(mode === state.mode));
  }
  for (const indicator of root.querySelectorAll('[data-scan-state]')) {
    indicator.dataset.scanState = state.scan;
  }
  for (const drawer of root.querySelectorAll('[data-drawer], .app-drawer')) {
    drawer.dataset.open = String(state.drawerOpen);
    drawer.setAttribute('aria-hidden', String(!state.drawerOpen));
  }
  for (const toggle of root.querySelectorAll('[data-toggle-drawer]')) toggle.setAttribute('aria-expanded', String(state.drawerOpen));
  setDrawerInert(root, state.drawerOpen && state.overlay === null);

  if (state.overlay !== previous.overlay) {
    if (state.overlay) openOverlay(root, state.overlay, trigger);
    else closeOverlay(root);
  }
  if (state.overlay === 'tutorial') renderTutorial(root, state.tutorialStep);
  else clearTutorial(root);
  syncDialogSheets(root);
}

function overlayAction(command) {
  return overlays.includes(command) ? { type: 'openOverlay', overlay: command } : null;
}

function consume(event) {
  event.preventDefault();
}

export function createFocusRestorer() {
  let trigger = null;

  return {
    record(candidate) {
      if (candidate && typeof candidate.focus === 'function') trigger = candidate;
    },
    clear() {
      trigger = null;
    },
    hasRecordedTrigger() {
      return trigger !== null;
    },
    restore(fallback) {
      const target = trigger?.isConnected && typeof trigger.focus === 'function' ? trigger : fallback;
      trigger = null;
      if (target?.isConnected && typeof target.focus === 'function') target.focus({ preventScroll: true });
      return target ?? null;
    }
  };
}

export function bindApplication(root, store, options = {}) {
  if (!root || !store || typeof store.getState !== 'function' || typeof store.dispatch !== 'function') {
    throw new TypeError('bindApplication requires an application root and state store.');
  }

  let previous = store.getState();
  let overlayTrigger = null;
  let statusPopoverOpen = false;
  const makeFocusRestorer = typeof options.createFocusRestorer === 'function' ? options.createFocusRestorer : createFocusRestorer;
  const drawerFocus = makeFocusRestorer();
  const popoverFocus = makeFocusRestorer();

  const drawerFallback = () => root.querySelector('[data-toggle-drawer]');
  const popoverFallback = () => root.querySelector('[data-toggle-status-popover], [data-status-popover-toggle]');
  const closeDrawer = ({ focusTransfer = false } = {}) => {
    if (!store.getState().drawerOpen) return;
    store.dispatch({ type: 'setDrawer', open: false });
    if (focusTransfer) drawerFocus.clear();
    else drawerFocus.restore(drawerFallback());
  };
  const closePopover = ({ focusTransfer = false } = {}) => {
    if (!statusPopoverOpen) return;
    statusPopoverOpen = false;
    renderPopover(root, false);
    if (focusTransfer) popoverFocus.clear();
    else popoverFocus.restore(popoverFallback());
  };
  const openOverlayFrom = (action, trigger) => {
    closeDrawer({ focusTransfer: true });
    closePopover({ focusTransfer: true });
    overlayTrigger = trigger;
    store.dispatch(action);
  };

  const render = (state) => {
    renderState(root, state, previous, overlayTrigger);
    previous = state;
    overlayTrigger = null;
  };
  const unsubscribe = store.subscribe(render);

  const onClick = (event) => {
    const withinPopover = closest(event.target, '[data-status-popover], [data-popover="status"], [data-toggle-status-popover], [data-status-popover-toggle]');
    const close = closest(event.target, '[data-close-overlay], [data-dialog-command="close"], [data-dialog-command="cancel"], [data-dialog-command="decline"]');
    const dialog = closest(event.target, '[data-dialog-command]');
    const drawer = closest(event.target, '[data-toggle-drawer], [data-close-drawer], .drawer-scrim');
    const popover = closest(event.target, '[data-toggle-status-popover], [data-status-popover-toggle]');
    const overlay = closest(event.target, '[data-open-overlay]');
    const overlayOpenAction = overlay ? overlayAction(overlay.dataset.openOverlay) : null;
    const menu = closest(event.target, '[data-menu-command]');
    const menuOpenAction = menu ? overlayAction(menu.dataset.menuCommand) : null;
    const transfersPopoverFocus = dialog?.dataset.dialogCommand === 'open-tutorial' || overlayOpenAction !== null || menuOpenAction !== null;
    if (statusPopoverOpen && !withinPopover) closePopover({ focusTransfer: transfersPopoverFocus });

    if (close) {
      consume(event);
      store.dispatch({ type: 'closeOverlay' });
      return;
    }

    if (dialog) {
      consume(event);
      if (dialog.dataset.dialogCommand === 'open-tutorial') {
        openOverlayFrom({ type: 'openOverlay', overlay: 'tutorial' }, dialog);
      } else if (dialog.dataset.dialogCommand === 'finish-setup') {
        store.dispatch({ type: 'closeOverlay' });
      }
      return;
    }

    if (drawer) {
      consume(event);
      if (drawer.matches('[data-close-drawer], .drawer-scrim')) closeDrawer();
      else if (store.getState().drawerOpen) closeDrawer();
      else {
        drawerFocus.record(drawer);
        store.dispatch({ type: 'setDrawer', open: true });
      }
      return;
    }

    if (popover) {
      consume(event);
      if (statusPopoverOpen) closePopover();
      else {
        popoverFocus.record(popover);
        statusPopoverOpen = true;
        renderPopover(root, true);
      }
      return;
    }

    if (overlay) {
      if (overlayOpenAction) {
        consume(event);
        openOverlayFrom(overlayOpenAction, overlay);
      }
      return;
    }

    if (menu) {
      const command = menu.dataset.menuCommand;
      if (menuOpenAction) {
        consume(event);
        openOverlayFrom(menuOpenAction, menu);
      } else if (routes.includes(command)) {
        consume(event);
        closePopover();
        store.dispatch({ type: 'navigate', route: command });
      }
      return;
    }

    const route = closest(event.target, '[data-route]');
    if (route && routes.includes(route.dataset.route)) {
      consume(event);
      closePopover();
      store.dispatch({ type: 'navigate', route: route.dataset.route });
      closeDrawer();
      return;
    }

    const mode = closest(event.target, '[data-set-mode], [data-mode-option]');
    if (mode) {
      const value = mode.dataset.setMode ?? mode.dataset.modeOption;
      consume(event);
      store.dispatch({ type: 'setMode', mode: value });
      return;
    }

    const scan = closest(event.target, '[data-set-scan]');
    if (scan) {
      consume(event);
      store.dispatch({ type: 'setScan', scan: scan.dataset.setScan });
      return;
    }

    const setup = closest(event.target, '[data-setup-command]');
    if (setup) {
      consume(event);
      const command = setup.dataset.setupCommand;
      if (command === 'next') store.dispatch({ type: 'nextSetup' });
      if (command === 'back') store.dispatch({ type: 'previousSetup' });
      if (command === 'defaults') store.dispatch({ type: 'setMode', mode: 'storybook' });
      if (command === 'finish') store.dispatch({ type: 'closeOverlay' });
      return;
    }

    const tutorial = closest(event.target, '[data-tutorial-command]');
    if (tutorial) {
      consume(event);
      const command = tutorial.dataset.tutorialCommand;
      if (command === 'next') store.dispatch({ type: 'nextTutorial' });
      if (command === 'back') store.dispatch({ type: 'previousTutorial' });
      if (command === 'skip' || command === 'finish') store.dispatch({ type: 'closeOverlay' });
      return;
    }

  };

  const onKeydown = (event) => {
    trapOverlayFocus(root, event);
    if (event.key !== 'Escape') return;
    if (store.getState().overlay !== null) {
      consume(event);
      store.dispatch({ type: 'closeOverlay' });
    } else if (statusPopoverOpen) {
      consume(event);
      closePopover();
    } else if (store.getState().drawerOpen) {
      consume(event);
      closeDrawer();
    }
  };

  const onResize = () => syncDialogSheets(root);
  root.addEventListener('click', onClick);
  root.addEventListener('keydown', onKeydown);
  root.ownerDocument?.defaultView?.addEventListener('resize', onResize);
  renderState(root, previous, { overlay: null }, null);
  renderPopover(root, false);

  return () => {
    unsubscribe();
    root.removeEventListener('click', onClick);
    root.removeEventListener('keydown', onKeydown);
    root.ownerDocument?.defaultView?.removeEventListener('resize', onResize);
    closeOverlay(root);
    closePopover();
    closeDrawer();
    setDrawerInert(root, false);
  };
}
