// Generates Assets/Scenes/TestDistrict.unity from code (called by BuildScript.PreExport when
// Build Settings has no enabled scene). Everything is primitives plus any FBX found under
// Assets/Art/Models/City (Block_*.fbx) and Assets/Art/Models/Vehicles (*.fbx).
// Menu: Gangs of Bharat > Generate Test District (regenerates the scene in the editor).
// UNVERIFIED: written against Unity 6 editor APIs, compiled only against stubs in the sandbox.
using System;
using System.Collections.Generic;
using System.IO;
using GangsOfBharat.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GangsOfBharat.EditorTools
{
    public static class TestSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/TestDistrict.unity";
        private const string MaterialFolder = "Assets/Scenes/TestDistrictMaterials";
        private const string CityModelFolder = "Assets/Art/Models/City";
        private const string VehicleModelFolder = "Assets/Art/Models/Vehicles";

        // The shared Movement code clamps positions to WorldBounds (0..8192 m), so the district
        // is built around a positive origin, not (0, 0, 0).
        private static readonly Vector3 Origin = new Vector3(256f, 0f, 256f);

        private static readonly Color[] FacadeColors =
        {
            new Color(0.96f, 0.60f, 0.20f), // saffron
            new Color(0.25f, 0.70f, 0.68f), // turquoise
            new Color(0.90f, 0.45f, 0.60f), // pink
            new Color(0.85f, 0.70f, 0.35f), // ochre
            new Color(0.93f, 0.88f, 0.76f), // cream
            new Color(0.55f, 0.75f, 0.35f), // lime
            new Color(0.55f, 0.60f, 0.85f), // lavender blue
            new Color(0.80f, 0.35f, 0.25f), // brick red
        };

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static Shader _shader;

        /// <summary>
        /// Returns the TestDistrict scene path, generating and saving the scene if the file does
        /// not exist yet. An existing file is reused as is.
        /// </summary>
        public static string EnsureTestDistrict()
        {
            if (File.Exists(ScenePath))
            {
                Debug.Log("TestSceneBuilder: reusing existing " + ScenePath);
                return ScenePath;
            }
            Generate();
            return ScenePath;
        }

        [MenuItem("Gangs of Bharat/Generate Test District")]
        public static void GenerateFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Generate();
            }
        }

        private static void Generate()
        {
            Materials.Clear();
            _shader = PickShader();
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets/Scenes", "TestDistrictMaterials");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Camera camera = null;
            Light sun = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (camera == null)
                {
                    camera = root.GetComponent<Camera>();
                }
                if (sun == null)
                {
                    sun = root.GetComponent<Light>();
                }
            }

            SetupLighting(sun);
            BuildStreet();
            PlaceCityModels();
            GameObject car = BuildCar("TestCar", Origin + new Vector3(-2.5f, 0f, -4f));
            PlaceVehicleModels();
            BuildPlayer(camera, car);

            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException("Could not save " + ScenePath);
            }
            Debug.Log("TestSceneBuilder: generated " + ScenePath + " with shader "
                + (_shader != null ? _shader.name : "(primitive default)"));
        }

        // ---------------------------------------------------------------- materials

        // Built-in pipeline unless a render pipeline asset is assigned (none is committed now).
        private static Shader PickShader()
        {
            bool srp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null
                || QualitySettings.renderPipeline != null;
            string[] chain = srp
                ? new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard" }
                : new[] { "Standard", "Legacy Shaders/Diffuse", "Universal Render Pipeline/Lit" };
            foreach (string name in chain)
            {
                Shader shader = Shader.Find(name);
                if (shader != null)
                {
                    return shader;
                }
            }
            Debug.LogWarning("TestSceneBuilder: no lit shader found; primitives keep their default material.");
            return null;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        // Materials are saved as assets so the player build includes them and their shader.
        // Returns null (keep the primitive's default material) if no shader was found.
        private static Material Mat(string name, Color color)
        {
            Material mat;
            if (Materials.TryGetValue(name, out mat))
            {
                return mat;
            }
            if (_shader == null)
            {
                return null;
            }
            string path = MaterialFolder + "/" + name + ".mat";
            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(_shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = _shader;
            }
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
            if (mat.HasProperty("_Glossiness"))
            {
                mat.SetFloat("_Glossiness", 0.1f);
            }
            if (mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", 0.1f);
            }
            EditorUtility.SetDirty(mat);
            Materials[name] = mat;
            return mat;
        }

        private static void Paint(GameObject go, Material mat)
        {
            if (mat == null)
            {
                return;
            }
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = mat;
            }
        }

        // ---------------------------------------------------------------- primitives

        private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 position,
            Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            Paint(go, mat);
            return go;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material mat)
        {
            return Prim(PrimitiveType.Cube, name, parent, position, scale, mat);
        }

        private static Transform Group(string name)
        {
            return new GameObject(name).transform;
        }

        // ---------------------------------------------------------------- lighting

        private static void SetupLighting(Light sun)
        {
            if (sun == null)
            {
                sun = new GameObject("Directional Light").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.name = "Sun";
            sun.color = new Color(1f, 0.92f, 0.78f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            // Explicit ambient colours: no lighting bake runs on the build machine, so do not
            // depend on a baked skybox ambient probe. The default skybox stays for the sky.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.80f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.52f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.26f, 0.22f);
        }

        // ---------------------------------------------------------------- street

        private static void BuildStreet()
        {
            Transform street = Group("Street");
            street.position = Origin;

            Prim(PrimitiveType.Plane, "Ground", street, Vector3.zero, new Vector3(40f, 1f, 40f),
                Mat("Ground", new Color(0.56f, 0.50f, 0.40f)));
            Material asphalt = Mat("Asphalt", new Color(0.17f, 0.17f, 0.19f));
            Box("MainRoad", street, new Vector3(0f, 0.02f, 0f), new Vector3(10f, 0.04f, 300f), asphalt);
            Box("CrossRoad", street, new Vector3(0f, 0.025f, 40f), new Vector3(300f, 0.04f, 10f), asphalt);

            Material paint = Mat("LanePaint", new Color(0.95f, 0.95f, 0.90f));
            for (float z = -140f; z <= 140f; z += 8f)
            {
                if (Mathf.Abs(z - 40f) < 7f)
                {
                    continue;
                }
                Box("Lane", street, new Vector3(0f, 0.045f, z), new Vector3(0.2f, 0.01f, 3f), paint);
            }

            Material pavement = Mat("Pavement", new Color(0.68f, 0.66f, 0.62f));
            Box("PavementLeft", street, new Vector3(-6.5f, 0.05f, 0f), new Vector3(3f, 0.1f, 300f), pavement);
            Box("PavementRight", street, new Vector3(6.5f, 0.05f, 0f), new Vector3(3f, 0.1f, 300f), pavement);

            BuildRow(street, -1f, 11);
            BuildRow(street, 1f, 23);
            BuildTrees(street);
        }

        // One side of the main road: shop-house blocks with an awning and some rooftop tanks.
        private static void BuildRow(Transform street, float side, int seed)
        {
            System.Random rng = new System.Random(seed);
            Material tank = Mat("WaterTank", new Color(0.12f, 0.12f, 0.14f));
            float z = -135f;
            int index = 0;
            while (z < 135f)
            {
                float width = 8f + (float)rng.NextDouble() * 6f;
                if (Mathf.Abs(z + width * 0.5f - 40f) < width * 0.5f + 6f)
                {
                    z += 1f; // keep the cross road clear
                    continue;
                }
                float depth = 8f + (float)rng.NextDouble() * 6f;
                float height = 6f + (float)rng.NextDouble() * 22f;
                int colour = rng.Next(FacadeColors.Length);
                float x = side * (8f + depth * 0.5f);
                float centreZ = z + width * 0.5f;

                Transform building = Group("Building_" + (side < 0f ? "L" : "R") + index);
                building.SetParent(street, false);
                building.localPosition = new Vector3(x, 0f, centreZ);
                Box("Body", building, new Vector3(0f, height * 0.5f, 0f), new Vector3(depth, height, width - 1f),
                    Mat("Facade" + colour, FacadeColors[colour]));

                int awning = (colour + 3) % FacadeColors.Length;
                Box("Awning", building, new Vector3(-side * (depth * 0.5f + 0.6f), 3.2f, 0f),
                    new Vector3(1.2f, 0.15f, width - 2f), Mat("Facade" + awning, FacadeColors[awning]));

                if (rng.NextDouble() < 0.5)
                {
                    Prim(PrimitiveType.Cylinder, "WaterTank", building,
                        new Vector3(0f, height + 0.6f, 0f), new Vector3(1.4f, 0.6f, 1.4f), tank);
                }
                z += width + 0.5f + (float)rng.NextDouble() * 1.5f;
                index++;
            }
        }

        private static void BuildTrees(Transform street)
        {
            Material trunk = Mat("Trunk", new Color(0.40f, 0.28f, 0.18f));
            Material leaves = Mat("Leaves", new Color(0.22f, 0.50f, 0.20f));
            for (float z = -120f; z <= 120f; z += 24f)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float treeZ = z + s * 12f;
                    if (Mathf.Abs(treeZ - 40f) < 8f)
                    {
                        continue; // cross road
                    }
                    Transform tree = Group("Tree");
                    tree.SetParent(street, false);
                    tree.localPosition = new Vector3(s * 6.8f, 0f, treeZ);
                    Prim(PrimitiveType.Cylinder, "Trunk", tree, new Vector3(0f, 1.25f, 0f),
                        new Vector3(0.35f, 1.25f, 0.35f), trunk);
                    Prim(PrimitiveType.Sphere, "Canopy", tree, new Vector3(0f, 3.4f, 0f),
                        new Vector3(3f, 2.6f, 3f), leaves);
                }
            }
        }

        // ---------------------------------------------------------------- vehicle

        // Kaali-peeli taxi from boxes and cylinders. Root sits on the ground, faces +Z.
        private static GameObject BuildCar(string name, Vector3 position)
        {
            GameObject car = new GameObject(name);
            car.transform.position = position;
            Material yellow = Mat("TaxiYellow", new Color(0.98f, 0.80f, 0.10f));
            Material black = Mat("TaxiBlack", new Color(0.08f, 0.08f, 0.08f));
            Material glass = Mat("Glass", new Color(0.35f, 0.45f, 0.55f));
            Box("Body", car.transform, new Vector3(0f, 0.75f, 0f), new Vector3(1.8f, 0.7f, 4f), yellow);
            Box("Cabin", car.transform, new Vector3(0f, 1.38f, -0.2f), new Vector3(1.6f, 0.56f, 2.1f), black);
            Box("Windscreen", car.transform, new Vector3(0f, 1.38f, 0.87f), new Vector3(1.5f, 0.48f, 0.05f), glass);
            Box("TaxiSign", car.transform, new Vector3(0f, 1.75f, -0.2f), new Vector3(0.6f, 0.18f, 0.3f), yellow);
            float[] xs = { -0.92f, 0.92f };
            float[] zs = { -1.3f, 1.3f };
            foreach (float x in xs)
            {
                foreach (float z in zs)
                {
                    GameObject wheel = Prim(PrimitiveType.Cylinder, "Wheel", car.transform,
                        new Vector3(x, 0.36f, z), new Vector3(0.72f, 0.12f, 0.72f), black);
                    wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }
            car.AddComponent<OfflineVehicle>();
            return car;
        }

        // ---------------------------------------------------------------- player + camera

        private static void BuildPlayer(Camera camera, GameObject car)
        {
            GameObject player = new GameObject("LocalPlayer");
            player.transform.position = Origin + new Vector3(-5.5f, 0f, -6f);
            Prim(PrimitiveType.Capsule, "Body", player.transform, new Vector3(0f, 1f, 0f), Vector3.one,
                Mat("Kurta", new Color(0.20f, 0.35f, 0.75f)));
            Box("Face", player.transform, new Vector3(0f, 1.55f, 0.4f), new Vector3(0.3f, 0.15f, 0.2f),
                Mat("Skin", new Color(0.72f, 0.52f, 0.38f)));

            PlayerController playerController = player.AddComponent<PlayerController>();
            VehicleController vehicleController = player.AddComponent<VehicleController>();
            ClientPrediction prediction = player.AddComponent<ClientPrediction>();

            if (camera == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                camera = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            camera.farClipPlane = 600f;
            camera.transform.position = player.transform.position + new Vector3(0f, 3f, -5f);
            camera.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
            FollowCamera follow = camera.gameObject.AddComponent<FollowCamera>();

            SetRef(playerController, "cameraTransform", camera.transform);
            SetRef(vehicleController, "prediction", prediction);
            SetRef(vehicleController, "playerController", playerController);
            SetRef(prediction, "playerController", playerController);
            SetRef(prediction, "vehicleController", vehicleController);
            SetRef(prediction, "visual", player.transform);
            SetBool(prediction, "offlineTestMode", true);
            SetRef(follow, "target", player.transform);
            SetRef(follow, "prediction", prediction);

            GameObject overlay = new GameObject("TestBuildOverlay");
            TestBuildOverlay label = overlay.AddComponent<TestBuildOverlay>();
            SetRef(label, "prediction", prediction);
        }

        private static void SetRef(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning("TestSceneBuilder: no field " + field + " on " + target.GetType().Name);
                return;
            }
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(UnityEngine.Object target, string field, bool value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning("TestSceneBuilder: no field " + field + " on " + target.GetType().Name);
                return;
            }
            property.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- optional FBX models

        private static List<GameObject> LoadModels(string folder, string namePrefix)
        {
            List<GameObject> models = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return models;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileName(path);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                    || !file.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model != null)
                {
                    models.Add(model);
                }
            }
            return models;
        }

        // Instantiates a model, scales it so its largest horizontal size is targetSize metres and
        // puts its lowest point on the ground at position.
        private static GameObject PlaceModel(GameObject model, Transform parent, Vector3 position, float yaw,
            float targetSize)
        {
            GameObject go = PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (go == null)
            {
                go = UnityEngine.Object.Instantiate(model);
            }
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Bounds bounds;
            if (TryGetBounds(go, out bounds))
            {
                float size = Mathf.Max(bounds.size.x, bounds.size.z);
                if (size > 0.01f)
                {
                    float scale = Mathf.Clamp(targetSize / size, 0.001f, 1000f);
                    go.transform.localScale = go.transform.localScale * scale;
                }
                if (TryGetBounds(go, out bounds))
                {
                    go.transform.position += new Vector3(0f, position.y - bounds.min.y, 0f);
                }
            }
            return go;
        }

        private static bool TryGetBounds(GameObject go, out Bounds bounds)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            bounds = new Bounds(go.transform.position, Vector3.zero);
            if (renderers.Length == 0)
            {
                return false;
            }
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return true;
        }

        // City blocks (Block_*.fbx) line both sides of the cross road, east of the junction.
        private static void PlaceCityModels()
        {
            List<GameObject> blocks = LoadModels(CityModelFolder, "Block_");
            if (blocks.Count == 0)
            {
                return;
            }
            Transform parent = Group("CityModels");
            int placed = 0;
            for (float x = 40f; x <= 140f && placed < 12; x += 22f)
            {
                for (int s = -1; s <= 1 && placed < 12; s += 2)
                {
                    GameObject model = blocks[placed % blocks.Count];
                    PlaceModel(model, parent, Origin + new Vector3(x, 0f, 40f + s * 16f), s < 0 ? 0f : 180f, 16f);
                    placed++;
                }
            }
            Debug.Log("TestSceneBuilder: placed " + placed + " city models from " + CityModelFolder);
        }

        // Vehicle models are parked as props beside the main road (their forward axis and pivot
        // are unknown, so the drivable car stays the primitive taxi).
        private static void PlaceVehicleModels()
        {
            List<GameObject> vehicles = LoadModels(VehicleModelFolder, "");
            if (vehicles.Count == 0)
            {
                return;
            }
            Transform parent = Group("VehicleModels");
            int count = Mathf.Min(vehicles.Count, 5); // z 6..34, clear of the cross road at 40
            for (int i = 0; i < count; i++)
            {
                PlaceModel(vehicles[i], parent, Origin + new Vector3(3.2f, 0f, 6f + i * 7f), 0f, 4.2f);
            }
            Debug.Log("TestSceneBuilder: parked " + count + " vehicle models from " + VehicleModelFolder);
        }
    }
}
