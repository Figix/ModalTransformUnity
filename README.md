# Figix Modal Transform for Unity

Keyboard-first modal transforms for the Unity 6 Scene view, using the familiar `W`, `E`, and `R` layout from 3ds Max and Unity.

This is an **Editor-only Unity Package**. It does not add runtime code to builds.

## Compatibility

- Minimum Editor version: **Unity 6.0 (`6000.0`)**
- Intended range: **Unity 6 (`6000.x`)**
- Platforms: Editor code is platform-independent; initial development is on Windows.

Batch compilation has been verified on:

- `6000.0.62f1`
- `6000.2.9f1`
- `6000.3.16f1`
- `6000.5.0f1`

The package deliberately uses APIs available in Unity 6000.0. Future Unity 6 releases are expected to remain compatible, but each new editor line should still be validated before being listed as tested.

## Installation

### Method 1: Unity Package Manager Git URL (recommended)

1. Open your Unity project.
2. Go to **Window → Package Management → Package Manager**.
3. Click the **+** button.
4. Choose **Install package from git URL...**.
5. Paste:

   ```text
   https://github.com/Figix/ModalTransformUnity.git
   ```

6. Click **Install**.

To pin a known version after tags are published, append a tag such as `#v0.1.0`.

### Method 2: `Packages/manifest.json`

Add the following entry inside the project's `dependencies` object:

```json
"com.figix.modal-transform": "https://github.com/Figix/ModalTransformUnity.git"
```

Example:

```json
{
  "dependencies": {
    "com.figix.modal-transform": "https://github.com/Figix/ModalTransformUnity.git"
  }
}
```

### Method 3: Local development package

Clone this repository anywhere outside the Unity project's `Assets` folder. In Package Manager choose **Install package from disk...** and select this repository's `package.json`.

## Usage

1. Select one or more GameObjects in the Scene view.
2. Move the pointer over the Scene view.
3. Press a transform key and move the mouse without holding a mouse button.
4. Confirm with Left Click or Enter, or cancel with Right Click or Esc.

| Action | Shortcut | Description |
|---|---:|---|
| Translate | `W` | Start modal movement. |
| Rotate | `E` | Start modal rotation. |
| Scale | `R` | Start modal scaling. |
| Axis constraint | `X`, `Y`, `Z` | Constrain to one axis. |
| Plane constraint | `Shift+X/Y/Z` | Exclude that axis. For example, `Shift+Z` uses the XY plane. |
| Numeric value | Number keys | Enter distance, degrees, or scale multiplier. |
| Confirm | Left Click or Enter | Keep the result as one Undo operation. |
| Cancel | Right Click or Esc | Restore the original transforms. |

The Scene toolbar's **Global/Local** setting controls translation and rotation constraint orientation. Axis-constrained scaling changes the corresponding local scale component in this MVP.

### Scene camera priority

Hold **Right Mouse Button first**, then use Unity's normal fly-camera keys:

- `RMB + W/A/S/D`: move forward, left, backward, and right
- `RMB + Q/E`: move down and up

The package does not consume these keys while RMB is already held. If a modal transform is already active, Right Click cancels it.

## Current MVP scope

- Scene view GameObject selection
- Single and multi-selection
- W/E/R modal transforms
- Global/Local axis constraints
- Plane constraints
- Numeric input
- Native Undo/Redo transaction
- RMB-first Scene camera input priority

Planned follow-ups include configurable shortcuts, snapping controls, cursor wrapping, pivot-mode refinements, prefab-stage validation, and broader Unity 6 version testing.

## Troubleshooting

- Make sure the mouse pointer is over the Scene view when pressing `W`, `E`, or `R`.
- Check **Window → General → Console** for compilation errors from another package or project script.
- If Package Manager cached an older Git revision, remove the package and install it again, or pin a newer tag.
- Unity must have Git installed and available to install a Git dependency.

## License

Copyright © 2026 Figix. Commercial project use and modification are permitted. Free redistribution requires attribution, and resale of this package or a modified standalone version is prohibited. See [LICENSE](LICENSE) for the complete terms.
