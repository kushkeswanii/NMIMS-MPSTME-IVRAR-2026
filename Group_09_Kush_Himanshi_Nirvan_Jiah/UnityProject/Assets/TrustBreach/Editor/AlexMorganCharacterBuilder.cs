using System.IO;
using TMPro;
using TrustBreach.Dialogue.NPC;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static TrustBreach.Editor.HumanoidMeshFactory;
using static TrustBreach.Editor.OfficeBuildKit;

namespace TrustBreach.Editor
{
    /// <summary>
    /// Builds "Alex Morgan" as a reusable prefab: a procedural low-poly humanoid (custom lofted meshes on a jointed
    /// skeleton, ~1.78 m, standard adult proportions), business-casual clothing, face, a readable temporary IT Security
    /// badge, a 3D voice AudioSource, subtitles, and the <see cref="NPCPresence"/> / <see cref="NPCVoice"/> controllers.
    /// No licensed full-body humanoid exists in the project (the only candidate, the Voice SDK "Melinda" sample, is a
    /// head-only mesh), so the body is generated.
    /// </summary>
    internal static class AlexMorganCharacterBuilder
    {
        internal const string PrefabPath = "Assets/TrustBreach/Prefabs/NPC_AlexMorgan.prefab";
        private const string MeshFolder = "Assets/TrustBreach/Avatars/AlexMorgan/Meshes";

        private static Material _skin, _hair, _blazer, _shirt, _trousers, _shoes, _belt, _eyeWhite, _pupil, _lips, _brow;

        /// <summary>Creates (or overwrites) the prefab and returns a connected instance in the active scene.</summary>
        internal static GameObject BuildPrefabInstance()
        {
            EnsureFolder(MeshFolder);
            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            CreateMaterials();

            var root = new GameObject("NPC_AlexMorgan");
            Transform hips = Bone("Hips", root.transform, new Vector3(0f, 0.95f, 0f));
            Transform spine = Bone("Spine", hips, new Vector3(0f, 0.07f, 0f));
            Transform neck = Bone("Neck", spine, new Vector3(0f, 0.45f, 0f));
            Transform head = Bone("Head", neck, new Vector3(0f, 0.09f, 0.01f));

            // ---- Torso: blazer, shirt, lapels, buttons, trousers, belt
            Part("Blazer", spine, Loft("Alex_Torso", new[]
            {
                R(-0.12f, 0.17f, 0.115f), R(0f, 0.155f, 0.105f), R(0.12f, 0.165f, 0.11f), R(0.26f, 0.185f, 0.115f),
                R(0.36f, 0.2f, 0.11f), R(0.42f, 0.195f, 0.095f), R(0.45f, 0.14f, 0.08f), R(0.47f, 0.07f, 0.06f)
            }), _blazer);
            Part("ShirtCollar", spine, Loft("Alex_Collar", new[] { R(0.4f, 0.075f, 0.07f), R(0.47f, 0.064f, 0.062f) }), _shirt);
            Box("ShirtFront", spine, new Vector3(0f, 0.33f, 0.112f), new Vector3(0.085f, 0.2f, 0.004f), _shirt).transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject lapel = Box(side < 0 ? "LapelL" : "LapelR", spine, new Vector3(side * 0.052f, 0.33f, 0.116f), new Vector3(0.032f, 0.22f, 0.005f), _belt);
                lapel.transform.localRotation = Quaternion.Euler(-6f, 0f, side * 17f);
            }
            for (int b = 0; b < 2; b++)
            {
                Sphere($"Button{b}", spine, new Vector3(0f, 0.06f + b * 0.09f, 0.108f), Vector3.one * 0.012f, _belt);
            }
            Part("Trousers", hips, Loft("Alex_Pelvis", new[] { R(-0.13f, 0.12f, 0.085f), R(-0.06f, 0.155f, 0.104f), R(0f, 0.165f, 0.11f), R(0.06f, 0.16f, 0.105f) }), _trousers);
            Part("Belt", hips, Loft("Alex_Belt", new[] { R(0.035f, 0.166f, 0.112f), R(0.06f, 0.163f, 0.109f) }), _belt);

