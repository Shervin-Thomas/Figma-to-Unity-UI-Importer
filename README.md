# Figma UI Importer

Overview

- Imports a Figma frame into Unity UI, preserving parent-child hierarchy.
- Renders non-text layers as Sprites; imports TEXT nodes as real `TextMeshProUGUI`.
- Positions are converted from Figma (top-left origin) to Unity (center-anchored) precisely.
- Canvas uses constant pixel size so the transform scale remains 1,1,1.

The importer class is `FigmaUIImporter` at [Assets/Editor/FigmaUIImporter.cs](Assets/Editor/FigmaUIImporter.cs).

What's New

- Hierarchical grouping: groups become parent GameObjects with nested children.
- Text import: Figma `TEXT` nodes become `TextMeshProUGUI` with characters, size, alignment, and color.
- Correct placement: children are positioned relative to their parent bounds to match Figma.
- Canvas scaler updated to constant pixel size; Canvas scale shows 1,1,1.

Requirements

- Unity 2019.4+ (EditorWindow + UI APIs).
- `SimpleJSON.cs` in the project (used to parse Figma JSON). Get it from https://github.com/Bunny83/SimpleJSON/blob/master/SimpleJSON.cs and place it under `Assets/Plugins/`.
- TextMesh Pro package. Install TMP essentials if prompted (`Window → TextMeshPro → Import TMP Essential Resources`).

Installation / Setup

1. Ensure `SimpleJSON.cs` exists at [Assets/Plugins/SimpleJSON.cs](Assets/Plugins/SimpleJSON.cs).
2. Verify the importer at [Assets/Editor/FigmaUIImporter.cs](Assets/Editor/FigmaUIImporter.cs).
3. If TMP assets are missing, import TMP essentials.
4. Open Unity and let scripts compile.

Using the Importer

1. Open `Tools → Figma UI Importer`.
2. Fill in:
   - File Key: from the Figma URL.
   - Access Token: your Figma personal access token.
   - Frame Node ID: e.g., `12:345`.
   - Target Canvas (optional): name of an existing `Canvas` to import into; blank creates `FigmaCanvas`.
3. Click Import Frame Layers.

What it does

- Downloads `https://api.figma.com/v1/files/{fileKey}` and finds the specified frame.
- Collects renderable nodes. TEXT nodes are not rasterized.
- Requests PNGs only for non-text nodes via the Figma image API.
- Saves sprites to `Assets/FigmaImages/` and creates `FigmaCanvas` → `FigmaFrame`.
- Builds the Unity hierarchy recursively, positioning children relative to parent boxes.

Output

- Sprites: `Assets/FigmaImages/`.
- Root canvas: `FigmaCanvas` with `Canvas`, `CanvasScaler` (Constant Pixel Size), `GraphicRaycaster`.
- Frame: `FigmaFrame` under the canvas.
- Children: GameObjects named by Figma node id. TEXT nodes have `TextMeshProUGUI` components.

Text specifics

- Content: from `node.characters`.
- Size: from `style.fontSize`.
- Alignment: maps `style.textAlignHorizontal` to Left/Center/Right/Justified.
- Color: first SOLID fill; alpha from `opacity` when present.
- Font: uses your project’s default TMP font; assign a specific font asset if needed.

Troubleshooting

- Frame node not found: confirm the ID and API access.
- Images missing: check token scope and network; see Console errors.
- Text invisible or pink: import TMP essentials and assign a valid TMP font asset.
- Misalignment: ensure the Game view resolution is near the Figma frame size; positions are computed relative to parent bounds.
- `SimpleJSON` errors: place `SimpleJSON.cs` under `Assets/Plugins/`.

Notes

- Texture import sets `Sprite` type and disables mipmaps.
- The importer preserves hierarchy; you can move groups as single units in Unity.
- Canvas scale remains 1,1,1 for predictable layout.

Source

Implementation: [Assets/Editor/FigmaUIImporter.cs](Assets/Editor/FigmaUIImporter.cs).

If you need enhancements (node naming, font mapping, vertical alignment, auto font assignment), extend `CreateUnityChildren()` and `CreateTextComponent()` in `FigmaUIImporter`.
