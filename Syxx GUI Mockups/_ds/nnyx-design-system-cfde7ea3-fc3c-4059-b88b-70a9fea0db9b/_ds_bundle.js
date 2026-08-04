/* @ds-bundle: {"format":3,"namespace":"NnyxDesignSystem_cfde7e","components":[{"name":"Logo","sourcePath":"components/brand/Logo.jsx"},{"name":"Button","sourcePath":"components/core/Button.jsx"},{"name":"Card","sourcePath":"components/core/Card.jsx"},{"name":"GlassPanel","sourcePath":"components/core/GlassPanel.jsx"},{"name":"SectionHeader","sourcePath":"components/core/GlassPanel.jsx"},{"name":"ProgressRing","sourcePath":"components/core/ProgressRing.jsx"},{"name":"StatCard","sourcePath":"components/core/StatCard.jsx"},{"name":"DetectionRow","sourcePath":"components/security/DetectionRow.jsx"},{"name":"SeverityBadge","sourcePath":"components/security/SeverityBadge.jsx"},{"name":"SidebarItem","sourcePath":"components/security/SidebarItem.jsx"},{"name":"SidebarSectionLabel","sourcePath":"components/security/SidebarItem.jsx"},{"name":"SigningBadge","sourcePath":"components/security/SigningBadge.jsx"},{"name":"StatusPill","sourcePath":"components/security/StatusPill.jsx"},{"name":"WorkerBadge","sourcePath":"components/security/StatusPill.jsx"}],"sourceHashes":{"components/brand/Logo.jsx":"867e31b69358","components/core/Button.jsx":"09d6a3188353","components/core/Card.jsx":"a5b347e9fc57","components/core/GlassPanel.jsx":"8d6f493b857b","components/core/ProgressRing.jsx":"9b7f96e12cc5","components/core/StatCard.jsx":"5746f790ad30","components/security/DetectionRow.jsx":"1e8d7df04b79","components/security/SeverityBadge.jsx":"633dc2fbc420","components/security/SidebarItem.jsx":"4c5551465124","components/security/SigningBadge.jsx":"c156f41a77f6","components/security/StatusPill.jsx":"b1fe84a5e1a2","ui_kits/nnyx-app/AppShell.jsx":"1413d28b020c","ui_kits/nnyx-app/Dashboard.jsx":"8fcfedbd88d0","ui_kits/nnyx-app/DetectionsScreen.jsx":"0812b419b10b","ui_kits/nnyx-app/ScanScreen.jsx":"011e7b6f2637","ui_kits/nnyx-app/TrustScreen.jsx":"a0e7e78a8f99","ui_kits/nnyx-app/data.jsx":"d550e1ed53bc"},"inlinedExternals":[],"unexposedExports":[]} */

(() => {

const __ds_ns = (window.NnyxDesignSystem_cfde7e = window.NnyxDesignSystem_cfde7e || {});

const __ds_scope = {};

(__ds_ns.__errors = __ds_ns.__errors || []);

// components/brand/Logo.jsx
try { (() => {
/**
 * Nnyx app logo — a rounded gradient tile holding a white shield with a
 * radar/magnifier lens. Faithful vector recreation of LogoView.swift
 * (which mirrors the shipped .icns icon).
 */
function Logo({
  size = 40,
  wordmark = false,
  label = "Nnyx",
  sublabel
}) {
  const tile = /*#__PURE__*/React.createElement("svg", {
    width: size,
    height: size,
    viewBox: "0 0 100 100",
    role: "img",
    "aria-label": "Nnyx logo",
    style: {
      display: "block",
      filter: "drop-shadow(0 1px 2px rgba(0,0,0,.35))"
    }
  }, /*#__PURE__*/React.createElement("defs", null, /*#__PURE__*/React.createElement("linearGradient", {
    id: "nnyxTile",
    x1: "0",
    y1: "0",
    x2: "1",
    y2: "1"
  }, /*#__PURE__*/React.createElement("stop", {
    offset: "0",
    stopColor: "var(--io-logo-1)"
  }), /*#__PURE__*/React.createElement("stop", {
    offset: "0.5",
    stopColor: "var(--io-logo-2)"
  }), /*#__PURE__*/React.createElement("stop", {
    offset: "1",
    stopColor: "var(--io-logo-3)"
  }))), /*#__PURE__*/React.createElement("rect", {
    x: "0",
    y: "0",
    width: "100",
    height: "100",
    rx: "22",
    fill: "url(#nnyxTile)"
  }), /*#__PURE__*/React.createElement("g", {
    transform: "translate(22 17)"
  }, /*#__PURE__*/React.createElement("path", {
    d: "M28 0 L56 13.6 C56 43.4 43.7 57.0 28 62 C12.3 57.0 0 43.4 0 13.6 Z",
    fill: "rgba(255,255,255,0.96)",
    stroke: "var(--io-logo-stroke)",
    strokeWidth: "1.8"
  })), /*#__PURE__*/React.createElement("g", {
    transform: "translate(50 46)"
  }, /*#__PURE__*/React.createElement("circle", {
    r: "13.5",
    fill: "none",
    stroke: "var(--io-logo-stroke)",
    strokeWidth: "5"
  }), /*#__PURE__*/React.createElement("path", {
    d: "M-13.5 0 H13.5 M0 -13.5 V13.5",
    stroke: "var(--io-logo-3)",
    strokeWidth: "2",
    opacity: "0.9"
  }), /*#__PURE__*/React.createElement("circle", {
    r: "2.5",
    fill: "var(--io-logo-lens)"
  })), /*#__PURE__*/React.createElement("path", {
    d: "M60 60 L71 71",
    stroke: "var(--io-logo-stroke)",
    strokeWidth: "6",
    strokeLinecap: "round"
  }));
  if (!wordmark) return tile;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 12
    }
  }, tile, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 2,
      lineHeight: 1
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-rounded)",
      fontWeight: "var(--weight-bold)",
      fontSize: size * 0.45,
      color: "var(--io-text)"
    }
  }, label), sublabel && /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-sans)",
      fontSize: "var(--text-caption)",
      color: "var(--io-text-secondary)"
    }
  }, sublabel)));
}
Object.assign(__ds_scope, { Logo });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/brand/Logo.jsx", error: String((e && e.message) || e) }); }

// components/core/Button.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const BG = {
  primary: "var(--io-blue)",
  secondary: "var(--io-surface-elevated)",
  success: "var(--io-success)",
  danger: "var(--io-critical)",
  ghost: "transparent"
};
const FG = {
  primary: "#ffffff",
  secondary: "var(--io-text)",
  success: "#ffffff",
  danger: "#ffffff",
  ghost: "var(--io-blue)"
};

/**
 * Nnyx primary control. Solid cyan by default; hover lifts (scale 1.02 +
 * brightness). Disabled drops to a muted surface so it stays legible.
 */
function Button({
  variant = "primary",
  size = "md",
  icon = null,
  disabled = false,
  fullWidth = false,
  children,
  style,
  ...rest
}) {
  const [hover, setHover] = React.useState(false);
  const pad = size === "sm" ? "6px 12px" : size === "lg" ? "12px 24px" : "10px 20px";
  const fontSize = size === "sm" ? "var(--text-caption)" : "var(--text-body)";
  return /*#__PURE__*/React.createElement("button", _extends({
    type: "button",
    disabled: disabled,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      display: "inline-flex",
      alignItems: "center",
      justifyContent: "center",
      gap: 8,
      width: fullWidth ? "100%" : "auto",
      padding: pad,
      fontFamily: "var(--font-sans)",
      fontSize,
      fontWeight: "var(--weight-semibold)",
      lineHeight: 1,
      color: disabled ? "var(--io-text-secondary)" : FG[variant],
      background: disabled ? "var(--io-surface-elevated)" : BG[variant],
      border: variant === "ghost" ? "1px solid transparent" : "none",
      borderRadius: "var(--radius-button)",
      cursor: disabled ? "not-allowed" : "pointer",
      transform: hover && !disabled ? "scale(1.02)" : "scale(1)",
      filter: hover && !disabled ? "brightness(1.1)" : "none",
      transition: "transform var(--dur-base) var(--ease-spring), filter var(--dur-base) var(--ease-smooth)",
      ...style
    }
  }, rest), icon, children);
}
Object.assign(__ds_scope, { Button });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Button.jsx", error: String((e && e.message) || e) }); }

// components/core/Card.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Standard Nnyx card surface (IoCard): slate fill, 12px radius, soft drop
 * shadow. Pass `glow` to add an accent halo, `tint` to fill with a faint
 * semantic wash + matching border (used by stat tiles when active).
 */
