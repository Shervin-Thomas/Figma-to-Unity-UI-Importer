using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SimpleJSON;
using System.Net;
using System.IO;
using System.Collections.Generic;
using TMPro;

public class FigmaUIImporter : EditorWindow
{
    [MenuItem("Tools/Figma UI Importer")]
    public static void Open()
    {
        GetWindow<FigmaUIImporter>("Figma UI Importer");
    }

    private string fileKey;
    private string token;
    private string frameNodeId;        // e.g. 12:345
    private string targetCanvasName;   // OPTIONAL

    private const string IMAGE_FOLDER = "Assets/FigmaImages/";

    private WebClient web;
    private JSONNode frameNode;
    private List<JSONNode> renderableNodes = new List<JSONNode>();

    // ===================== UI =====================
    private void OnGUI()
    {
        GUILayout.Label("Figma → Unity (Full Layer Import)", EditorStyles.boldLabel);

        fileKey = EditorGUILayout.TextField("File Key", fileKey);
        token = EditorGUILayout.TextField("Access Token", token);
        frameNodeId = EditorGUILayout.TextField("Frame Node ID", frameNodeId);
        targetCanvasName =
            EditorGUILayout.TextField("Target Canvas (optional)", targetCanvasName);

        GUILayout.Space(10);

        if (GUILayout.Button("Import Frame Layers"))
            ImportFrame();
    }

    // ===================== IMPORT =====================
    void ImportFrame()
    {
        if (string.IsNullOrEmpty(fileKey) ||
            string.IsNullOrEmpty(token) ||
            string.IsNullOrEmpty(frameNodeId))
        {
            Debug.LogError("Missing inputs");
            return;
        }

        if (!Directory.Exists(IMAGE_FOLDER))
            Directory.CreateDirectory(IMAGE_FOLDER);

        web = new WebClient();
        web.Headers.Add("X-Figma-Token", token);

        // 1️⃣ Download full document
        string json;
        try
        {
            json = web.DownloadString(
                $"https://api.figma.com/v1/files/{fileKey}");
        }
        catch (WebException e)
        {
            Debug.LogError("Figma API error:\n" + e.Message);
            return;
        }

        JSONNode root = JSON.Parse(json);

        // 2️⃣ Find frame node
        frameNode = FindNodeById(root["document"], frameNodeId);
        if (frameNode == null)
        {
            Debug.LogError("Frame node not found");
            return;
        }

        // 3️⃣ Collect renderable nodes
        renderableNodes.Clear();
        CollectRenderableNodes(frameNode);

        if (renderableNodes.Count == 0)
        {
            Debug.LogError("No renderable nodes found");
            return;
        }

        // 4️⃣ Batch export images
        Dictionary<string, string> imagePaths =
            DownloadImages(renderableNodes);

        // 5️⃣ Build Unity UI
        BuildUnityUI(imagePaths);

        AssetDatabase.Refresh();
        Debug.Log("✅ Full frame assembled successfully");
    }

    // ===================== FIND NODE =====================
    JSONNode FindNodeById(JSONNode node, string id)
    {
        if (node["id"].Value == id)
            return node;

        if (!node.HasKey("children"))
            return null;

        foreach (JSONNode child in node["children"].AsArray)
        {
            JSONNode found = FindNodeById(child, id);
            if (found != null)
                return found;
        }

        return null;
    }

    // ===================== COLLECT NODES =====================
    void CollectRenderableNodes(JSONNode node)
    {
        string type = node["type"].Value;

        if (type != "GROUP" &&
            type != "BOOLEAN_OPERATION" &&
            type != "SLICE")
        {
            if (node.HasKey("absoluteBoundingBox"))
                renderableNodes.Add(node);
        }

        if (!node.HasKey("children")) return;

        foreach (JSONNode child in node["children"].AsArray)
            CollectRenderableNodes(child);
    }

    // ===================== IMAGE EXPORT =====================
    Dictionary<string, string> DownloadImages(List<JSONNode> nodes)
    {
        Dictionary<string, string> result = new Dictionary<string, string>();
        List<string> ids = new List<string>();

        foreach (JSONNode n in nodes)
        {
            // Do not request raster images for TEXT nodes
            if (n.HasKey("type") && n["type"].Value == "TEXT")
                continue;
            ids.Add(n["id"].Value); // SAFE
        }

        string idList = string.Join(",", ids);
        string url =
            $"https://api.figma.com/v1/images/{fileKey}?ids={idList}&format=png&scale=2";

        string json = web.DownloadString(url);
        JSONNode root = JSON.Parse(json);

        foreach (string id in ids)
        {
            string imgUrl = root["images"][id];
            if (string.IsNullOrEmpty(imgUrl)) continue;

            string path = IMAGE_FOLDER + id.Replace(":", "_") + ".png";

            File.WriteAllBytes(path, web.DownloadData(imgUrl));
            AssetDatabase.ImportAsset(path);

            TextureImporter ti =
                AssetImporter.GetAtPath(path) as TextureImporter;

            if (ti != null)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }

            result[id] = path;
        }