            // ---- Neck and head
            Part("NeckSkin", neck, Loft("Alex_Neck", new[] { R(-0.04f, 0.05f, 0.05f), R(0.1f, 0.047f, 0.05f) }), _skin);
            Part("HeadSkin", head, Loft("Alex_Head", new[]
            {
                R(0f, 0.045f, 0.05f, 0.015f), R(0.03f, 0.065f, 0.075f, 0.01f), R(0.07f, 0.075f, 0.09f, 0.005f), R(0.11f, 0.08f, 0.098f),
                R(0.15f, 0.083f, 0.1f, -0.003f), R(0.19f, 0.078f, 0.095f, -0.008f), R(0.22f, 0.06f, 0.075f, -0.012f), R(0.24f, 0.03f, 0.04f, -0.015f)
            }, 24), _skin);
            Part("Hair", head, Loft("Alex_Hair", new[]
            {
                R(0.1f, 0.086f, 0.1f, -0.012f), R(0.16f, 0.089f, 0.104f, -0.012f), R(0.2f, 0.086f, 0.106f, -0.012f),
                R(0.235f, 0.066f, 0.082f, -0.016f), R(0.256f, 0.03f, 0.045f, -0.018f)
            }, 24), _hair);
            Transform eyeL = Eye("EyeL", head, -0.033f, out Transform pupilL, out Transform lidL);
            Transform eyeR = Eye("EyeR", head, 0.033f, out Transform pupilR, out Transform lidR);
            Box("BrowL", head, new Vector3(-0.034f, 0.143f, 0.094f), new Vector3(0.032f, 0.006f, 0.008f), _brow).transform.localRotation = Quaternion.Euler(0f, 0f, 4f);
            Box("BrowR", head, new Vector3(0.034f, 0.143f, 0.094f), new Vector3(0.032f, 0.006f, 0.008f), _brow).transform.localRotation = Quaternion.Euler(0f, 0f, -4f);
            Sphere("Nose", head, new Vector3(0f, 0.09f, 0.098f), new Vector3(0.018f, 0.03f, 0.026f), _skin);
            GameObject mouth = Box("Mouth", head, new Vector3(0f, 0.058f, 0.092f), new Vector3(0.034f, 0.006f, 0.008f), _lips);
            Sphere("EarL", head, new Vector3(-0.082f, 0.105f, 0f), new Vector3(0.015f, 0.04f, 0.025f), _skin);
            Sphere("EarR", head, new Vector3(0.082f, 0.105f, 0f), new Vector3(0.015f, 0.04f, 0.025f), _skin);

            // ---- Arms (hanging along -Y from the shoulder joints)
            Transform upperL = Arm(spine, -1, out Transform foreL);
            Transform upperR = Arm(spine, 1, out Transform foreR);

            // ---- Legs
            for (int side = -1; side <= 1; side += 2)
            {
                string s = side < 0 ? "L" : "R";
                Transform thigh = Bone($"Thigh{s}", hips, new Vector3(side * 0.095f, 0f, 0f));
                Part("Trouser", thigh, Loft("Alex_Thigh", new[] { R(0.02f, 0.085f, 0.085f), R(-0.2f, 0.075f, 0.075f), R(-0.42f, 0.06f, 0.06f), R(-0.47f, 0.052f, 0.052f) }), _trousers);
                Transform shin = Bone($"Shin{s}", thigh, new Vector3(0f, -0.45f, 0f));
                Part("Trouser", shin, Loft("Alex_Shin", new[] { R(0.01f, 0.057f, 0.057f), R(-0.2f, 0.05f, 0.052f), R(-0.4f, 0.042f, 0.044f) }), _trousers);
                Transform foot = Bone($"Foot{s}", shin, new Vector3(0f, -0.44f, 0f));
                Part("Shoe", foot, Loft("Alex_Shoe", new[]
                {
                    R(-0.06f, 0.04f, 0.033f, -0.025f), R(-0.035f, 0.048f, 0.04f, -0.022f), R(0.05f, 0.05f, 0.036f, -0.028f),
                    R(0.13f, 0.044f, 0.026f, -0.034f), R(0.17f, 0.028f, 0.016f, -0.04f)
                }, 18, alongZ: true), _shoes);
            }

            // ---- Badge on a lanyard (presented to the participant with the ShowBadge gesture)
            Transform badge = BuildBadge(spine);
            Transform badgePresent = Bone("BadgePresentPose", spine, new Vector3(-0.07f, 0.24f, 0.34f));
            badgePresent.localRotation = Quaternion.Euler(-12f, 8f, 0f);
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject strap = Box(side < 0 ? "LanyardL" : "LanyardR", spine, new Vector3(side * 0.035f, 0.38f, 0.118f), new Vector3(0.012f, 0.16f, 0.003f),
                    Lit("Alex_Lanyard", Palette.Teal, 0.3f));
                strap.transform.localRotation = Quaternion.Euler(-8f, 0f, side * -12f);
            }

