"""SEO metadata injection for public HTML pages.

Defines per-page title, description, Open Graph, Twitter Card, canonical URL
and JSON-LD structured data. ``inject_meta_tags`` rewrites the <head> of an
HTML document at serve time so the on-disk files stay clean and the metadata
stays in one place.

The injection is idempotent: if a tag already exists (matched by name/property)
it is replaced; otherwise it is appended before ``</head>``.

E-10: Pricing in structured data is read from environment variables
(SYXX_SEO_PRICE_MONTHLY, SYXX_SEO_PRICE_YEARLY) to prevent drift between
the SEO page and the actual Stripe prices. Defaults match the Stripe catalog.
"""

from __future__ import annotations

import json
import os
import re

from . import __version__

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

CANONICAL_ORIGIN = "https://syxx.app"

# E-10: SEO prices are configurable to prevent drift from Stripe.
# The operator should update these when Stripe prices change.
_SEO_PRICE_MONTHLY = os.environ.get("SYXX_SEO_PRICE_MONTHLY", "3.95")
_SEO_PRICE_YEARLY = os.environ.get("SYXX_SEO_PRICE_YEARLY", "19.95")

# ---------------------------------------------------------------------------
# JSON-LD — SoftwareApplication (landing page only)
# ---------------------------------------------------------------------------


def _build_software_application_ld_json():
    """Build JSON-LD with current pricing. Called at module load; prices are
    read from env so a deploy can update them without code changes."""
    return json.dumps(
        {
            "@context": "https://schema.org",
            "@type": "SoftwareApplication",
            "name": "Syxx Dimensions",
            "applicationCategory": "SecurityApplication",
            "operatingSystem": "macOS, Windows, Ubuntu",
            "description": (
                "A DNS investigative and protection suite. Traces DNS lookups "
                "end to end — spoofed answers, poisoned caches, silent "
                "exfiltration — and writes your resolver's story."
            ),
            "url": "https://syxx.app",
            # A200: derive from the package source of truth, not a hardcoded string.
            "softwareVersion": __version__,
            "offers": [
                {
                    "@type": "Offer",
                    "price": _SEO_PRICE_MONTHLY,
                    "priceCurrency": "USD",
                    "description": "Monthly subscription",
                },
                {
                    "@type": "Offer",
                    "price": _SEO_PRICE_YEARLY,
                    "priceCurrency": "USD",
                    "description": "Yearly subscription",
                },
            ],
            "author": {
                "@type": "Organization",
                "name": "Syxx",
                "url": "https://syxx.app",
            },
        },
        separators=(",", ":"),
    )


_SOFTWARE_APPLICATION_LD_JSON = _build_software_application_ld_json()

# ---------------------------------------------------------------------------
# Per-page metadata
# ---------------------------------------------------------------------------

# Keys match the ``path`` parameter passed to ``inject_meta_tags``.
# Each dict may contain:
#   title, description, keywords, canonical,
#   og_title, og_description, og_type, og_image,
#   twitter_card, twitter_site,
#   json_ld  (raw JSON string for a <script type="application/ld+json"> block)

