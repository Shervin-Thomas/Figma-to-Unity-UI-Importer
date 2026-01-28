# Figma UI Importer

**Overview**

This editor utility imports a Figma frame into Unity UI by:
- downloading the Figma file JSON
- locating a frame node by its ID
- exporting renderable child nodes as PNGs
- creating a `Canvas` and `Image` GameObjects positioned to match the frame

The importer class is `FigmaUIImporter` and lives at [Assets/Editor/FigmaUIImporter.cs](Assets/Editor/FigmaUIImporter.cs).

**Requirements**

- Unity 2019.4+ (script uses `UnityEditor` APIs; test on your project Unity version)
- `SimpleJSON.cs` must be available in the project (the importer uses `SimpleJSON` types).

We include a copy at [Assets/Plugins/SimpleJSON.cs](Assets/Plugins/SimpleJSON.cs) in this repository. If you don't have it, download SimpleJSON and add it under `Assets/Plugins/` or another Editor/runtime-accessible folder.

**Installation / Setup**

1. Verify `SimpleJSON.cs` is present at [Assets/Plugins/SimpleJSON.cs](Assets/Plugins/SimpleJSON.cs). If it's missing, place `SimpleJSON.cs` there.

   Example PowerShell copy (adjust source path):

```powershell
Copy-Item "C:\path\to\SimpleJSON.cs" -Destination "Assets\Plugins\"
```

2. Ensure the importer file is present at [Assets/Editor/FigmaUIImporter.cs](Assets/Editor/FigmaUIImporter.cs).
3. Open Unity and let it compile the scripts.

**Using the Importer**

1. In Unity, open the menu: `Tools → Figma UI Importer`.
2. In the window, fill the fields:
- **File Key**: the Figma file key (from the file URL: https://www.figma.com/file/<fileKey>/...)
- **Access Token**: your Figma personal access token (see Figma docs)
- **Frame Node ID**: the node id for the target frame (example `12:345`) — use Figma Inspect or the file JSON to find this.
3. Click **Import Frame Layers**.

The importer will:
- Download the full file JSON from `https://api.figma.com/v1/files/{fileKey}`
- Find the frame node with the supplied ID
- Collect renderable child nodes (skips GROUP/BOOLEAN_OPERATION/SLICE unless they have an absolute bounding box)
- Request image URLs from `https://api.figma.com/v1/images/{fileKey}?ids=...&format=png&scale=2`
- Download PNGs into `Assets/FigmaImages/`
- Configure each image as a `Sprite` and add GameObjects under a created `FigmaCanvas` → `FigmaFrame`

**Output / Where things appear**

- Image files: `Assets/FigmaImages/` (created if missing)
- Canvas root: a GameObject named `FigmaCanvas` with `Canvas`, `CanvasScaler` and `GraphicRaycaster`
- Frame container: `FigmaFrame` under the canvas
- Child images: GameObjects named by their Figma node id (e.g. `12:345`)

**Fields explained (UI)**

- **File Key**: string between `/file/` and the next `/` in the Figma URL.
- **Access Token**: Figma personal access token (a token with `file_read` scope is sufficient for public/private files you have access to).
- **Frame Node ID**: the Figma node id for the frame to import. You can obtain this from the Figma API JSON or via plugins/tools that reveal node ids.

**Troubleshooting**

- If `Frame node not found` appears: verify the `Frame Node ID` and that the file JSON contains the node (the importer searches `document` recursively).
- If images fail to download: check `Access Token` and file permissions; inspect any WebException message in the Unity Console.
- If sprites show up blank: ensure `Assets/FigmaImages/` was imported (Assets → Refresh happened automatically) and the PNG files are present and imported as `Sprite`.
- If `SimpleJSON` errors occur: confirm `SimpleJSON.cs` is in an `Assets` folder and contains the `SimpleJSON` namespace; move it to `Assets/Plugins/` if necessary.

**Notes & Tips**

- The importer sets `TextureImporter.textureType = Sprite` and disables mipmaps for downloaded images.
- Positions are converted from Figma's coordinate system to Unity `RectTransform.anchoredPosition` (center-anchored with Y flipped).
- The importer currently creates a new `FigmaCanvas` each run; delete or rename existing ones if you want multiple imports.

**Source & Attribution**

See the importer implementation at [Assets/Editor/FigmaUIImporter.cs](Assets/Editor/FigmaUIImporter.cs).

If you need enhancements (layer naming, grouping, transforms, scale adjustments, or text handling), open an issue or extend `BuildUnityUI` and `CollectRenderableNodes` in `FigmaUIImporter`.

---
Generated README for the `FigmaUIImporter` editor tool.