function Card({
  glow,
  tint,
  padding = 16,
  radius = 12,
  children,
  style,
  ...rest
}) {
  const tintColor = tint ? `var(--io-${tint})` : null;
  return /*#__PURE__*/React.createElement("div", _extends({
    style: {
      background: tintColor ? `color-mix(in srgb, ${tintColor} 8%, var(--io-surface))` : "var(--io-surface)",
      border: `1px solid ${tintColor ? `color-mix(in srgb, ${tintColor} 30%, transparent)` : "transparent"}`,
      borderRadius: radius,
      padding,
      boxShadow: glow ? `var(--shadow-card), 0 0 12px color-mix(in srgb, var(--io-${glow}) 30%, transparent)` : "var(--shadow-card)",
      color: "var(--io-text)",
      ...style
    }
  }, rest), children);
}
Object.assign(__ds_scope, { Card });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Card.jsx", error: String((e && e.message) || e) }); }

// components/core/GlassPanel.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Translucent container with a hairline white edge (GlassPanel). In the
 * shipped app this resolves to the same slate surface as Card for palette
 * consistency, but keeps the distinctive 16px radius + 1px white-10% stroke.
 */
function GlassPanel({
  radius = 16,
  padding = 16,
  children,
  style,
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    style: {
      background: "var(--io-surface)",
      borderRadius: radius,
      border: "1px solid var(--io-hairline)",
      boxShadow: "var(--shadow-panel)",
      padding,
      color: "var(--io-text)",
      ...style
    }
  }, rest), children);
}

/** Icon + title row used as a section header inside panels. */
function SectionHeader({
  icon,
  title,
  color = "var(--io-blue)",
  style
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8,
      ...style
    }
  }, icon && /*#__PURE__*/React.createElement("span", {
    style: {
      color,
      display: "inline-flex"
    }
  }, icon), /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-sans)",
      fontSize: "var(--text-title3)",
      fontWeight: "var(--weight-semibold)",
      color: "var(--io-text)"
    }
  }, title));
}
Object.assign(__ds_scope, { GlassPanel, SectionHeader });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/GlassPanel.jsx", error: String((e && e.message) || e) }); }

// components/core/ProgressRing.jsx
try { (() => {
/**
 * Circular threat/progress ring with a severity-graded gradient stroke
 * (IoProgressRing). 0 → green, rising through amber/orange to red.
 * Optionally renders a centered value + caption.
 */
function ProgressRing({
  progress = 0,
  size = 120,
  lineWidth = 8,
  value = null,
  caption = null,
  glow = true
}) {
  const p = Math.max(0, Math.min(1, progress));
  const r = (size - lineWidth) / 2;
  const c = 2 * Math.PI * r;
  const stops = p < 0.25 ? ["var(--io-success)", "var(--io-success)"] : p < 0.5 ? ["var(--io-success)", "var(--io-warning)"] : p < 0.8 ? ["var(--io-warning)", "var(--io-orange)"] : ["var(--io-orange)", "var(--io-critical)"];
  const tone = p >= 0.8 ? "var(--io-critical)" : p >= 0.5 ? "var(--io-warning)" : p >= 0.25 ? "var(--io-orange)" : "var(--io-success)";
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: "relative",
      width: size,
      height: size
    }
  }, /*#__PURE__*/React.createElement("svg", {
    width: size,
    height: size,
    style: glow ? {
      filter: `drop-shadow(0 0 12px color-mix(in srgb, ${tone} 45%, transparent))`
    } : undefined
  }, /*#__PURE__*/React.createElement("defs", null, /*#__PURE__*/React.createElement("linearGradient", {
    id: `ring-${size}-${Math.round(p * 100)}`,
    x1: "0",
    y1: "0",
    x2: "1",
    y2: "1"
  }, /*#__PURE__*/React.createElement("stop", {
    offset: "0",
    stopColor: stops[0]
  }), /*#__PURE__*/React.createElement("stop", {
    offset: "1",
    stopColor: stops[1]
  }))), /*#__PURE__*/React.createElement("circle", {
    cx: size / 2,
    cy: size / 2,
    r: r,
    fill: "none",
    stroke: "var(--io-surface-elevated)",
    strokeWidth: lineWidth
  }), /*#__PURE__*/React.createElement("circle", {
    cx: size / 2,
    cy: size / 2,
    r: r,
    fill: "none",
    stroke: `url(#ring-${size}-${Math.round(p * 100)})`,
    strokeWidth: lineWidth,
    strokeLinecap: "round",
    strokeDasharray: c,
    strokeDashoffset: c * (1 - p),
    transform: `rotate(-90 ${size / 2} ${size / 2})`,
    style: {
      transition: "stroke-dashoffset var(--dur-slow) var(--ease-smooth)"
    }
  })), (value != null || caption) && /*#__PURE__*/React.createElement("div", {
    style: {
      position: "absolute",
      inset: 0,
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      justifyContent: "center",
      gap: 4
    }
  }, value != null && /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-rounded)",
      fontWeight: "var(--weight-bold)",
      fontSize: size * 0.3,
      color: tone,
      fontVariantNumeric: "tabular-nums"
    }
  }, value), caption && /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: "var(--text-eyebrow)",
      fontWeight: "var(--weight-bold)",
      letterSpacing: "var(--tracking-eyebrow)",
      color: "var(--io-text-secondary)",
      textTransform: "uppercase"
    }
  }, caption)));
}
Object.assign(__ds_scope, { ProgressRing });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/ProgressRing.jsx", error: String((e && e.message) || e) }); }

// components/core/StatCard.jsx
try { (() => {
/**
 * Big-number stat tile (IoStatCard). When `value` > 0 it lights up in its
 * semantic color with a faint wash + border; at 0 it stays muted slate.
 * Used for the dashboard severity breakdown (Critical / High / Medium / Low).
 */
function StatCard({
  value,
  label,
  color = "blue"
}) {
  const [hover, setHover] = React.useState(false);
  const active = Number(value) > 0;
  const c = `var(--io-${color})`;
  return /*#__PURE__*/React.createElement("div", {
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      flex: 1,
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      gap: 8,
      padding: "20px 16px",
      borderRadius: "var(--radius-card)",
      background: active ? `color-mix(in srgb, ${c} 8%, var(--io-surface))` : "var(--io-surface)",
      border: `1px solid ${active ? `color-mix(in srgb, ${c} 30%, transparent)` : "var(--io-border)"}`,
      transform: hover ? "scale(1.03)" : "scale(1)",
      transition: "transform var(--dur-base) var(--ease-spring)"
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-rounded)",
      fontWeight: "var(--weight-bold)",
      fontSize: 36,
      lineHeight: 1,
      color: active ? c : "var(--io-text-secondary)",
      fontVariantNumeric: "tabular-nums"
    }
  }, value), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: "var(--text-caption)",
      textTransform: "uppercase",
      letterSpacing: "var(--tracking-badge)",
      color: "var(--io-text-secondary)"
    }
  }, label));
}
Object.assign(__ds_scope, { StatCard });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/StatCard.jsx", error: String((e && e.message) || e) }); }

// components/security/SeverityBadge.jsx
try { (() => {
const SEV = {
  critical: {
    color: "var(--io-critical)",
    label: "CRITICAL"
  },
  high: {
    color: "var(--io-high)",
    label: "HIGH"
  },
  medium: {
    color: "var(--io-warning)",
    label: "MEDIUM"
  },
  low: {
    color: "var(--io-success)",
    label: "LOW"
  },
  info: {
    color: "var(--io-info)",
    label: "INFO"
  }
};

/**
 * Threat severity badge (IoSeverityBadge): a colored dot + uppercase label
 * on a faint wash of the same color. Maps 1:1 to ThreatSeverity.
 */
function SeverityBadge({
  severity = "info"
}) {
  const s = SEV[severity] || SEV.info;
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: "inline-flex",
      alignItems: "center",
      gap: 4,
      padding: "4px 8px",
      borderRadius: "var(--radius-chip)",
      background: `color-mix(in srgb, ${s.color} 15%, transparent)`
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 6,
      height: 6,
      borderRadius: "50%",
      background: s.color
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: "var(--text-eyebrow)",
      fontWeight: "var(--weight-bold)",
      letterSpacing: "var(--tracking-badge)",
      color: s.color
    }
  }, s.label));
}
Object.assign(__ds_scope, { SeverityBadge });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/security/SeverityBadge.jsx", error: String((e && e.message) || e) }); }

