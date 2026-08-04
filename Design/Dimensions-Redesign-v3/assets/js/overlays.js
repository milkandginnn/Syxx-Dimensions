const triggers = new WeakMap();
const inertSnapshots = new WeakMap();
const tutorialSnapshots = new WeakMap();
const tutorialOverlaySnapshots = new WeakMap();

const tutorialSteps = Object.freeze([
  {
    target: '[data-status-strip]',
    text: 'This strip shows DNS encryption, protocol, Unbound, and scan freshness. A question mark means Syxx could not confirm the state.'
  },
  {
    target: '[data-tutorial-target="modules"], [data-route="dashboard"]',
    text: 'Each module answers one network question. Use Dashboard for a complete scan, or open one module for a focused check.'
  },
  {
    target: '[data-tutorial-target="scan"], [data-action="run-quick-scan"]',
    text: 'Running a complete scan gathers the evidence needed for a conclusion. Missing or interrupted checks stay incomplete.'
  },
  {
    target: '[data-tutorial-target="conclusion"], [data-route="conclusion"]',
    text: 'Conclusion explains what the completed checks support, what is missing, and what to do next. Save it as a plain-text file when you need a record.'
  }
]);

const focusableSelector = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])'
].join(',');

function overlayFor(root, name) {
  return [...root.querySelectorAll('[data-overlay]')].find((overlay) => overlay.dataset.overlay === name) ?? null;
}

function activeOverlay(root) {
  return [...root.querySelectorAll('[data-overlay]')].find((overlay) => !overlay.hidden && overlay.getAttribute('aria-hidden') !== 'true') ?? null;
}

function focusableIn(container) {
  return [...container.querySelectorAll(focusableSelector)].filter((element) => !element.hidden && element.getAttribute('aria-hidden') !== 'true');
}

function inertRegions(root) {
  const explicit = [...root.querySelectorAll('[data-inert-when-overlay]')];
  if (explicit.length) return explicit;
  return [...root.querySelectorAll('.app-chrome, .app-sidebar, .app-rail, .compact-title-row, .app-main')];
}

function setInert(root, enabled) {
  const snapshot = inertSnapshots.get(root) ?? new Map();
  inertSnapshots.set(root, snapshot);

  for (const region of inertRegions(root)) {
    if (enabled) {
      if (!snapshot.has(region)) snapshot.set(region, { ariaHidden: region.getAttribute('aria-hidden'), inert: region.inert });
      region.inert = true;
      region.setAttribute('aria-hidden', 'true');
    } else if (snapshot.has(region)) {
      const previous = snapshot.get(region);
      region.inert = previous.inert;
      if (previous.ariaHidden === null) region.removeAttribute('aria-hidden');
      else region.setAttribute('aria-hidden', previous.ariaHidden);
      snapshot.delete(region);
    }
  }
}

function focusFirst(overlay) {
  const target = overlay.querySelector('[autofocus]') ?? focusableIn(overlay)[0] ?? overlay;
  if (!target.hasAttribute('tabindex') && target === overlay) target.setAttribute('tabindex', '-1');
  target.focus({ preventScroll: true });
}

export function focusOverlay(overlay) {
  focusFirst(overlay);
  return overlay;
}

export function openOverlay(root, name, trigger) {
  const overlay = overlayFor(root, name);
  if (!overlay) return null;

  if (trigger && typeof trigger.focus === 'function') triggers.set(root, trigger);
  for (const candidate of root.querySelectorAll('[data-overlay]')) {
    const isActive = candidate === overlay;
    candidate.hidden = !isActive;
    candidate.setAttribute('aria-hidden', String(!isActive));
  }

  overlay.setAttribute('role', 'dialog');
  overlay.setAttribute('aria-modal', 'true');
  setInert(root, true);
  syncDialogSheets(root);
  requestFocus(overlay);
  return overlay;
}

function requestFocus(overlay) {
  const view = overlay.ownerDocument?.defaultView;
  if (typeof view?.requestAnimationFrame === 'function') view.requestAnimationFrame(() => focusOverlay(overlay));
  else focusOverlay(overlay);
}

export function closeOverlay(root) {
  for (const overlay of root.querySelectorAll('[data-overlay]')) {
    overlay.hidden = true;
    overlay.setAttribute('aria-hidden', 'true');
  }
  clearTutorial(root);
  setInert(root, false);

  const trigger = triggers.get(root);
  triggers.delete(root);
  if (trigger?.isConnected && typeof trigger.focus === 'function') trigger.focus({ preventScroll: true });
}

export function trapOverlayFocus(root, event) {
  if (event.key !== 'Tab') return;
  const overlay = activeOverlay(root);
  if (!overlay) return;

  const controls = focusableIn(overlay);
  if (!controls.length) {
    event.preventDefault();
    focusFirst(overlay);
    return;
  }

  const first = controls[0];
  const last = controls.at(-1);
  const current = overlay.ownerDocument.activeElement;
  if (event.shiftKey && (current === first || !overlay.contains(current))) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && (current === last || !overlay.contains(current))) {
    event.preventDefault();
    first.focus();
  }
}

export function syncDialogSheets(root) {
  const view = root.ownerDocument?.defaultView;
  const compact = typeof view?.matchMedia === 'function' && view.matchMedia('(max-width: 640px)').matches;
  for (const dialog of root.querySelectorAll('[data-overlay] .dialog')) dialog.classList.toggle('sheet', compact);
}

