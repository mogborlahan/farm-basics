#!/usr/bin/env python3
"""Build self-contained design-system HTML pages from Markdown for Farm Basics.

Pipeline: pandoc (gfm + TOC) -> extract sidebar TOC -> assemble shell (sidebar,
topbar, footer, scrollspy JS, checkbox persistence) with embedded CSS.

Usage:
  python build_page.py --md in.md --out out.html --title "..." --subtitle "..."
      [--meta "chip1,chip2|kind" ]  kinds: forest|sage|gold|ink (default ink)
      [--phases "done,done,now,pending,HL"]  5 sidebar phase dots
      [--progress]                  enable checkbox persistence + progress stats
      [--note "version text"]
Requires pandoc.  Set PANDOC env var to override the default binary path.
"""

import argparse
import os
import re
import subprocess
import sys

PANDOC_DEFAULT = r"C:\Users\gbola\anaconda3\Library\bin\pandoc.exe"
SKILL_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSS = os.path.join(SKILL_ROOT, "assets", "farm-basics.css")

MARK = (
    '<svg viewBox="0 0 48 48" class="fb-mark" aria-hidden="true" focusable="false" '
    'width="38" height="38">'
    '<path d="M9 24a15 15 0 0 1 30 0" stroke="#5C9247" stroke-width="4" fill="none" '
    'stroke-linecap="round"/>'
    '<path d="M14.6 30a9.4 9.4 0 0 1 18.8 0" stroke="#126A3A" stroke-width="4" fill="none" '
    'stroke-linecap="round" opacity=".55"/>'
    '<circle cx="24" cy="34" r="4.2" fill="#FDBB26"/></svg>'
)

LOGO_FILE = os.path.join(SKILL_ROOT, "assets", "farm-basics-logo.svg")


def load_logo():
    """Return the Farm Basics logo as a self-contained, scalable <svg>.

    Strips fixed width/height and pins a matching viewBox so CSS controls size
    (sidebar brand, mobile topbar, print header). Falls back to MARK if missing.
    """
    try:
        if not os.path.exists(LOGO_FILE):
            return MARK
        with open(LOGO_FILE, encoding="utf-8") as f:
            svg = f.read().strip()
        svg = re.sub(r'\s+width="[^"]*"', "", svg, count=1)
        svg = re.sub(r'\s+height="[^"]*"', "", svg, count=1)
        svg = re.sub(
            r"^<svg",
            '<svg class="fb-logo" viewBox="0 0 1104 1059" aria-hidden="true" '
            'focusable="false"',
            svg,
            count=1,
        )
        return svg
    except Exception:
        return MARK

JS = r"""
document.addEventListener('DOMContentLoaded', function () {
  var app = document.querySelector('.fb-app');
  var burger = document.querySelector('.fb-burger');
  var overlay = document.querySelector('.fb-overlay');
  function closeNav() { if (app) app.classList.remove('nav-open'); }
  if (burger) burger.addEventListener('click', function () { app.classList.toggle('nav-open'); });
  if (overlay) overlay.addEventListener('click', closeNav);

  // scrollspy
  var links = Array.prototype.slice.call(document.querySelectorAll('.fb-toc a'));
  var map = {};
  links.forEach(function (a) { map[a.getAttribute('href').slice(1)] = a; });
  var heads = links.map(function (a) { return document.getElementById(a.getAttribute('href').slice(1)); }).filter(Boolean);
  if ('IntersectionObserver' in window && heads.length) {
    var io = new IntersectionObserver(function (es) {
      es.forEach(function (e) {
        var id = e.target.id;
        if (e.isIntersecting) {
          links.forEach(function (a) { a.classList.remove('active'); });
          if (map[id]) map[id].classList.add('active');
        }
      });
    }, { rootMargin: '-10% 0px -80% 0px' });
    heads.forEach(function (h) { io.observe(h); });
  }

  // checkbox persistence + progress
  var checks = Array.prototype.slice.call(document.querySelectorAll('.fb-check'));
  var bar = document.querySelector('.fb-progress-fill');
  var num = document.querySelector('.fb-progress-num');
  var key = 'fb:' + location.pathname.split('/').pop();
  var state = {};
  try { state = JSON.parse(localStorage.getItem(key)) || {}; } catch (e) {}
  function paint() {
    checks.forEach(function (c) {
      var on = !!state[c.dataset.id];
      c.checked = on;
      var li = c.closest('li');
      if (li) li.classList.toggle('done', on);
    });
    if (bar && num) {
      var done = checks.filter(function (c) { return c.checked; }).length;
      var pct = checks.length ? Math.round(100 * done / checks.length) : 0;
      bar.style.width = pct + '%';
      num.textContent = done + ' / ' + checks.length + ' complete - ' + pct + '%';
    }
  }
  checks.forEach(function (c, i) {
    c.dataset.id = 't' + i;
    c.addEventListener('change', function () {
      state[c.dataset.id] = c.checked;
      try { localStorage.setItem(key, JSON.stringify(state)); } catch (e) {}
      paint();
    });
  });
  paint();
});
"""