// components/security/SidebarItem.jsx
try { (() => {
/**
 * Sidebar navigation item (IoSidebarItem). Selected = solid cyan fill +
 * white semibold label; hover = faint slate fill + 1.02 lift. Optional
 * trailing count badge (a capsule).
 */
function SidebarItem({
  label,
  icon = null,
  selected = false,
  badge = 0,
  onClick
}) {
  const [hover, setHover] = React.useState(false);
  return /*#__PURE__*/React.createElement("button", {
    type: "button",
    onClick: onClick,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      display: "flex",
      alignItems: "center",
      gap: 12,
      width: "100%",
      padding: "10px 12px",
      border: "none",
      textAlign: "left",
      cursor: "pointer",
      borderRadius: "var(--radius-button)",
      background: selected ? "var(--io-blue)" : hover ? "var(--io-surface)" : "transparent",
      color: selected ? "#fff" : "var(--io-text)",
      transform: hover && !selected ? "scale(1.02)" : "scale(1)",
      transition: "transform var(--dur-base) var(--ease-spring), background var(--dur-fast) var(--ease-smooth)",
      fontFamily: "var(--font-sans)"
    }
  }, icon && /*#__PURE__*/React.createElement("span", {
    style: {
      display: "inline-flex",
      opacity: selected ? 1 : 0.85
    }
  }, icon), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1,
      fontSize: "var(--text-body)",
      fontWeight: selected ? "var(--weight-semibold)" : "var(--weight-regular)",
      whiteSpace: "nowrap",
      overflow: "hidden",
      textOverflow: "ellipsis"
    }
  }, label), badge > 0 && /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: "var(--text-small)",
      fontWeight: "var(--weight-bold)",
      fontVariantNumeric: "tabular-nums",
      padding: "2px 6px",
      borderRadius: "var(--radius-pill)",
      background: selected ? "rgba(255,255,255,0.25)" : "var(--io-surface-elevated)",
      color: selected ? "#fff" : "var(--io-text-secondary)"
    }
  }, badge));
}

/** Uppercase micro-label that groups sidebar sections ("NAVIGATION"). */
function SidebarSectionLabel({
  children
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: "var(--text-eyebrow)",
      fontWeight: "var(--weight-bold)",
      letterSpacing: "var(--tracking-eyebrow)",
      color: "var(--io-text-secondary)",
      padding: "0 12px 4px"
    }
  }, children);
}
Object.assign(__ds_scope, { SidebarItem, SidebarSectionLabel });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/security/SidebarItem.jsx", error: String((e && e.message) || e) }); }

// components/security/SigningBadge.jsx
try { (() => {
/**
 * Code-signing source badge. Mirrors SigningSource in IoDesignSystem.swift —
 * the app shows each finding's provenance with an emoji + label on a wash of
 * the source's color. (Emoji are intentional here; this is one of the very
 * few places Nnyx uses them.)
 */
const SOURCES = {
  apple: {
    emoji: "🔐",
    label: "Apple System",
    color: "var(--io-sign-apple)"
  },
  appstore: {
    emoji: "🛒",
    label: "Mac App Store",
    color: "var(--io-sign-appstore)"
  },
  developer: {
    emoji: "📜",
    label: "Developer ID",
    color: "var(--io-sign-developer)"
  },
  development: {
    emoji: "🛠️",
    label: "Development",
    color: "var(--io-sign-dev)"
  },
  unsigned: {
    emoji: "⚠️",
    label: "Unsigned",
    color: "var(--io-sign-unsigned)"
  }
};
function SigningBadge({
  source = "unsigned",
  emoji = true
}) {
  const s = SOURCES[source] || SOURCES.unsigned;
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: "inline-flex",
      alignItems: "center",
      gap: 4,
      padding: "2px 6px",
      borderRadius: "var(--radius-badge)",
      background: `color-mix(in srgb, ${s.color} 12%, transparent)`,
      fontSize: "var(--text-badge)",
      fontWeight: "var(--weight-bold)",
      color: s.color
    }
  }, emoji && /*#__PURE__*/React.createElement("span", {
    "aria-hidden": "true"
  }, s.emoji), s.label);
}
Object.assign(__ds_scope, { SigningBadge });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/security/SigningBadge.jsx", error: String((e && e.message) || e) }); }

// components/security/DetectionRow.jsx
try { (() => {
const BAR = {
  critical: "var(--io-critical)",
  high: "var(--io-high)",
  medium: "var(--io-warning)",
  low: "var(--io-success)",
  info: "var(--io-info)"
};

/**
 * A single finding row (IoDetectionRow): a left severity bar, then a header
 * line with severity badge + name + signing badge + scanner-module chip,
 * over a two-line evidence string. Hover gives a faint slate tint.
 */
function DetectionRow({
  detection = {},
  onClick
}) {
  const {
    severity = "info",
    name = "Unknown finding",
    signing = "unsigned",
    scanner = "scanner",
    evidence = ""
  } = detection;
  const [hover, setHover] = React.useState(false);
  return /*#__PURE__*/React.createElement("div", {
    onClick: onClick,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      display: "flex",
      gap: 16,
      alignItems: "stretch",
      padding: "12px 16px 12px 0",
      cursor: onClick ? "pointer" : "default",
      background: hover ? "var(--io-surface)" : "transparent",
      transition: "background var(--dur-fast) var(--ease-smooth)"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: 4,
      borderRadius: 2,
      background: BAR[severity],
      flexShrink: 0
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 6,
      minWidth: 0,
      flex: 1
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8,
      flexWrap: "wrap"
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.SeverityBadge, {
    severity: severity
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: "var(--text-body)",
      fontWeight: "var(--weight-semibold)",
      color: "var(--io-text)",
      whiteSpace: "nowrap",
      overflow: "hidden",
      textOverflow: "ellipsis"
    }
  }, name), /*#__PURE__*/React.createElement(__ds_scope.SigningBadge, {
    source: signing
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: "var(--text-eyebrow)",
      color: "var(--io-text-secondary)",
      padding: "2px 6px",
      borderRadius: "var(--radius-badge)",
      background: "var(--io-surface-elevated)",
      fontFamily: "var(--font-mono)"
    }
  }, scanner)), evidence && /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: "var(--text-caption)",
      color: "var(--io-text-secondary)",
      display: "-webkit-box",
      WebkitLineClamp: 2,
      WebkitBoxOrient: "vertical",
      overflow: "hidden"
    }
  }, evidence)));
}
Object.assign(__ds_scope, { DetectionRow });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/security/DetectionRow.jsx", error: String((e && e.message) || e) }); }

// components/security/StatusPill.jsx
try { (() => {
/**
 * Status pill — a small capsule with a colored dot + label, optionally
 * glowing. Used for "Protection On", service "RUNNING"/"HEALTHY" states.
 * Set `solid` for the squared 4px-radius variant used inside service cards.
 */
function StatusPill({
  label,
  color = "success",
  glow = false,
  solid = false
}) {
  const c = `var(--io-${color})`;
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: "inline-flex",
      alignItems: "center",
      gap: 4,
      padding: solid ? "3px 6px" : "4px 8px",
      borderRadius: solid ? "var(--radius-badge)" : "var(--radius-chip)",
      background: `color-mix(in srgb, ${c} ${solid ? 15 : 12}%, transparent)`,
      boxShadow: glow ? `0 0 4px color-mix(in srgb, ${c} 40%, transparent)` : "none"
    }
  }, !solid && /*#__PURE__*/React.createElement("span", {
    style: {
      width: 6,
      height: 6,
      borderRadius: "50%",
      background: c
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: solid ? "var(--text-badge)" : "var(--text-eyebrow)",
      fontWeight: "var(--weight-bold)",
      color: c,
      letterSpacing: "var(--tracking-badge)"
    }
  }, label));
}
const WORKERS = {
  systemDaemon: {
    label: "System Daemon",
    color: "#5e5ce6"
  },
  userAgent: {
    label: "User Agent",
    color: "var(--io-success)"
  },
  backgroundHelper: {
    label: "Background Helper",
    color: "var(--io-high)"
  }
};

/**
 * Worker badge — capsule identifying which privileged context did the work
 * (system daemon / user agent / background helper), shown during scans.
 */
function WorkerBadge({
  worker = "userAgent",
  icon = null
}) {
  const w = WORKERS[worker] || WORKERS.userAgent;
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: "inline-flex",
      alignItems: "center",
      gap: 4,
      padding: "3px 8px",
      borderRadius: "var(--radius-pill)",
      background: `color-mix(in srgb, ${w.color} 12%, transparent)`,
      color: w.color,
      fontSize: "var(--text-small)",
      fontWeight: "var(--weight-medium)"
    }
  }, icon, w.label);
}
Object.assign(__ds_scope, { StatusPill, WorkerBadge });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/security/StatusPill.jsx", error: String((e && e.message) || e) }); }

