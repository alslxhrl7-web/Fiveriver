#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 「다섯 개의 강」 M1 다부동 시연 구간 그레이박스 생성기 (시연 빌드 기획서 B안 2장 기준)
/// 메뉴: Five Rivers > M1 그레이박스 만들기 (B안)
/// 좌표: 기준점(0,0,0)은 플레이어 시작 위치. x 동쪽+, z 북쪽+, y 높이. 단위는 미터.
/// 넣는 위치: Assets/_Project/Scripts/Editor/ (Editor 폴더 안이어야 한다)
/// </summary>
public static class M01GreyboxBuilder
{
    const string ScenePath = "Assets/_Project/Scenes/Missions/M01_Dabudong_Greybox.unity";
    const string AssetFolder = "Assets/_Project/Art/WIP/Greybox";

    // B 전방 경사면: z 15 → 80 사이에서 높이 0 → 12m
    static float SlopeHeight(float z)
    {
        if (z <= 15f) return 0f;
        if (z >= 80f) return 12f;
        return 12f * (z - 15f) / 65f;
    }

    // E 능선 뒷면: z 85 → 105 사이에서 높이 14 → 6m (적 스폰이 진지에서 안 보이는 곳)
    static float RidgeBackHeight(float z)
    {
        return Mathf.Lerp(14f, 6f, Mathf.InverseLerp(85f, 105f, z));
    }

    [MenuItem("Five Rivers/M1 그레이박스 만들기 (B안)")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EnsureFolder("Assets/_Project/Scenes/Missions");
        EnsureFolder(AssetFolder);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var root = new GameObject("M01_Greybox");
        var terrain = Child(root, "Terrain");
        var zones = Child(root, "Zones");
        var cover = Child(root, "Cover");
        var markers = Child(root, "Markers");
        var paths = Child(root, "Paths");

        // ---------- 재질 ----------
        var mGround = Mat("GB_Ground", new Color(0.42f, 0.40f, 0.35f));
        var mSlope = Mat("GB_Slope", new Color(0.47f, 0.50f, 0.38f));
        var mRidge = Mat("GB_Ridge", new Color(0.36f, 0.38f, 0.30f));
        var mBoundary = Mat("GB_Boundary", new Color(0.28f, 0.27f, 0.25f));
        var mRoad = Mat("GB_Road", new Color(0.66f, 0.58f, 0.44f));
        var mTrench = Mat("GB_TrenchWall", new Color(0.45f, 0.33f, 0.22f));
        var mTrenchFloor = Mat("GB_TrenchFloor", new Color(0.38f, 0.30f, 0.22f));
        var mPlayerZone = Mat("GB_PlayerZone", new Color(0.30f, 0.48f, 0.75f));
        var mCover = Mat("GB_Cover", new Color(0.30f, 0.30f, 0.32f));
        var mPlayer = Mat("GB_Player", new Color(0.18f, 0.44f, 0.82f));
        var mNpc = Mat("GB_Npc", new Color(0.62f, 0.62f, 0.62f));
        var mEnemy = Mat("GB_EnemySpawn", new Color(0.89f, 0.38f, 0.18f));
        var mTank = Mat("GB_Tank", new Color(0.12f, 0.62f, 0.42f));
        var mFlare = Mat("GB_Flare", new Color(0.95f, 0.75f, 0.25f));
        var mProp = Mat("GB_Prop", new Color(0.55f, 0.45f, 0.30f));
        var lEnemy = LineMat("GB_Line_Enemy", new Color(0.89f, 0.38f, 0.18f));
        var lTank = LineMat("GB_Line_Tank", new Color(0.12f, 0.62f, 0.42f));
        var lPlayer = LineMat("GB_Line_Player", new Color(0.18f, 0.44f, 0.82f));

        // ---------- 지형 ----------
        Box(terrain, "Ground", new Vector3(7.5f, -0.05f, 30f), new Vector3(135f, 0.1f, 190f), mGround);
        Wedge(terrain, "B_Slope", -35f, 35f, 15f, 80f, 0f, 12f, mSlope);
        Wedge(terrain, "E_RidgeFront", -35f, 35f, 80f, 85f, 12f, 14f, mRidge);
        Wedge(terrain, "E_RidgeBack", -35f, 35f, 85f, 105f, 14f, 6f, mRidge);
        Wedge(terrain, "E_Behind", -35f, 35f, 105f, 120f, 6f, 6f, mRidge);
        // 경계: 보이지 않는 벽 대신 가파른 비탈
        Wedge(terrain, "Boundary_West", -60f, -40f, -60f, 120f, 10f, 10f, mBoundary);
        Wedge(terrain, "Boundary_East", 55f, 75f, -60f, 120f, 10f, 10f, mBoundary);
        Wedge(terrain, "Boundary_South", -40f, 55f, -60f, -45f, 6f, 6f, mBoundary);

        // C 계곡 도로: (38, -40) → (45, 110), 폭 5m
        var road = Box(terrain, "C_Road", new Vector3(41.5f, 0.02f, 35f), new Vector3(5f, 0.04f, 150.2f), mRoad);
        road.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(7f, 150f) * Mathf.Rad2Deg, 0f);

