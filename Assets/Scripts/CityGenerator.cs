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

  // Patent - Seed
  public Transform parentHolder;
  public int randomSeed;

  // Sidewalks
  public float sidewalkWidth = 0.6f;
  public float sidewalkHeightOffset = 0.12f; // altura de la acera (la base sobre la que apoyarán edificios)
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

  // Internal list to trank created objects
  readonly List<GameObject> createdBuildings = new List<GameObject>();

  [Header("Fire / damage effects")]
  public ParticleSystem firePrefab;                // prefab de partículas de fuego (arrastrar en inspector)
  [Range(0f, 1f)] public float fireSpawnProbability = 0.2f; // probabilidad por edificio (ej. 0.2 = 20%)
  public float fireYOffset = 0.1f;                 // offset vertical sobre la azotea para evitar z-fighting
  public bool attachFireToRoofChild = true;        // si true, busca child "Roof" y lo usa como ancla
  public int maxTotalFires = 100;                  // límite global para evitar exceso de partículas

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
      Debug.LogWarning("CityGenerator: buildingPrefab no asignado.");
      return;
    }

    ClearCity(); // limpiar antes de generar
    Random.InitState(randomSeed);

    // ---------- Preparación: elegir índices aleatorios de edificios que tendrán fuego ----------
    int totalBuildings = blocksX * blocksY * 9; // 3x3 edificios por bloque
    int firesToSpawn = Mathf.Clamp(Mathf.RoundToInt(totalBuildings * fireSpawnProbability), 0, Mathf.Min(totalBuildings, maxTotalFires));

    HashSet<int> fireIndices = new HashSet<int>();
    if (firesToSpawn > 0) {
      // creamos lista de índices [0..totalBuildings-1]
      List<int> indices = new List<int>(totalBuildings);
      for (int i = 0; i < totalBuildings; i++) indices.Add(i);

      // Fisher-Yates shuffle usando Random (ya inicializado con Random.InitState(randomSeed))
      for (int i = indices.Count - 1; i > 0; i--) {
        int j = Random.Range(0, i + 1);
        int tmp = indices[i]; indices[i] = indices[j]; indices[j] = tmp;
      }

      // tomamos los primeros firesToSpawn índices mezclados
      for (int k = 0; k < firesToSpawn; k++) fireIndices.Add(indices[k]);

      Debug.Log($"CityGenerator: seleccionados {fireIndices.Count} edificios para fuego (de {totalBuildings} posibles).");
    }
    // variable para rastrear índice global del edificio al crear (debe inicializarse antes de crear edificios)
    int currentBuildingGlobalIndex = 0;
    // ---------- fin preparación ----------

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

    // Tamaños útiles
    float cellSizeX = buildingSize.x + gapBetweenBuildings; // espacio por "celda" en X entre centros
    float cellSizeZ = buildingSize.y + gapBetweenBuildings; // espacio por "celda" en Z entre centros

    // Tamaño total de un bloque (3 celdas). Ajustamos para que la suma de gaps sea correcta
    float blockSizeX = 3f * cellSizeX - gapBetweenBuildings; // restamos 1 gap extra para centrar correctamente
    float blockSizeZ = 3f * cellSizeZ - gapBetweenBuildings;

    // Offset inicial para centrar cada bloque alrededor del origen del bloque
    float halfBlockSizeX = blockSizeX * 0.5f;
    float halfBlockSizeZ = blockSizeZ * 0.5f;

    // Generar capas auxiliares primero (calles, intersecciones, aceras, taxis)
    GenerateStreets(parentTransform);
    GenerateIntersections(parentTransform);
    GenerateSidewalks(parentTransform);
    SpawnTaxis(parentTransform);

    // Ahora generar edificios, pero apoyados sobre la altura de la acera
    float baseY = sidewalkHeightOffset; // ahora la base ya no es 0, los edificios descansan sobre la acera

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

            // Altura aleatoria
            float height = Random.Range(buildingHeightMin, buildingHeightMax);

            // Instantiate
            GameObject go = Instantiate(buildingPrefab, parentTransform);
            go.name = $"B_{bx}_{bz}_c{cellX}_{cellZ}";

            // Set scale based on footprint and height
            Vector3 newScale = new Vector3(buildingSize.x, height, buildingSize.y);
            go.transform.localScale = newScale;

            // Ajustar posición para que la base quede en sidewalkHeightOffset
            Vector3 position = new Vector3(posX, baseY + height * 0.5f, posZ);
            go.transform.position = position;

            createdBuildings.Add(go);

            // --- Fire spawn determinista según selección previa ---
            if (firePrefab != null && fireIndices != null) {
              // si el índice global actual está en el conjunto, instanciamos fuego
              if (fireIndices.Contains(currentBuildingGlobalIndex)) {
                Transform anchor = null;

                // si existe child "Roof" y queremos adjuntar ahí, lo usamos
                if (attachFireToRoofChild) {
                  anchor = go.transform.Find("Roof");
                }

                // Si no hay anchor, usar el transform del edificio mismo
                Transform fireParent = anchor != null ? anchor : go.transform;

                // calcular posición local para la instancia de fuego:
                Vector3 localPos;
                if (anchor != null) {
                  localPos = Vector3.up * fireYOffset; // pequeño ajuste sobre el Roof
                } else {
                  float halfHeight = newScale.y * 0.5f;
                  localPos = new Vector3(0f, halfHeight + fireYOffset, 0f);
                }

                // Instanciar y parentar como hijo para que se mueva con el edificio
                ParticleSystem fireInstance = Instantiate(firePrefab, fireParent);
                fireInstance.transform.localPosition = localPos;
                fireInstance.transform.localRotation = Quaternion.identity;

                // Asegurar que el sistema de partículas use espacio LOCAL (para moverse con el objeto)
                var main = fireInstance.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                fireInstance.transform.localScale = Vector3.one;
                fireInstance.Play();
              }
            }
            // Incrementar el índice global después de procesar este edificio
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

    Debug.Log($"CityGenerator: Generados {createdBuildings.Count} edificios ({blocksX}x{blocksY} bloques).");
  }
  public void ClearCity() {

    // Destruir el holder raíz completo si existe
    GameObject root = GameObject.Find(defaultHolderName);
    if (root != null) {
      #if UNITY_EDITOR
      if (!Application.isPlaying) DestroyImmediate(root);
      else
              #endif
        Destroy(root);
    }

    // limpar lista interna
    if (createdBuildings != null) createdBuildings.Clear();

    Debug.Log("CityGenerator: Ciudad limpiada.");
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

    // Vertical streets (columnas entre bloques)
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
      ); // centrado en Z
      // Asignar material si existe
      if (streetMaterial != null) {
        Renderer rend = strip.GetComponent<Renderer>();
        rend.sharedMaterial = streetMaterial;
      }
      // quitar collider si no se necesita
      DestroyImmediateIfEditor(strip.GetComponent<Collider>());
    }

    // Horizontal streets (filas entre bloques)
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
    float intersectionSizeX = streetWidth + sidewalkWidth - 3f * sidewalkWidth;
    float intersectionSizeZ = streetWidth + sidewalkWidth - 3f * sidewalkWidth;
    float yOffset = 0.01f; // ligera separación vertical para evitar z-fighting
    float thickness = 0.02f; // altura del "plano" de la intersección

    // recorremos cruces entre bloques (hay blocksX-1 cruces en X y blocksY-1 en Z)
    for (int bx = 0; bx < blocksX - 1; bx++) {
      // centro world X de la calle vertical entre bloque bx y bx+1
      float centerX = bx * (blockSizeX + streetWidth) + halfBlockSizeX + streetWidth * 0.5f;

      for (int bz = 0; bz < blocksY - 1; bz++) {
        // centro world Z de la calle horizontal entre bloque bz y bz+1
        float centerZ = bz * (blockSizeZ + streetWidth) + halfBlockSizeZ + streetWidth * 0.5f;

        // Crear primitive y parentarlo sin conservar la posición mundial (trabajamos en local)
        GameObject inter = GameObject.CreatePrimitive(PrimitiveType.Cube);
        inter.name = $"Intersection_{bx}_{bz}";
        inter.transform.SetParent(interHolder, false);

        // escala: X y Z según intersectionSize, Y = thickness
        inter.transform.localScale = new Vector3(intersectionSizeX, thickness, intersectionSizeZ);

        // calcular center en local respecto al parentTransform (root)
        Vector3 centerLocal = new Vector3(
          centerX - parentTransform.position.x,
          0f,
          centerZ - parentTransform.position.z
        );

        // asignar localPosition (añadimos yOffset en Y)
        inter.transform.localPosition = centerLocal + new Vector3(0f, yOffset + thickness * 0.5f, 0f);

        // material y limpieza de collider
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

    // Para cada bloque, crear 4 piezas que formen un anillo alrededor del bloque (N, S, E, W).
    // Además añadimos pequeñas piezas en las 4 esquinas para que las aceras se vean continuas.
    for (int bx = 0; bx < blocksX; bx++) {
      for (int bz = 0; bz < blocksY; bz++) {
        // Origen del bloque (misma fórmula que en GenerateCity)
        float blockOriginX = bx * (blockSizeX + streetWidth);
        float blockOriginZ = bz * (blockSizeZ + streetWidth);

        // Centro del bloque
        float centerX = blockOriginX + halfBlockSizeX;
        float centerZ = blockOriginZ + halfBlockSizeZ;

        Debug.Log($"Block {bx},{bz} center(world)=({centerX:F3},{centerZ:F3})");
        Debug.Log($"North expected pos(world)=({centerX:F3},{centerZ + halfBlockSizeZ + sidewalkWidth * 0.5f:F3})");

        // NORTH (parte superior del bloque)
        {
          GameObject north = GameObject.CreatePrimitive(PrimitiveType.Cube);
          north.name = $"Sidewalk_Block_{bx}_{bz}_N";
          north.transform.parent = sidewalkHolder;
          // ancho en X = blockSizeX, grosor en Z = sidewalkWidth
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

        // SOUTH (parte inferior del bloque)
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

        // EAST (lado derecho del bloque)
        {
          GameObject east = GameObject.CreatePrimitive(PrimitiveType.Cube);
          east.name = $"Sidewalk_Block_{bx}_{bz}_E";
          east.transform.parent = sidewalkHolder;
          // grosor en X = sidewalkWidth, largo en Z = blockSizeZ
          east.transform.localScale = new Vector3(sidewalkWidth, sidewalkHeightOffset, blockSizeZ);
          east.transform.position = new Vector3(
            centerX + halfBlockSizeX + sidewalkWidth * 0.5f,
            sidewalkHeightOffset * 0.5f,
            centerZ
          );
          if (sidewalkMaterial != null) east.GetComponent<Renderer>().sharedMaterial = sidewalkMaterial;
          DestroyImmediateIfEditor(east.GetComponent<Collider>());
        }

        // WEST (lado izquierdo del bloque)
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

        // --- Esquinas: piezas pequeñas en las 4 esquinas del bloque ---
        // tamaño cuadrado: sidewalkWidth x sidewalkHeightOffset x sidewalkWidth
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

        // Nota: esto crea una pieza de esquina por bloque. Si prefieres evitar
        // geometría superpuesta entre bloques adyacentes, podemos cambiar la
        // estrategia para crear sólo las esquinas NE por bloque (o solo en
        // bloques en los bordes), avísame y lo adapto.
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
            rot = Quaternion.Euler(0f, 0f, 0f); // sin rotación
          } else {
            rot = Quaternion.Euler(0f, 90f, 0f); // rotado 90° en Y
          }

          GameObject t = Instantiate(taxiPrefab, pos, Quaternion.identity, vehiclesHolder);
          t.name = $"Taxi_{bx}_{bz}";
          // añadir luz si no existe
          if (t.GetComponentInChildren<Light>() == null && taxiLightIntensity > 0f) {
            GameObject lightGO = new GameObject("TaxiLight");
            lightGO.transform.parent = t.transform;
            lightGO.transform.localPosition = new Vector3(0f, 0.5f, 0f);

            // Rotamos el objeto para que el foco mire hacia arriba
            lightGO.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            Light lt = lightGO.AddComponent<Light>();
            lt.type = LightType.Spot;
            lt.intensity = taxiLightIntensity;
            lt.range = taxiLightRange;
            lt.spotAngle = taxiLightAngule;
            lt.shadows = LightShadows.None;
          }
          spawned++;
        }
      }
    }
  }

  // Helper para destruir colliders en editor sin errores
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
