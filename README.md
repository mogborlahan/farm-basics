# Farm Basics

Farm Basics is a product blueprint for giving Nigerian smallholder farms a single digital identity, so a farmer, their plot, and their produce history travel together from planting to market.

This repository contains the public landing page plus the internal blueprint: the full specification set (business case, product requirements, engineering design, design system) and the implementation sprint plan derived from it.

## Contents

- `Plan/` - the implementation programme in Farm Basics design-system HTML (dark sidebar, brand mark, scrollspy TOC; self-contained):
  - `Farm-Basics-Sprint-Plan.html` / `.md` - the 20-section, dependency-aware sprint plan (S1-S24 across BRD phases 0-3 + hardening)
  - `Farm-Basics-Implementation-Sprints.html` / `.md` - the full project as progressive tasks → sub-tasks → micro-tasks, every micro-task ≤ 10 minutes, with interactive checkboxes (progress persists in your browser)
  - `Recommended-Skills.md` - researched agent/developer skills, mapped to the stack (largest subset installed; tracks installs via the local `skills/skills-lock.json`)
  - `assets/farm-basics.css`, `tools/build_page.py`, `regenerate.ps1` - shared design-system shell and build pipeline (pandoc + Python)
- `Doc/` - the specification set, grouped into themed folders:
  - `01-Business/` - BRD v2 + PRD Master and Volumes A-D
  - `02-Engineering/` - architecture, schema/ERD, API, security, integrations, CI/CD, observability, DR runbook, master-data strategy
  - `03-Legal/` - regulatory/compliance memo, Terms of Service & Privacy (draft)
  - `04-QA/` - master test plan, UAT & pilot protocol, accessibility conformance
  - `05-Design/` - Design System 1 & 2, styleguides, brand/pitch assets
  - `06-Operations/` - Part A — Platform Operations (unified system spec) and Part B — Administration & Governance (admin console spec). Note: these two filenames use an em-dash, not a hyphen
- `Archived/` - superseded v1-era per-domain specs (cross-reference only)
- `site/` - the static landing page (HTML and CSS, no build step)

## Live

- Landing page: https://farm-basics.netlify.app
- Portfolio: https://mogbolahan.netlify.app

## Contact

- Gbolahan Folarin
- gbolahan.folarin@outlook.com
- https://linkedin.com/in/mogbolahan
