# Changelog

## [0.2.1] - 2026-11-02

### Added

- Gizmo shown during modal transforms; axis constraints show only the relevant axis.
- Cursor is hidden on Windows during a modal transform.
- HUD uses the complement of the background colour and appears only while a transform is active.

### Changed

- Axis keys cycle World → Local → World … on each press; W/E/R reset to free.

## [0.2.0] - 2026-11-02

### Added

- Configurable W/E/R and X/Y/Z key bindings in Preferences → Figix → Modal Transform.
- Infinite mouse movement while a modal transform is active.
- In-preferences version display and update button.

### Fixed

- Pressing an axis key twice now switches World → Local instead of resetting to free mode.

## [0.1.0] - 2026-10-13

### Added

- Unity 6 Editor-only UPM package structure.
- W/E/R keyboard-first modal translate, rotate, and scale tools.
- Global/Local axis and plane constraints.
- Numeric input, multi-selection, confirm/cancel, and Undo/Redo support.
- RMB-first pass-through for Unity Scene view fly-camera navigation.