// ui_kits/nnyx-app/AppShell.jsx
try { (() => {
// Nnyx app shell — sidebar nav + routed detail pane.
const {
  useState
} = React;
const DS = window.NnyxDesignSystem_cfde7e;
function Icon({
  name,
  size = 16,
  color,
  style
}) {
  const C = window.LucideReact && window.LucideReact[name];
  return C ? /*#__PURE__*/React.createElement(C, {
    size: size,
    color: color,
    style: style,
    absoluteStrokeWidth: false,
    strokeWidth: 2
  }) : null;
}
window.Icon = Icon;
function Sidebar({
  section,
  setSection
}) {
  const {
    Logo,
    SidebarItem,
    SidebarSectionLabel,
    StatusPill
  } = DS;
  const render = items => items.map(it => /*#__PURE__*/React.createElement(SidebarItem, {
    key: it.id,
    label: it.label,
    icon: /*#__PURE__*/React.createElement(Icon, {
      name: it.icon,
      size: 16
    }),
    badge: it.badge,
    selected: section === it.id,
    onClick: () => setSection(it.id)
  }));
  return /*#__PURE__*/React.createElement("aside", {
    style: {
      width: 280,
      flexShrink: 0,
      background: "var(--io-bg)",
      borderRight: "1px solid var(--io-border)",
      display: "flex",
      flexDirection: "column",
      height: "100%"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      padding: "20px 16px 16px"
    }
  }, /*#__PURE__*/React.createElement(Logo, {
    size: 44,
    wordmark: true,
    label: "Nnyx",
    sublabel: "macOS Security"
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      height: 1,
      background: "var(--io-border)",
      margin: "0 16px"
    }
  }), /*#__PURE__*/React.createElement("nav", {
    style: {
      flex: 1,
      overflowY: "auto",
      padding: "12px",
      display: "flex",
      flexDirection: "column",
      gap: 20
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 4
    }
  }, /*#__PURE__*/React.createElement(SidebarSectionLabel, null, "Navigation"), render(window.NNYX_NAV.primary)), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 4
    }
  }, /*#__PURE__*/React.createElement(SidebarSectionLabel, null, "Threat Intel"), render(window.NNYX_NAV.intel))), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: "12px 16px 16px",
      borderTop: "1px solid var(--io-border)",
      display: "flex",
      alignItems: "center",
      gap: 8
    }
  }, /*#__PURE__*/React.createElement(StatusPill, {
    label: "Protection On",
    color: "success",
    glow: true
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), /*#__PURE__*/React.createElement("button", {
    style: footBtn,
    title: "Help"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "Lightbulb",
    size: 13,
    color: "var(--io-text-secondary)"
  })), /*#__PURE__*/React.createElement("button", {
    style: footBtn,
    title: "Open Logs"
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "FolderOpen",
    size: 13,
    color: "var(--io-text-secondary)"
  }))));
}
const footBtn = {
  display: "inline-flex",
  alignItems: "center",
  justifyContent: "center",
  width: 26,
  height: 26,
  background: "var(--io-surface)",
  border: "1px solid var(--io-border)",
  borderRadius: 6,
  cursor: "pointer"
};
function Toolbar({
  title
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      height: 44,
      flexShrink: 0,
      display: "flex",
      alignItems: "center",
      gap: 10,
      padding: "0 20px",
      borderBottom: "1px solid var(--io-border)",
      background: "var(--io-bg)"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: 8
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 12,
      height: 12,
      borderRadius: "50%",
      background: "#ff5f57"
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      width: 12,
      height: 12,
      borderRadius: "50%",
      background: "#febc2e"
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      width: 12,
      height: 12,
      borderRadius: "50%",
      background: "#28c840"
    }
  })), /*#__PURE__*/React.createElement(Icon, {
    name: "PanelLeft",
    size: 16,
    color: "var(--io-blue)",
    style: {
      marginLeft: 8
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 13,
      fontWeight: 600,
      color: "var(--io-text)"
    }
  }, title));
}
function AppShell() {
  const [section, setSection] = useState("dashboard");
  const titles = {
    dashboard: "Dashboard",
    scan: "Scan",
    detections: "Detections",
    incidents: "Incidents",
    remediation: "Remediation",
    intel: "Threat Intel",
    trust: "Trust Bank",
    logs: "Logs & History",
    settings: "Settings"
  };
  let pane;
  if (section === "dashboard") pane = /*#__PURE__*/React.createElement(Dashboard, {
    go: setSection
  });else if (section === "scan") pane = /*#__PURE__*/React.createElement(ScanScreen, null);else if (section === "detections") pane = /*#__PURE__*/React.createElement(DetectionsScreen, null);else if (section === "trust") pane = /*#__PURE__*/React.createElement(TrustScreen, null);else pane = /*#__PURE__*/React.createElement(Placeholder, {
    title: titles[section]
  });
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      height: "100vh",
      width: "100vw",
      overflow: "hidden",
      background: "var(--io-bg)",
      color: "var(--io-text)",
      fontFamily: "var(--font-sans)"
    }
  }, /*#__PURE__*/React.createElement(Sidebar, {
    section: section,
    setSection: setSection
  }), /*#__PURE__*/React.createElement("main", {
    style: {
      flex: 1,
      display: "flex",
      flexDirection: "column",
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement(Toolbar, {
    title: titles[section]
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      overflowY: "auto"
    }
  }, pane)));
}
function Placeholder({
  title
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      height: "100%",
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      justifyContent: "center",
      gap: 12,
      color: "var(--io-text-secondary)"
    }
  }, /*#__PURE__*/React.createElement(Icon, {
    name: "Construction",
    size: 40,
    color: "var(--io-text-secondary)"
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 16,
      fontWeight: 600,
      color: "var(--io-text)"
    }
  }, title), /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 13
    }
  }, "This pane is part of the full app \u2014 not mocked in this kit."));
}
Object.assign(window, {
  Sidebar,
  Toolbar,
  AppShell,
  Placeholder
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/nnyx-app/AppShell.jsx", error: String((e && e.message) || e) }); }

// ui_kits/nnyx-app/Dashboard.jsx
try { (() => {
// Dashboard — mirrors IoDashboardView: header, integrity self-check,
// real-time services, threat ring, severity breakdown, recent findings.
const DASH_DS = window.NnyxDesignSystem_cfde7e;
function SectionTitle({
  icon,
  children
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8,
      marginBottom: 12
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: icon,
    size: 17,
    color: "var(--io-blue)"
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 18,
      fontWeight: 600,
      color: "var(--io-text)"
    }
  }, children));
}
function ServiceCard({
  icon,
  iconColor,
  title,
  status,
  statusColor,
  rows
}) {
  const {
    StatusPill
  } = DASH_DS;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minWidth: 0,
      padding: 16,
      background: "var(--io-surface)",
      borderRadius: 12,
      border: "1px solid var(--io-border)"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8,
      marginBottom: 16
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: icon,
    size: 16,
    color: iconColor
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 13,
      fontWeight: 700,
      color: "var(--io-text)"
    }
  }, title), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), /*#__PURE__*/React.createElement(StatusPill, {
    label: status,
    color: statusColor,
    solid: true
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      justifyContent: "space-between",
      gap: 12
    }
  }, rows.map((r, i) => /*#__PURE__*/React.createElement("div", {
    key: i,
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 2,
      alignItems: r.align || "flex-start"
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 9,
      fontWeight: 600,
      letterSpacing: ".03em",
      color: "var(--io-text-secondary)",
      textTransform: "uppercase"
    }
  }, r.label), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: r.big ? 18 : 12,
      fontWeight: r.big ? 700 : 600,
      fontFamily: r.mono ? "var(--font-mono)" : "var(--font-rounded)",
      color: "var(--io-text)"
    }
  }, r.value)))));
}
function Dashboard({
  go
}) {
  const {
    Logo,
    Button,
    Card,
    StatCard,
    ProgressRing,
    DetectionRow
  } = DASH_DS;
  const dets = window.NNYX_DETECTIONS;
  const active = dets.filter(d => d.severity !== "low");
  const score = 38;
  const counts = {
    critical: dets.filter(d => d.severity === "critical").length,
    high: dets.filter(d => d.severity === "high").length,
    medium: dets.filter(d => d.severity === "medium").length,
    low: dets.filter(d => d.severity === "low").length
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      padding: 32,
      display: "flex",
      flexDirection: "column",
      gap: 24
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 16
    }
  }, /*#__PURE__*/React.createElement(Logo, {
    size: 64
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 4
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 22,
      fontWeight: 600
    }
  }, "Nnyx: macOS Security"), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      color: "var(--io-text-secondary)"
    }
  }, "Version 1.0.0 \xB7 Last scan: 6 minutes ago")), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), /*#__PURE__*/React.createElement(Button, {
    variant: "success",
    icon: /*#__PURE__*/React.createElement(window.Icon, {
      name: "Zap",
      size: 14
    })
  }, "Quick Scan"), /*#__PURE__*/React.createElement(Button, {
    variant: "primary",
    icon: /*#__PURE__*/React.createElement(window.Icon, {
      name: "ShieldCheck",
      size: 14
    })
  }, "Full Scan")), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement(SectionTitle, {
    icon: "LockKeyhole"
  }, "Anti-Tampering Self-Check"), /*#__PURE__*/React.createElement(Card, {
    padding: 20
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: 16,
      alignItems: "flex-start"
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: "BadgeCheck",
    size: 32,
    color: "var(--io-success)",
    style: {
      filter: "drop-shadow(0 0 8px color-mix(in srgb,var(--io-success) 45%,transparent))"
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 6
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 14,
      fontWeight: 600
    }
  }, "App integrity verified"), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      fontFamily: "var(--font-mono)",
      color: "#fff",
      fontWeight: 700
    }
  }, "$ codesign --verify --deep /Applications/Nnyx.app"), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      color: "var(--io-text-secondary)"
    }
  }, "HMAC-signed manifest matches sealed state \xB7 NonceLedger replay window clean \xB7 0 unexpected mutations."))))), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement(SectionTitle, {
    icon: "ShieldHalf"
  }, "Real-Time Protection & Services"), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: 16
    }
  }, /*#__PURE__*/React.createElement(ServiceCard, {
    icon: "Activity",
    iconColor: "var(--io-electric)",
    title: "FSEvents File Monitor",
    status: "RUNNING",
    statusColor: "success",
    rows: [{
      label: "Events Count",
      value: "24,711",
      big: true
    }, {
      label: "Last Active",
      value: "12s ago",
      align: "flex-end"
    }]
  }), /*#__PURE__*/React.createElement(ServiceCard, {
    icon: "Cpu",
    iconColor: "var(--io-blue)",
    title: "Privileged Helper (XPC)",
    status: "HEALTHY",
    statusColor: "success",
    rows: [{
      label: "Helper Version",
      value: "1.2.0",
      mono: true
    }, {
      label: "Auth",
      value: "Admin-signed",
      align: "flex-end"
    }]
  }))), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement(SectionTitle, {
    icon: "ShieldAlert"
  }, "System Protection Status"), /*#__PURE__*/React.createElement(Card, {
    padding: 24
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 40
    }
  }, /*#__PURE__*/React.createElement(ProgressRing, {
    progress: score / 100,
    size: 140,
    lineWidth: 10,
    value: score,
    caption: "THREAT"
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 12,
      flex: 1
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 18,
      fontWeight: 600,
      color: "var(--io-orange)"
    }
  }, "Investigate Findings"), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 14,
      color: "var(--io-text-secondary)"
    }
  }, "2,481 files scanned in 3.4s \xB7 2 active findings, 1 suppressed false positive.")), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      gap: 8
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 10,
      textTransform: "uppercase",
      letterSpacing: ".06em",
      color: "var(--io-text-secondary)"
    }
  }, "Trend"), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 36,
      color: "var(--io-success)"
    }
  }, "\u2193"))))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: 16
    }
  }, /*#__PURE__*/React.createElement(StatCard, {
    value: counts.critical,
    label: "Critical",
    color: "critical"
  }), /*#__PURE__*/React.createElement(StatCard, {
    value: counts.high,
    label: "High",
    color: "high"
  }), /*#__PURE__*/React.createElement(StatCard, {
    value: counts.medium,
    label: "Medium",
    color: "warning"
  }), /*#__PURE__*/React.createElement(StatCard, {
    value: counts.low,
    label: "Low",
    color: "success"
  })), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8,
      marginBottom: 12
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: "ListChecks",
    size: 17,
    color: "var(--io-blue)"
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 18,
      fontWeight: 600
    }
  }, "Recent Findings"), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      color: "var(--io-text-secondary)"
    }
  }, active.length, " active")), /*#__PURE__*/React.createElement(Card, {
    padding: 0
  }, dets.slice(0, 4).map((d, i) => /*#__PURE__*/React.createElement("div", {
    key: d.id
  }, /*#__PURE__*/React.createElement(DetectionRow, {
    detection: d,
    onClick: () => go("detections")
  }), i < 3 && /*#__PURE__*/React.createElement("div", {
    style: {
      height: 1,
      background: "var(--io-border)",
      margin: "0 16px"
    }
  }))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      justifyContent: "center",
      alignItems: "center",
      gap: 8,
      padding: 12,
      borderTop: "1px solid var(--io-border)"
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      color: "var(--io-text-secondary)"
    }
  }, "+ 1 more"), /*#__PURE__*/React.createElement("span", {
    style: {
      color: "var(--io-text-secondary)"
    }
  }, "\xB7"), /*#__PURE__*/React.createElement("button", {
    onClick: () => go("detections"),
    style: {
      background: "none",
      border: "none",
      color: "var(--io-blue)",
      fontSize: 12,
      cursor: "pointer"
    }
  }, "View All")))));
}
window.Dashboard = Dashboard;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/nnyx-app/Dashboard.jsx", error: String((e && e.message) || e) }); }

