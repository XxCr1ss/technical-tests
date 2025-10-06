using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CityGenerator : MonoBehaviour {
  // Standard name of father object
  const string defaultHolderName = "City_Generated";

  // Grid
  public int blocksX = 15;
  public int blocksY = 15;

  // Building
  public GameObject buildingPrefab;
  public Vector2 buildingSize = new Vector2(1, 1);
  public float buildingHeightMin = 2f, buildingHeightMax = 10f;
  public float gapBetweenBuildings = 0.2f;

  // Street
  public float streetWidth = 3f;

  // Parent - Seed
  public Transform parentHolder;
  public int randomSeed;

  // Sidewalks
  public float sidewalkWidth = 0.6f;
  public float sidewalkHeightOffset = 0.12f; // Sidewalk height (the base on which buildings will rest)
  public Vector3 sidewalkOffset = new Vector3(-1.7f, 0f, -1.7f);

  // Materials
  public Material streetMaterial;
  public Material intersectionMaterial;
  public Material sidewalkMaterial;

  // Taxis
  public GameObject taxiPrefab;
  [Range(0f, 1f)] public float taxiSpawnProbability = 0.25f;
  public int maxTaxis = 30;
  public float taxiLightIntensity = 2f;
  public float taxiLightRange = 3f;
  public float taxiLightAngule = 90f;
  public bool generateOnStart = true;
  [Header("Fire / damage effects")]
  public ParticleSystem firePrefab; // Fire particle prefab (drag into inspector)
  [Range(0f, 1f)] public float fireSpawnProbability = 0.2f; // Probability per building (e.g., 0.2 = 20%)
  public float fireYOffset = 0.1f; // Vertical offset above the rooftop to prevent z-fighting
  public bool attachFireToRoofChild = true; // If true, searches for child "Roof" and uses it as an anchor
  public int maxTotalFires = 100; // Global limit to prevent excessive particles

  // Internal list to track created objects
  readonly List<GameObject> createdBuildings = new List<GameObject>();
  void Start() {
    if (generateOnStart) {
      GenerateCity();
    }
  }
  void OnValidate() {
    blocksX = Mathf.Max(1, blocksX);
    blocksY = Mathf.Max(1, blocksY);
    buildingSize.x = Mathf.Max(0.01f, buildingSize.x);
    buildingSize.y = Mathf.Max(0.01f, buildingSize.y);
    gapBetweenBuildings = Mathf.Max(0f, gapBetweenBuildings);
    streetWidth = Mathf.Max(0f, streetWidth);
    buildingHeightMin = Mathf.Max(0.01f, buildingHeightMin);
    if (buildingHeightMax < buildingHeightMin) buildingHeightMax = buildingHeightMin;
  }
  public void GenerateCity() {
    if (buildingPrefab == null) {
      Debug.LogWarning("CityGenerator: buildingPrefab not assigned.");
      return;
    }

    ClearCity(); // Clear before generating
    Random.InitState(randomSeed);

    // ---------- Preparation: select random indices of buildings that will have fire ----------
    int totalBuildings = blocksX * blocksY * 9; // 3x3 buildings per block
    int firesToSpawn = Mathf.Clamp(
      Mathf.RoundToInt(totalBuildings * fireSpawnProbability),
      0,
      Mathf.Min(totalBuildings, maxTotalFires)
    );

    var fireIndices = new HashSet<int>();
    if (firesToSpawn > 0) {
      // Create list of indices [0..totalBuildings-1]
      var indices = new List<int>(totalBuildings);
      for (int i = 0; i < totalBuildings; i++) indices.Add(i);

      // Fisher-Yates shuffle using Random (already initialized with Random.InitState(randomSeed))
      for (int i = indices.Count - 1; i > 0; i--) {
        int j = Random.Range(0, i + 1);
        int tmp = indices[i];
        indices[i] = indices[j];
        indices[j] = tmp;
      }

      // Take the first firesToSpawn shuffled indices
      for (int k = 0; k < firesToSpawn; k++) fireIndices.Add(indices[k]);

      Debug.Log($"CityGenerator: selected {fireIndices.Count} buildings for fire (out of {totalBuildings} possible).");
    }
    // Variable to track the global building index during creation (must be initialized before creating buildings)
    int currentBuildingGlobalIndex = 0;
    // ---------- end preparation ----------

    // Crear o usar parentHolder
    Transform parentTransform = parentHolder;
    if (parentTransform == null) {
      GameObject holder = GameObject.Find(defaultHolderName);
      if (holder == null) {
        holder = new GameObject(defaultHolderName);
        #if UNITY_EDITOR
        if (!Application.isPlaying) {
          EditorUtility.SetDirty(holder);
        }
        #endif
      }
      parentTransform = holder.transform;
    }

    // Useful sizes
    float cellSizeX = buildingSize.x + gapBetweenBuildings; // Space per "cell" in X between centers
    float cellSizeZ = buildingSize.y + gapBetweenBuildings; // Space per "cell" in Z between centers

    // Total size of a block (3 cells). Adjust to make the sum of gaps correct
    float blockSizeX = 3f * cellSizeX - gapBetweenBuildings; // Subtract 1 extra gap to center correctly
    float blockSizeZ = 3f * cellSizeZ - gapBetweenBuildings;

    // Initial offset to center each block around the block origin
    float halfBlockSizeX = blockSizeX * 0.5f;
    float halfBlockSizeZ = blockSizeZ * 0.5f;

    // Generate auxiliary layers first (streets, intersections, sidewalks, taxis)
    GenerateStreets(parentTransform);
    GenerateIntersections(parentTransform);
    GenerateSidewalks(parentTransform);
    SpawnTaxis(parentTransform);

    // Now generate buildings, resting on the sidewalk height
    float baseY = sidewalkHeightOffset; // Now the base is no longer 0, buildings rest on the sidewalk

    for (int bx = 0; bx < blocksX; bx++) {
      for (int bz = 0; bz < blocksY; bz++) {
        // Base position for the block (top-left origin shifted by blocks and streets)
        float blockOriginX = bx * (blockSizeX + streetWidth);
        float blockOriginZ = bz * (blockSizeZ + streetWidth);

        // For each block, create a 3x3 matrix of buildings
        for (int cellX = 0; cellX < 3; cellX++) {
          for (int cellZ = 0; cellZ < 3; cellZ++) {
            // local offset inside the block:
            // - start at -halfBlockSize + half building footprint, then step by cellSize
            float localOffsetX = -halfBlockSizeX + cellX * cellSizeX + buildingSize.x * 0.5f;
            float localOffsetZ = -halfBlockSizeZ + cellZ * cellSizeZ + buildingSize.y * 0.5f;

            float posX = blockOriginX + localOffsetX;
            float posZ = blockOriginZ + localOffsetZ;

            // Random height
            float height = Random.Range(buildingHeightMin, buildingHeightMax);

            // Instantiate
            GameObject go = Instantiate(buildingPrefab, parentTransform);
            go.name = $"B_{bx}_{bz}_c{cellX}_{cellZ}";

            // Set scale based on footprint and height
            Vector3 newScale = new Vector3(buildingSize.x, height, buildingSize.y);
            go.transform.localScale = newScale;

            // Adjust position so the base is at sidewalkHeightOffset
            Vector3 position = new Vector3(posX, baseY + height * 0.5f, posZ);
            go.transform.position = position;

            createdBuildings.Add(go);

            // --- Deterministic fire spawn based on prior selection ---
            if (firePrefab != null && fireIndices != null) {
              // If the current global index is in the set, instantiate fire
              if (fireIndices.Contains(currentBuildingGlobalIndex)) {
                Transform anchor = null;

                // If child "Roof" exists and we want to attach there, use it
                if (attachFireToRoofChild) {
                  anchor = go.transform.Find("Roof");
                }

                // If there is no anchor, use the building's own transform
                Transform fireParent = anchor != null
                        ? anchor
                        : go.transform;

                // Calculate local position for the fire instance:
                Vector3 localPos;
                if (anchor != null) {
                  localPos = Vector3.up * fireYOffset; // Small adjustment above the Roof
                }
                else {
                  float halfHeight = newScale.y * 0.5f;
                  localPos = new Vector3(0f, halfHeight + fireYOffset, 0f);
                }

                // Instantiate and parent as a child so it moves with the building
                ParticleSystem fireInstance = Instantiate(firePrefab, fireParent);
                fireInstance.transform.localPosition = localPos;
                fireInstance.transform.localRotation = Quaternion.identity;

                // Ensure the particle system uses LOCAL space (to move with the object)
                ParticleSystem.MainModule main = fireInstance.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                fireInstance.transform.localScale = Vector3.one;
                fireInstance.Play();
              }
            }
            // Increment the global index after processing this building
            currentBuildingGlobalIndex++;
          }
        }
      }
    }

    #if UNITY_EDITOR
    if (!Application.isPlaying) {
      EditorUtility.SetDirty(parentTransform.gameObject);
    }
    #endif

    Debug.Log($"CityGenerator: Generated {createdBuildings.Count} buildings ({blocksX}x{blocksY} blocks).");
  }
  public void ClearCity() {
    // Destroy the entire root holder if it exists
    GameObject root = GameObject.Find(defaultHolderName);
    if (root != null) {
      #if UNITY_EDITOR
      if (!Application.isPlaying) DestroyImmediate(root);
      else
              #endif
        Destroy(root);
    }

    // Clear internal list
    if (createdBuildings != null) createdBuildings.Clear();

    Debug.Log("CityGenerator: City cleared.");
  }
  Transform CreateOrGetHolder(string name, Transform parent = null) {
    string rootName = defaultHolderName;
    GameObject root = GameObject.Find(rootName);
    if (root == null) {
      root = new GameObject(rootName);
      #if UNITY_EDITOR
      if (!Application.isPlaying) EditorUtility.SetDirty(root);
      #endif
    }

    Transform rootT = root.transform;
    Transform child = rootT.Find(name);
    if (child != null) return child;

    GameObject go = new GameObject(name);
    if (parent != null) go.transform.parent = parent;
    go.transform.parent = rootT;

    #if UNITY_EDITOR
    if (!Application.isPlaying) EditorUtility.SetDirty(go);
    #endif

    return go.transform;
  }
  void GenerateStreets(Transform parentTransform) {
    Transform streetsHolder = CreateOrGetHolder("Streets", parentTransform);

    float cellSizeX = buildingSize.x + gapBetweenBuildings;
    float cellSizeZ = buildingSize.y + gapBetweenBuildings;
    float blockSizeX = 3f * cellSizeX - gapBetweenBuildings;
    float blockSizeZ = 3f * cellSizeZ - gapBetweenBuildings;
    float halfBlockSizeX = blockSizeX * 0.5f;
    float halfBlockSizeZ = blockSizeZ * 0.5f;

    float totalSizeX = blocksX * (blockSizeX + streetWidth) - streetWidth;
    float totalSizeZ = blocksY * (blockSizeZ + streetWidth) - streetWidth;

    // Vertical streets (columns between blocks)
    for (int bx = 0; bx < blocksX - 1; bx++) {
      float centerX = bx * (blockSizeX + streetWidth) + halfBlockSizeX + streetWidth * 0.5f;
      GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
      strip.name = $"Street_V_{bx}";
      strip.transform.parent = streetsHolder;
      strip.transform.localScale = new Vector3(streetWidth, 0.02f, totalSizeZ);
      strip.transform.position = new Vector3(
        centerX,
        0f,
        totalSizeZ * 0.5f - totalSizeZ * 0.5f + totalSizeZ * 0.5f
      ); // Centered in Z
      // Assign material if it exists
      if (streetMaterial != null) {
        Renderer rend = strip.GetComponent<Renderer>();
        rend.sharedMaterial = streetMaterial;
      }
      // Remove collider if not needed
      DestroyImmediateIfEditor(strip.GetComponent<Collider>());
    }

    // Horizontal streets (rows between blocks)
    for (int bz = 0; bz < blocksY - 1; bz++) {
      float centerZ = bz * (blockSizeZ + streetWidth) + halfBlockSizeZ + streetWidth * 0.5f;
      GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
      strip.name = $"Street_H_{bz}";
      strip.transform.parent = streetsHolder;
      strip.transform.localScale = new Vector3(totalSizeX, 0.02f, streetWidth);
      strip.transform.position = new Vector3(totalSizeX * 0.5f, 0f, centerZ);
      if (streetMaterial != null) {
        Renderer rend = strip.GetComponent<Renderer>();
        rend.sharedMaterial = streetMaterial;
      }
      DestroyImmediateIfEditor(strip.GetComponent<Collider>());
    }
  }
  void GenerateIntersections(Transform parentTransform) {
    Transform interHolder = CreateOrGetHolder("Intersections", parentTransform);

    float cellSizeX = buildingSize.x + gapBetweenBuildings;
    float cellSizeZ = buildingSize.y + gapBetweenBuildings;
    float blockSizeX = 3f * cellSizeX - gapBetweenBuildings;
    float blockSizeZ = 3f * cellSizeZ - gapBetweenBuildings;
    float halfBlockSizeX = blockSizeX * 0.5f;
    float halfBlockSizeZ = blockSizeZ * 0.5f;

    // Intersection should reach the outer corner of sidewalk rings:
    float intersectionSizeX = streetWidth + sidewalkWidth;
    float intersectionSizeZ = streetWidth + sidewalkWidth;
    float yOffset = 0.01f; // Slight vertical separation to prevent z-fighting
    float thickness = 0.02f; // Height of the intersection "plane"

    // Iterate through crossings between blocks (there are blocksX-1 crossings in X and blocksY-1 in Z)
    for (int bx = 0; bx < blocksX - 1; bx++) {
      // World center X of the vertical street between block bx and bx+1
      float centerX = bx * (blockSizeX + streetWidth) + halfBlockSizeX + streetWidth * 0.5f;

      for (int bz = 0; bz < blocksY - 1; bz++) {
        // World center Z of the horizontal street between block bz and bz+1
        float centerZ = bz * (blockSizeZ + streetWidth) + halfBlockSizeZ + streetWidth * 0.5f;

        // Create primitive and parent it without preserving world position (we work in local space)
        GameObject inter = GameObject.CreatePrimitive(PrimitiveType.Cube);
        inter.name = $"Intersection_{bx}_{bz}";
        inter.transform.SetParent(interHolder, false);

        // Scale: X and Z according to intersectionSize, Y = thickness
        inter.transform.localScale = new Vector3(intersectionSizeX, thickness, intersectionSizeZ);

        // Calculate center in local space relative to parentTransform (root)
        Vector3 centerLocal = new Vector3(
          centerX - parentTransform.position.x,
          0f,
          centerZ - parentTransform.position.z
        );

        // Assign localPosition (add yOffset in Y)
        inter.transform.localPosition = centerLocal + new Vector3(0f, yOffset + thickness * 0.5f, 0f);

        // Material and collider cleanup
        if (intersectionMaterial != null) {
          Renderer rend = inter.GetComponent<Renderer>();
          rend.sharedMaterial = intersectionMaterial;
        }
        DestroyImmediateIfEditor(inter.GetComponent<Collider>());
      }
    }
  }
  void GenerateSidewalks(Transform parentTransform) {
    GameObject root = new GameObject("CityRoot");

    Transform sidewalkHolder = CreateOrGetHolder("Sidewalks", parentTransform);

    float cellSizeX = buildingSize.x + gapBetweenBuildings;
    float cellSizeZ = buildingSize.y + gapBetweenBuildings;
    float blockSizeX = 3f * cellSizeX - gapBetweenBuildings;
    float blockSizeZ = 3f * cellSizeZ - gapBetweenBuildings;
    float halfBlockSizeX = blockSizeX * 0.5f;
    float halfBlockSizeZ = blockSizeZ * 0.5f;

    // For each block, create 4 pieces that form a ring around the block (N, S, E, W).
    // Additionally, add small pieces at the 4 corners so that the sidewalks look continuous.
    for (int bx = 0; bx < blocksX; bx++) {
      for (int bz = 0; bz < blocksY; bz++) {
        // Block origin (same formula as in GenerateCity)
        float blockOriginX = bx * (blockSizeX + streetWidth);
        float blockOriginZ = bz * (blockSizeZ + streetWidth);

        // Block center
        float centerX = blockOriginX + halfBlockSizeX;
        float centerZ = blockOriginZ + halfBlockSizeZ;

        Debug.Log($"Block {bx},{bz} center(world)=({centerX:F3},{centerZ:F3})");
        Debug.Log($"North expected pos(world)=({centerX:F3},{centerZ + halfBlockSizeZ + sidewalkWidth * 0.5f:F3})");

        // NORTH (top part of the block)
        {
          GameObject north = GameObject.CreatePrimitive(PrimitiveType.Cube);
          north.name = $"Sidewalk_Block_{bx}_{bz}_N";
          north.transform.parent = sidewalkHolder;
          // Width in X = blockSizeX, thickness in Z = sidewalkWidth
          north.transform.localScale = new Vector3(blockSizeX, sidewalkHeightOffset, sidewalkWidth);
          north.transform.position = new Vector3(
            centerX,
            sidewalkHeightOffset * 0.5f,
            centerZ + halfBlockSizeZ + sidewalkWidth * 0.5f
          );
          if (sidewalkMaterial != null) north.GetComponent<Renderer>().sharedMaterial = sidewalkMaterial;
          DestroyImmediateIfEditor(north.GetComponent<Collider>());
          Debug.Log($"North actual pos(world) = {north.transform.position}");
        }

        // SOUTH (bottom part of the block)
        {
          GameObject south = GameObject.CreatePrimitive(PrimitiveType.Cube);
          south.name = $"Sidewalk_Block_{bx}_{bz}_S";
          south.transform.parent = sidewalkHolder;
          south.transform.localScale = new Vector3(blockSizeX, sidewalkHeightOffset, sidewalkWidth);
          south.transform.position = new Vector3(
            centerX,
            sidewalkHeightOffset * 0.5f,
            centerZ - halfBlockSizeZ - sidewalkWidth * 0.5f
          );
          if (sidewalkMaterial != null) south.GetComponent<Renderer>().sharedMaterial = sidewalkMaterial;
          DestroyImmediateIfEditor(south.GetComponent<Collider>());
        }

        // EAST (right side of the block)
        {
          GameObject east = GameObject.CreatePrimitive(PrimitiveType.Cube);
          east.name = $"Sidewalk_Block_{bx}_{bz}_E";
          east.transform.parent = sidewalkHolder;
          // Thickness in X = sidewalkWidth, length in Z = blockSizeZ
          east.transform.localScale = new Vector3(sidewalkWidth, sidewalkHeightOffset, blockSizeZ);
          east.transform.position = new Vector3(
            centerX + halfBlockSizeX + sidewalkWidth * 0.5f,
            sidewalkHeightOffset * 0.5f,
            centerZ
          );
          if (sidewalkMaterial != null) east.GetComponent<Renderer>().sharedMaterial = sidewalkMaterial;
          DestroyImmediateIfEditor(east.GetComponent<Collider>());
        }

        // WEST (left side of the block)
        {
          GameObject west = GameObject.CreatePrimitive(PrimitiveType.Cube);
          west.name = $"Sidewalk_Block_{bx}_{bz}_W";
          west.transform.parent = sidewalkHolder;
          west.transform.localScale = new Vector3(sidewalkWidth, sidewalkHeightOffset, blockSizeZ);
          west.transform.position = new Vector3(
            centerX - halfBlockSizeX - sidewalkWidth * 0.5f,
            sidewalkHeightOffset * 0.5f,
            centerZ
          );
          if (sidewalkMaterial != null) west.GetComponent<Renderer>().sharedMaterial = sidewalkMaterial;
          DestroyImmediateIfEditor(west.GetComponent<Collider>());
        }

        // --- Corners: small pieces at the 4 corners of the block ---
        // Square size: sidewalkWidth x sidewalkHeightOffset x sidewalkWidth
        {
          // NE
          GameObject cornerNE = GameObject.CreatePrimitive(PrimitiveType.Cube);
          cornerNE.name = $"Sidewalk_Block_{bx}_{bz}_Corner_NE";
          cornerNE.transform.parent = sidewalkHolder;
          cornerNE.transform.localScale = new Vector3(sidewalkWidth, sidewalkHeightOffset, sidewalkWidth);
          cornerNE.transform.position = new Vector3(
            centerX + halfBlockSizeX + sidewalkWidth * 0.5f,
            sidewalkHeightOffset * 0.5f,
            centerZ + halfBlockSizeZ + sidewalkWidth * 0.5f
          );
          if (sidewalkMaterial != null) cornerNE.GetComponent<Renderer>().sharedMaterial = sidewalkMaterial;
          DestroyImmediateIfEditor(cornerNE.GetComponent<Collider>());

          // NW
          GameObject cornerNW = GameObject.CreatePrimitive(PrimitiveType.Cube);
          cornerNW.name = $"Sidewalk_Block_{bx}_{bz}_Corner_NW";
          cornerNW.transform.parent = sidewalkHolder;
          cornerNW.transform.localScale = new Vector3(sidewalkWidth, sidewalkHeightOffset, sidewalkWidth);
          cornerNW.transform.position = new Vector3(
            centerX - halfBlockSizeX - sidewalkWidth * 0.5f,
            sidewalkHeightOffset * 0.5f,
            centerZ + halfBlockSizeZ + sidewalkWidth * 0.5f
          );
          if (sidewalkMaterial != null) cornerNW.GetComponent<Renderer>().sharedMaterial = sidewalkMaterial;
          DestroyImmediateIfEditor(cornerNW.GetComponent<Collider>());

          // SE
          GameObject cornerSE = GameObject.CreatePrimitive(PrimitiveType.Cube);
          cornerSE.name = $"Sidewalk_Block_{bx}_{bz}_Corner_SE";
          cornerSE.transform.parent = sidewalkHolder;
          cornerSE.transform.localScale = new Vector3(sidewalkWidth, sidewalkHeightOffset, sidewalkWidth);
          cornerSE.transform.position = new Vector3(
            centerX + halfBlockSizeX + sidewalkWidth * 0.5f,
            sidewalkHeightOffset * 0.5f,
            centerZ - halfBlockSizeZ - sidewalkWidth * 0.5f
          );
          if (sidewalkMaterial != null) cornerSE.GetComponent<Renderer>().sharedMaterial = sidewalkMaterial;
          DestroyImmediateIfEditor(cornerSE.GetComponent<Collider>());

          // SW
          GameObject cornerSW = GameObject.CreatePrimitive(PrimitiveType.Cube);
          cornerSW.name = $"Sidewalk_Block_{bx}_{bz}_Corner_SW";
          cornerSW.transform.parent = sidewalkHolder;
          cornerSW.transform.localScale = new Vector3(sidewalkWidth, sidewalkHeightOffset, sidewalkWidth);
          cornerSW.transform.position = new Vector3(
            centerX - halfBlockSizeX - sidewalkWidth * 0.5f,
            sidewalkHeightOffset * 0.5f,
            centerZ - halfBlockSizeZ - sidewalkWidth * 0.5f
          );
          if (sidewalkMaterial != null) cornerSW.GetComponent<Renderer>().sharedMaterial = sidewalkMaterial;
          DestroyImmediateIfEditor(cornerSW.GetComponent<Collider>());
        }

        // Note: This creates one corner piece per block. If you prefer to avoid
        // overlapping geometry between adjacent blocks, we can change the
        // strategy to only create the NE corners per block (or only on
        // edge blocks), let me know and I'll adapt it.
      }
    }
    sidewalkHolder.position = sidewalkOffset;
  }
  void SpawnTaxis(Transform parentTransform) {
    if (taxiPrefab == null || taxiSpawnProbability <= 0f || maxTaxis <= 0) return;

    Transform vehiclesHolder = CreateOrGetHolder("Vehicles", parentTransform);

    float cellSizeX = buildingSize.x + gapBetweenBuildings;
    float cellSizeZ = buildingSize.y + gapBetweenBuildings;
    float blockSizeX = 3f * cellSizeX - gapBetweenBuildings;
    float blockSizeZ = 3f * cellSizeZ - gapBetweenBuildings;
    float halfBlockSizeX = blockSizeX * 0.5f;
    float halfBlockSizeZ = blockSizeZ * 0.5f;

    Random.InitState(randomSeed);

    int spawned = 0;
    for (int bx = 0; bx < blocksX - 1; bx++) {
      if (spawned >= maxTaxis) break;
      float centerX = bx * (blockSizeX + streetWidth) + halfBlockSizeX + streetWidth * 0.5f;
      for (int bz = 0; bz < blocksY - 1; bz++) {
        if (spawned >= maxTaxis) break;
        float centerZ = bz * (blockSizeZ + streetWidth) + halfBlockSizeZ + streetWidth * 0.5f;
        if (Random.value <= taxiSpawnProbability) {
          Vector3 pos = new Vector3(centerX, sidewalkHeightOffset + 0.02f, centerZ);

          Quaternion rot;
          if (Random.value < 0.5f) {
            rot = Quaternion.Euler(0f, 0f, 0f); // No rotation
          }
          else {
            rot = Quaternion.Euler(0f, 90f, 0f); // Rotated 90° on Y
          }

          GameObject t = Instantiate(taxiPrefab, pos, Quaternion.identity, vehiclesHolder);
          t.name = $"Taxi_{bx}_{bz}";

          // Check if there are already lights and if the intensity is enough to create them
          if (t.GetComponentInChildren<Light>() == null && taxiLightIntensity > 0f) {
            // ----------------------------------------------------
            // 1. SPOT LIGHT CREATION (Roof Light)
            // ----------------------------------------------------
            GameObject spotLightGO = new GameObject("TaxiSpotLight");
            spotLightGO.transform.parent = t.transform;
            // Centered and elevated position (on the roof)
            spotLightGO.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            // We rotate the object so the spot looks down (-90 in X) or up (90 in X)
            // Since the original code had -90 in X, I will keep it so it points UP
            spotLightGO.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            Light spotLt = spotLightGO.AddComponent<Light>();
            spotLt.type = LightType.Spot;
            spotLt.intensity = taxiLightIntensity;
            spotLt.range = taxiLightRange;
            spotLt.spotAngle = taxiLightAngule;
            spotLt.shadows = LightShadows.None;

            // ----------------------------------------------------
            // 2. POINT LIGHT CREATION (Ambient Light)
            // ----------------------------------------------------
            GameObject pointLightGO = new GameObject("TaxiPointLight");
            pointLightGO.transform.parent = t.transform;
            // Position similar to the Spot Light to illuminate from the center of the taxi
            pointLightGO.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            // The Point Light does not need rotation

            Light pointLt = pointLightGO.AddComponent<Light>();
            pointLt.type = LightType.Point;
            pointLt.intensity = taxiLightIntensity * 0.5f; // Use a lower intensity for ambient light
            pointLt.range = taxiLightRange * 0.5f; // Use a smaller range for ambient light
            pointLt.shadows = LightShadows.None;
          }
          spawned++;
        }
      }
    }
  }

  // Helper to destroy colliders in editor without errors
  void DestroyImmediateIfEditor(Component comp) {
    if (comp == null) return;
    #if UNITY_EDITOR
    if (!Application.isPlaying) DestroyImmediate(comp);
    else Destroy(comp);
    #else
        Destroy(comp);
    #endif
  }
}