PAGE_META: dict[str, dict] = {
    # ---- landing page ----
    "/": {
        "title": "Syxx Dimensions — DNS Investigation Suite",
        "description": (
            "Every resolver tells a story. Syxx traces DNS lookups end to "
            "end — spoofed answers, poisoned caches, silent exfiltration — "
            "and writes your resolver's story."
        ),
        "keywords": (
            "DNS security, DNS investigation, DNS spoofing detection, "
            "DNS hijacking, DNS exfiltration, DNS censorship, resolver analysis, "
            "network security tool, macOS DNS tool, Windows DNS tool"
        ),
        "canonical": "/",
        "og_title": "Syxx Dimensions — DNS Investigation Suite",
        "og_description": (
            "Syxx traces DNS lookups end to end — spoofed answers, poisoned "
            "caches, silent exfiltration — and writes your resolver's story."
        ),
        "og_type": "website",
        "og_image": "/assets/syxx-logo.png",
        "twitter_card": "summary",
        "twitter_site": "@syxxdimensions",
        "json_ld": _SOFTWARE_APPLICATION_LD_JSON,
    },
    # ---- DNS leak test (main) ----
    "/leak-test": {
        "title": "Syxx DNS Leak Test — Free DNS Resolver Check",
        # A200: describe the observation, not a conclusion — the test shows which
        # resolvers handled the lookups, it does not determine whether traffic
        # is "leaking" or identify the recipient as "your ISP".
        "description": (
            "See which DNS resolvers handled your lookups. Syxx's free test "
            "shows the resolver IPs that reached our authoritative server — "
            "compare them against your VPN or encrypted DNS provider."
        ),
        "keywords": ("DNS leak test, DNS privacy, VPN leak test, DNS leak check, free DNS test, ISP DNS monitoring"),
        "canonical": "/leak-test",
        "og_title": "Syxx DNS Leak Test — Free DNS Resolver Check",
        "og_description": (
            "See which DNS resolvers handled your lookups. Compare the IPs "
            "against your VPN or encrypted DNS provider. Free, no account required."
        ),
        "og_type": "website",
        "twitter_card": "summary",
        "twitter_site": "@syxxdimensions",
    },
    # ---- DNS leak test (alt) ----
    "/leaktest": {
        "title": "Syxx DNS Leak Test — Free DNS Resolver Check",
        "description": (
            "See which DNS resolvers handled your lookups. Syxx's free test "
            "shows the resolver IPs that reached our authoritative server — "
            "compare them against your VPN or encrypted DNS provider."
        ),
        "keywords": ("DNS leak test, DNS privacy, VPN leak, DNS leak check, free DNS test, resolver leak"),
        "canonical": "/leaktest",
        "og_title": "Syxx DNS Leak Test — Free DNS Resolver Check",
        "og_description": ("See which DNS resolvers handled your lookups. Free, no account required."),
        "og_type": "website",
        "twitter_card": "summary",
        "twitter_site": "@syxxdimensions",
    },
    # ---- Legal pages (DRAFT — held out of index until owner approval, A199) ----
    # Remove "robots" key and re-add to sitemap.xml + robots.txt Allow on approval.
    "/privacy": {
        "robots": "noindex, nofollow",
        "title": "Syxx — Privacy Policy",
        "description": (
            "Syxx is a DNS-integrity tool. Its job is to inspect your own "
            "network from your own device and tell you honestly what it found."
        ),
        "canonical": "/privacy",
        "og_title": "Syxx — Privacy Policy",
        "og_description": (
            "Syxx is a DNS-integrity tool. Its job is to inspect your own "
            "network from your own device and tell you honestly what it found."
        ),
        "og_type": "article",
        "twitter_card": "summary",
    },
    "/terms": {
        "robots": "noindex, nofollow",
        "title": "Syxx — Terms of Service",
        "canonical": "/terms",
        "og_type": "article",
        "twitter_card": "summary",
    },
    # ---- API docs ----
    "/docs/api.html": {
        "title": "Syxx API Documentation — DNS Investigation Endpoints",
        "description": (
            "REST API documentation for the Syxx Dimensions DNS investigation "
            "suite. Endpoints for DNS investigation, authoritative lookup, "
            "trace, baseline comparison, exposure analysis and passive DNS."
        ),
        "keywords": ("DNS API, DNS investigation API, REST API, DNS lookup API, passive DNS API, network security API"),
        "canonical": "/docs/api",
        "og_title": "Syxx API Documentation",
        "og_description": (
            "REST API reference for DNS investigation, authoritative lookup, "
            "trace, baseline, exposure and passive DNS endpoints."
        ),
        "og_type": "article",
        "twitter_card": "summary",
    },
    # ---- Help / FAQ ----
    "/docs/help.html": {
        "title": "Syxx Dimensions — Help & Documentation",
        "description": (
            "Comprehensive help and feature documentation for the Syxx "
            "Dimensions DNS investigation suite. Learn how to detect DNS "
            "spoofing, hijacking, exfiltration and censorship."
        ),
        "keywords": (
            "Syxx help, DNS investigation guide, DNS security documentation, "
            "DNS spoofing detection guide, network security help"
        ),
        "canonical": "/docs/help",
        "og_title": "Syxx Dimensions — Help & Documentation",
        "og_description": ("Feature documentation and guides for the Syxx Dimensions DNS investigation suite."),
        "og_type": "article",
        "twitter_card": "summary",
    },
}

# ---------------------------------------------------------------------------
# Helpers — tag generation
# ---------------------------------------------------------------------------


def _meta_tag(name: str, content: str, *, attr: str = "name") -> str:
    """Build a <meta> element."""
    return f'<meta {attr}="{_esc(name)}" content="{_esc(content)}">'


def _link_canonical(href: str) -> str:
    """Build a <link rel="canonical"> element."""
    url = href if href.startswith("http") else CANONICAL_ORIGIN + href
    return f'<link rel="canonical" href="{_esc(url)}">'