            // ---- Voice (3D) at the mouth, subtitles above the head
            Transform voiceGo = Bone("Voice", head, new Vector3(0f, 0.06f, 0.09f));
            var source = voiceGo.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 1.2f;
            source.maxDistance = 14f;
            source.dopplerLevel = 0f;
            source.spread = 30f;
            source.priority = 64;
            Transform subtitle = Bone("Subtitle", root.transform, new Vector3(0f, 2.1f, 0f));
            Panel("Plate", subtitle, Vector2.zero, new Vector2(1.25f, 0.16f), Unlit("Alex_SubtitlePlate", new Color(0.04f, 0.05f, 0.08f)));
            TextMeshPro subtitleText = Text("Text", subtitle, "...", new Vector3(0f, 0f, -0.003f), 0.24f, Color.white, new Vector2(1.18f, 0.15f));
            subtitleText.textWrappingMode = TextWrappingModes.Normal;

            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.9f, 0f);
            collider.height = 1.8f;
            collider.radius = 0.24f;

            var presence = root.AddComponent<NPCPresence>();
            InjectFields(presence, ("_hips", hips), ("_spine", spine), ("_neck", neck), ("_head", head), ("_upperArmL", upperL), ("_forearmL", foreL),
                ("_upperArmR", upperR), ("_forearmR", foreR), ("_pupilL", pupilL), ("_pupilR", pupilR), ("_lidL", lidL), ("_lidR", lidR),
                ("_mouth", mouth.transform), ("_badge", badge), ("_badgePresentPose", badgePresent));
            var voice = voiceGo.gameObject.AddComponent<NPCVoice>();
            InjectFields(voice, ("_source", source), ("_presence", presence), ("_subtitleRoot", subtitle.gameObject), ("_subtitleText", subtitleText));
            subtitle.gameObject.SetActive(false);

            // Returns the prefab ASSET root; `root` stays in the scene as the connected instance.
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
            return root;
        }

        private static Transform Arm(Transform spine, int side, out Transform forearm)
        {
            string s = side < 0 ? "L" : "R";
            Transform upper = Bone($"UpperArm{s}", spine, new Vector3(side * 0.205f, 0.41f, 0f));
            upper.localRotation = Quaternion.Euler(0f, 0f, side * 5f);
            Part("Sleeve", upper, Loft("Alex_UpperArm", new[] { R(0.01f, 0.03f, 0.034f), R(-0.03f, 0.048f, 0.052f), R(-0.15f, 0.044f, 0.046f), R(-0.31f, 0.038f, 0.041f) }), _blazer);
            Sphere("Deltoid", upper, new Vector3(-side * 0.012f, -0.02f, 0f), new Vector3(0.115f, 0.1f, 0.11f), _blazer);
            forearm = Bone($"Forearm{s}", upper, new Vector3(0f, -0.3f, 0f));
            Part("Sleeve", forearm, Loft("Alex_Forearm", new[] { R(0.025f, 0.039f, 0.042f), R(-0.18f, 0.033f, 0.036f), R(-0.225f, 0.031f, 0.034f) }), _blazer);
            Part("Cuff", forearm, Loft("Alex_Cuff", new[] { R(-0.215f, 0.031f, 0.034f), R(-0.255f, 0.029f, 0.032f) }), _shirt);
            Transform hand = Bone($"Hand{s}", forearm, new Vector3(0f, -0.255f, 0f));
            Part("Skin", hand, Loft("Alex_Hand", new[]
            {
                R(0.005f, 0.018f, 0.026f), R(-0.04f, 0.021f, 0.04f), R(-0.1f, 0.018f, 0.042f), R(-0.15f, 0.014f, 0.033f), R(-0.175f, 0.008f, 0.018f)
            }, 16), _skin);
            return upper;
        }

        private static Transform Eye(string name, Transform head, float x, out Transform pupil, out Transform lid)
        {
            Transform eye = Bone(name, head, new Vector3(x, 0.115f, 0.084f));
            Sphere("White", eye, Vector3.zero, Vector3.one * 0.024f, _eyeWhite);
            GameObject pupilGo = Sphere("Pupil", eye, new Vector3(0f, 0f, 0.0105f), new Vector3(0.012f, 0.012f, 0.006f), _pupil);
            pupil = pupilGo.transform;
            GameObject lidGo = Box("Lid", eye, new Vector3(0f, 0.006f, 0.007f), new Vector3(0.028f, 0.003f, 0.02f), _skin);
            lid = lidGo.transform;
            return eye;
        }

        private static Transform BuildBadge(Transform spine)
        {
            Transform badge = Bone("Badge", spine, new Vector3(0.0f, 0.26f, 0.12f));
            badge.localRotation = Quaternion.Euler(-8f, 0f, 0f);
            const float w = 0.06f, h = 0.09f;
            GameObject card = Box("Card", badge, Vector3.zero, new Vector3(w, h, 0.003f), Lit("Badge_White", new Color(0.97f, 0.97f, 0.96f), 0.4f));
            card.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Panel("Header", badge, Vector2.zero, new Vector2(w, 0.022f), Lit("Badge_Header_17243D", Palette.Navy, 0.4f), 2).transform.localPosition = new Vector3(0f, 0.034f, -0.0016f);
            Text("Company", badge, "APERTURE SYSTEMS", new Vector3(0f, 0.034f, -0.0018f), 0.032f, Color.white, new Vector2(w - 0.004f, 0.012f), style: FontStyles.Bold);
            Text("Name", badge, "ALEX MORGAN", new Vector3(0f, 0.008f, -0.0018f), 0.05f, Palette.ScreenText, new Vector2(w - 0.004f, 0.014f), style: FontStyles.Bold);
            Text("Role", badge, "IT Security", new Vector3(0f, -0.008f, -0.0018f), 0.038f, Palette.MutedText, new Vector2(w - 0.004f, 0.012f));
            Panel("TempStrip", badge, Vector2.zero, new Vector2(w, 0.016f), Lit("Alex_BadgeTemp", new Color(0.85f, 0.2f, 0.15f), 0.4f), 2).transform.localPosition = new Vector3(0f, -0.034f, -0.0016f);
            Text("Temp", badge, "TEMPORARY", new Vector3(0f, -0.034f, -0.0018f), 0.034f, Color.white, new Vector2(w - 0.004f, 0.012f), style: FontStyles.Bold);
            return badge;
        }

        // ------------------------------------------------------------------ helpers
        private static void CreateMaterials()
        {
            _skin = Lit("Alex_Skin", new Color(0.76f, 0.58f, 0.46f), 0.32f);
            _hair = Lit("Alex_Hair", new Color(0.16f, 0.11f, 0.08f), 0.25f);
            _blazer = Lit("Alex_Blazer", new Color(0.13f, 0.18f, 0.29f), 0.18f);
            _shirt = Lit("Alex_Shirt", new Color(0.8f, 0.87f, 0.95f), 0.25f);
            _trousers = Lit("Alex_Trousers", new Color(0.24f, 0.25f, 0.27f), 0.15f);
            _shoes = Lit("Alex_Shoes", new Color(0.27f, 0.17f, 0.1f), 0.55f);
            _belt = Lit("Alex_Dark", new Color(0.08f, 0.09f, 0.12f), 0.35f);
            _eyeWhite = Lit("Alex_EyeWhite", new Color(0.95f, 0.95f, 0.93f), 0.6f);
            _pupil = Lit("Alex_Pupil", new Color(0.12f, 0.08f, 0.05f), 0.8f);
            _lips = Lit("Alex_Lips", new Color(0.55f, 0.32f, 0.29f), 0.4f);
            _brow = Lit("Alex_Brow", new Color(0.14f, 0.1f, 0.07f), 0.2f);
        }

        private static Transform Bone(string name, Transform parent, Vector3 localPosition) => Group(name, parent, localPosition);

        private static GameObject Part(string name, Transform bone, Mesh mesh, Material material)
        {
            Mesh saved = Save(mesh, $"{MeshFolder}/{mesh.name}.asset");
            var go = new GameObject(name);
            go.transform.SetParent(bone, false);
            go.AddComponent<MeshFilter>().sharedMesh = saved;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return go;
        }

        internal static void InjectFields(Object target, params (string field, Object value)[] values) =>
            OfficeInteractionsBuilder.Inject(target, values);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