// ui_kits/nnyx-app/DetectionsScreen.jsx
try { (() => {
// Detections — master/detail. Filterable list on the left, full finding
// detail (evidence, MITRE, remediation) on the right. Mirrors DetectionsView.
const {
  useState: useDetState
} = React;
const DET_DS = window.NnyxDesignSystem_cfde7e;
function FilterChip({
  label,
  active,
  color,
  onClick
}) {
  return /*#__PURE__*/React.createElement("button", {
    onClick: onClick,
    style: {
      fontSize: 11,
      padding: "3px 9px",
      borderRadius: 6,
      cursor: "pointer",
      background: active ? `color-mix(in srgb,${color} 18%,transparent)` : "transparent",
      border: `1px solid ${active ? color : "var(--io-border)"}`,
      color: active ? color : "var(--io-text-secondary)",
      fontWeight: active ? 700 : 400
    }
  }, label);
}
function InfoBlock({
  icon,
  title,
  children,
  accent
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 8
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 7
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: icon,
    size: 15,
    color: accent || "var(--io-text)"
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 14,
      fontWeight: 600,
      color: accent || "var(--io-text)"
    }
  }, title)), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: 12,
      borderRadius: 8,
      background: "var(--io-surface)",
      fontSize: 13,
      lineHeight: 1.5,
      color: accent || "var(--io-text-secondary)"
    }
  }, children));
}
function DetectionsScreen() {
  const {
    DetectionRow,
    SeverityBadge,
    SigningBadge,
    ProgressRing
  } = DET_DS;
  const dets = window.NNYX_DETECTIONS;
  const [filter, setFilter] = useDetState(null);
  const [selId, setSelId] = useDetState("d1");
  const shown = dets.filter(d => !filter || d.severity === filter);
  const sel = dets.find(d => d.id === selId) || shown[0];
  const colors = {
    critical: "var(--io-critical)",
    high: "var(--io-high)",
    medium: "var(--io-warning)",
    low: "var(--io-success)"
  };
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      height: "100%"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: 420,
      flexShrink: 0,
      borderRight: "1px solid var(--io-border)",
      display: "flex",
      flexDirection: "column"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      padding: "10px 16px",
      borderBottom: "1px solid var(--io-border)",
      background: "color-mix(in srgb,var(--io-orange) 10%,transparent)"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 14,
      fontWeight: 700,
      color: "var(--io-orange)"
    }
  }, "2 active findings need review"), /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 12,
      color: "var(--io-text-secondary)",
      marginTop: 2
    }
  }, "1 suppressed false positive \xB7 0 informational \xB7 score 38/100")), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 6,
      padding: "8px 16px",
      borderBottom: "1px solid var(--io-border)"
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      color: "var(--io-text-secondary)"
    }
  }, "Filter:"), /*#__PURE__*/React.createElement(FilterChip, {
    label: "All",
    active: !filter,
    color: "var(--io-blue)",
    onClick: () => setFilter(null)
  }), ["critical", "high", "medium", "low"].map(s => /*#__PURE__*/React.createElement(FilterChip, {
    key: s,
    label: s[0].toUpperCase() + s.slice(1),
    active: filter === s,
    color: colors[s],
    onClick: () => setFilter(filter === s ? null : s)
  }))), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      overflowY: "auto"
    }
  }, shown.map((d, i) => /*#__PURE__*/React.createElement("div", {
    key: d.id,
    onClick: () => setSelId(d.id),
    style: {
      background: selId === d.id ? "var(--io-surface)" : "transparent",
      borderLeft: selId === d.id ? "2px solid var(--io-blue)" : "2px solid transparent"
    }
  }, /*#__PURE__*/React.createElement(DetectionRow, {
    detection: d,
    onClick: () => setSelId(d.id)
  }), i < shown.length - 1 && /*#__PURE__*/React.createElement("div", {
    style: {
      height: 1,
      background: "var(--io-border)",
      margin: "0 16px"
    }
  }))))), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      overflowY: "auto",
      padding: 24
    }
  }, sel && /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 20
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "flex-start",
      gap: 12
    }
  }, /*#__PURE__*/React.createElement(SeverityBadge, {
    severity: sel.severity
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 4,
      flex: 1
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 18,
      fontWeight: 700
    }
  }, sel.name), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 13,
      color: "var(--io-text-secondary)"
    }
  }, sel.category)), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      gap: 2
    }
  }, /*#__PURE__*/React.createElement(ProgressRing, {
    progress: sel.confidence,
    size: 50,
    lineWidth: 5,
    value: `${Math.round(sel.confidence * 100)}%`
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 10,
      color: "var(--io-text-secondary)"
    }
  }, "Confidence"))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: 8,
      flexWrap: "wrap"
    }
  }, /*#__PURE__*/React.createElement(SigningBadge, {
    source: sel.signing
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 11,
      fontFamily: "var(--font-mono)",
      color: "var(--io-text-secondary)",
      padding: "3px 8px",
      background: "var(--io-surface-elevated)",
      borderRadius: 4
    }
  }, sel.scanner)), (sel.file || sel.severity !== "low") && /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: 8,
      flexWrap: "wrap",
      padding: 12,
      background: "color-mix(in srgb,var(--io-surface) 60%,transparent)",
      borderRadius: 8
    }
  }, sel.file && /*#__PURE__*/React.createElement(DET_DS.Button, {
    variant: "danger",
    size: "sm",
    icon: /*#__PURE__*/React.createElement(window.Icon, {
      name: "Archive",
      size: 13
    })
  }, "Quarantine File"), /*#__PURE__*/React.createElement(DET_DS.Button, {
    variant: "secondary",
    size: "sm",
    icon: /*#__PURE__*/React.createElement(window.Icon, {
      name: "Ban",
      size: 13
    })
  }, "Disable Service"), /*#__PURE__*/React.createElement(DET_DS.Button, {
    variant: "secondary",
    size: "sm",
    icon: /*#__PURE__*/React.createElement(window.Icon, {
      name: "OctagonX",
      size: 13
    })
  }, "Terminate Process")), /*#__PURE__*/React.createElement(InfoBlock, {
    icon: "AlignLeft",
    title: "Description"
  }, sel.description), /*#__PURE__*/React.createElement(InfoBlock, {
    icon: "FileSearch",
    title: "Evidence"
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-mono)",
      fontSize: 12
    }
  }, sel.evidence)), sel.file && /*#__PURE__*/React.createElement(InfoBlock, {
    icon: "File",
    title: "File"
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-mono)",
      fontSize: 12
    }
  }, sel.file)), sel.mitre.length > 0 && /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 8
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 7
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: "ShieldHalf",
    size: 15
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 14,
      fontWeight: 600
    }
  }, "MITRE ATT&CK")), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: 6,
      flexWrap: "wrap"
    }
  }, sel.mitre.map(t => /*#__PURE__*/React.createElement("div", {
    key: t.id,
    style: {
      padding: "4px 8px",
      borderRadius: 6,
      background: "color-mix(in srgb,var(--io-electric) 12%,transparent)",
      color: "var(--io-electric)"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 11,
      fontWeight: 700,
      fontFamily: "var(--font-mono)"
    }
  }, t.id), /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 10
    }
  }, t.name))))), /*#__PURE__*/React.createElement(InfoBlock, {
    icon: "Wrench",
    title: "What you should do",
    accent: "var(--io-orange)"
  }, sel.remediation))));
}
window.DetectionsScreen = DetectionsScreen;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/nnyx-app/DetectionsScreen.jsx", error: String((e && e.message) || e) }); }

