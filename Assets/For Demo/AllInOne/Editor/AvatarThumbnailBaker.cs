using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>Renders a portrait of each avatar and writes the avatar catalog. Needs a graphics device.</summary>
    public static class AvatarThumbnailBaker
    {
        private const string AvatarFolder = "Assets/Shared/Avatars/Final/";
        private const string DefinitionFolder = "Assets/Shared/Avatars/Humanoid Avatar Model/";
        // The shape of the pictures on the cards
        private const int Width = 512;
        private const int Height = 324;
        private const float FieldOfView = 15f;

        private static readonly string[] Prefabs =
        {
            "Male_Avatar_1", "Male_Avatar_2", "Male_Avatar_3", "Female_Avatar_4", "Female_Avatar_5", "Female_Avatar_6"
        };

        [MenuItem(AllInOnePaths.Menu + "Bake avatar thumbnails")]
        public static void Bake()
        {
            Directory.CreateDirectory(AllInOnePaths.Thumbnails);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
            AddLight(1.1f, new Vector3(35f, 150f, 0f));
            AddLight(0.45f, new Vector3(20f, 215f, 0f));
            Camera camera = CreateCamera();
            bool wasAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            var catalog = StudyContentImporter.LoadOrCreate<AgentAvatarCatalog>(AllInOnePaths.Avatars);
            catalog.avatars = Prefabs.Select((name, i) => CreateEntry(name, i + 1, camera)).ToArray();
            StudyContentImporter.Save(catalog);
            ShaderUtil.allowAsyncCompilation = wasAsync;
            Debug.Log("[AIO] Avatar thumbnails baked.");
        }

        private static AgentAvatarCatalog.Entry CreateEntry(string prefabName, int number, Camera camera)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarFolder + prefabName + ".prefab");
            string skeleton = prefabName.StartsWith("Female") ? "Female.fbx" : "Male.fbx";
            string path = AllInOnePaths.Thumbnails + "avatar-" + number + ".png";
            Render(prefab, camera, path);
            return new AgentAvatarCatalog.Entry
            {
                id = "avatar-" + number,
                label = "Avatar " + number,
                prefab = prefab,
                definition = AssetDatabase.LoadAllAssetsAtPath(DefinitionFolder + skeleton).OfType<Avatar>().First(),
                thumbnail = Import(path)
            };
        }

        private static void Render(GameObject prefab, Camera camera, string path)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Vector3 head = model.GetComponentsInChildren<Transform>().First(t => t.name == "Head").position;
            camera.transform.position = head + new Vector3(0.05f, 0.11f, 1.45f);
            camera.transform.LookAt(head + new Vector3(0f, 0.065f, 0f));
            // The first picture after a change of scene has no light yet.
            camera.Render();
            camera.Render();

            RenderTexture.active = camera.targetTexture;
            var picture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(path, picture.EncodeToPNG());
            Object.DestroyImmediate(picture);
            Object.DestroyImmediate(model);
        }

        private static Texture2D Import(string path)
        {
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = Width;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = Width, format = TextureImporterFormat.ASTC_6x6
            });
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void AddLight(float intensity, Vector3 angles)
        {
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.transform.eulerAngles = angles;
        }

        private static Camera CreateCamera()
        {
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            ColorUtility.TryParseHtmlString("#F5F1EB", out Color background);
            camera.backgroundColor = background;
            camera.targetTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
            return camera;
        }
    }
}