#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PrefabIconGeneratorWindow : EditorWindow
{
    [SerializeField] private List<GameObject> prefabs = new List<GameObject>();

    [SerializeField] private string outputFolder = "Assets/GeneratedIcons";
    [SerializeField] private int resolution = 512;
    [SerializeField, Range(0f, 90f)] private float cameraElevation = 35f;
    [SerializeField, Range(0f, 360f)] private float cameraAzimuth = 45f;
    [SerializeField, Range(0f, 360f)] private float prefabRotationY = 0f;
    [SerializeField, Range(1f, 2f)] private float framingPadding = 1.15f;
    [SerializeField] private bool overwriteExisting = true;

    [MenuItem("Tools/Axiom/Prefab Icon Generator")]
    private static void OpenWindow()
    {
        GetWindow<PrefabIconGeneratorWindow>("Prefab Icon Generator");
    }

    // Adds selected prefab assets from the Project window to the list.
    [MenuItem("Assets/Axiom/Add Selected Prefabs To Icon Generator")]
    private static void AddSelectedPrefabsToWindow()
    {
        PrefabIconGeneratorWindow window = GetWindow<PrefabIconGeneratorWindow>("Prefab Icon Generator");
        window.AddPrefabs(Selection.objects);
        window.Show();
    }

    [MenuItem("Assets/Axiom/Add Selected Prefabs To Icon Generator", true)]
    private static bool ValidateAddSelectedPrefabsToWindow()
    {
        foreach (UnityEngine.Object selected in Selection.objects)
        {
            GameObject gameObject = selected as GameObject;
            if (gameObject == null)
                continue;

            string path = AssetDatabase.GetAssetPath(gameObject);
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void AddPrefabs(UnityEngine.Object[] selectedObjects)
    {
        foreach (UnityEngine.Object selected in selectedObjects)
        {
            GameObject gameObject = selected as GameObject;
            if (gameObject == null)
                continue;

            string path = AssetDatabase.GetAssetPath(gameObject);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!prefabs.Contains(gameObject))
                prefabs.Add(gameObject);
        }

        Repaint();
    }

    private void OnGUI()
    {
        SerializedObject serializedWindow = new SerializedObject(this);
        serializedWindow.Update();

        EditorGUILayout.LabelField("Prefab list", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedWindow.FindProperty("prefabs"), new GUIContent("Prefabs"), true);
        serializedWindow.ApplyModifiedProperties();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Camera and object", EditorStyles.boldLabel);

        resolution = EditorGUILayout.IntPopup(
            "PNG resolution",
            resolution,
            new[] { "128", "256", "512", "1024", "2048" },
            new[] { 128, 256, 512, 1024, 2048 }
        );

        cameraElevation = EditorGUILayout.Slider("View from above", cameraElevation, 0f, 90f);
        cameraAzimuth = EditorGUILayout.Slider("Camera rotation around object", cameraAzimuth, 0f, 360f);
        prefabRotationY = EditorGUILayout.Slider("Prefab rotation around Y", prefabRotationY, 0f, 360f);
        framingPadding = EditorGUILayout.Slider("Framing margin", framingPadding, 1f, 2f);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        outputFolder = EditorGUILayout.TextField("Output folder", outputFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(65f)))
            BrowseOutputFolder();
        EditorGUILayout.EndHorizontal();

        overwriteExisting = EditorGUILayout.Toggle("Overwrite existing PNGs", overwriteExisting);

        EditorGUILayout.HelpBox(
            "Each prefab is loaded into an isolated Editor scene, rendered, and unloaded before the next prefab is processed. " +
            "The original prefab asset is not modified. PNG filenames use the prefab root object's name.",
            MessageType.Info
        );

        EditorGUILayout.Space(8f);

        int assignedCount = 0;
        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null)
                assignedCount++;
        }

        using (new EditorGUI.DisabledScope(assignedCount == 0))
        {
            if (GUILayout.Button("Generate All PNG Icons", GUILayout.Height(32f)))
                GenerateAll();
        }

        EditorGUILayout.LabelField("Assigned prefabs: " + assignedCount);
    }

    private void BrowseOutputFolder()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string startingFolder = Path.IsPathRooted(outputFolder)
            ? outputFolder
            : Path.Combine(projectRoot, outputFolder);

        string selectedFolder = EditorUtility.OpenFolderPanel("Choose PNG output folder", startingFolder, string.Empty);
        if (string.IsNullOrEmpty(selectedFolder))
            return;

        string fullProjectRoot = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullSelectedFolder = Path.GetFullPath(selectedFolder);
        string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (fullSelectedFolder.Equals(assetsRoot, StringComparison.OrdinalIgnoreCase))
        {
            outputFolder = "Assets";
        }
        else if (fullSelectedFolder.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            outputFolder = "Assets/" + fullSelectedFolder.Substring(assetsRoot.Length + 1).Replace('\\', '/');
        }
        else if (fullSelectedFolder.StartsWith(fullProjectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            outputFolder = fullSelectedFolder.Substring(fullProjectRoot.Length + 1).Replace('\\', '/');
        }
        else
        {
            outputFolder = fullSelectedFolder;
        }
    }

    private void GenerateAll()
    {
        string resolvedOutputFolder;
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            resolvedOutputFolder = Path.IsPathRooted(outputFolder)
                ? Path.GetFullPath(outputFolder)
                : Path.GetFullPath(Path.Combine(projectRoot, outputFolder));

            Directory.CreateDirectory(resolvedOutputFolder);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Prefab Icon Generator", "Output folder could not be created. Check the folder path and permissions.", "OK");
            return;
        }

        int succeeded = 0;
        int skipped = 0;
        int failed = 0;
        bool cancelled = false;
        HashSet<string> processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                string displayName = prefab != null ? prefab.name : "Missing prefab";

                if (EditorUtility.DisplayCancelableProgressBar(
                    "Prefab Icon Generator",
                    "Processing " + displayName + " (" + (i + 1) + "/" + prefabs.Count + ")",
                    prefabs.Count == 0 ? 1f : (float)i / prefabs.Count))
                {
                    cancelled = true;
                    break;
                }

                if (prefab == null)
                {
                    failed++;
                    Debug.LogWarning("[Prefab Icon Generator] A null entry was skipped.");
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    failed++;
                    Debug.LogWarning("[Prefab Icon Generator] '" + prefab.name + "' is not a .prefab asset and was skipped.", prefab);
                    continue;
                }

                if (!processedPaths.Add(assetPath))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    bool wasSkipped;
                    CapturePrefab(assetPath, resolvedOutputFolder, out wasSkipped);

                    if (wasSkipped)
                        skipped++;
                    else
                        succeeded++;
                }
                catch (Exception exception)
                {
                    failed++;
                    Debug.LogError("[Prefab Icon Generator] Failed to capture '" + prefab.name + "'.\n" + exception);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        string result = "Generated: " + succeeded + "\nSkipped: " + skipped + "\nFailed: " + failed;
        if (cancelled)
            result += "\n\nGeneration was cancelled.";

        EditorUtility.DisplayDialog("Prefab Icon Generator", result, "OK");
    }

    private void CapturePrefab(string assetPath, string destinationFolder, out bool wasSkipped)
    {
        wasSkipped = false;

        GameObject prefabRoot = null;
        Camera captureCamera = null;
        RenderTexture renderTexture = null;
        Texture2D image = null;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            // Load this prefab into its own isolated preview scene.
            prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
            if (prefabRoot == null)
                throw new InvalidOperationException("Unity could not load the prefab contents.");

            string fileName = SanitizeFileName(prefabRoot.name) + ".png";
            string outputPath = Path.Combine(destinationFolder, fileName);

            if (File.Exists(outputPath) && !overwriteExisting)
            {
                wasSkipped = true;
                return;
            }

            List<Renderer> renderers = GetUsableRenderers(prefabRoot);
            if (renderers.Count == 0)
                throw new InvalidOperationException("The prefab contains no enabled Renderer components on active objects.");

            Bounds initialBounds = CalculateBounds(renderers);

            // Rotate the prefab around the center of its visible geometry, not around an arbitrary root pivot.
            if (Mathf.Abs(prefabRotationY) > 0.001f)
                prefabRoot.transform.RotateAround(initialBounds.center, Vector3.up, prefabRotationY);

            Bounds finalBounds = CalculateBounds(renderers);

            GameObject cameraObject = new GameObject("__PrefabIconCaptureCamera__");
            SceneManager.MoveGameObjectToScene(cameraObject, prefabRoot.scene);
            captureCamera = cameraObject.AddComponent<Camera>();
            ConfigureCamera(captureCamera, finalBounds, renderers);

            CreateLighting(prefabRoot.scene);

            renderTexture = new RenderTexture(
                resolution,
                resolution,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            );
            renderTexture.name = "__PrefabIconRenderTexture__";
            renderTexture.antiAliasing = 1;
            renderTexture.useMipMap = false;
            renderTexture.autoGenerateMips = false;

            if (!renderTexture.Create())
                throw new InvalidOperationException("Unity could not create the capture RenderTexture.");

            captureCamera.targetTexture = renderTexture;
            captureCamera.Render();

            RenderTexture.active = renderTexture;
            image = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, false);
            image.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            image.Apply(false, false);

            File.WriteAllBytes(outputPath, image.EncodeToPNG());
            Debug.Log("[Prefab Icon Generator] Saved: " + outputPath);
        }
        finally
        {
            RenderTexture.active = previousActive;

            if (captureCamera != null)
                captureCamera.targetTexture = null;

            if (image != null)
                DestroyImmediate(image);

            if (renderTexture != null)
            {
                renderTexture.Release();
                DestroyImmediate(renderTexture);
            }

            // This also closes the isolated preview scene and destroys its temporary camera and lights.
            if (prefabRoot != null)
                PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private void ConfigureCamera(Camera camera, Bounds bounds, List<Renderer> renderers)
    {
        camera.enabled = false;
        camera.cameraType = CameraType.Preview;
        camera.orthographic = true;
        camera.aspect = 1f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.cullingMask = ~0;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.useOcclusionCulling = false;
        camera.nearClipPlane = 0.01f;

        float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
        Vector3 orbitDirection = Quaternion.Euler(cameraElevation, cameraAzimuth, 0f) * Vector3.back;
        float distance = radius * 3f + 1f;

        camera.transform.position = bounds.center + orbitDirection * distance;

        // A horizontal reference vector keeps the image orientation stable, including a near top-down view.
        Vector3 cameraUp = Quaternion.Euler(0f, cameraAzimuth, 0f) * Vector3.forward;
        camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, cameraUp);

        camera.farClipPlane = Mathf.Max(1000f, distance + radius * 4f + 10f);

        float maxHorizontal = 0f;
        float maxVertical = 0f;

        // Fit all eight corners of every renderer's world-space bounds into the orthographic view.
        foreach (Renderer renderer in renderers)
        {
            Bounds rendererBounds = renderer.bounds;
            Vector3 min = rendererBounds.min;
            Vector3 max = rendererBounds.max;

            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                Vector3 corner = new Vector3(
                    (cornerIndex & 1) == 0 ? min.x : max.x,
                    (cornerIndex & 2) == 0 ? min.y : max.y,
                    (cornerIndex & 4) == 0 ? min.z : max.z
                );

                Vector3 cameraLocal = camera.transform.InverseTransformPoint(corner);
                maxHorizontal = Mathf.Max(maxHorizontal, Mathf.Abs(cameraLocal.x));
                maxVertical = Mathf.Max(maxVertical, Mathf.Abs(cameraLocal.y));
            }
        }

        float requiredSize = Mathf.Max(maxVertical, maxHorizontal / camera.aspect);
        camera.orthographicSize = Mathf.Max(0.05f, requiredSize * framingPadding);
    }

    private static List<Renderer> GetUsableRenderers(GameObject root)
    {
        Renderer[] allRenderers = root.GetComponentsInChildren<Renderer>(true);
        List<Renderer> usableRenderers = new List<Renderer>();

        foreach (Renderer renderer in allRenderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            usableRenderers.Add(renderer);
        }

        return usableRenderers;
    }

    private static Bounds CalculateBounds(List<Renderer> renderers)
    {
        if (renderers == null || renderers.Count == 0)
            throw new InvalidOperationException("Cannot calculate bounds: no renderers were found.");

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    private static void CreateLighting(Scene scene)
    {
        CreateDirectionalLight(
            scene,
            "__PrefabIconKeyLight__",
            1.2f,
            new Color(1f, 0.95f, 0.85f),
            Quaternion.Euler(50f, -30f, 0f),
            LightShadows.Soft
        );

        CreateDirectionalLight(
            scene,
            "__PrefabIconFillLight__",
            0.45f,
            new Color(0.65f, 0.78f, 1f),
            Quaternion.Euler(25f, 145f, 0f),
            LightShadows.None
        );
    }

    private static void CreateDirectionalLight(
        Scene scene,
        string objectName,
        float intensity,
        Color color,
        Quaternion rotation,
        LightShadows shadows)
    {
        GameObject lightObject = new GameObject(objectName);
        SceneManager.MoveGameObjectToScene(lightObject, scene);

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.shadows = shadows;
        light.transform.rotation = rotation;
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidCharacter, '_');

        return string.IsNullOrWhiteSpace(fileName) ? "PrefabIcon" : fileName;
    }
}
#endif
