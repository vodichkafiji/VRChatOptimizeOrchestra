using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TurboOptimizer
{
    // Собирает копию аватара тем же конвейером, что и загрузка в VRChat
    // (NDMF, Modular Avatar, AAO, Meshia, VRCFury, d4rk), и считает ранг на результате.
    public static class Verifier
    {
        public const string CloneSuffix = " (TurboOptimizer test)";

        public static PerfReport BuildAndMeasure(GameObject avatar, Rank target, bool keepClone)
        {
            var callbacks = Reflect.FindType("VRC.SDKBase.Editor.BuildPipeline.VRCBuildPipelineCallbacks");
            var preprocess = callbacks?.GetMethod("OnPreprocessAvatar", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
            if (preprocess == null) return new PerfReport { Error = L.T("Не найден VRCBuildPipelineCallbacks в VRChat SDK.", "VRCBuildPipelineCallbacks not found in the VRChat SDK.") };

            RemoveOldClones(avatar);
            // VRCFury при сборке может вынести объекты (сокеты, WorldSpace…) в корень сцены — запоминаем, что было до.
            var scene = avatar.scene;
            var rootsBefore = new System.Collections.Generic.HashSet<GameObject>(scene.GetRootGameObjects());
            var clone = Object.Instantiate(avatar);
            clone.name = avatar.name + CloneSuffix;
            clone.SetActive(true);
            try
            {
                EditorUtility.DisplayProgressBar(Brand.Name, L.T("Собираю копию аватара как при загрузке…", "Building an avatar copy like on upload…"), 0.3f);
                bool ok;
                try { ok = (bool)preprocess.Invoke(null, new object[] { clone }); }
                catch (Exception e) { return new PerfReport { Error = L.T("Сборка упала: ", "Build failed: ") + (e.InnerException ?? e).Message + L.T("\nПодробности в Console.", "\nDetails in the Console.") }; }
                if (!ok) return new PerfReport { Error = L.T("Сборка не прошла. Открой Console: там ошибка одного из инструментов (AAO, VRCFury, d4rk…).", "The build failed. Open the Console: one of the tools (AAO, VRCFury, d4rk…) reported an error.") };

                // Объекты EditorOnly Unity выкидывает при упаковке, а не на этом шаге — убираем сами.
                foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                    if (t != null && t != clone.transform && t.CompareTag("EditorOnly")) Object.DestroyImmediate(t.gameObject);

                EditorUtility.DisplayProgressBar(Brand.Name, L.T("Считаю ранг…", "Calculating rank…"), 0.9f);
                var report = PerfStats.Measure(clone, target);
                report.Sources = SpsLever.Sources(clone);
                try
                {
                    var mm = MaterialMergeAnalyzer.Analyze(avatar);
                    report.MaterialGroups = mm.Groups.Select(g => g.Select(m => m.name).ToList()).ToList();
                    report.Materials = MaterialMergeAnalyzer.ToText(mm) + "\n" + MaterialScan.Source(avatar);
                }
                catch (Exception e) { report.Materials = L.T("Разбор материалов упал: ", "Material analysis failed: ") + e.Message; }
                // какие физбоны остались после всех инструментов и сколько стоит каждый
                foreach (var pb in clone.GetComponentsInChildren<Component>(true))
                {
                    if (pb == null || pb.GetType().Name != "VRCPhysBone") continue;
                    var so = new SerializedObject(pb);
                    var root = so.FindProperty("rootTransform")?.objectReferenceValue as Transform;
                    if (root == null) root = pb.transform;
                    int tr = PhysBoneScan.Affected(root, PhysBoneScan.Ignored(so)).Count;
                    int col = so.FindProperty("colliders")?.arraySize ?? 0;
                    var name = System.Text.RegularExpressions.Regex.Replace(root.name, @"^\[VF\d+\]\s*", "");
                    report.PhysBones.Add((name, tr, col, Math.Max(0, tr - 1) * col));
                }
                return report;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                // убрать всё, что сборка оставила в корне сцены
                foreach (var go in scene.GetRootGameObjects())
                    if (go != clone && !rootsBefore.Contains(go)) Object.DestroyImmediate(go);
                if (keepClone)
                {
                    clone.transform.position = avatar.transform.position + Vector3.right * 1.5f;
                    Undo.RegisterCreatedObjectUndo(clone, "Orchestra test copy");
                    Selection.activeGameObject = clone;
                }
                else if (clone != null) Object.DestroyImmediate(clone);
            }
        }

        public static void RemoveOldClones(GameObject avatar)
        {
            foreach (var go in avatar.scene.GetRootGameObjects())
                if (go.name == avatar.name + CloneSuffix) Object.DestroyImmediate(go);
        }

        // Мусор от старых проверок в корне сцены: объекты, которые создаёт сборка VRCFury/SPS.
        public static System.Collections.Generic.List<GameObject> FindJunk(GameObject avatar)
        {
            var list = new System.Collections.Generic.List<GameObject>();
            var roots = avatar.scene.GetRootGameObjects();
            // имена всех объектов внутри аватаров сцены: сборка выносит их копии в корень (world-space)
            var avatarNames = new System.Collections.Generic.HashSet<string>();
            foreach (var r in roots)
                if (AvatarUtil.IsAvatar(r))
                    foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t != r.transform) avatarNames.Add(t.name);
            var known = new System.Collections.Generic.HashSet<string>
            {
                "LegacyLights", "WorldSpace", "Nonuniform SPS Sockets", "World Scale Detector", "SpsAutoDistance",
            };
            foreach (var go in roots)
            {
                if (AvatarUtil.IsAvatar(go) || go.name.EndsWith(CloneSuffix)) continue;
                if (PrefabUtility.IsPartOfPrefabInstance(go)) continue; // объекты сцены из префабов — не наши
                if (go.GetComponent<Camera>() != null) continue;
                var n = go.name;
                bool generated = known.Contains(n) || n.StartsWith("BakedSps") || n.StartsWith("[VF") || avatarNames.Contains(n);
                if (generated) list.Add(go);
            }
            return list;
        }
    }
}