        // ---------- A 진지: 동서 40m × 남북 8m, 흙벽 높이 1.2m, 동쪽 끝이 입구 ----------
        Box(zones, "A_Floor", new Vector3(0f, 0.01f, 0f), new Vector3(40f, 0.02f, 8f), mTrenchFloor);
        Box(zones, "A_Parapet_North", new Vector3(0f, 0.6f, 4.5f), new Vector3(41f, 1.2f, 1f), mTrench);
        Box(zones, "A_Wall_South", new Vector3(0f, 0.6f, -4.5f), new Vector3(41f, 1.2f, 1f), mTrench);
        Box(zones, "A_Wall_West", new Vector3(-20.5f, 0.6f, 0f), new Vector3(1f, 1.2f, 10f), mTrench);

        // D 측면 둔덕: 8m × 6m, 높이 1.5m
        Box(zones, "D_Mound", new Vector3(28f, 0.75f, -6f), new Vector3(8f, 1.5f, 6f), mPlayerZone);

        // ---------- 엄폐 지점 12곳 ----------
        var cps = new (string id, float x, float z, string note)[]
        {
            ("CP01", -22f, 62f, "진입로 1"), ("CP02", -18f, 48f, "진입로 1"), ("CP03", -20f, 36f, "진입로 1"),
            ("CP04", -4f, 66f, "진입로 2"), ("CP05", 3f, 52f, "진입로 2"), ("CP06", -2f, 38f, "진입로 2"),
            ("CP07", 24f, 64f, "진입로 3"), ("CP08", 20f, 50f, "진입로 3"), ("CP09", 26f, 38f, "진입로 3"),
            ("CP10", -30f, 44f, "서쪽 측면"), ("CP11", 12f, 58f, "2와 3 사이"), ("CP12", 32f, 46f, "동쪽 측면"),
        };
        foreach (var cp in cps)
        {
            var g = Box(cover, cp.id, new Vector3(cp.x, SlopeHeight(cp.z) + 0.6f, cp.z), new Vector3(1.6f, 1.2f, 1f), mCover);
            Label(g, cp.id, cp.note);
        }

