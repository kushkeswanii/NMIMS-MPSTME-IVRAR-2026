using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TrustBreach.Editor
{
    /// <summary>
    /// Primitive-based construction kit for the corporate office: palette materials, boxes, text, screens
    /// and furniture compositions. Conventions:
    ///  - Only leaf primitives are scaled; groups always keep unit scale so children/text are never distorted.
    ///  - A group "faces" a direction when its local -Z points at the person using/reading it
    ///    (matching Unity Quads and TextMeshPro, whose visible side faces local -Z).
    /// </summary>
    internal static class OfficeBuildKit
    {
        internal const string MaterialsFolder = "Assets/TrustBreach/Materials/Office";

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        // ------------------------------------------------------------------ palette
        internal static class Palette
        {
            public static readonly Color Navy = new Color(0.09f, 0.14f, 0.24f);
            public static readonly Color Teal = new Color(0.0f, 0.62f, 0.64f);
            public static readonly Color OffWhite = new Color(0.93f, 0.92f, 0.9f);
            public static readonly Color Graphite = new Color(0.16f, 0.17f, 0.19f);
            public static readonly Color Oak = new Color(0.58f, 0.43f, 0.3f);
            public static readonly Color Walnut = new Color(0.33f, 0.22f, 0.15f);
            public static readonly Color AlertRed = new Color(0.85f, 0.12f, 0.12f);
            public static readonly Color WarningAmber = new Color(0.96f, 0.6f, 0.08f);
            public static readonly Color ScreenText = new Color(0.12f, 0.13f, 0.16f);
            public static readonly Color MutedText = new Color(0.42f, 0.44f, 0.48f);
        }

        internal static void ResetCache() => Cache.Clear();

        internal static Material Lit(string name, Color color, float smoothness = 0.35f, float metallic = 0f)
        {
            Material m = GetOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Self-lit surfaces: screens, LED strips, ceiling light panels.</summary>
        internal static Material Unlit(string name, Color color)
        {
            Material m = GetOrCreate(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        internal static Material Transparent(string name, Color color, float smoothness = 0.9f)
        {
            Material m = GetOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material GetOrCreate(string name, string shaderName)
        {
            if (Cache.TryGetValue(name, out Material cached))
            {
                return cached;
            }
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            Cache[name] = material;
            return material;
        }

        // ------------------------------------------------------------------ hierarchy
        internal static Transform Group(string name, Transform parent, Vector3 localPosition, Quaternion? localRotation = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation ?? Quaternion.identity;
            return go.transform;
        }

        /// <summary>Rotation for a group whose user/reader stands in <paramref name="towardUser"/> direction.</summary>
        internal static Quaternion Facing(Vector3 towardUser) => Quaternion.LookRotation(-towardUser.normalized, Vector3.up);

        internal static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material material,
            bool collider = false, bool castShadows = true)
        {
            return Primitive(PrimitiveType.Cube, name, parent, center, size, material, collider, castShadows);
        }

        internal static GameObject Cylinder(string name, Transform parent, Vector3 center, float diameter, float height,
            Material material, bool collider = false)
        {
            // Unity's cylinder is 2 units tall at scale 1.
            return Primitive(PrimitiveType.Cylinder, name, parent, center, new Vector3(diameter, height * 0.5f, diameter),
                material, collider, true);
        }

        internal static GameObject Sphere(string name, Transform parent, Vector3 center, Vector3 size, Material material)
        {
            return Primitive(PrimitiveType.Sphere, name, parent, center, size, material, false, true);
        }

        internal static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 center, Vector3 size,
            Material material, bool collider, bool castShadows)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (!collider)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }
            else if (type != PrimitiveType.Cube && go.TryGetComponent(out Collider existing) && !(existing is BoxCollider))
            {
                // Keep physics simple and cheap on Quest: boxes everywhere.
                Object.DestroyImmediate(existing);
                go.AddComponent<BoxCollider>();
            }
            return go;
        }

        /// <summary>Flat panel (Quad) whose visible side faces the group's local -Z.</summary>
        internal static GameObject Panel(string name, Transform parent, Vector2 center, Vector2 size, Material material, int layer = 0)
        {
            GameObject quad = Primitive(PrimitiveType.Quad, name, parent, new Vector3(center.x, center.y, -0.0012f * layer),
                new Vector3(size.x, size.y, 1f), material, false, false);
            return quad;
        }

        /// <summary>Adds a single BoxCollider to <paramref name="group"/> enclosing all child renderers.</summary>
        internal static BoxCollider FitCollider(Transform group, float padding = 0f)
        {
            var renderers = group.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return null;
            }
            var bounds = new Bounds();
            bool first = true;
            foreach (Renderer r in renderers)
            {
                Bounds b = r.bounds;
                Vector3 min = b.min, max = b.max;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                    Vector3 local = group.InverseTransformPoint(corner);
                    if (first) { bounds = new Bounds(local, Vector3.zero); first = false; }
                    else bounds.Encapsulate(local);
                }
            }
            var box = group.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size + Vector3.one * padding;
            return box;
        }

        internal static void MarkStatic(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<TMP_Text>() != null)
                {
                    continue; // TMP regenerates its mesh at runtime; static batching would break it
                }
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.ReflectionProbeStatic);
            }
        }

        // ------------------------------------------------------------------ text
        internal static TextMeshPro Text(string name, Transform parent, string text, Vector3 localPosition, float fontSize,
            Color color, Vector2 box, TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            FontStyles style = FontStyles.Normal, Quaternion? localRotation = null)
        {
            Transform t = Group(name, parent, localPosition, localRotation);
            var tmp = t.gameObject.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.fontStyle = style;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.rectTransform.sizeDelta = box;
            tmp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return tmp;
        }

        /// <summary>
        /// A screen surface: returns an unscaled group in the screen plane (origin = centre, local -Z toward the viewer)
        /// with a self-lit background, plus the bezel renderer for hover highlighting.
        /// </summary>
        internal static Transform Screen(string name, Transform parent, Vector3 localPosition, Quaternion localRotation,
            Vector2 size, Color background, out Renderer bezel, Material bezelMaterial)
        {
            Transform screen = Group(name, parent, localPosition, localRotation);
            bezel = Box("Bezel", screen, new Vector3(0f, 0f, 0.012f), new Vector3(size.x + 0.024f, size.y + 0.024f, 0.022f),
                bezelMaterial).GetComponent<Renderer>();
            Panel("Display", screen, Vector2.zero, size, Unlit($"Screen_{ColorUtility.ToHtmlStringRGB(background)}", background));
            return screen;
        }

        internal static GameObject ScreenRect(Transform screen, string name, float x, float y, float w, float h, Color color, int layer = 1)
        {
            return Panel(name, screen, new Vector2(x, y), new Vector2(w, h), Unlit($"Screen_{ColorUtility.ToHtmlStringRGB(color)}", color), layer);
        }

        internal static TextMeshPro ScreenText(Transform screen, string name, string text, float x, float y, float w, float h,
            float fontSize, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, FontStyles style = FontStyles.Normal)
        {
            // (x, y) is the rect centre for centred text, otherwise its top-left corner.
            bool centred = alignment == TextAlignmentOptions.Center;
            Vector3 position = centred ? new Vector3(x, y, -0.006f) : new Vector3(x + w * 0.5f, y - h * 0.5f, -0.006f);
            return Text(name, screen, text, position, fontSize, color, new Vector2(w, h), alignment, style);
        }

        // ------------------------------------------------------------------ furniture
        /// <summary>Task chair; local +Z is the direction the sitter faces.</summary>
        internal static Transform TaskChair(string name, Transform parent, Vector3 position, Vector3 sitterFaces, Material fabric)
        {
            Transform chair = Group(name, parent, position, Quaternion.LookRotation(sitterFaces, Vector3.up));
            Material metal = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);
            for (int i = 0; i < 5; i++)
            {
                GameObject leg = Box($"BaseLeg{i}", chair, Vector3.zero, new Vector3(0.05f, 0.03f, 0.32f), metal);
                leg.transform.localRotation = Quaternion.Euler(0f, i * 72f, 0f);
                leg.transform.localPosition = leg.transform.localRotation * new Vector3(0f, 0.06f, 0.16f);
            }
            Cylinder("GasLift", chair, new Vector3(0f, 0.25f, 0f), 0.05f, 0.36f, metal);
            Box("Seat", chair, new Vector3(0f, 0.47f, 0.02f), new Vector3(0.5f, 0.08f, 0.48f), fabric);
            Box("Back", chair, new Vector3(0f, 0.82f, -0.24f), new Vector3(0.46f, 0.56f, 0.06f), fabric);
            Box("ArmL", chair, new Vector3(-0.27f, 0.62f, 0.0f), new Vector3(0.04f, 0.04f, 0.3f), metal);
            Box("ArmR", chair, new Vector3(0.27f, 0.62f, 0.0f), new Vector3(0.04f, 0.04f, 0.3f), metal);
            FitCollider(chair);
            return chair;
        }

        /// <summary>Desk with the user at local -Z. Returns the desk group (top surface at y = 0.74).</summary>
        internal static Transform Desk(string name, Transform parent, Vector3 position, Quaternion rotation, Vector2 size,
            Material top, Material frame)
        {
            Transform desk = Group(name, parent, position, rotation);
            Box("Top", desk, new Vector3(0f, 0.725f, 0f), new Vector3(size.x, 0.03f, size.y), top);
            Box("LegL", desk, new Vector3(-size.x * 0.5f + 0.03f, 0.355f, 0f), new Vector3(0.04f, 0.71f, size.y - 0.08f), frame);
            Box("LegR", desk, new Vector3(size.x * 0.5f - 0.03f, 0.355f, 0f), new Vector3(0.04f, 0.71f, size.y - 0.08f), frame);
            Box("Modesty", desk, new Vector3(0f, 0.5f, size.y * 0.5f - 0.06f), new Vector3(size.x - 0.1f, 0.35f, 0.02f), frame);
            FitCollider(desk);
            return desk;
        }

        /// <summary>Monitor on a stand; returns the screen group (local -Z toward the user).</summary>
        internal static Transform Monitor(string name, Transform parent, Vector3 basePosition, Quaternion rotation, Vector2 screenSize,
            Color background, out Renderer bezel)
        {
            Transform monitor = Group(name, parent, basePosition, rotation);
            Material metal = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);
            Box("StandBase", monitor, new Vector3(0f, 0.008f, 0.04f), new Vector3(0.22f, 0.016f, 0.16f), metal);
            Box("StandNeck", monitor, new Vector3(0f, 0.17f, 0.07f), new Vector3(0.05f, 0.3f, 0.025f), metal);
            float centerY = 0.17f + screenSize.y * 0.5f;
            Transform screen = Screen("Screen", monitor, new Vector3(0f, centerY, 0.04f), Quaternion.Euler(5f, 0f, 0f),
                screenSize, background, out bezel, Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
            return screen;
        }

        internal static Transform KeyboardAndMouse(Transform parent, Vector3 position, Quaternion rotation)
        {
            Transform set = Group("KeyboardAndMouse", parent, position, rotation);
            Material shell = Lit("Peripheral_Dark", new Color(0.12f, 0.12f, 0.14f), 0.4f);
            Material keys = Lit("Peripheral_Keys", new Color(0.24f, 0.25f, 0.28f), 0.2f);
            Box("Keyboard", set, new Vector3(0f, 0.008f, 0f), new Vector3(0.44f, 0.016f, 0.14f), shell);
            for (int row = 0; row < 4; row++)
            {
                Box($"KeyRow{row}", set, new Vector3(0f, 0.018f, -0.045f + row * 0.03f), new Vector3(0.4f, 0.006f, 0.022f), keys);
            }
            Box("Mouse", set, new Vector3(0.3f, 0.015f, 0.01f), new Vector3(0.06f, 0.03f, 0.1f), shell);
            return set;
        }

        internal static Transform Plant(string name, Transform parent, Vector3 position, float scale = 1f)
        {
            Transform plant = Group(name, parent, position);
            Material pot = Lit("Planter_White", new Color(0.88f, 0.87f, 0.85f), 0.5f);
            Material leafDark = Lit("Foliage_Dark", new Color(0.13f, 0.33f, 0.16f), 0.2f);
            Material leafLight = Lit("Foliage_Light", new Color(0.24f, 0.48f, 0.22f), 0.2f);
            Cylinder("Pot", plant, new Vector3(0f, 0.22f * scale, 0f), 0.4f * scale, 0.44f * scale, pot);
            Cylinder("Stem", plant, new Vector3(0f, 0.6f * scale, 0f), 0.04f * scale, 0.5f * scale, Lit("Bark", Palette.Walnut, 0.1f));
            Sphere("FoliageA", plant, new Vector3(0f, 1.0f * scale, 0f), new Vector3(0.6f, 0.55f, 0.6f) * scale, leafDark);
            Sphere("FoliageB", plant, new Vector3(0.12f, 1.25f * scale, 0.05f), new Vector3(0.42f, 0.4f, 0.42f) * scale, leafLight);
            Sphere("FoliageC", plant, new Vector3(-0.1f, 1.18f * scale, -0.1f), new Vector3(0.38f, 0.36f, 0.38f) * scale, leafLight);
            var col = plant.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.6f * scale, 0f);
            col.size = new Vector3(0.45f, 1.2f, 0.45f) * scale;
            return plant;
        }

        /// <summary>Lounge seating; local +Z is the direction the sitter faces. Width along local X.</summary>
        internal static Transform Sofa(string name, Transform parent, Vector3 position, Vector3 sitterFaces, float width, Material fabric)
        {
            Transform sofa = Group(name, parent, position, Quaternion.LookRotation(sitterFaces, Vector3.up));
            Material legs = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);
            Box("Base", sofa, new Vector3(0f, 0.23f, 0f), new Vector3(width, 0.22f, 0.85f), fabric);
            Box("Cushion", sofa, new Vector3(0f, 0.4f, 0.04f), new Vector3(width - 0.24f, 0.12f, 0.72f), fabric);
            Box("Back", sofa, new Vector3(0f, 0.6f, -0.35f), new Vector3(width, 0.52f, 0.16f), fabric);
            Box("ArmL", sofa, new Vector3(-width * 0.5f + 0.08f, 0.45f, 0f), new Vector3(0.16f, 0.3f, 0.85f), fabric);
            Box("ArmR", sofa, new Vector3(width * 0.5f - 0.08f, 0.45f, 0f), new Vector3(0.16f, 0.3f, 0.85f), fabric);
            Box("LegsFront", sofa, new Vector3(0f, 0.06f, 0.36f), new Vector3(width - 0.1f, 0.12f, 0.03f), legs);
            Box("LegsBack", sofa, new Vector3(0f, 0.06f, -0.36f), new Vector3(width - 0.1f, 0.12f, 0.03f), legs);
            FitCollider(sofa);
            return sofa;
        }

        /// <summary>Glass partition between two floor points, with mullions, rails and a frosted privacy band.</summary>
        internal static Transform GlassWall(string name, Transform parent, Vector2 from, Vector2 to, float height,
            bool frostedBand = true, float mullionSpacing = 1.5f)
        {
            Transform wall = Group(name, parent, Vector3.zero);
            Material glass = Transparent("Glass_Clear", new Color(0.78f, 0.88f, 0.94f, 0.16f));
            Material frost = Transparent("Glass_Frosted", new Color(0.95f, 0.96f, 0.97f, 0.55f), 0.4f);
            Material frame = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);

            Vector3 a = new Vector3(from.x, 0f, from.y), b = new Vector3(to.x, 0f, to.y);
            float length = Vector3.Distance(a, b);
            Quaternion rot = Quaternion.LookRotation(Vector3.Cross(b - a, Vector3.up).normalized, Vector3.up);
            Transform span = Group("Span", wall, (a + b) * 0.5f, rot);

            GameObject pane = Box("Glass", span, new Vector3(0f, height * 0.5f, 0f), new Vector3(length, height, 0.02f), glass,
                collider: true, castShadows: false);
            pane.GetComponent<BoxCollider>().size = new Vector3(1f, 1f, 6f); // 12 cm thick physics wall
            Box("RailBottom", span, new Vector3(0f, 0.04f, 0f), new Vector3(length, 0.08f, 0.06f), frame);
            Box("RailTop", span, new Vector3(0f, height - 0.03f, 0f), new Vector3(length, 0.06f, 0.06f), frame);
            if (frostedBand)
            {
                Box("FrostedBand", span, new Vector3(0f, 1.25f, -0.012f), new Vector3(length, 0.3f, 0.002f), frost, castShadows: false);
                Box("FrostedBandBack", span, new Vector3(0f, 1.25f, 0.012f), new Vector3(length, 0.3f, 0.002f), frost, castShadows: false);
            }
            int mullions = Mathf.Max(1, Mathf.RoundToInt(length / mullionSpacing));
            for (int i = 0; i <= mullions; i++)
            {
                float x = -length * 0.5f + length * i / mullions;
                Box($"Mullion{i}", span, new Vector3(x, height * 0.5f, 0f), new Vector3(0.05f, height, 0.07f), frame);
            }
            return wall;
        }

        /// <summary>Solid wall between two floor points.</summary>
        internal static GameObject SolidWall(string name, Transform parent, Vector2 from, Vector2 to, float height, Material material,
            float thickness = 0.15f)
        {
            Vector3 a = new Vector3(from.x, 0f, from.y), b = new Vector3(to.x, 0f, to.y);
            GameObject wall = Box(name, parent, (a + b) * 0.5f + Vector3.up * height * 0.5f,
                new Vector3(Vector3.Distance(a, b), height, thickness), material, collider: true);
            wall.transform.localRotation = Quaternion.LookRotation(Vector3.Cross(b - a, Vector3.up).normalized, Vector3.up);
            return wall;
        }
    }
}
