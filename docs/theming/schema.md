# Fluence Theme Schema

A Fluence theme is a `.json` file placed in `~/.fluence/themes/`. The file uses a
Fluence-specific wrapper format that may optionally embed a TextMate grammar theme.

## Top-level structure

```jsonc
{
  "name": "My Theme",       // Display name shown in the theme picker
  "font": { ... },          // Optional — font overrides
  "colors": { ... },        // Optional — UI color overrides
  "semanticTokenColors": { ... }, // Optional — semantic token color overrides
  "textMate": { ... }       // Optional — embedded TextMate theme object
}
```

### `font`

| Key      | Type   | Description                                      |
|----------|--------|--------------------------------------------------|
| `family` | string | CSS-style font family list, e.g. `"JetBrains Mono, monospace"` |
| `size`   | number | Base font size in pixels (minimum 8)             |

### `colors`

Each value must be a valid CSS color string (`#RRGGBB`, `#AARRGGBB`, or named colors
that Avalonia's `Color.Parse` accepts). Unknown or invalid values are silently ignored
and the built-in default is used instead.

#### Color keys — Fluence vocabulary (preferred)

| JSON key                      | Maps to                          | Description                                  |
|-------------------------------|----------------------------------|----------------------------------------------|
| `shell.background`            | `ThemeColors.Shell`              | Outermost window / title-bar background      |
| `surface.background`          | `ThemeColors.Surface`            | Sidebar / panel background                   |
| `surface.elevated`            | `ThemeColors.SurfaceElevated`    | Dropdowns, tooltips, elevated panels         |
| `input.background`            | `ThemeColors.InputBackground`    | Text-input fields background                 |
| `border`                      | `ThemeColors.Border`             | Dividers and border strokes                  |
| `text.primary`                | `ThemeColors.TextPrimary`        | Primary foreground text                      |
| `text.secondary`              | `ThemeColors.TextSecondary`      | Dimmed / helper text                         |
| `accent`                      | `ThemeColors.Accent`             | Focus rings, active highlights, links        |
| `error`                       | `ThemeColors.Error`              | Error indicators, squiggles                  |
| `editor.background`           | `ThemeColors.EditorBackground`   | Code editor background                       |
| `editor.foreground`           | `ThemeColors.EditorForeground`   | Code editor default text color               |
| `editor.selection`            | `ThemeColors.EditorSelection`    | Selected text background in the editor       |
| `activityBar.background`      | `ThemeColors.ActivityBarBackground`         | Activity bar background         |
| `activityBar.activeBg`        | `ThemeColors.ActivityBarActiveBackground`   | Active item highlight in the bar|
| `activityBar.foreground`      | `ThemeColors.ActivityBarForeground`         | Active icon color               |
| `activityBar.inactiveFg`      | `ThemeColors.ActivityBarInactiveForeground` | Inactive icon color             |
| `bottomBar.background`        | `ThemeColors.BottomBarBackground`           | Status / bottom bar background  |

#### VS Code vocabulary fallbacks (backwards compatibility)

Theme files that use VS Code key names continue to work. The Fluence key is tried first;
if absent, the VS Code key is used.

| VS Code key                   | Fluence equivalent               |
|-------------------------------|----------------------------------|
| `sideBar.background`          | `surface.background`             |
| `foreground`                  | `text.primary`                   |
| `descriptionForeground`       | `text.secondary`                 |
| `focusBorder`                 | `accent`                         |
| `errorForeground`             | `error`                          |
| `editor.selectionBackground`  | `editor.selection`               |
| `activityBar.activeBackground`| `activityBar.activeBg`           |
| `activityBar.inactiveForeground` | `activityBar.inactiveFg`      |

### `semanticTokenColors`

Maps semantic token type names to colors. Each value can be a plain hex string or an
object with a `foreground` key:

```jsonc
"semanticTokenColors": {
  "class": "#4EC9B0",
  "method": { "foreground": "#DCDCAA" }
}
```

Supported token types (defaults shown):

| Token type       | Default    |
|------------------|------------|
| `class`          | `#4EC9B0`  |
| `delegateName`   | `#4EC9B0`  |
| `record`         | `#4EC9B0`  |
| `interface`      | `#B8D7A3`  |
| `struct`         | `#86C691`  |
| `recordStruct`   | `#86C691`  |
| `enum`           | `#B8D7A3`  |
| `enumMember`     | `#51B6C4`  |
| `typeParameter`  | `#B8D7A3`  |
| `method`         | `#DCDCAA`  |
| `extensionMethod`| `#DCDCAA`  |
| `event`          | `#DCDCAA`  |
| `property`       | `#9CDCFE`  |
| `field`          | `#D4D4D4`  |
| `staticSymbol`   | `#51B6C4`  |
| `local`          | `#9CDCFE`  |
| `parameter`      | `#9CDCFE`  |

### `textMate`

An optional embedded TextMate theme object (same schema as a standalone `.tmTheme.json`
/ `vscode-dark` style file with a `settings` array). When present, only this subtree is
passed to the TextMate engine; the rest of the Fluence file is ignored by TextMate.

When absent, the entire file is passed to the TextMate parser (which tolerates unknown
keys), so VS Code-compatible theme files work without modification.

```jsonc
"textMate": {
  "name": "My Theme",
  "settings": [
    {
      "settings": {
        "background": "#1E1E1E",
        "foreground": "#D4D4D4"
      }
    },
    {
      "scope": "comment",
      "settings": { "foreground": "#6A9955", "fontStyle": "italic" }
    }
  ]
}
```

## Installation

Themes can be installed via the Settings panel (Preferences → Themes → Install) or by
copying the `.json` file into `~/.fluence/themes/`. Live-reload is active: editing a
theme file on disk applies it instantly without restarting.

To switch themes, open **Preferences → Settings** and set the `Theme` field to the
theme file name (with or without the `.json` extension) or `FluenceDark` for the
built-in theme.