        // ---------- 마커 (충돌 없음) ----------
        Marker(markers, "SPAWN_PLAYER", new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 0.9f, 0.6f), PrimitiveType.Capsule, mPlayer, "북쪽을 바라본다");
        Marker(markers, "NPC_KIM", new Vector3(6f, 0.9f, 0f), new Vector3(0.6f, 0.9f, 0.6f), PrimitiveType.Capsule, mNpc, "김덕만");
        Marker(markers, "NPC_PARK", new Vector3(-15f, 0.9f, 0f), new Vector3(0.6f, 0.9f, 0.6f), PrimitiveType.Capsule, mNpc, "박용팔");
        Marker(markers, "SEAT_TAESEOK", new Vector3(1.5f, 0.3f, -0.5f), new Vector3(0.6f, 0.6f, 0.6f), PrimitiveType.Sphere, mPlayer, "새벽에 태석이 앉는 자리");
        Marker(markers, "A_EAST_ENTRY", new Vector3(20f, 0.3f, -2f), new Vector3(0.6f, 0.6f, 0.6f), PrimitiveType.Sphere, mPlayer, "도입·새벽 경로 입구");
        Marker(markers, "T34_START", new Vector3(46f, 1.35f, 105f), new Vector3(3.4f, 2.7f, 7f), PrimitiveType.Cube, mTank, "T-34 등장 1:45");
        Marker(markers, "T34_STOP", new Vector3(40f, 1.35f, 12f), new Vector3(3.4f, 2.7f, 7f), PrimitiveType.Cube, mTank, "정지 2:10, 진지 중심에서 약 42m");
        Marker(markers, "TRUCK_STOP", new Vector3(38f, 1.25f, -35f), new Vector3(2.4f, 2.5f, 7f), PrimitiveType.Cube, mProp, "트럭 하차 0:00");
        Marker(markers, "REINF_START", new Vector3(38f, 0.3f, -40f), new Vector3(0.6f, 0.6f, 0.6f), PrimitiveType.Sphere, mPlayer, "보충병 등장 2:25");
        Marker(markers, "FLARE_W", new Vector3(-10f, 120f, 60f), new Vector3(2f, 2f, 2f), PrimitiveType.Sphere, mFlare, "조명탄 서쪽 (120m)");
        Marker(markers, "FLARE_E", new Vector3(15f, 120f, 55f), new Vector3(2f, 2f, 2f), PrimitiveType.Sphere, mFlare, "조명탄 동쪽 (120m)");
        Marker(markers, "ENEMY_SPAWN_R1", new Vector3(-20f, RidgeBackHeight(98f) + 0.5f, 98f), Vector3.one, PrimitiveType.Sphere, mEnemy, "진입로 1 스폰");
        Marker(markers, "ENEMY_SPAWN_R2", new Vector3(0f, RidgeBackHeight(98f) + 0.5f, 98f), Vector3.one, PrimitiveType.Sphere, mEnemy, "진입로 2 스폰");
        Marker(markers, "ENEMY_SPAWN_R3", new Vector3(25f, RidgeBackHeight(98f) + 0.5f, 98f), Vector3.one, PrimitiveType.Sphere, mEnemy, "진입로 3 스폰");

        // 소품 자리 (충돌 있음)
        var ammo = Box(zones, "AMMO_01", new Vector3(0f, 0.25f, -2f), new Vector3(0.8f, 0.5f, 0.5f), mProp);
        Label(ammo, "AMMO_01", "탄약 상자");
        var mansu = Box(zones, "PROP_MANSU", new Vector3(-18f, 0.15f, -1f), new Vector3(0.6f, 0.3f, 1.8f), mCover);
        Label(mansu, "PROP_MANSU", "담요를 덮은 형체");

        // ---------- 경로 ----------
        Line(paths, "Route_1", lEnemy, new Vector3(-20f, 9.5f, 98f), new Vector3(-20f, 14.5f, 85f), P(-22f, 62f), P(-18f, 48f), P(-20f, 36f), new Vector3(-20f, 0.5f, 6f));
        Line(paths, "Route_2", lEnemy, new Vector3(0f, 9.5f, 98f), new Vector3(0f, 14.5f, 85f), P(-4f, 66f), P(3f, 52f), P(-2f, 38f), new Vector3(0f, 0.5f, 6f));
        Line(paths, "Route_3", lEnemy, new Vector3(25f, 9.5f, 98f), new Vector3(25f, 14.5f, 85f), P(24f, 64f), P(20f, 50f), P(26f, 38f), new Vector3(22f, 0.5f, 6f));
        Line(paths, "T34_Path", lTank, new Vector3(46f, 0.5f, 105f), new Vector3(44f, 0.5f, 60f), new Vector3(40f, 0.5f, 12f));
        Line(paths, "Intro_Path", lPlayer, new Vector3(38f, 0.3f, -35f), new Vector3(20f, 0.3f, -2f), new Vector3(0f, 0.3f, 0f));
        Line(paths, "Reinforcement_Path", lPlayer, new Vector3(38f, 0.3f, -40f), new Vector3(20f, 0.3f, -2f), new Vector3(1.5f, 0.3f, -0.5f));
        Line(paths, "Run_To_D", lPlayer, new Vector3(0f, 0.3f, 0f), new Vector3(20f, 0.3f, -2f), new Vector3(28f, 1.6f, -6f));

        // ---------- 카메라: 플레이어 눈높이에서 북쪽 ----------
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 1.6f, 0f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            cam.farClipPlane = 1000f;
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        Debug.Log("[Five Rivers] M1 그레이박스(B안)를 만들었습니다: " + ScenePath);
    }

    // 경사면 위의 점
    static Vector3 P(float x, float z) => new Vector3(x, SlopeHeight(z) + 0.5f, z);

    // ---------- 도우미 ----------
    static GameObject Child(GameObject parent, string name)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent.transform, false);
        return g;
    }

    static GameObject Box(GameObject parent, string name, Vector3 center, Vector3 size, Material mat)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent.transform, false);
        g.transform.localPosition = center;
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        return g;
    }

    static void Marker(GameObject parent, string id, Vector3 center, Vector3 scale, PrimitiveType shape, Material mat, string note)
    {
        var g = GameObject.CreatePrimitive(shape);
        g.name = id;
        g.transform.SetParent(parent.transform, false);
        g.transform.localPosition = center;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        Label(g, id, note);
    }

    static void Label(GameObject g, string id, string note)
    {
        var m = g.AddComponent<GreyboxMarker>();
        m.id = id;
        m.note = note;
    }

    static void Line(GameObject parent, string name, Material mat, params Vector3[] points)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent.transform, false);
        var lr = g.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = points.Length;
        lr.SetPositions(points);
        lr.widthMultiplier = 0.35f;
        lr.sharedMaterial = mat;
        lr.startColor = mat.color;
        lr.endColor = mat.color;
    }

    // 바닥 높이 0, 윗면이 z0에서 h0, z1에서 h1인 쐐기 모양 지형
    static void Wedge(GameObject parent, string name, float x0, float x1, float z0, float z1, float h0, float h1, Material mat)
    {
        var b0 = new Vector3(x0, 0f, z0); var b1 = new Vector3(x1, 0f, z0);
        var b2 = new Vector3(x1, 0f, z1); var b3 = new Vector3(x0, 0f, z1);
        var t0 = new Vector3(x0, h0, z0); var t1 = new Vector3(x1, h0, z0);
        var t2 = new Vector3(x1, h1, z1); var t3 = new Vector3(x0, h1, z1);

        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }
        Quad(t3, t2, t1, t0); // 윗면
        Quad(b0, b1, b2, b3); // 아랫면
        Quad(t0, t1, b1, b0); // 남쪽
        Quad(t2, t3, b3, b2); // 북쪽
        Quad(t3, t0, b0, b3); // 서쪽
        Quad(t1, t2, b2, b1); // 동쪽

        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        string path = $"{AssetFolder}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);

        var g = new GameObject(name);
        g.transform.SetParent(parent.transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        g.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    static Material Mat(string name, Color c)
    {
        string path = $"{AssetFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material LineMat(string name, Color c)
    {
        string path = $"{AssetFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        EditorUtility.SetDirty(m);
        return m;
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
