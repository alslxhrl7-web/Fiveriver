#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// M1 다부동 그레이박스를 사실적인 외형으로 다듬은 씬(M01_Dabudong)을 만든다.
/// 메뉴: Five Rivers > M1 사실적으로 다듬기
/// 그레이박스 씬을 복사해서 시작하므로 좌표, 엄폐물 콜라이더, GreyboxMarker ID는 그대로 유지된다.
/// 외부 에셋 없이 텍스처, 메시, 식생을 모두 코드로 생성한다.
/// </summary>
public static class M01RealisticBuilder
{
    const string GreyScene = "Assets/_Project/Scenes/Missions/M01_Dabudong_Greybox.unity";
    const string OutScene = "Assets/_Project/Scenes/Missions/M01_Dabudong.unity";
    const string Dir = "Assets/_Project/Art/Realistic";

    // 지형 범위(그레이박스 경계와 동일): x -60..75, z -60..120
    const float X0 = -60f, Z0 = -60f, W = 135f, L = 180f, H = 30f;
    const int HRes = 513, ARes = 512;
    const float TerrainY = -0.03f;

    static Terrain terrain;
    static Material skyMat;

    // =====================================================================
    //  진입점
    // =====================================================================
    [MenuItem("Five Rivers/M1 사실적으로 다듬기")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!File.Exists(FullPath(GreyScene))) M01GreyboxBuilder.Build();

        foreach (var d in new[] { "", "/Textures", "/Materials", "/Meshes", "/Prefabs", "/Terrain" }) EnsureFolder(Dir + d);