function rememberTutorialTarget(root, target) {
  const snapshots = tutorialSnapshots.get(root) ?? new Map();
  tutorialSnapshots.set(root, snapshots);
  if (!snapshots.has(target)) {
    snapshots.set(target, {
      position: target.style.position,
      zIndex: target.style.zIndex,
      pointerEvents: target.style.pointerEvents,
      className: target.className
    });
  }
}

function tutorialCard(root) {
  const existing = root.querySelector('[data-tutorial-card]');
  if (existing) return existing;
  const overlay = overlayFor(root, 'tutorial');
  if (!overlay) return null;
  const card = root.ownerDocument.createElement('section');
  card.className = 'tutorial-card';
  card.dataset.tutorialCard = '';
  card.tabIndex = -1;
  const text = root.ownerDocument.createElement('p');
  text.dataset.tutorialText = '';
  card.append(text);
  overlay.append(card);
  return card;
}

function isCompact(root) {
  const view = root.ownerDocument?.defaultView;
  return typeof view?.matchMedia === 'function' && view.matchMedia('(max-width: 640px)').matches;
}

export function keepCompactTutorialInOverlay(overlay, card) {
  if (!overlay.contains(card)) overlay.append(card);
  card.dataset.tutorialPlacement = 'inline';
  return card;
}

function rememberTutorialOverlay(root, overlay, card) {
  const snapshots = tutorialOverlaySnapshots.get(root) ?? new Map();
  tutorialOverlaySnapshots.set(root, snapshots);
  if (snapshots.has(overlay)) return;
  snapshots.set(overlay, {
    overlayStyle: overlay.getAttribute('style'),
    placement: overlay.dataset.tutorialPlacement,
    cardStyle: card.getAttribute('style'),
    cardPlacement: card.dataset.tutorialPlacement
  });
}

function positionTutorialCard(root, overlay, card, target) {
  rememberTutorialOverlay(root, overlay, card);
  if (isCompact(root)) {
    keepCompactTutorialInOverlay(overlay, card);

    const rect = target.getBoundingClientRect();
    const view = root.ownerDocument.defaultView;
    const width = view.innerWidth;
    const height = view.innerHeight;
    const cardHeight = card.getBoundingClientRect().height || 160;
    const left = Math.max(12, Math.min(rect.left, width - 352));
    const top = rect.bottom + 12 + cardHeight <= height ? rect.bottom + 12 : Math.max(12, rect.top - cardHeight - 12);
    overlay.dataset.tutorialPlacement = 'inline';
    overlay.style.position = 'fixed';
    overlay.style.inset = 'auto';
    overlay.style.display = 'block';
    overlay.style.left = `${left}px`;
    overlay.style.top = `${top}px`;
    overlay.style.padding = '0';
    overlay.style.overflow = 'visible';
    overlay.style.background = 'transparent';
    overlay.style.pointerEvents = 'auto';
    card.style.position = 'static';
    card.style.margin = '0';
    card.style.pointerEvents = 'auto';
    return;
  }

  const rect = target.getBoundingClientRect();
  const view = root.ownerDocument.defaultView;
  const width = view.innerWidth;
  const height = view.innerHeight;
  const cardHeight = card.getBoundingClientRect().height || 160;
  const left = Math.max(12, Math.min(rect.left, width - 352));
  const top = rect.bottom + 12 + cardHeight <= height ? rect.bottom + 12 : Math.max(12, rect.top - cardHeight - 12);
  card.style.position = 'fixed';
  card.style.left = `${left}px`;
  card.style.top = `${top}px`;
}

export function renderTutorial(root, step) {
  const detail = tutorialSteps[step];
  if (!detail) return null;
  const target = root.querySelector(detail.target);
  const card = tutorialCard(root);
  const overlay = overlayFor(root, 'tutorial');
  if (!target || !card || !overlay) return null;

  clearTutorial(root);
  rememberTutorialTarget(root, target);
  target.classList.add('tutorial-spotlight');
  target.style.position = 'relative';
  target.style.zIndex = '72';
  target.style.pointerEvents = 'none';
  target.setAttribute('data-tutorial-active', 'true');
  target.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });

  const text = card.querySelector('[data-tutorial-text]');
  if (text) text.textContent = detail.text;
  const stepLabel = card.querySelector('[data-tutorial-step]');
  if (stepLabel) stepLabel.textContent = String(step + 1);
  positionTutorialCard(root, overlay, card, target);
  return { target, card };
}

export function clearTutorial(root) {
  const snapshots = tutorialSnapshots.get(root);
  if (snapshots) {
    for (const [target, previous] of snapshots) {
      target.style.position = previous.position;
      target.style.zIndex = previous.zIndex;
      target.style.pointerEvents = previous.pointerEvents;
      target.className = previous.className;
      target.removeAttribute('data-tutorial-active');
    }
    snapshots.clear();
  }

  const snapshotsForRoot = tutorialOverlaySnapshots.get(root);
  if (snapshotsForRoot) {
    for (const [overlay, previous] of snapshotsForRoot) {
      if (previous.overlayStyle === null) overlay.removeAttribute('style');
      else overlay.setAttribute('style', previous.overlayStyle);
      if (previous.placement === undefined) delete overlay.dataset.tutorialPlacement;
      else overlay.dataset.tutorialPlacement = previous.placement;

      const card = overlay.querySelector('[data-tutorial-card]');
      if (!card) continue;
      if (previous.cardStyle === null) card.removeAttribute('style');
      else card.setAttribute('style', previous.cardStyle);
      if (previous.cardPlacement === undefined) delete card.dataset.tutorialPlacement;
      else card.dataset.tutorialPlacement = previous.cardPlacement;
    }
    snapshotsForRoot.clear();
  }
}