// ui_kits/nnyx-app/ScanScreen.jsx
try { (() => {
// Scan screen — mode picker, module toggles, options, and a live progress
// simulation. Mirrors ScanView.swift's structure and interactions.
const {
  useState: useScanState,
  useEffect: useScanEffect,
  useRef: useScanRef
} = React;
const SCAN_DS = window.NnyxDesignSystem_cfde7e;
function ModeCard({
  mode,
  selected,
  onClick
}) {
  const [hover, setHover] = useScanState(false);
  return /*#__PURE__*/React.createElement("button", {
    onClick: onClick,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      textAlign: "left",
      cursor: "pointer",
      padding: 12,
      borderRadius: 10,
      background: selected ? "var(--io-blue)" : "var(--io-surface)",
      border: `1px solid ${selected ? "var(--io-blue)" : "var(--io-border)"}`,
      transform: hover && !selected ? "scale(1.02)" : "scale(1)",
      transition: "transform .2s cubic-bezier(.34,1.56,.64,1)",
      display: "flex",
      flexDirection: "column",
      gap: 8
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: mode.icon,
    size: 16,
    color: selected ? "#fff" : "var(--io-blue)"
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 14,
      fontWeight: 700,
      color: selected ? "#fff" : "var(--io-text)"
    }
  }, mode.label)), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      lineHeight: 1.35,
      color: selected ? "rgba(255,255,255,.85)" : "var(--io-text-secondary)"
    }
  }, mode.desc));
}
function ModuleToggle({
  mod,
  on,
  onToggle
}) {
  const [hover, setHover] = useScanState(false);
  return /*#__PURE__*/React.createElement("button", {
    onClick: onToggle,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      cursor: "pointer",
      padding: 10,
      minHeight: 70,
      borderRadius: 8,
      textAlign: "left",
      display: "flex",
      flexDirection: "column",
      justifyContent: "space-between",
      background: on ? "color-mix(in srgb,var(--io-blue) 8%,transparent)" : "rgba(255,255,255,.02)",
      border: `1.2px solid ${on ? "color-mix(in srgb,var(--io-blue) 40%,transparent)" : hover ? "rgba(255,255,255,.12)" : "transparent"}`,
      transform: hover ? "scale(1.02)" : "scale(1)",
      transition: "transform .15s"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      justifyContent: "space-between",
      alignItems: "center"
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: mod.icon,
    size: 18,
    color: on ? "var(--io-blue)" : "var(--io-text-secondary)"
  }), on ? /*#__PURE__*/React.createElement(window.Icon, {
    name: "CircleCheck",
    size: 15,
    color: "var(--io-blue)"
  }) : /*#__PURE__*/React.createElement("span", {
    style: {
      width: 14,
      height: 14,
      borderRadius: "50%",
      border: "1.5px solid var(--io-text-secondary)",
      opacity: .5
    }
  })), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      fontWeight: 500,
      color: on ? "var(--io-text)" : "var(--io-text-secondary)"
    }
  }, mod.label));
}
function ScanScreen() {
  const {
    Button,
    WorkerBadge
  } = SCAN_DS;
  const [mode, setMode] = useScanState("quick");
  const [mods, setMods] = useScanState(() => new Set(["rootkit", "persistence", "process", "yara", "trust"]));
  const [scanning, setScanning] = useScanState(false);
  const [pct, setPct] = useScanState(0);
  const [files, setFiles] = useScanState(0);
  const timer = useScanRef(null);
  const toggle = id => setMods(s => {
    const n = new Set(s);
    n.has(id) ? n.delete(id) : n.add(id);
    return n;
  });
  const start = () => {
    setScanning(true);
    setPct(0);
    setFiles(0);
    clearInterval(timer.current);
    timer.current = setInterval(() => {
      setPct(p => {
        const np = Math.min(1, p + 0.018 + Math.random() * 0.02);
        setFiles(Math.round(np * 2481));
        if (np >= 1) {
          clearInterval(timer.current);
          setTimeout(() => setScanning(false), 700);
        }
        return np;
      });
    }, 90);
  };
  useScanEffect(() => () => clearInterval(timer.current), []);
  const phase = pct < 0.2 ? "Enumerating launch items…" : pct < 0.5 ? "Hashing & trust lookup…" : pct < 0.8 ? "YARA / MachO analysis…" : "Correlating events…";
  const barColor = pct < 0.5 ? "var(--io-blue)" : pct < 0.85 ? "var(--io-orange)" : "var(--io-success)";
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      padding: 24,
      display: "flex",
      flexDirection: "column",
      gap: 12
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center"
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 17,
      fontWeight: 700
    }
  }, "Scan Mode"), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), scanning ? /*#__PURE__*/React.createElement(Button, {
    variant: "danger",
    size: "sm",
    onClick: () => {
      clearInterval(timer.current);
      setScanning(false);
    },
    icon: /*#__PURE__*/React.createElement(window.Icon, {
      name: "X",
      size: 13
    })
  }, "Cancel") : /*#__PURE__*/React.createElement(Button, {
    variant: "primary",
    onClick: start,
    icon: /*#__PURE__*/React.createElement(window.Icon, {
      name: "Play",
      size: 13
    })
  }, "Start Scan")), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "repeat(auto-fill,minmax(180px,1fr))",
      gap: 12
    }
  }, window.NNYX_SCAN_MODES.map(m => /*#__PURE__*/React.createElement(ModeCard, {
    key: m.id,
    mode: m,
    selected: mode === m.id,
    onClick: () => !scanning && setMode(m.id)
  })))), /*#__PURE__*/React.createElement("div", {
    style: {
      height: 1,
      background: "var(--io-border)"
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: 24,
      display: "flex",
      flexDirection: "column",
      gap: 10
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 14,
      fontWeight: 700
    }
  }, "Scanning Modules"), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "repeat(auto-fill,minmax(150px,1fr))",
      gap: 10
    }
  }, window.NNYX_MODULES.map(m => /*#__PURE__*/React.createElement(ModuleToggle, {
    key: m.id,
    mod: m,
    on: mods.has(m.id),
    onToggle: () => toggle(m.id)
  }))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexWrap: "wrap",
      gap: "8px 20px",
      marginTop: 8
    }
  }, ["Use ClamAV/YARA when available", "Include hidden files", "Descend into app bundles", "Expand & scan archives"].map((o, i) => /*#__PURE__*/React.createElement("label", {
    key: i,
    style: {
      display: "flex",
      alignItems: "center",
      gap: 6,
      fontSize: 12,
      color: "var(--io-text-secondary)",
      cursor: "pointer"
    }
  }, /*#__PURE__*/React.createElement("input", {
    type: "checkbox",
    defaultChecked: i < 2,
    style: {
      accentColor: "var(--io-blue)"
    }
  }), o)))), /*#__PURE__*/React.createElement("div", {
    style: {
      height: 1,
      background: "var(--io-border)"
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: 24
    }
  }, scanning ? /*#__PURE__*/React.createElement("div", {
    style: {
      padding: 16,
      borderRadius: 10,
      background: "color-mix(in srgb,var(--io-blue) 5%,transparent)",
      display: "flex",
      flexDirection: "column",
      gap: 8
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 10
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: "RefreshCw",
    size: 15,
    color: "var(--io-blue)"
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 15,
      fontWeight: 600
    }
  }, phase), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), /*#__PURE__*/React.createElement(WorkerBadge, {
    worker: "userAgent"
  }), /*#__PURE__*/React.createElement(WorkerBadge, {
    worker: "backgroundHelper"
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      fontFamily: "var(--font-mono)",
      color: "var(--io-text-secondary)"
    }
  }, files, " / 2481")), /*#__PURE__*/React.createElement("div", {
    style: {
      position: "relative",
      height: 8,
      borderRadius: 999,
      background: "var(--io-surface-elevated)",
      overflow: "hidden"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: "absolute",
      inset: 0,
      width: `${pct * 100}%`,
      background: barColor,
      borderRadius: 999,
      transition: "width .25s, background .4s"
    }
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 6
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: "Search",
    size: 12,
    color: "var(--io-blue)"
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      fontFamily: "var(--font-mono)",
      color: "var(--io-text-secondary)",
      overflow: "hidden",
      textOverflow: "ellipsis",
      whiteSpace: "nowrap"
    }
  }, Math.round(pct * 100), "% \xB7 /Users/you/Library/LaunchAgents/com.update.helper.plist"))) : /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 20,
      padding: 16,
      borderRadius: 10,
      background: "var(--io-surface)",
      border: "1px solid var(--io-border)"
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: "ShieldCheck",
    size: 34,
    color: "var(--io-success)"
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 4
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 15,
      fontWeight: 600
    }
  }, "Configure a scan mode and press Start Scan"), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 12,
      color: "var(--io-text-secondary)"
    }
  }, "Last run: 2,481 files \xB7 3.4s \xB7 score 38/100 \xB7 1 suppressed false positive")))));
}
window.ScanScreen = ScanScreen;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/nnyx-app/ScanScreen.jsx", error: String((e && e.message) || e) }); }