        return result;
    }

    // ===================== UNITY BUILD =====================
    void BuildUnityUI(Dictionary<string, string> images)
    {
        GameObject canvasGO = GetOrCreateCanvas();

        GameObject frameGO = new GameObject("FigmaFrame");
        frameGO.transform.SetParent(canvasGO.transform, false);

        RectTransform frameRT = frameGO.AddComponent<RectTransform>();
        frameRT.anchorMin = frameRT.anchorMax = new Vector2(0.5f, 0.5f);
        frameRT.anchoredPosition = Vector2.zero;

        var frameBox = frameNode["absoluteBoundingBox"];
        frameRT.sizeDelta =
            new Vector2(frameBox["width"].AsFloat,
                        frameBox["height"].AsFloat);

        // Recursively build the full hierarchy so grouped components
        // become parent-child GameObjects in Unity.
        CreateUnityChildren(frameNode, frameGO.transform, frameBox, images);
    }

    // Recursively build nodes and children preserving hierarchy.
    void CreateUnityChildren(JSONNode node, Transform parentTransform, JSONNode parentBox, Dictionary<string, string> images)
    {
        if (!node.HasKey("children")) return;

        foreach (JSONNode child in node["children"].AsArray)
        {
            // Some nodes may not have bounding boxes (e.g., certain effects)
            if (!child.HasKey("absoluteBoundingBox"))
            {
                // Still traverse deeper in case descendants are renderable
                CreateUnityChildren(child, parentTransform, parentBox, images);
                continue;
            }

            var box = child["absoluteBoundingBox"];
            string id = child["id"].Value;

            GameObject go = new GameObject(id);
            go.transform.SetParent(parentTransform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(box["width"].AsFloat, box["height"].AsFloat);

            // Convert Figma top-left coords to Unity center-anchored coords.
            float localX = box["x"].AsFloat - parentBox["x"].AsFloat;
            float localY = box["y"].AsFloat - parentBox["y"].AsFloat;
            float parentW = parentBox["width"].AsFloat;
            float parentH = parentBox["height"].AsFloat;
            float childW = box["width"].AsFloat;
            float childH = box["height"].AsFloat;
            rt.anchoredPosition = new Vector2(-parentW / 2f + localX + childW / 2f,
                                              parentH / 2f - (localY + childH / 2f));

            // Create Text for Figma TEXT nodes, else add Image if available.
            string type = child["type"].Value;
            if (type == "TEXT")
            {
                CreateTextComponent(go, child);
            }
            else if (images.ContainsKey(id))
            {
                Image img = go.AddComponent<Image>();
                img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(images[id]);
                img.raycastTarget = false;
            }

            // Recurse into children, using this node's box as the new reference.
            CreateUnityChildren(child, go.transform, box, images);
        }
    }

    void CreateTextComponent(GameObject go, JSONNode node)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.raycastTarget = false;
        tmp.text = node.HasKey("characters") ? node["characters"].Value : string.Empty;

        // Font size
        if (node.HasKey("style") && node["style"].HasKey("fontSize"))
        {
            float fs = node["style"]["fontSize"].AsFloat;
            if (fs > 0) tmp.fontSize = fs;
        }

        // Alignment (horizontal only basic mapping)
        if (node.HasKey("style") && node["style"].HasKey("textAlignHorizontal"))
        {
            string align = node["style"]["textAlignHorizontal"].Value;
            switch (align)
            {
                case "CENTER": tmp.alignment = TextAlignmentOptions.Center; break;
                case "RIGHT": tmp.alignment = TextAlignmentOptions.Right; break;
                case "JUSTIFIED": tmp.alignment = TextAlignmentOptions.Justified; break;
                default: tmp.alignment = TextAlignmentOptions.Left; break;
            }
        }

        // Color from first SOLID fill if available
        if (node.HasKey("fills") && node["fills"].IsArray && node["fills"].Count > 0)
        {
            var fill = node["fills"][0];
            if (fill.HasKey("type") && fill["type"].Value == "SOLID" && fill.HasKey("color"))
            {
                var c = fill["color"];
                float r = c["r"].AsFloat;
                float g = c["g"].AsFloat;
                float b = c["b"].AsFloat;
                float a = 1f;
                if (fill.HasKey("opacity")) a = fill["opacity"].AsFloat;
                tmp.color = new Color(r, g, b, a);
            }
        }
    }

    // ===================== CANVAS RESOLUTION =====================
    GameObject GetOrCreateCanvas()
    {
        if (!string.IsNullOrEmpty(targetCanvasName))
        {
            Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
            foreach (Canvas c in canvases)
            {
                if (c.name.Equals(
                    targetCanvasName,
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log("Using existing Canvas: " + c.name);
                    return c.gameObject;
                }
            }

            Debug.LogWarning(
                $"Canvas '{targetCanvasName}' not found. Creating new one.");
        }

        GameObject canvasGO = new GameObject(
            string.IsNullOrEmpty(targetCanvasName)
                ? "FigmaCanvas"
                : targetCanvasName);

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        // Keep transform scale at 1,1,1 by using constant pixel size.
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        scaler.referencePixelsPerUnit = 100f;

        // Ensure no inherited scaling from any parent
        var rt = canvasGO.GetComponent<RectTransform>();
        if (rt != null)
            rt.localScale = Vector3.one;

        canvasGO.AddComponent<GraphicRaycaster>();
        return canvasGO;
    }
}