        var scene = EditorSceneManager.OpenScene(GreyScene, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(scene, OutScene);

        var rootGo = GameObject.Find("M01_Greybox");
        var cover = rootGo.transform.Find("Cover");
        var zones = rootGo.transform.Find("Zones");
        var markers = rootGo.transform.Find("Markers");
        var paths = rootGo.transform.Find("Paths");
        var oldTerrain = rootGo.transform.Find("Terrain");
        for (int i = oldTerrain.childCount - 1; i >= 0; i--) Object.DestroyImmediate(oldTerrain.GetChild(i).gameObject);
        Object.DestroyImmediate(oldTerrain.gameObject);

        var env = new GameObject("M01_Environment").transform;

        var tex = MakeTextures();
        var mats = MakeMaterials(tex);

        BuildTerrain(env, tex, out var td);
        BuildApron(env, mats, tex);

        var keepOut = new List<Vector3>();
        foreach (var m in markers.GetComponentsInChildren<GreyboxMarker>()) keepOut.Add(m.transform.position);
        foreach (var m in cover.GetComponentsInChildren<GreyboxMarker>()) keepOut.Add(m.transform.position);
        PlaceVegetation(td, mats, keepOut);

        DressTrench(env, zones, mats);
        DressCover(env, cover, mats);
        HideGreyboxVisuals(markers, paths);
        SetupLighting();
        SetupCamera();

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), OutScene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Five Rivers] M1 사실적 씬을 만들었습니다: " + OutScene);
    }

    // =====================================================================
    //  배치 모드 스크린샷 (검증용): -executeMethod M01RealisticBuilder.Capture -shotDir <폴더>
    // =====================================================================
    public static void Capture()
    {
        string outDir = ArgValue("-shotDir") ?? "Shots";
        Directory.CreateDirectory(outDir);
        EditorSceneManager.OpenScene(OutScene, OpenSceneMode.Single);
        terrain = Object.FindAnyObjectByType<Terrain>();
        var views = new (string name, Vector3 pos, Vector3 look)[]
        {
            ("1_player_north", new Vector3(0f, 1.6f, 0f), new Vector3(0f, 6f, 80f)),
            ("2_trench_east", new Vector3(-12f, 1.7f, 0f), new Vector3(30f, 1.0f, -6f)),
            ("3_aerial", new Vector3(-55f, 38f, -40f), new Vector3(5f, 4f, 60f)),
            ("4_valley", new Vector3(47f, 2.5f, -25f), new Vector3(15f, 8f, 70f)),
            ("5_cover", new Vector3(-6f, 2.2f, 24f), new Vector3(-4f, 4f, 60f)),
            ("6_sandbags", new Vector3(2f, 1.5f, 1.5f), new Vector3(10f, 0.8f, 4.2f)),
        };
        foreach (var v in views)
        {
            var go = new GameObject("cap");
            var cam = go.AddComponent<Camera>();
            var ad = go.AddComponent<UniversalAdditionalCameraData>();
            ad.renderPostProcessing = true;
            ad.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 62f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 1000f;
            cam.transform.position = v.pos;
            cam.transform.LookAt(v.look);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            for (int i = 0; i < 2; i++) cam.Render();
            RenderTexture.active = rt;
            var t2 = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            t2.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); t2.Apply();
            File.WriteAllBytes(Path.Combine(outDir, v.name + ".png"), t2.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(t2); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        }
        Debug.Log("[Five Rivers] 스크린샷 저장: " + outDir);
    }

    static string ArgValue(string key)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
        return null;
    }

    // =====================================================================
    //  노이즈 / 수학
    // =====================================================================
    static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    static float VNoise(float x, float y, int pw, int ph, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        int x0 = Mod(xi, pw), x1 = Mod(xi + 1, pw), y0 = Mod(yi, ph), y1 = Mod(yi + 1, ph);
        float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // 타일링되는 fBm (u,v는 0..1, fx/fy는 첫 옥타브의 가로/세로 주파수 정수)
    static float TN(float u, float v, int fx, int fy, int oct, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < oct; o++)
        {
            int ox = fx << o, oy = fy << o;
            sum += amp * VNoise(u * ox, v * oy, ox, oy, seed + o * 17);
            norm += amp; amp *= 0.5f;
        }
        return sum / norm;
    }

    // 월드 좌표용(타일링 없음) fBm
    static float GN(float x, float y, int oct, int seed)
    {
        const int P = 1 << 22;
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < oct; o++)
        {
            sum += amp * VNoise(x, y, P, P, seed + o * 17);
            norm += amp; x *= 2f; y *= 2f; amp *= 0.5f;
        }
        return sum / norm;
    }

    static float S(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    static float SgnPow(float v, float e) => Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), e);
    static Color C(float r, float g, float b) => new Color(r, g, b, 1f);
    static Color Mul(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, 1f);
    static float Rf(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    // =====================================================================
    //  지형 높이
    // =====================================================================
    static float Profile(float z)
    {
        if (z <= 15f) return 0f;
        if (z <= 80f) { float t = (z - 15f) / 65f; return 12f * (0.45f * t + 0.55f * t * t * (3f - 2f * t)); }
        if (z <= 85f) return Mathf.Lerp(12f, 14f, (z - 80f) / 5f);
        if (z <= 105f) return Mathf.Lerp(14f, 6f, S(85f, 105f, z));
        return 6f;
    }

    static float RoadX(float z) => 38f + 7f * (z + 40f) / 150f;
    static float RoadDist(float x, float z) => Mathf.Abs(x - RoadX(z));

    static float TrenchDist(float x, float z)
    {
        float dx = Mathf.Max(0f, Mathf.Abs(x) - 25f), dz = Mathf.Max(0f, Mathf.Abs(z) - 8f);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static float BaseHeight(float x, float z)
    {
        float mx = S(-50f, -30f, x) * (1f - S(26f, 36f, x));
        float edge = S(0f, 10f, Mathf.Min(Mathf.Min(x - X0, X0 + W - x), Mathf.Min(z - Z0, Z0 + L - z)));
        float h = Profile(z) * mx;
        float bw = 10f * S(-44f, -56f, x);
        float be = 10f * S(55f, 67f, x);
        float bs = 6f * S(-45f, -57f, z);
        float bn = 8f * S(110f, 119f, z);
        float bound = Mathf.Max(Mathf.Max(bw, be), Mathf.Max(bs, bn));
        h = Mathf.Max(h, bound) * edge;

        // D 측면 둔덕 (28, -6), 8m x 6m, 높이 1.5m
        float d = Mathf.Sqrt(Mathf.Pow((x - 28f) / 5.2f, 2f) + Mathf.Pow((z + 6f) / 4.2f, 2f));
        h += 1.55f * (1f - S(0.55f, 1f, d));

        float clear = S(0f, 6f, TrenchDist(x, z)) * S(3.5f, 9f, RoadDist(x, z));
        clear = Mathf.Max(clear, 0f);
        float amp = Mathf.Lerp(0.2f, 1.0f, S(8f, 45f, z)) + 2.2f * (bound / 10f);
        float n = (GN(x * 0.045f + 100f, z * 0.045f + 100f, 4, 7) - 0.5f) * 2f;
        float n2 = (GN(x * 0.22f + 40f, z * 0.22f + 40f, 3, 9) - 0.5f) * 0.35f;
        return h + (n * amp + n2) * clear;
    }

    static float SnapY(float x, float z)
    {
        return TerrainY + terrain.SampleHeight(new Vector3(x, 0f, z));
    }

    // =====================================================================
    //  텍스처
    // =====================================================================
    class TexSet { public Texture2D grassA, grassN, dirtA, dirtN, rockA, rockN, roadA, roadN, woodA, woodN, clothA, clothN, barkA, barkN, needleA, needleN, grassBlade; }

    static TexSet MakeTextures()
    {
        var t = new TexSet();
        Gen("grass", 512, (u, v) =>
        {
            float n1 = TN(u, v, 4, 4, 4, 1), n2 = TN(u, v, 16, 16, 3, 2), f = TN(u, v, 96, 96, 2, 9);
            Color c = Color.Lerp(C(.22f, .31f, .10f), C(.43f, .40f, .20f), S(.45f, .72f, n1));
            c = Color.Lerp(c, C(.13f, .22f, .07f), n2 * .6f);
            return Mul(c, 1.15f + .35f * f);
        }, (u, v) => TN(u, v, 48, 48, 3, 5), .55f, out t.grassA, out t.grassN);

        Gen("dirt", 512, (u, v) =>
        {
            float n = TN(u, v, 5, 5, 5, 11), p = TN(u, v, 40, 40, 2, 4), f = TN(u, v, 80, 80, 2, 3);
            Color c = Color.Lerp(C(.26f, .22f, .17f), C(.44f, .38f, .30f), n);
            c = Color.Lerp(c, C(.56f, .52f, .46f), S(.70f, .78f, p) * .8f);
            return Mul(c, 1.0f + .25f * f);
        }, (u, v) => TN(u, v, 24, 24, 4, 3) + S(.7f, .8f, TN(u, v, 40, 40, 2, 4)) * .6f, 1.0f, out t.dirtA, out t.dirtN);

        Gen("rock", 512, (u, v) =>
        {
            float r = 1f - Mathf.Abs(2f * TN(u, v, 5, 5, 5, 21) - 1f);
            float strata = TN(u, v, 3, 14, 3, 23);
            Color c = Color.Lerp(C(.20f, .19f, .18f), C(.46f, .43f, .39f), S(.2f, .8f, strata * .6f + r * .4f));
            c = Color.Lerp(c, C(.30f, .31f, .26f), S(.62f, .8f, TN(u, v, 9, 9, 3, 25)) * .35f);
            return Mul(c, .85f + .3f * TN(u, v, 64, 64, 2, 27));
        }, (u, v) => (1f - Mathf.Abs(2f * TN(u, v, 5, 5, 5, 21) - 1f)) * .8f + TN(u, v, 3, 14, 3, 23) * .3f, 3.5f, out t.rockA, out t.rockN);

        Gen("road", 512, (u, v) =>
        {
            float n = TN(u, v, 6, 6, 4, 31), p = TN(u, v, 56, 56, 2, 33);
            Color c = Color.Lerp(C(.47f, .39f, .29f), C(.62f, .54f, .42f), n);
            c = Color.Lerp(c, C(.66f, .63f, .58f), S(.68f, .78f, p) * .7f);
            float rut = Mathf.Exp(-Mathf.Pow((u - .28f) / .06f, 2f)) + Mathf.Exp(-Mathf.Pow((u - .72f) / .06f, 2f));
            return Mul(c, (.88f + .25f * TN(u, v, 90, 90, 2, 35)) * (1f - .28f * rut));
        }, (u, v) => TN(u, v, 30, 30, 3, 37) * .8f - (Mathf.Exp(-Mathf.Pow((u - .28f) / .06f, 2f)) + Mathf.Exp(-Mathf.Pow((u - .72f) / .06f, 2f))) * .5f, 1f, out t.roadA, out t.roadN);

        Gen("wood", 512, (u, v) =>
        {
            float g = TN(u, v, 3, 60, 3, 41), ring = TN(u, v, 2, 14, 3, 43);
            Color c = Color.Lerp(C(.26f, .21f, .15f), C(.46f, .37f, .28f), g * .7f + ring * .3f);
            float seam = Mathf.Exp(-Mathf.Pow(Mathf.Repeat(v * 4f, 1f) / .015f, 2f)) + Mathf.Exp(-Mathf.Pow((1f - Mathf.Repeat(v * 4f, 1f)) / .015f, 2f));
            return Mul(c, 1f - .55f * Mathf.Clamp01(seam));
        }, (u, v) => TN(u, v, 3, 60, 3, 41) * .5f, 1.5f, out t.woodA, out t.woodN);

        Gen("cloth", 512, (u, v) =>
        {
            float wx = .5f + .5f * Mathf.Sin(u * 6.2832f * 64f), wy = .5f + .5f * Mathf.Sin(v * 6.2832f * 64f);
            float weave = wx * wy * .5f + (1f - wx) * (1f - wy) * .5f;
            Color c = Color.Lerp(C(.46f, .43f, .33f), C(.68f, .63f, .49f), TN(u, v, 6, 6, 4, 51));
            return Mul(c, .72f + .35f * weave + .1f * TN(u, v, 48, 48, 2, 53));
        }, (u, v) =>
        {
            float wx = .5f + .5f * Mathf.Sin(u * 6.2832f * 64f), wy = .5f + .5f * Mathf.Sin(v * 6.2832f * 64f);
            return wx * wy * .5f + (1f - wx) * (1f - wy) * .5f + TN(u, v, 8, 8, 3, 55) * .6f;
        }, 1.6f, out t.clothA, out t.clothN);

        Gen("bark", 256, (u, v) =>
        {
            float g = TN(u, v, 24, 3, 4, 61);
            Color c = Color.Lerp(C(.13f, .11f, .09f), C(.31f, .27f, .22f), g);
            return Mul(c, .8f + .3f * TN(u, v, 48, 6, 2, 63));
        }, (u, v) => TN(u, v, 24, 3, 4, 61), 3f, out t.barkA, out t.barkN);

        Gen("needle", 512, (u, v) =>
        {
            float n = TN(u, v, 8, 8, 4, 71), sp = TN(u, v, 80, 80, 2, 73);
            Color c = Color.Lerp(C(.09f, .18f, .07f), C(.24f, .38f, .14f), n);
            return Mul(c, .75f + .6f * sp);
        }, (u, v) => TN(u, v, 64, 64, 3, 73), 1.5f, out t.needleA, out t.needleN);

        t.grassBlade = MakeGrassBlade();
        return t;
    }

    static string FullPath(string assetPath)
    {
        return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
    }

    static Texture2D SavePng(string name, Color32[] px, int size, bool normal, bool alpha)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.SetPixels32(px); t.Apply();
        string path = $"{Dir}/Textures/{name}.png";
        File.WriteAllBytes(FullPath(path), t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.alphaIsTransparency = alpha;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 8;
        ti.wrapMode = alpha ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void Gen(string name, int size, System.Func<float, float, Color> col, System.Func<float, float, float> hgt,
        float strength, out Texture2D albedo, out Texture2D normal)
    {
        var h = new float[size, size];
        var a = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size, v = (y + .5f) / size;
                a[y * size + x] = col(u, v);
                h[x, y] = hgt(u, v);
            }
        var n = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = h[(x + 1) % size, y] - h[(x + size - 1) % size, y];
                float dy = h[x, (y + 1) % size] - h[x, (y + size - 1) % size];
                var nv = new Vector3(-dx * strength * 4f, -dy * strength * 4f, 1f).normalized;
                n[y * size + x] = new Color(nv.x * .5f + .5f, nv.y * .5f + .5f, nv.z * .5f + .5f, 1f);
            }
        albedo = SavePng(name + "_albedo", a, size, false, false);
        normal = SavePng(name + "_normal", n, size, true, false);
    }

    static Texture2D MakeGrassBlade()
    {
        const int size = 128;
        var rng = new System.Random(77);
        var blades = new List<Vector4>(); // 기준 x, 높이, 기울기, 두께
        for (int i = 0; i < 16; i++) blades.Add(new Vector4(Rf(rng, .1f, .9f), Rf(rng, .45f, 1f), Rf(rng, -.18f, .18f), Rf(rng, .018f, .035f)));
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size, v = (y + .5f) / size;
                Color c = new Color(.2f, .3f, .1f, 0f);
                foreach (var b in blades)
                {
                    if (v > b.y) continue;
                    float t = v / b.y;
                    float cx = b.x + b.z * t * t;
                    float w = b.w * (1f - t * .95f);
                    if (Mathf.Abs(u - cx) < w)
                    {
                        c = Color.Lerp(C(.10f, .17f, .05f), C(.50f, .58f, .24f), t);
                        c.a = 1f;
                    }
                }
                px[y * size + x] = c;
            }
        return SavePng("grass_blade", px, size, false, true);
    }

    // =====================================================================
    //  머티리얼
    // =====================================================================
    class MatSet { public Material ground, bark, needle, bush, rock, wood, cloth, dirt, sandbag; }

    static Material MakeMat(string name, Texture2D albedo, Texture2D normal, Vector2 tiling, Color tint, float smooth)
    {
        string path = $"{Dir}/Materials/{name}.mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetTexture("_BaseMap", albedo);
        m.SetTextureScale("_BaseMap", tiling);
        m.SetColor("_BaseColor", tint);
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.SetTextureScale("_BumpMap", tiling);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
        }
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static MatSet MakeMaterials(TexSet t)
    {
        var m = new MatSet();
        m.bark = MakeMat("RL_Bark", t.barkA, t.barkN, new Vector2(1f, 3f), Color.white, .15f);
        m.needle = MakeMat("RL_Needle", t.needleA, t.needleN, new Vector2(2f, 2f), Color.white, .2f);
        m.bush = MakeMat("RL_Bush", t.needleA, t.needleN, new Vector2(2f, 2f), C(1.5f, 1.45f, .9f), .15f);
        m.rock = MakeMat("RL_Rock", t.rockA, t.rockN, new Vector2(1.5f, 1.5f), C(.74f, .76f, .78f), .2f);
        m.wood = MakeMat("RL_Wood", t.woodA, t.woodN, new Vector2(1f, 1f), Color.white, .2f);
        m.cloth = MakeMat("RL_Blanket", t.clothA, t.clothN, new Vector2(1.5f, 1.5f), C(.55f, .6f, .45f), .1f);
        m.sandbag = MakeMat("RL_Sandbag", t.clothA, t.clothN, new Vector2(1f, 1f), C(.92f, .94f, .88f), .05f);
        m.dirt = MakeMat("RL_Earth", t.dirtA, t.dirtN, new Vector2(1f, 1f), Color.white, .08f);
        return m;
    }

    // =====================================================================
    //  메시 빌더
    // =====================================================================
    class MB
    {
        public List<Vector3> v = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>();
        public List<int>[] t;
        public MB(int subs = 1) { t = new List<int>[subs]; for (int i = 0; i < subs; i++) t[i] = new List<int>(); }
        public int Add(Vector3 p, Vector2 u) { v.Add(p); uv.Add(u); return v.Count - 1; }
        public void Tri(int s, int a, int b, int c) { t[s].Add(a); t[s].Add(b); t[s].Add(c); }
        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv);
            m.subMeshCount = t.Length;
            for (int s = 0; s < t.Length; s++) m.SetTriangles(t[s], s);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }
    }

    static Mesh SaveMesh(Mesh m, string name)
    {
        string path = $"{Dir}/Meshes/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // 회전체. y가 증가하는 방향으로 링이 쌓인다. prof(t)=(반지름, 높이)
    static void Revolve(MB mb, int sub, Matrix4x4 xf, int rings, int seg, System.Func<float, Vector2> prof,
        System.Func<float, float, float> rad, float uvU, float uvV, float phiExp = 1f)
    {
        int b0 = mb.v.Count;
        for (int i = 0; i <= rings; i++)
        {
            float t = (float)i / rings;
            Vector2 p = prof(t);
            for (int j = 0; j <= seg; j++)
            {
                float ph = (float)j / seg * Mathf.PI * 2f;
                float r = p.x * rad(t, ph);
                float cx = Mathf.Cos(ph), sz = Mathf.Sin(ph);
                if (phiExp != 1f) { cx = SgnPow(cx, phiExp); sz = SgnPow(sz, phiExp); }
                mb.Add(xf.MultiplyPoint3x4(new Vector3(cx * r, p.y, sz * r)), new Vector2((float)j / seg * uvU, t * uvV));
            }
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < seg; j++)
            {
                int a = b0 + i * (seg + 1) + j, b = a + 1, d = a + (seg + 1), c = d + 1;
                mb.Tri(sub, a, c, b); mb.Tri(sub, a, d, c);
            }
    }

    static void AddBox(MB mb, int sub, Vector3 center, Quaternion rot, Vector3 size, float tile)
    {
        Vector3[] ns = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        Vector3[] us = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.right, Vector3.left };
        Vector3 h = size * .5f;
        for (int f = 0; f < 6; f++)
        {
            Vector3 n = ns[f], u = us[f], v = Vector3.Cross(u, n);
            float nE = Mathf.Abs(n.x) * h.x + Mathf.Abs(n.y) * h.y + Mathf.Abs(n.z) * h.z;
            float uE = Mathf.Abs(u.x) * h.x + Mathf.Abs(u.y) * h.y + Mathf.Abs(u.z) * h.z;
            float vE = Mathf.Abs(v.x) * h.x + Mathf.Abs(v.y) * h.y + Mathf.Abs(v.z) * h.z;
            Vector3 c0 = n * nE;
            float U = 2f * uE / tile, V = 2f * vE / tile;
            int a = mb.Add(center + rot * (c0 - u * uE - v * vE), new Vector2(0, 0));
            int b = mb.Add(center + rot * (c0 - u * uE + v * vE), new Vector2(0, V));
            int c = mb.Add(center + rot * (c0 + u * uE + v * vE), new Vector2(U, V));
            int d = mb.Add(center + rot * (c0 + u * uE - v * vE), new Vector2(U, 0));
            mb.Tri(sub, a, b, c); mb.Tri(sub, a, c, d);
        }
    }

    // 모래주머니 한 개 (초타원체 + 약간의 변형)
    static void AddBag(MB mb, Vector3 center, Quaternion rot, Vector3 size, int seed)
    {
        var xf = Matrix4x4.TRS(center, rot, new Vector3(size.x * .5f, size.y * .5f, size.z * .5f));
        Revolve(mb, 0, xf, 8, 12,
            t => { float a = Mathf.PI * t; return new Vector2(SgnPow(Mathf.Sin(a), .4f), -SgnPow(Mathf.Cos(a), .4f)); },
            (t, ph) => .93f + .1f * Hash((int)(t * 8f), (int)(ph * 3f), seed), 1f, 1f, .45f);
    }

    static void AddSandbagWall(MB mb, float width, int rows, int depth, int seed, float y0 = 0f)
    {
        var rng = new System.Random(seed);
        for (int r = 0; r < rows; r++)
            for (int dz = 0; dz < depth; dz++)
            {
                float shift = ((r + dz) % 2) * .28f;
                float x = -width * .5f + .28f + shift;
                while (x < width * .5f - .2f)
                {
                    var size = new Vector3(Rf(rng, .50f, .58f), Rf(rng, .21f, .25f), Rf(rng, .32f, .38f));
                    var pos = new Vector3(x, y0 + .12f + r * .215f, (dz - (depth - 1) * .5f) * .34f + Rf(rng, -.02f, .02f));
                    var rot = Quaternion.Euler(Rf(rng, -3f, 3f), Rf(rng, -6f, 6f), Rf(rng, -4f, 4f));
                    AddBag(mb, pos, rot, size, rng.Next());
                    x += size.x * .96f;
                }
            }
    }

    // 흙 둔덕(참호 흉벽). x축 방향으로 길고 단면은 사다리꼴.
    static Mesh MakeBerm(string name, float length, float height, float baseW, float topW, int seed)
    {
        var mb = new MB();
        int nx = Mathf.Max(2, Mathf.RoundToInt(length / .6f));
        var prof = new Vector2[]
        {
            new Vector2(-baseW * .5f, 0f), new Vector2(-(baseW + topW) * .25f, height * .5f),
            new Vector2(-topW * .5f, height), new Vector2(0f, height),
            new Vector2(topW * .5f, height), new Vector2((baseW + topW) * .25f, height * .5f), new Vector2(baseW * .5f, 0f),
        };
        int np = prof.Length;
        float acc = 0f;
        var cum = new float[np];
        for (int j = 1; j < np; j++) { acc += (prof[j] - prof[j - 1]).magnitude; cum[j] = acc; }
        for (int i = 0; i <= nx; i++)
        {
            float x = -length * .5f + length * i / nx;
            for (int j = 0; j < np; j++)
            {
                float nz = (GN(x * .8f + seed, j * 3.1f, 3, seed) - .5f);
                float py = prof[j].y, pz = prof[j].x;
                if (j > 0 && j < np - 1) { py *= 1f + .22f * nz; pz += .16f * nz; }
                mb.Add(new Vector3(x, py, pz), new Vector2(x / 2f, cum[j] / 2f));
            }
        }
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < np - 1; j++)
            {
                int a = i * np + j, b = (i + 1) * np + j, c = (i + 1) * np + j + 1, d = i * np + j + 1;
                mb.Tri(0, a, b, c); mb.Tri(0, a, c, d);
            }
        return SaveMesh(mb.ToMesh(name), name);
    }

    static GameObject Visual(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.SetPositionAndRotation(pos, rot);
        g.transform.localScale = scale;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return g;
    }

    static GameObject Visual(Transform parent, string name, Mesh mesh, Material[] mats, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var g = Visual(parent, name, mesh, mats[0], pos, rot, scale);
        g.GetComponent<MeshRenderer>().sharedMaterials = mats;
        return g;
    }

    // =====================================================================
    //  지형
    // =====================================================================
    static void BuildTerrain(Transform env, TexSet tex, out TerrainData td)
    {
        td = new TerrainData { heightmapResolution = HRes, size = new Vector3(W, H, L) };
        var h = new float[HRes, HRes];
        for (int y = 0; y < HRes; y++)
            for (int x = 0; x < HRes; x++)
            {
                float wx = X0 + (float)x / (HRes - 1) * W, wz = Z0 + (float)y / (HRes - 1) * L;
                h[y, x] = Mathf.Clamp01(BaseHeight(wx, wz) / H);
            }
        td.SetHeights(0, 0, h);

        TerrainLayer Layer(string name, Texture2D a, Texture2D n, float tile, float smooth)
        {
            string p = $"{Dir}/Terrain/{name}.terrainlayer";
            AssetDatabase.DeleteAsset(p);
            var l = new TerrainLayer { diffuseTexture = a, normalMapTexture = n, tileSize = new Vector2(tile, tile), smoothness = smooth, normalScale = 1f };
            AssetDatabase.CreateAsset(l, p);
            return l;
        }
        td.terrainLayers = new[]
        {
            Layer("TL_Grass", tex.grassA, tex.grassN, 7f, .05f),
            Layer("TL_Dirt", tex.dirtA, tex.dirtN, 5f, .08f),
            Layer("TL_Rock", tex.rockA, tex.rockN, 6f, .2f),
            Layer("TL_Road", tex.roadA, tex.roadN, 5f, .06f),
        };

        td.alphamapResolution = ARes;
        var am = new float[ARes, ARes, 4];
        const int DRes = 512;
        td.SetDetailResolution(DRes, 16);
        td.detailPrototypes = new[]
        {
            new DetailPrototype
            {
                prototypeTexture = tex.grassBlade, usePrototypeMesh = false, renderMode = DetailRenderMode.GrassBillboard,
                minWidth = .9f, maxWidth = 1.5f, minHeight = .5f, maxHeight = 1.0f, noiseSpread = .35f,
                healthyColor = C(.62f, .70f, .38f), dryColor = C(.78f, .70f, .40f),
            }
        };
        var dens = new int[DRes, DRes];
        for (int y = 0; y < ARes; y++)
            for (int x = 0; x < ARes; x++)
            {
                float nx = (x + .5f) / ARes, nz = (y + .5f) / ARes;
                float wx = X0 + nx * W, wz = Z0 + nz * L;
                float steep = td.GetSteepness(nx, nz);
                float hh = BaseHeight(wx, wz);
                float dirtN = GN(wx * .12f + 50f, wz * .12f + 50f, 4, 81);
                float dTr = TrenchDist(wx, wz);
                float dRoad = RoadDist(wx, wz);
                float inRoadZ = (wz > -46f && wz < 112f) ? 1f : 0f;

                float grass = 1f;
                float dirt = Mathf.Max(S(.52f, .78f, dirtN) * .9f, 1f - S(0f, 5f, dTr));
                dirt = Mathf.Max(dirt, S(8.5f, 13f, hh) * .35f);
                float d2 = Mathf.Sqrt(Mathf.Pow((wx - 28f) / 5.2f, 2f) + Mathf.Pow((wz + 6f) / 4.2f, 2f));
                dirt = Mathf.Max(dirt, 1f - S(.8f, 1.35f, d2));
                float rock = S(27f, 38f, steep) * .95f + S(.78f, .9f, GN(wx * .35f + 5f, wz * .35f + 5f, 3, 83)) * S(12f, 24f, steep) * .5f;
                float road = (1f - S(2.2f, 3.4f, dRoad)) * inRoadZ;

                float wg = grass * (1f - dirt) * (1f - rock);
                float wd = dirt * (1f - rock);
                float wr = rock;
                float wroad = road;
                wg *= 1f - road; wd *= 1f - road; wr *= 1f - road;
                float sum = wg + wd + wr + wroad + 1e-5f;
                am[y, x, 0] = wg / sum; am[y, x, 1] = wd / sum; am[y, x, 2] = wr / sum; am[y, x, 3] = wroad / sum;

                if (steep < 30f && am[y, x, 0] > .45f)
                    dens[y, x] = Mathf.RoundToInt(Mathf.Clamp01(am[y, x, 0] * (.35f + 1.1f * GN(wx * .25f + 9f, wz * .25f + 9f, 2, 85))) * 7f);
            }
        // 스플랫맵과 디테일 레이어는 에셋이 만들어진 뒤에 써야 저장된다
        string tdPath = $"{Dir}/Terrain/M01_TerrainData.asset";
        AssetDatabase.DeleteAsset(tdPath);
        AssetDatabase.CreateAsset(td, tdPath);
        td.SetAlphamaps(0, 0, am);
        td.SetDetailLayer(0, 0, 0, dens);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "Terrain";
        go.transform.SetParent(env, false);
        go.transform.position = new Vector3(X0, TerrainY, Z0);
        terrain = go.GetComponent<Terrain>();
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 4f;
        terrain.basemapDistance = 400f;
        terrain.detailObjectDistance = 90f;
        terrain.detailObjectDensity = 1f;
        terrain.treeDistance = 400f;
        terrain.treeBillboardDistance = 400f;
        terrain.shadowCastingMode = ShadowCastingMode.On;
    }

    // 지형 바깥을 채우는 넓은 원판. 멀리서 낮은 산등성이로 올라가 안개에 묻힌다.
    static void BuildApron(Transform env, MatSet m, TexSet tex)
    {
        var mat = MakeMat("RL_Apron", tex.grassA, tex.grassN, new Vector2(1f, 1f), C(.85f, .85f, .7f), .05f);
        var mb = new MB();
        const int seg = 160, rings = 56;
        float cx = X0 + W * .5f, cz = Z0 + L * .5f;
        for (int i = 0; i <= rings; i++)
        {
            float r = 20f * Mathf.Pow(1500f / 20f, (float)i / rings);
            for (int j = 0; j <= seg; j++)
            {
                float a = (float)j / seg * Mathf.PI * 2f;
                float px = cx + Mathf.Cos(a) * r, pz = cz + Mathf.Sin(a) * r;
                float n = GN(Mathf.Cos(a) * 2.2f + r * .0035f, Mathf.Sin(a) * 2.2f + r * .0035f, 4, 91);
                float hh = (r < 260f ? 0f : 150f * S(260f, 1000f, r) * (.25f + n * 1.1f));
                mb.Add(new Vector3(px, hh, pz), new Vector2(px / 20f, pz / 20f));
            }
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < seg; j++)
            {
                int a = i * (seg + 1) + j, b = (i + 1) * (seg + 1) + j, c = b + 1, d = a + 1;
                mb.Tri(0, a, c, b); mb.Tri(0, a, d, c);
            }
        Visual(env, "Apron", SaveMesh(mb.ToMesh("RL_Apron"), "RL_Apron"), mat, new Vector3(0f, TerrainY - .3f, 0f), Quaternion.identity, Vector3.one);
    }
    // =====================================================================
    //  식생 (소나무, 관목, 바위)
    // =====================================================================
    static GameObject SavePrefab(GameObject go, string name)
    {
        string path = $"{Dir}/Prefabs/{name}.prefab";
        AssetDatabase.DeleteAsset(path);
        var p = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return p;
    }

    static GameObject MakePinePrefab(string name, int seed, MatSet m, float taper)
    {
        var mb = new MB(2);
        // 줄기
        Revolve(mb, 0, Matrix4x4.identity, 6, 8,
            t => new Vector2(Mathf.Lerp(.24f, .06f, t), t * 8.8f),
            (t, ph) => 1f + .08f * Hash((int)(t * 6f), (int)(ph * 2f), seed), 2f, 4f);
        // 가지층
        int tiers = 6;
        for (int k = 0; k < tiers; k++)
        {
            float y0 = 1.7f + k * 1.2f, hh = 2.5f - k * .25f, r0 = (1.9f - k * .28f) * taper;
            int kk = k;
            Revolve(mb, 1, Matrix4x4.identity, 4, 16,
                t => new Vector2(r0 * Mathf.Pow(1f - t, .85f), y0 + hh * t - .55f * Mathf.Sin(t * 3.14f) * .0f),
                (t, ph) => (1f + .2f * Mathf.Sin(ph * 5f + kk * 1.7f) * (1f - t)) * (1f + .1f * Hash(kk, (int)(ph * 7f), seed)) - .12f * t,
                3f, 1.5f);
        }
        var mesh = SaveMesh(mb.ToMesh(name), name);
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.bark, m.needle };
        var cap = go.AddComponent<CapsuleCollider>();
        cap.center = new Vector3(0, 2f, 0); cap.radius = .3f; cap.height = 4f;
        return SavePrefab(go, name);
    }

    static GameObject MakeBlobPrefab(string name, int seed, Material mat, Vector3 radii, float rough, bool flatBottom, bool collider)
    {
        var mb = new MB();
        var xf = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, radii);
        float minY = flatBottom ? -.55f : -1f;
        Revolve(mb, 0, xf, 14, 22,
            t => { float a = Mathf.PI * t; return new Vector2(Mathf.Sin(a), Mathf.Max(minY, -Mathf.Cos(a))); },
            (t, ph) =>
            {
                float n = GN(Mathf.Cos(ph) * 1.7f + t * 2.3f + seed, Mathf.Sin(ph) * 1.7f + t * 2.9f + seed * .5f, 4, seed);
                float ridge = 1f - Mathf.Abs(2f * n - 1f);
                return 1f + rough * (ridge - .5f) * 1.6f;
            }, 3f, 2f);
        var mesh = SaveMesh(mb.ToMesh(name), name);
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider) { var c = go.AddComponent<CapsuleCollider>(); c.radius = radii.x * .6f; c.height = radii.y * 1.2f; c.center = new Vector3(0, radii.y * .3f, 0); }
        return SavePrefab(go, name);
    }

    static void PlaceVegetation(TerrainData td, MatSet m, List<Vector3> keepOut)
    {
        var protos = new[]
        {
            MakePinePrefab("RL_PineA", 3, m, 1f),
            MakePinePrefab("RL_PineB", 8, m, .8f),
            MakeBlobPrefab("RL_Bush", 5, m.bush, new Vector3(.95f, .7f, .95f), .5f, true, false),
            MakeBlobPrefab("RL_RockA", 11, m.rock, new Vector3(1f, .7f, .85f), .9f, true, false),
            MakeBlobPrefab("RL_RockB", 17, m.rock, new Vector3(.9f, .6f, 1.1f), .95f, true, false),
            MakeBlobPrefab("RL_RockC", 23, m.rock, new Vector3(.8f, .9f, .8f), .85f, true, false),
        };
        td.treePrototypes = System.Array.ConvertAll(protos, p => new TreePrototype { prefab = p });

        var rng = new System.Random(1234);
        var list = new List<TreeInstance>();
        int pines = 0, bushes = 0, rocks = 0, tries = 0;
        const int maxPine = 520, maxBush = 420, maxRock = 300;
        while ((pines < maxPine || bushes < maxBush || rocks < maxRock) && tries < 120000)
        {
            tries++;
            float x = X0 + (float)rng.NextDouble() * W, z = Z0 + (float)rng.NextDouble() * L;
            if (TrenchDist(x, z) < 9f || RoadDist(x, z) < 6.5f) continue;
            bool near = false;
            foreach (var k in keepOut)
                if ((k.x - x) * (k.x - x) + (k.z - z) * (k.z - z) < 20f) { near = true; break; }
            if (near) continue;
            float nx = (x - X0) / W, nz = (z - Z0) / L;
            float steep = td.GetSteepness(nx, nz);
            float lane = (x > -32f && x < 30f && z > 18f && z < 90f) ? .12f : 1f;   // 진입로 시야 확보
            float clump = GN(x * .03f + 300f, z * .03f + 300f, 3, 21);
            int kind = rng.Next(100);
            int proto; float ws, hs;
            if (kind < 45 && pines < maxPine && steep < 34f && rng.NextDouble() < lane * S(.35f, .6f, clump) + .02f)
            {
                proto = rng.Next(2); ws = Rf(rng, .8f, 1.4f); hs = ws * Rf(rng, .9f, 1.25f); pines++;
            }
            else if (kind < 75 && bushes < maxBush && steep < 36f && rng.NextDouble() < lane * .5f + .08f)
            {
                proto = 2; ws = Rf(rng, .7f, 1.5f); hs = ws * Rf(rng, .7f, 1.1f); bushes++;
            }
            else if (rocks < maxRock && (steep > 20f || rng.NextDouble() < .2f))
            {
                proto = 3 + rng.Next(3); ws = Rf(rng, .5f, 2.0f) * (steep > 25f ? 1.4f : 1f); hs = ws * Rf(rng, .7f, 1.2f); rocks++;
            }
            else continue;
            list.Add(new TreeInstance
            {
                position = new Vector3(nx, 0f, nz), prototypeIndex = proto, widthScale = ws, heightScale = hs,
                rotation = Rf(rng, 0f, 6.283f), color = Color.white, lightmapColor = Color.white,
            });
        }
        td.SetTreeInstances(list.ToArray(), true);
    }

    // =====================================================================
    //  참호와 소품
    // =====================================================================
    static void DressTrench(Transform env, Transform zones, MatSet m)
    {
        var props = new GameObject("Trench_Visuals").transform;
        props.SetParent(env, false);

        // 그레이박스 메시는 숨기고 충돌체는 그대로 둔다 (D 둔덕은 지형이 대신한다)
        foreach (var r in zones.GetComponentsInChildren<MeshRenderer>()) r.enabled = false;
        var mound = zones.Find("D_Mound");
        if (mound != null) Object.DestroyImmediate(mound.GetComponent<Collider>());

        const float bermH = 1.0f;
        var bermLong = MakeBerm("RL_Berm_Long", 41f, bermH, 1.9f, .75f, 5);
        var bermWest = MakeBerm("RL_Berm_West", 11f, bermH, 1.9f, .75f, 9);
        Visual(props, "Berm_North", bermLong, m.dirt, new Vector3(0f, 0f, 4.5f), Quaternion.identity, Vector3.one);
        Visual(props, "Berm_South", bermLong, m.dirt, new Vector3(0f, 0f, -4.5f), Quaternion.identity, Vector3.one);
        Visual(props, "Berm_West", bermWest, m.dirt, new Vector3(-20.5f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), Vector3.one);

        // 흉벽 위 모래주머니 한 줄 (두 겹으로 폭 확보)
        var bags = new MB();
        float topY = bermH - .06f;
        void BagRow(float zc, float x0, float x1, int seed)
        {
            var rng = new System.Random(seed);
            for (int row = 0; row < 2; row++)
            {
                float zr = zc + (row == 0 ? -.17f : .17f);
                float x = x0 + row * .27f;
                while (x < x1)
                {
                    var size = new Vector3(Rf(rng, .52f, .6f), Rf(rng, .2f, .25f), Rf(rng, .32f, .38f));
                    AddBag(bags, new Vector3(x, topY + size.y * .5f, zr + Rf(rng, -.02f, .02f)),
                        Quaternion.Euler(Rf(rng, -3f, 3f), Rf(rng, -5f, 5f), Rf(rng, -3f, 3f)), size, rng.Next());
                    x += size.x * .95f;
                }
            }
        }
        BagRow(4.5f, -20f, 20.4f, 101);
        BagRow(-4.5f, -20f, 20.4f, 103);
        // 서쪽 벽은 z방향
        {
            var rng = new System.Random(105);
            for (int row = 0; row < 2; row++)
            {
                float xr = -20.5f + (row == 0 ? -.17f : .17f);
                float z = -4.6f + row * .27f;
                while (z < 4.7f)
                {
                    var size = new Vector3(Rf(rng, .32f, .38f), Rf(rng, .2f, .25f), Rf(rng, .52f, .6f));
                    AddBag(bags, new Vector3(xr, topY + size.y * .5f, z), Quaternion.Euler(Rf(rng, -3f, 3f), Rf(rng, -5f, 5f), Rf(rng, -3f, 3f)), size, rng.Next());
                    z += size.z * .95f;
                }
            }
        }
        Visual(props, "Sandbag_Parapets", SaveMesh(bags.ToMesh("RL_Parapet_Sandbags"), "RL_Parapet_Sandbags"), m.sandbag, Vector3.zero, Quaternion.identity, Vector3.one);

        // 참호 안쪽 널판 보강 (기둥 + 가로 널판)
        var wood = new MB();
        var wr = new System.Random(201);
        foreach (float side in new[] { 1f, -1f })
        {
            float zc = side * 3.62f;
            for (float x = -19.2f; x <= 19.4f; x += 2.4f)
                AddBox(wood, 0, new Vector3(x, .5f, zc - side * .06f), Quaternion.Euler(0f, 0f, Rf(wr, -1.5f, 1.5f)), new Vector3(.14f, 1.1f, .14f), 1.2f);
            for (int k = 0; k < 4; k++)
                for (float x = -19.2f; x < 19.4f; x += 2.4f)
                    AddBox(wood, 0, new Vector3(x + 1.2f + Rf(wr, -.03f, .03f), .14f + k * .22f, zc), Quaternion.Euler(0f, 0f, Rf(wr, -1.2f, 1.2f)), new Vector3(2.36f, .2f, .04f), 1.2f);
        }
        Visual(props, "Wood_Revetment", SaveMesh(wood.ToMesh("RL_Revetment"), "RL_Revetment"), m.wood, Vector3.zero, Quaternion.identity, Vector3.one);

        // 탄약 상자(AMMO_01), 담요 덮인 형체(PROP_MANSU)
        var crates = new MB();
        AddBox(crates, 0, new Vector3(0f, .25f, -2f), Quaternion.Euler(0f, 4f, 0f), new Vector3(.8f, .5f, .5f), 1f);
        AddBox(crates, 0, new Vector3(.9f, .18f, -2.1f), Quaternion.Euler(0f, -12f, 0f), new Vector3(.6f, .36f, .4f), 1f);
        AddBox(crates, 0, new Vector3(.1f, .7f, -2.05f), Quaternion.Euler(0f, 18f, 0f), new Vector3(.55f, .3f, .4f), 1f);
        AddBox(crates, 0, new Vector3(-.85f, .15f, -1.9f), Quaternion.Euler(0f, 31f, 0f), new Vector3(.5f, .3f, .35f), 1f);
        Visual(props, "Ammo_Crates", SaveMesh(crates.ToMesh("RL_Crates"), "RL_Crates"), m.wood, Vector3.zero, Quaternion.identity, Vector3.one);

        var body = new MB();
        var bx = Matrix4x4.TRS(new Vector3(-18f, .02f, -1f), Quaternion.Euler(0f, 6f, 0f), new Vector3(.34f, .34f, .95f));
        Revolve(body, 0, bx, 10, 14,
            t => { float a = Mathf.PI * t; return new Vector2(Mathf.Sin(a), Mathf.Max(0f, -Mathf.Cos(a))); },
            (t, ph) => 1f + .12f * Mathf.Sin(ph * 3f + t * 5f), 2f, 2f, .8f);
        Visual(props, "Blanket_Body", SaveMesh(body.ToMesh("RL_Blanket"), "RL_Blanket"), m.cloth, Vector3.zero, Quaternion.identity, Vector3.one);
    }

    static void DressCover(Transform env, Transform cover, MatSet m)
    {
        var props = new GameObject("Cover_Visuals").transform;
        props.SetParent(env, false);

        var walls = new Mesh[3];
        for (int i = 0; i < 3; i++)
        {
            var mb = new MB();
            AddSandbagWall(mb, 1.6f, 5, 2, 300 + i * 7);
            walls[i] = SaveMesh(mb.ToMesh("RL_SandbagWall" + i), "RL_SandbagWall" + i);
        }
        var rocks = new Mesh[3];
        for (int i = 0; i < 3; i++)
            rocks[i] = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/Prefabs/RL_Rock{(char)('A' + i)}.prefab").GetComponent<MeshFilter>().sharedMesh;

        var rng = new System.Random(555);
        int idx = 0;
        foreach (Transform cp in cover)
        {
            float x = cp.position.x, z = cp.position.z, gy = SnapY(x, z);
            cp.position = new Vector3(x, gy + .6f, z);
            cp.GetComponent<MeshRenderer>().enabled = false;
            if (idx % 2 == 0)
            {
                Visual(props, cp.name + "_Rock", rocks[idx % 3], m.rock, new Vector3(x, gy + .45f, z),
                    Quaternion.Euler(0f, Rf(rng, 0f, 360f), 0f), new Vector3(.85f, .85f, .62f));
                for (int k = 0; k < 2; k++)
                    Visual(props, cp.name + "_RockSmall" + k, rocks[(idx + k + 1) % 3], m.rock,
                        new Vector3(x + Rf(rng, -1.1f, 1.1f), SnapY(x, z) + .1f, z + Rf(rng, -.8f, .8f)),
                        Quaternion.Euler(0f, Rf(rng, 0f, 360f), 0f), Vector3.one * Rf(rng, .22f, .4f));
            }
            else
            {
                Visual(props, cp.name + "_Sandbags", walls[idx % 3], m.sandbag, new Vector3(x, gy - .02f, z),
                    Quaternion.Euler(0f, Rf(rng, -8f, 8f), 0f), Vector3.one);
            }
            idx++;
        }
    }

    static void HideGreyboxVisuals(Transform markers, Transform paths)
    {
        foreach (var r in markers.GetComponentsInChildren<MeshRenderer>()) r.enabled = false;
        foreach (var l in paths.GetComponentsInChildren<LineRenderer>()) l.enabled = false;
    }

    // =====================================================================
    //  조명, 하늘, 안개, 후처리, 카메라
    // =====================================================================
    static void SetupLighting()
    {
        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>()) if (l.type == LightType.Directional) sun = l;
        if (sun == null) sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.name = "Sun_Dawn";
        sun.transform.rotation = Quaternion.Euler(13f, 262f, 0f);     // 동쪽 낮은 새벽 해
        sun.color = C(1f, .78f, .6f);
        sun.intensity = 2.7f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = .92f;
        RenderSettings.sun = sun;

        string skyPath = $"{Dir}/Materials/RL_Sky.mat";
        AssetDatabase.DeleteAsset(skyPath);
        skyMat = new Material(Shader.Find("Skybox/Procedural"));
        skyMat.SetFloat("_SunSize", .045f);
        skyMat.SetFloat("_SunSizeConvergence", 5f);
        skyMat.SetFloat("_AtmosphereThickness", 1.25f);
        skyMat.SetColor("_SkyTint", C(.52f, .5f, .58f));
        skyMat.SetColor("_GroundColor", C(.36f, .33f, .3f));
        skyMat.SetFloat("_Exposure", 1.05f);
        AssetDatabase.CreateAsset(skyMat, skyPath);
        RenderSettings.skybox = skyMat;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = C(.46f, .54f, .68f);
        RenderSettings.ambientEquatorColor = C(.40f, .38f, .36f);
        RenderSettings.ambientGroundColor = C(.22f, .19f, .15f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = .0028f;
        RenderSettings.fogColor = C(.74f, .70f, .62f);
        DynamicGI.UpdateEnvironment();

        var urp = (GraphicsSettings.currentRenderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        if (urp != null) { urp.shadowDistance = 150f; EditorUtility.SetDirty(urp); }

        // 후처리 볼륨
        string profPath = $"{Dir}/M01_PostProfile.asset";
        AssetDatabase.DeleteAsset(profPath);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, profPath);
        T Add<T>() where T : VolumeComponent
        {
            var c = prof.Add<T>(true);
            AssetDatabase.AddObjectToAsset(c, prof);
            return c;
        }
        Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
        var bloom = Add<Bloom>(); bloom.threshold.Override(.95f); bloom.intensity.Override(.3f); bloom.scatter.Override(.7f);
        var ca = Add<ColorAdjustments>(); ca.postExposure.Override(.55f); ca.contrast.Override(14f); ca.saturation.Override(-12f);
        var vg = Add<Vignette>(); vg.intensity.Override(.27f); vg.smoothness.Override(.45f);
        EditorUtility.SetDirty(prof);

        var volGo = new GameObject("Post_Volume");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = prof;
    }

    static void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.transform.position = new Vector3(0f, SnapY(0f, 0f) + 1.6f, 0f);
        cam.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        cam.farClipPlane = 1000f;
        var ad = cam.GetComponent<UniversalAdditionalCameraData>();
        if (ad == null) ad = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        ad.renderPostProcessing = true;
        ad.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        ad.antialiasingQuality = AntialiasingQuality.High;
    }

    static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