// ui_kits/nnyx-app/TrustScreen.jsx
try { (() => {
// Trust Bank — known-good binaries keyed by code-directory hash, with the
// helper-mediated approve/revoke workflow. Mirrors TrustDatabankView.
const {
  useState: useTrustState
} = React;
const TRUST_DS = window.NnyxDesignSystem_cfde7e;
const TRUST_RECORDS = [{
  name: "Xcode",
  path: "/Applications/Xcode.app",
  team: "Apple (APPLE)",
  cls: "appleSystem",
  signing: "appstore"
}, {
  name: "Visual Studio Code",
  path: "/Applications/Visual Studio Code.app",
  team: "Microsoft (UBF8T346G9)",
  cls: "trustedVendor",
  signing: "developer"
}, {
  name: "Ollama",
  path: "/Applications/Ollama.app",
  team: "Ollama (9XXXXXX1)",
  cls: "developerSigned",
  signing: "developer"
}, {
  name: "Homebrew gpg",
  path: "/opt/homebrew/bin/gpg",
  team: "GNU",
  cls: "nsrlKnownGood",
  signing: "developer"
}, {
  name: "iimage-helper",
  path: "/usr/local/bin/iimage-helper",
  team: "—",
  cls: "userApproved",
  signing: "unsigned"
}];
const CLS_LABEL = {
  appleSystem: "Apple System",
  trustedVendor: "Trusted Vendor",
  developerSigned: "Developer Signed",
  nsrlKnownGood: "NSRL Known-Good",
  userApproved: "User Approved"
};
function TrustScreen() {
  const {
    Button,
    SigningBadge
  } = TRUST_DS;
  const [q, setQ] = useTrustState("");
  const recs = TRUST_RECORDS.filter(r => !q || r.name.toLowerCase().includes(q.toLowerCase()) || r.path.toLowerCase().includes(q.toLowerCase()));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      padding: 24,
      display: "flex",
      flexDirection: "column",
      gap: 20
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 4
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 22,
      fontWeight: 600
    }
  }, "Cryptographic Trust Databank"), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 14,
      color: "var(--io-text-secondary)",
      maxWidth: 560
    }
  }, "Binaries Nnyx treats as known-good (suppressed from findings), keyed by code-directory hash.")), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: 16,
      padding: "10px 16px",
      background: "var(--io-surface)",
      borderRadius: 10,
      border: "1px solid var(--io-border)"
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      alignItems: "flex-end"
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 9,
      fontWeight: 700,
      textTransform: "uppercase",
      color: "var(--io-text-secondary)"
    }
  }, "Active"), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 18,
      fontWeight: 700,
      fontFamily: "var(--font-rounded)",
      color: "var(--io-success)"
    }
  }, TRUST_RECORDS.length)), /*#__PURE__*/React.createElement("div", {
    style: {
      width: 1,
      background: "var(--io-border)"
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      alignItems: "flex-end"
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 9,
      fontWeight: 700,
      textTransform: "uppercase",
      color: "var(--io-text-secondary)"
    }
  }, "Revoked"), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 18,
      fontWeight: 700,
      fontFamily: "var(--font-rounded)",
      color: "var(--io-text-secondary)"
    }
  }, "1")))), /*#__PURE__*/React.createElement("div", {
    style: {
      height: 1,
      background: "var(--io-border)"
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      gap: 8,
      alignItems: "center",
      padding: 12,
      background: "var(--io-surface)",
      borderRadius: 10,
      border: "1px solid var(--io-border)"
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: "ShieldPlus",
    size: 16,
    color: "var(--io-blue)"
  }), /*#__PURE__*/React.createElement("input", {
    placeholder: "Path to a binary you personally vouch for\u2026",
    style: {
      flex: 1,
      background: "var(--io-bg)",
      border: "1px solid var(--io-border)",
      borderRadius: 6,
      padding: "8px 10px",
      color: "var(--io-text)",
      fontSize: 13,
      fontFamily: "var(--font-mono)"
    }
  }), /*#__PURE__*/React.createElement(Button, {
    variant: "primary",
    size: "sm",
    icon: /*#__PURE__*/React.createElement(window.Icon, {
      name: "Lock",
      size: 13
    })
  }, "Authorize Trust")), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: "Search",
    size: 14,
    color: "var(--io-text-secondary)"
  }), /*#__PURE__*/React.createElement("input", {
    value: q,
    onChange: e => setQ(e.target.value),
    placeholder: "Search known-good records\u2026",
    style: {
      flex: 1,
      background: "var(--io-surface)",
      border: "1px solid var(--io-border)",
      borderRadius: 6,
      padding: "7px 10px",
      color: "var(--io-text)",
      fontSize: 13
    }
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      border: "1px solid var(--io-border)",
      borderRadius: 10,
      overflow: "hidden"
    }
  }, recs.map((r, i) => /*#__PURE__*/React.createElement("div", {
    key: r.path,
    style: {
      display: "flex",
      alignItems: "center",
      gap: 12,
      padding: "12px 16px",
      borderTop: i ? "1px solid var(--io-border)" : "none",
      background: "var(--io-surface)"
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: "KeyRound",
    size: 18,
    color: "var(--io-success)"
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: 3,
      minWidth: 0,
      flex: 1
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 14,
      fontWeight: 600
    }
  }, r.name), /*#__PURE__*/React.createElement(SigningBadge, {
    source: r.signing,
    emoji: false
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 10,
      padding: "2px 6px",
      borderRadius: 4,
      background: "var(--io-surface-elevated)",
      color: "var(--io-text-secondary)"
    }
  }, CLS_LABEL[r.cls])), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 11,
      fontFamily: "var(--font-mono)",
      color: "var(--io-text-secondary)",
      overflow: "hidden",
      textOverflow: "ellipsis",
      whiteSpace: "nowrap"
    }
  }, r.path, " \xB7 ", r.team)), /*#__PURE__*/React.createElement(Button, {
    variant: "ghost",
    size: "sm",
    icon: /*#__PURE__*/React.createElement(window.Icon, {
      name: "ShieldX",
      size: 13
    })
  }, "Revoke")))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 8,
      fontSize: 12,
      color: "var(--io-text-secondary)"
    }
  }, /*#__PURE__*/React.createElement(window.Icon, {
    name: "Info",
    size: 13,
    color: "var(--io-text-secondary)"
  }), "All mutations are admin-authenticated, helper-mediated, audit-logged and reversible. The app never writes this store directly."));
}
window.TrustScreen = TrustScreen;
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/nnyx-app/TrustScreen.jsx", error: String((e && e.message) || e) }); }

