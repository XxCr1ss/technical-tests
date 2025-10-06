using System;
using UnityEngine;
using Random = System.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class BuildingTextureGenerator : MonoBehaviour {
  [Header("Texture")]
  public int textureWidth = 512;
  public int textureHeight = 1024;
  [Header("Window grid")]
  public int cols = 8;
  public int rows = 20;
  [Range(0f, 1f)] public float windowOnProbability = 0.33f;
  [Range(0f, 0.4f)] public float windowPaddingNormalized = 0.08f; // padding fraction inside cell
  [Header("Colors / emission")]
  public Color facadeColor = new Color(0.03f, 0.05f, 0.08f);
  public Color windowOffColor = new Color(0.02f, 0.02f, 0.025f);
  public Color[] windowPalette = {
                                         new Color(1f, 0.85f, 0.45f), // warm
                                         new Color(0.45f, 1f, 0.6f), // greenish
                                         new Color(0.6f, 0.6f, 1f) // cool
                                 };
  public float emissionIntensity = 2f;
  [Header("Target")]
  public Material targetMaterial; // material to apply the textures
  [Header("Options")]
  public int randomSeed;
  public bool saveAsAsset;
  public string assetNamePrefix = "BuildingTex_";
  [Header("Top face (roof) clearing")]
  public bool clearTopFace = true; // if true, it will clear the top strip
  [Range(0f, 0.5f)]
  public float topFaceNormalizedHeight = 0.12f; // fraction of the texture in Y that corresponds to the roof
  [Header("Roof material")]
  public Material roofMaterial;

  // Context menu to quickly generate from inspector
  [ContextMenu("Generate Textures")]
  public void GenerateFromInspector() {
    GenerateAndApply(randomSeed);
  }
  public void GenerateAndApply(int seed) {
    Random rng = new Random(seed);

    Texture2D albedo = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
    Texture2D emission = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);

    // Fill base
    var baseFill = new Color[textureWidth * textureHeight];
    for (int i = 0; i < baseFill.Length; i++) baseFill[i] = facadeColor;
    albedo.SetPixels(baseFill);

    var emBase = new Color[textureWidth * textureHeight];
    for (int i = 0; i < emBase.Length; i++) emBase[i] = Color.black;
    emission.SetPixels(emBase);

    // Grid cell size in pixels
    int cellW = textureWidth / cols;
    int cellH = textureHeight / rows;
    int padX = Mathf.Max(1, Mathf.RoundToInt(cellW * windowPaddingNormalized));
    int padY = Mathf.Max(1, Mathf.RoundToInt(cellH * windowPaddingNormalized));

    for (int cy = 0; cy < rows; cy++) {
      for (int cx = 0; cx < cols; cx++) {
        // cell bounds
        int x0 = cx * cellW + padX;
        int x1 = (cx + 1) * cellW - padX;
        int y0 = cy * cellH + padY;
        int y1 = (cy + 1) * cellH - padY;

        // slight randomness in window shape
        int jitterW = rng.Next(-Mathf.Max(0, padX / 3), Mathf.Max(1, padX / 3));
        int jitterH = rng.Next(-Mathf.Max(0, padY / 3), Mathf.Max(1, padY / 3));
        x0 = Mathf.Clamp(x0 + jitterW, 0, textureWidth - 1);
        x1 = Mathf.Clamp(x1 + jitterW, 0, textureWidth - 1);
        y0 = Mathf.Clamp(y0 + jitterH, 0, textureHeight - 1);
        y1 = Mathf.Clamp(y1 + jitterH, 0, textureHeight - 1);

        bool isOn = rng.NextDouble() <= windowOnProbability;

        Color winColor = windowPalette[rng.Next(0, windowPalette.Length)];
        float brightness = 0.85f + (float)rng.NextDouble() * 0.3f;

        // draw window rectangle
        for (int y = y0; y <= y1; y++) {
          for (int x = x0; x <= x1; x++) {
            // border thickness
            int border = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(x1 - x0, y1 - y0) * 0.08f));
            bool isBorder = x < x0 + border || x > x1 - border || y < y0 + border || y > y1 - border;
            if (isBorder) {
              albedo.SetPixel(x, y, Color.Lerp(facadeColor, windowOffColor, 0.6f));
            }
            else {
              if (isOn) {
                // vary intensity with small noise; make windows slightly textured
                float nx = (float)x / textureWidth + (float)rng.NextDouble() * 0.01f;
                float ny = (float)y / textureHeight + (float)rng.NextDouble() * 0.01f;
                float n = Mathf.PerlinNoise(nx * 10f, ny * 10f) * 0.2f + 0.9f;
                Color c = winColor * (brightness * n);
                albedo.SetPixel(x, y, c);
                // emission with radial falloff
                float cxF = (x0 + x1) * 0.5f;
                float cyF = (y0 + y1) * 0.5f;
                float dx = (x - cxF) / (x1 - x0);
                float dy = (y - cyF) / (y1 - y0);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float fall = Mathf.Clamp01(1f - dist);
                Color em = winColor * (fall * brightness * (emissionIntensity * 0.6f));
                // clamp to avoid HDR overflow in RGBA32
                emission.SetPixel(x, y, em);
              }
              else {
                albedo.SetPixel(x, y, windowOffColor * (0.6f + (float)rng.NextDouble() * 0.2f));
                // emission remains black
              }
            }
          }
        }
      }
    }

    // --- Clear top face (roof) area so there are NO windows there ---
    if (clearTopFace) {
      int topRows = Mathf.Clamp(Mathf.RoundToInt(textureHeight * topFaceNormalizedHeight), 1, textureHeight);
      int yStart = textureHeight - topRows; // from this row up to top (inclusive)
      for (int y = yStart; y < textureHeight; y++) {
        for (int x = 0; x < textureWidth; x++) {
          albedo.SetPixel(x, y, facadeColor); // ensure facade color on the roof
          emission.SetPixel(x, y, Color.black); // no emission on the roof
        }
      }
    }
    // ---------------------------------------------------------------

    albedo.Apply();
    emission.Apply();

    // Assign to material
    if (targetMaterial != null) {
      targetMaterial.SetTexture("_MainTex", albedo);
      targetMaterial.SetTexture("_EmissionMap", emission);
      targetMaterial.EnableKeyword("_EMISSION");
      // set emission color to white; texture controls color per pixel
      targetMaterial.SetColor("_EmissionColor", Color.white * 1f);
      // for URP/Standard the emission map will now glow visually
    }

    // ---- NEW: Ensure child "Roof" quad uses facadeColor ----
    // Search for a direct child named "Roof" (case-sensitive). If found,
    // instance its material to avoid modifying shared assets and assign the color.
    Transform roofTransform = transform.Find("Roof");
    if (roofTransform != null) {
      Renderer roofRenderer = roofTransform.GetComponent<Renderer>();
      if (roofRenderer != null) {
        // Material instance (creates one if needed)
        Material roofMat = roofMaterial;
        // Assign color to the most common properties:
        // - URP/Lit: "_BaseColor"
        // - Standard: "_Color"
        if (roofMat.HasProperty("_BaseColor")) {
          roofMat.SetColor("_BaseColor", facadeColor);
        }
        if (roofMat.HasProperty("_Color")) {
          roofMat.SetColor("_Color", facadeColor);
        }
        // Ensure the main texture (if it exists) is not tinting the color
        // If you want to remove the texture and use a plain color, uncomment the following line:
        // roofMat.SetTexture("_MainTex", null);

        // Disable emission on the roof (ensure black)
        if (roofMat.HasProperty("_EmissionColor")) {
          roofMat.SetColor("_EmissionColor", Color.black);
        }
        roofMat.DisableKeyword("_EMISSION");
        // Assign the instance back (roofRenderer.material already does this), but reaffirm sharedMaterial in the editor if necessary.
        #if UNITY_EDITOR
        EditorUtility.SetDirty(roofMat);
        #endif
      }
      else {
        Debug.LogWarning(
          $"BuildingTextureGenerator: child 'Roof' found on '{name}' but has no Renderer. Add a MeshRenderer or SpriteRenderer to the Quad."
        );
      }
    }
    else {
      // We did not find a child named "Roof". This is not a critical error, just informational.
      Debug.Log(
        $"BuildingTextureGenerator: no child named 'Roof' found on '{name}'. If you want the roof to adopt the color, create a child Quad called 'Roof'."
      );
    }
    // ---------------------------------------------------------------

    #if UNITY_EDITOR
    if (saveAsAsset) {
      string folder = "Assets/GeneratedBuildingTextures";
      if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "GeneratedBuildingTextures");
      string baseName = assetNamePrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss");
      // create copies because texture will be destroyed when run in editor outside play mode
      Texture2D albedoCopy = Instantiate(albedo);
      Texture2D emissionCopy = Instantiate(emission);
      AssetDatabase.CreateAsset(albedoCopy, $"{folder}/{baseName}_albedo.asset");
      AssetDatabase.CreateAsset(emissionCopy, $"{folder}/{baseName}_emission.asset");
      AssetDatabase.SaveAssets();
      Debug.Log($"Saved textures to {folder} as {baseName}_*.asset");
    }
    #endif
  }
}
