---
name: git-release-workflow
description: Standardized English Git commit conventions and bilingual (EN/RU) release changelog formatting for OmniHID
---

# Git Commit & Release Changelog Workflow Guide

This skill defines the standardized workflow for creating Git commits and release changelogs for the **OmniHID** project (Universal Peripheral Telemetry & Battery Probe: `OmniHid.Core`, `omni-hid.exe` CLI, declarative device profiles in `devices/`, and installer packages).

---

## 1. Git Commit Standards

### General Rules
1. **English Only**: All commit messages (headers, bodies, footers) MUST be written in English.
2. **Conventional Commits Format**: Every commit title must follow the standard type prefix:
   - `feat: release vX.Y.Z - <short summary>` (for release commits)
   - `feat: <description>` (for new features or capabilities)
   - `fix: <description>` (for bug fixes and corrections)
   - `docs: <description>` (for documentation, README, or issue template updates)
   - `refactor: <description>` (for code restructuring without behavioral changes)
   - `style: <description>` (for formatting or code styling adjustments)
   - `chore: <description>` (for build scripts, CI workflows, or configuration updates)

### Release Commit Title Pattern (MANDATORY FOR ALL RELEASES)
For any version bump or release commit, the commit title **MUST ALWAYS** follow this exact format:
```
feat: release vX.Y.Z - <concise summary of major changes in lower-case>
```
Examples:
- `feat: release v0.0.4 - enhanced device diagnostics exporter and github issue templates`
- `feat: release v0.0.3 - battery protocol calibration engine, ic fingerprinter, and verified device profiles`
- `feat: release v0.0.2 - compx and areon protocol handlers with live sniffer diff monitor`
- `feat: release v0.0.1 - initial release (omnihid core library, cli diagnostic suite, and portable bundle)`

> **CRITICAL**: Do NOT use scoped prefixes like `feat(cli):` or `chore:` when releasing a new version. The prefix MUST be `feat: release vX.Y.Z - ...`.

### Commit Body Pattern
For multi-faceted commits or releases, include concise bullet points detailing key changes:
```
feat: release v0.0.3 - battery protocol calibration engine, ic fingerprinter, and verified device profiles

- Add guided A-B battery and charger calibration engine with plug/unplug differential detection
- Introduce IcFingerprinter for automatic hardware controller and chipset identification
- Implement Compx, Areson, and SinoWealth vendor protocol handlers for wireless mice and keyboards
- Expand declarative peripheral profile database across devices/*.json with embedded manifest loading
- Provide automated Inno Setup installer compilation and portable zip distribution scripts
- Bump version to 0.0.3 across assembly attributes, csproj, and Inno Setup installer
```

---

## 2. Bilingual Release Changelog Workflow

Whenever committing a version bump / release, or upon user request for release notes, ALWAYS output a copy-paste ready changelog formatted in **both English and Russian**.

### Release Changelog Template

```markdown
## 🇺🇸 English Release Notes (vX.Y.Z)

### What's New
- Feature description 1
- Feature description 2

### Improvements & Fixes
- Fix description 1
- Fix description 2

---

## 🇷🇺 Что нового в версии vX.Y.Z (Русский)

### Что нового
- Описание новшества 1
- Описание новшества 2

### Исправления и улучшения
- Описание исправления 1
- Описание исправления 2
```