def _esc(s: str) -> str:
    """Escape attribute value for safe HTML embedding.

    SM-046: also escape single quotes — attribute values may be delimited by
    single quotes in some HTML parsers, and an unescaped ' in an attribute
    value can break out of the attribute context.
    """
    return (
        s.replace("&", "&amp;")
        .replace('"', "&quot;")
        .replace("'", "&#39;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
    )


def _build_tags(meta: dict) -> str:
    """Return the full block of <meta>/<link>/<script> tags for a page."""
    lines: list[str] = []

    # Canonical
    if "canonical" in meta:
        lines.append(_link_canonical(meta["canonical"]))

    # Robots directive (A199: hold draft legal pages out of the index)
    if "robots" in meta:
        lines.append(_meta_tag("robots", meta["robots"]))

    # Standard meta tags
    if "description" in meta:
        lines.append(_meta_tag("description", meta["description"]))
    if "keywords" in meta:
        lines.append(_meta_tag("keywords", meta["keywords"]))

    # Open Graph
    if "og_title" in meta:
        lines.append(_meta_tag("og:title", meta["og_title"], attr="property"))
    if "og_description" in meta:
        lines.append(_meta_tag("og:description", meta["og_description"], attr="property"))
    if "og_type" in meta:
        lines.append(_meta_tag("og:type", meta["og_type"], attr="property"))
    if "og_image" in meta:
        url = meta["og_image"]
        if not url.startswith("http"):
            url = CANONICAL_ORIGIN + url
        lines.append(_meta_tag("og:image", url, attr="property"))

    # Twitter Card
    if "twitter_card" in meta:
        lines.append(_meta_tag("twitter:card", meta["twitter_card"]))
    if "twitter_site" in meta:
        lines.append(_meta_tag("twitter:site", meta["twitter_site"]))

    # JSON-LD structured data
    # A200: escape < to prevent </script> injection via operator-controlled price
    # fields. json.dumps does not escape HTML-significant characters.
    if "json_ld" in meta:
        safe_ld = meta["json_ld"].replace("<", "\\u003c")
        lines.append(f'<script type="application/ld+json">{safe_ld}</script>')

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# HTML rewriting
# ---------------------------------------------------------------------------

# Regex to find an existing tag by (name|property) + content.
# SM-047 + E264: match <meta> tags regardless of attribute order or quoting style.
# The regex uses lookaheads to check for (name|property) and content attributes
# in any order. E264: also handle single-quoted attributes and attributes with
# extra whitespace, which the previous regex missed.
_TAG_RE = re.compile(
    r'<meta\s+(?=[^>]*(?:name|property)\s*=\s*["\'][^"\']*["\'])'
    r'(?=[^>]*content\s*=\s*["\'][^"\']*["\'])[^>]*/?\s*>',
    re.IGNORECASE,
)
_CANONICAL_RE = re.compile(
    r'<link\s+[^>]*rel\s*=\s*["\']canonical["\'][^>]*href\s*=\s*["\'][^"\']*["\'][^>]*/?>|'
    r'<link\s+[^>]*href\s*=\s*["\'][^"\']*["\'][^>]*rel\s*=\s*["\']canonical["\'][^>]*/?>',
    re.IGNORECASE,
)
_JSONLD_RE = re.compile(
    r'<script\s+type\s*=\s*"application/ld\+json"\s*>.*?</script>',
    re.IGNORECASE | re.DOTALL,
)


# Individual matchers for dedup.
def _tag_identity(tag: str) -> str | None:
    """Return a dedup key for a <meta> tag, e.g. 'description' or 'og:title'.

    E264: Handle both single and double quoted attributes.
    """
    m = re.search(r'(?:name|property)\s*=\s*["\']([^"\']*)["\']', tag, re.IGNORECASE)
    return m.group(1).lower() if m else None


def inject_meta_tags(html_content: str, page_path: str) -> str:
    """Inject or replace SEO meta tags in *html_content*.

    *page_path* is the route path (e.g. ``"/"``, ``"/leak-test"``).
    If no metadata is defined for the path the HTML is returned unchanged.

    Existing ``<meta name="…">``, ``<meta property="…">``, ``<link
    rel="canonical">`` and ``<script type="application/ld+json">`` tags are
    removed before the canonical set is injected, so the result is always
    consistent regardless of what the on-disk file contains.
    """
    meta = PAGE_META.get(page_path)
    if not meta:
        return html_content

    new_block = _build_tags(meta)
    if not new_block:
        return html_content

    # Collect identities we are about to inject.
    injected_identities: set[str] = set()
    for line in new_block.split("\n"):
        ident = _tag_identity(line)
        if ident:
            injected_identities.add(ident)

    # --- remove existing tags we are about to replace ---
    def _drop_meta(m: re.Match) -> str:
        tag = m.group(0)
        ident = _tag_identity(tag)
        if ident and ident in injected_identities:
            return ""  # drop — we will re-emit
        return tag  # keep

    html_content = _TAG_RE.sub(_drop_meta, html_content)

    # Remove existing canonical if we are injecting one.
    if "canonical" in meta:
        html_content = _CANONICAL_RE.sub("", html_content)

    # Remove existing JSON-LD if we are injecting one.
    if "json_ld" in meta:
        html_content = _JSONLD_RE.sub("", html_content)

    # --- inject before </head> ---
    head_close = html_content.lower().find("</head>")
    if head_close == -1:
        # Fallback: prepend to document (degenerate HTML).
        return new_block + "\n" + html_content

    # Collapse any blank lines left by removed tags.
    html_content = re.sub(r"\n\s*\n\s*\n", "\n\n", html_content)

    return html_content[:head_close] + new_block + "\n" + html_content[head_close:]


def page_title(page_path: str) -> str | None:
    """Return the configured <title> for a page, or None."""
    meta = PAGE_META.get(page_path)
    return meta["title"] if meta and "title" in meta else None