def sh(cmd):
    return subprocess.run(cmd, capture_output=True, text=True, check=True)


def run_pandoc(md):
    pandoc = os.environ.get("PANDOC", PANDOC_DEFAULT)
    out = os.path.join(SKILL_ROOT, ".fragment.html")
    sh([pandoc, md, "-f", "gfm", "-t", "html5", "--wrap=none", "-o", out])
    with open(out, encoding="utf-8") as f:
        return f.read()


def build_toc(body):
    """Sidebar TOC from h2 headings (section level, like --toc-depth=2)."""
    items = []
    for m in re.finditer(r'<h2 id="([^"]+)">(.*?)</h2>', body, re.S):
        label = re.sub(r"<[^>]+>", "", m.group(2))
        items.append('<li><a href="#%s">%s</a></li>' % (m.group(1), label))
    return "<ul>" + "".join(items) + "</ul>"


def assemble(md, out, title, subtitle, meta, phases, progress, note):
    frag = run_pandoc(md)
    toc_inner = build_toc(frag)
    body = re.sub(
        r'<input type="checkbox"[^>]*/?>',
        '<input type="checkbox" class="fb-check" />',
        frag,
    )
    logo = load_logo()

    with open(CSS, encoding="utf-8") as f:
        css = f.read()

    chips = ""
    for item in meta:
        label, _, kind = item.partition("|")
        chips += '<span class="fb-chip %s">%s</span>' % (kind or "ink", label)
    if progress:
        chips += (
            '<span class="fb-progress"><div class="fb-progress-bar">'
            '<div class="fb-progress-fill"></div></div>'
            '<div class="fb-progress-num">0 / 0 complete - 0%</div></span>'
        )

    dots = b""
    states = phases.split(",") if phases else []
    for i, st in enumerate(states):
        dots += b'<span class="fb-phase-dot %s"></span>' % (b"done" if "done" in st else b"now" if "now" in st else b"")

    html = f'''<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title}</title>
<style>{css}</style>
</head>
<body>
<div class="fb-app">
  <aside class="fb-sidebar">
    <div class="fb-brand">{logo}
      <div class="fb-brandtag">Implementation Programme</div>
    </div>
    <div class="fb-toc-head">On this page</div>
    <nav class="fb-toc">{toc_inner}</nav>
    <div class="fb-sidefoot">
      <div class="fb-phase"><div class="fb-phase-title">Phases</div>
        <div class="fb-arcs">{dots.decode()}</div></div>
      <div class="fb-pill-item">{note or ''}</div>
      <div class="fb-version">Built {title} &middot; DS2 v2 &middot; self-contained</div>
    </div>
  </aside>
  <div class="fb-overlay"></div>
  <div class="fb-main">
    <header class="fb-printhead">{logo}<div class="fb-printhead-meta"><div class="fb-printhead-title">{title}</div><div class="fb-printhead-sub">{subtitle}</div></div></header>
    <header class="fb-topbar">
      <button class="fb-burger" aria-label="Menu">&#9776;</button>
      <div class="fb-topinfo"><div class="fb-toptitle">{title}</div><div class="fb-topsub">{subtitle}</div></div>
      <div class="fb-topmeta">{chips}</div>
    </header>
    <main class="fb-content">{body}</main>
    <footer class="fb-footer">
      <span>Farm Basics implementation toolkit</span>
      <span>Sources: Plan\\Farm-Basics-Sprint-Plan.md &middot; Doc\\ set &middot; Design System 2</span>
    </footer>
  </div>
</div>
<script>{JS}</script>
</body>
</html>'''
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write(html)
    os.remove(os.path.join(SKILL_ROOT, ".fragment.html"))
    print("wrote", out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--md", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--title", required=True)
    ap.add_argument("--subtitle", default="")
    ap.add_argument("--meta", default="", help="comma-separated label|kind chips")
    ap.add_argument("--phases", default="done,done,now,pending,HL")
    ap.add_argument("--note", default="")
    ap.add_argument("--progress", action="store_true")
    a = ap.parse_args()
    assemble(a.md, a.out, a.title, a.subtitle,
             [x.strip() for x in a.meta.split(",") if x.strip()],
             a.phases, a.progress, a.note)


if __name__ == "__main__":
    sys.exit(main())