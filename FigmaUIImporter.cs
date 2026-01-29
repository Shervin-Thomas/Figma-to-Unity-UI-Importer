using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SimpleJSON;
using System.Net;
using System.IO;
using System.Collections.Generic;

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
            ids.Add(n["id"].Value); // SAFE

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

        foreach (JSONNode node in renderableNodes)
        {
            string id = node["id"].Value;
            if (!images.ContainsKey(id)) continue;

            var box = node["absoluteBoundingBox"];

            float x = box["x"].AsFloat - frameBox["x"].AsFloat;
            float y = box["y"].AsFloat - frameBox["y"].AsFloat;

            GameObject go = new GameObject(id);
            go.transform.SetParent(frameGO.transform, false);

            Image img = go.AddComponent<Image>();
            img.sprite =
                AssetDatabase.LoadAssetAtPath<Sprite>(images[id]);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta =
                new Vector2(box["width"].AsFloat,
                            box["height"].AsFloat);

            rt.anchoredPosition =
                new Vector2(x + box["width"].AsFloat / 2f,
                            -(y + box["height"].AsFloat / 2f));

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
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
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);

        canvasGO.AddComponent<GraphicRaycaster>();
        return canvasGO;
    }
}