// ui_kits/nnyx-app/data.jsx
try { (() => {
// Shared mock data for the Nnyx UI kit. All static — no real scanning.
const NNYX_DETECTIONS = [{
  id: "d1",
  severity: "critical",
  name: "Input Injection Daemon",
  signing: "unsigned",
  scanner: "BehaviorScanner",
  category: "Persistence",
  confidence: 0.96,
  evidence: "Unsigned binary at /usr/local/bin/unsigned_injector registered a global CGEventTap and installed a LaunchDaemon (com.x.helper.plist).",
  file: "/usr/local/bin/unsigned_injector",
  description: "A process is intercepting global keyboard and mouse events while running unsigned — a common keylogger / input-injection pattern.",
  mitre: [{
    id: "T1056.001",
    name: "Keylogging"
  }, {
    id: "T1543.004",
    name: "Launch Daemon"
  }],
  remediation: "Quarantine the binary and remove the LaunchDaemon. Verify no legitimate accessibility tool installed it."
}, {
  id: "d2",
  severity: "high",
  name: "Unsigned Login Item",
  signing: "unsigned",
  scanner: "PersistenceScanner",
  category: "Persistence",
  confidence: 0.81,
  evidence: "~/Library/LaunchAgents/com.update.helper.plist runs an ad-hoc-signed binary at every login; not present in any known-good vendor list.",
  file: "~/Library/LaunchAgents/com.update.helper.plist",
  description: "A login item launches an unsigned background agent. Provenance could not be established.",
  mitre: [{
    id: "T1543.001",
    name: "Launch Agent"
  }],
  remediation: "Disable the service, then Remove & Delete if you do not recognize the publisher."
}, {
  id: "d3",
  severity: "high",
  name: "DNS Exfiltration Pattern",
  signing: "developer",
  scanner: "NetworkMonitor",
  category: "Network",
  confidence: 0.74,
  evidence: "Sustained high-entropy TXT lookups to *.metrics-collect.io — 412 queries in 60s, avg label length 41 chars.",
  file: null,
  description: "Outbound DNS traffic resembles tunneled data exfiltration: abnormally long, high-entropy subdomain labels at high frequency.",
  mitre: [{
    id: "T1048.003",
    name: "Exfiltration Over DNS"
  }],
  remediation: "Identify the responsible process and block the domain in the content filter if the traffic is unexpected."
}, {
  id: "d4",
  severity: "medium",
  name: "Outdated Browser Extension",
  signing: "appstore",
  scanner: "AppInventory",
  category: "Software",
  confidence: 0.45,
  evidence: "Safari extension 'PageHelper' requests broad host permissions and has not been updated in 14 months.",
  file: "~/Library/Safari/Extensions/PageHelper.safariextz",
  description: "An installed extension holds wide permissions and appears abandoned. Not malicious, but an enlarged attack surface.",
  mitre: [],
  remediation: "Review whether the extension is still needed; remove if not."
}, {
  id: "d5",
  severity: "low",
  name: "Ollama Helper",
  signing: "developer",
  scanner: "TrustScanner",
  category: "Trust",
  confidence: 0.12,
  evidence: "Developer ID signed (team 9XXXXXX), present in CIRCL Hashlookup as known-good — suppressed from active findings.",
  file: "/Applications/Ollama.app",
  description: "A signed, known-good vendor binary. Shown for transparency; it does not count toward the threat score.",
  mitre: [],
  remediation: "No action needed."
}];
const NNYX_NAV = {
  primary: [{
    id: "dashboard",
    label: "Dashboard",
    icon: "Gauge",
    badge: 0
  }, {
    id: "scan",
    label: "Scan",
    icon: "ScanSearch",
    badge: 0
  }, {
    id: "incidents",
    label: "Incidents",
    icon: "Layers",
    badge: 0
  }, {
    id: "detections",
    label: "Detections",
    icon: "TriangleAlert",
    badge: 2
  }, {
    id: "remediation",
    label: "Remediation",
    icon: "Archive",
    badge: 1
  }],
  intel: [{
    id: "intel",
    label: "Threat Intel",
    icon: "Download",
    badge: 0
  }, {
    id: "trust",
    label: "Trust Bank",
    icon: "KeyRound",
    badge: 0
  }, {
    id: "logs",
    label: "Logs & History",
    icon: "History",
    badge: 0
  }, {
    id: "settings",
    label: "Settings",
    icon: "Settings",
    badge: 0
  }]
};
const NNYX_SCAN_MODES = [{
  id: "quick",
  label: "Quick",
  icon: "Zap",
  desc: "Home folder, login items & persistence — the daily check."
}, {
  id: "full",
  label: "Full",
  icon: "ShieldCheck",
  desc: "Every readable volume with all engines. Thorough; slower."
}, {
  id: "file",
  label: "File(s)",
  icon: "FileSearch",
  desc: "Drag in specific files or folders to triage on demand."
}, {
  id: "custom",
  label: "Custom",
  icon: "SlidersHorizontal",
  desc: "Pick a path and choose exactly which modules run."
}, {
  id: "cloud",
  label: "Cloud Drive",
  icon: "Cloud",
  desc: "iCloud / Dropbox / Google Drive synced locations."
}, {
  id: "external",
  label: "External",
  icon: "HardDrive",
  desc: "Mounted USB & external drives as they connect."
}];
const NNYX_MODULES = [{
  id: "rootkit",
  label: "Rootkit Scan",
  icon: "ShieldHalf"
}, {
  id: "persistence",
  label: "Persistence",
  icon: "RefreshCw"
}, {
  id: "process",
  label: "Process Scan",
  icon: "Cpu"
}, {
  id: "memory",
  label: "Memory Scan",
  icon: "MemoryStick"
}, {
  id: "network",
  label: "Network Scan",
  icon: "Network"
}, {
  id: "firewall",
  label: "Firewall Review",
  icon: "BrickWall"
}, {
  id: "ai",
  label: "AI Assistant",
  icon: "BrainCircuit"
}, {
  id: "inventory",
  label: "App Inventory",
  icon: "LayoutGrid"
}, {
  id: "yara",
  label: "YARA Matching",
  icon: "FileSearch"
}, {
  id: "trust",
  label: "Trust Metadata",
  icon: "KeyRound"
}, {
  id: "archive",
  label: "Archive Scan",
  icon: "Archive"
}, {
  id: "remote",
  label: "Remote Forensics",
  icon: "Radio"
}];
Object.assign(window, {
  NNYX_DETECTIONS,
  NNYX_NAV,
  NNYX_SCAN_MODES,
  NNYX_MODULES
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/nnyx-app/data.jsx", error: String((e && e.message) || e) }); }

__ds_ns.Logo = __ds_scope.Logo;

__ds_ns.Button = __ds_scope.Button;

__ds_ns.Card = __ds_scope.Card;

__ds_ns.GlassPanel = __ds_scope.GlassPanel;

__ds_ns.SectionHeader = __ds_scope.SectionHeader;

__ds_ns.ProgressRing = __ds_scope.ProgressRing;

__ds_ns.StatCard = __ds_scope.StatCard;

__ds_ns.DetectionRow = __ds_scope.DetectionRow;

__ds_ns.SeverityBadge = __ds_scope.SeverityBadge;

__ds_ns.SidebarItem = __ds_scope.SidebarItem;

__ds_ns.SidebarSectionLabel = __ds_scope.SidebarSectionLabel;

__ds_ns.SigningBadge = __ds_scope.SigningBadge;

__ds_ns.StatusPill = __ds_scope.StatusPill;

__ds_ns.WorkerBadge = __ds_scope.WorkerBadge;

})();
