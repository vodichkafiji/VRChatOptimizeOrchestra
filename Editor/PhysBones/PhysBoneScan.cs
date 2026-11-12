using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    // Всё, что планировщику нужно знать об одном физбоне.
    public class PbInfo
    {
        public Component Pb;
        public string Name;                 // имя корневой кости
        public Transform Root;
        public Transform Parent;            // родитель корня: сливать можно только с общим родителем
        public HashSet<Transform> Affected; // кости, которые физбон двигает (с учётом Ignore Transforms)
        public HashSet<Transform> Effective; // Affected без «пустых» концов — их AAO заменит на endpoint сам
        public int Weighted;                // из них с весами вершин (реально что-то двигают)
        public int Colliders;
        public bool Animated;               // компонент или его объект включается анимацией
        public bool HasParameter;           // параметр используется аниматором
        public bool Useless;                // ни одной кости с весами — AAO всё равно удалит
        public string Item = "Прочее";      // вещь (меш), которую физбон в основном двигает
        public string SymKey;               // имя без L/R и номеров: одинаковое у симметричных физбонов

        public int Checks => Math.Max(0, Affected.Count - 1) * Colliders;
    }

    public static class PhysBoneScan
    {
        public static readonly HashSet<string> NotSettings = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset",
            "m_GameObject", "m_Enabled", "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier",
            "rootTransform", "ignoreTransforms",
        };

        public static List<PbInfo> Scan(GameObject avatar)
        {
            var owners = BoneOwners(avatar);
            var weighted = new HashSet<Transform>(owners.Keys);
            var animated = AnimatedPaths(avatar);
            var list = new List<PbInfo>();
            foreach (var pb in avatar.GetComponentsInChildren<Component>(true))
            {
                if (pb == null || pb.GetType().Name != "VRCPhysBone" || AvatarUtil.IsEditorOnly(pb.transform)) continue;
                var so = new SerializedObject(pb);
                var root = so.FindProperty("rootTransform")?.objectReferenceValue as Transform;
                if (root == null) root = pb.transform;
                var affected = Affected(root, Ignored(so));
                var path = AnimationUtility.CalculateTransformPath(pb.transform, avatar.transform);
                var info = new PbInfo
                {
                    Pb = pb,
                    Name = root.name,
                    Root = root,
                    Parent = root.parent,
                    Affected = affected,
                    Weighted = affected.Count(weighted.Contains),
                    Colliders = so.FindProperty("colliders")?.arraySize ?? 0,
                    Animated = animated.Contains(path),
                    HasParameter = !string.IsNullOrEmpty(so.FindProperty("parameter")?.stringValue),
                };
                info.Useless = info.Weighted == 0;
                info.Effective = new HashSet<Transform>(affected.Where(t => t == root || HasWeight(t, affected, weighted)));
                info.SymKey = SymmetryKey(root.name);
                // вещь = меш с наибольшим числом вершин на костях этого физбона
                var votes = new Dictionary<string, int>();
                foreach (var t in info.Effective)
                    if (owners.TryGetValue(t, out var byRenderer))
                        foreach (var kv in byRenderer) votes[kv.Key] = (votes.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
                var mesh = votes.Count > 0 ? votes.OrderByDescending(kv => kv.Value).First().Key : "Прочее";
                // авторы часто сливают всю одежду в один меш, поэтому группа = меш + «семейство» по имени кости
                info.Item = $"{mesh}: {Family(info.SymKey, root.name)}";
                list.Add(info);
            }
            return list;
        }

        // Есть ли вес на кости или ниже по цепочке.
        static bool HasWeight(Transform t, HashSet<Transform> affected, HashSet<Transform> weighted)
        {
            if (weighted.Contains(t)) return true;
            foreach (Transform c in t) if (affected.Contains(c) && HasWeight(c, affected, weighted)) return true;
            return false;
        }

        public static HashSet<Transform> Ignored(SerializedObject so)
        {
            var set = new HashSet<Transform>();
            var ign = so.FindProperty("ignoreTransforms");
            if (ign != null)
                for (int i = 0; i < ign.arraySize; i++)
                    if (ign.GetArrayElementAtIndex(i).objectReferenceValue is Transform t && t != null) set.Add(t);
            return set;
        }

        public static HashSet<Transform> Affected(Transform root, HashSet<Transform> ignored)
        {
            var set = new HashSet<Transform>();
            void Walk(Transform t)
            {
                if (ignored.Contains(t)) return;
                set.Add(t);
                foreach (Transform c in t) Walk(c);
            }
            Walk(root);
            return set;
        }

        public static HashSet<Transform> WeightedBones(GameObject avatar) => new HashSet<Transform>(BoneOwners(avatar).Keys);

        // Для каждой кости с весами: какие меши она двигает и сколькими вершинами.
        // Объекты с обычным MeshRenderer считаются «весом» на себе и своём родителе.
        public static Dictionary<Transform, Dictionary<string, int>> BoneOwners(GameObject avatar)
        {
            var map = new Dictionary<Transform, Dictionary<string, int>>();
            void Add(Transform t, string item, int n)
            {
                if (t == null) return;
                if (!map.TryGetValue(t, out var d)) map[t] = d = new Dictionary<string, int>();
                d[item] = (d.TryGetValue(item, out var v) ? v : 0) + n;
            }
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (AvatarUtil.IsEditorOnly(r.transform)) continue;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    var bones = smr.bones;
                    var count = new int[bones.Length];
                    var weights = smr.sharedMesh.GetAllBoneWeights();
                    var perVertex = smr.sharedMesh.GetBonesPerVertex();
                    var kept = ItemScan.KeptVertices(smr); // части меша, убранные TurboOptimizer, не считаем
                    for (int v = 0, w = 0; v < perVertex.Length; v++)
                        for (int k = 0; k < perVertex[v]; k++, w++)
                            if ((kept == null || kept[v]) && weights[w].weight > 0.0001f && weights[w].boneIndex < count.Length) count[weights[w].boneIndex]++;
                    for (int i = 0; i < bones.Length; i++) if (count[i] > 0) Add(bones[i], r.name, count[i]);
                }
                else if (r is MeshRenderer)
                {
                    Add(r.transform, r.name, 100);
                    Add(r.transform.parent, r.name, 100);
                }
            }
            return map;
        }

        // "bow_root_elbow" -> "bow", "piercing_bow_root" -> "piercing"; служебные слова пропускаются
        static readonly HashSet<string> Generic = new HashSet<string> { "root", "phys", "physbone", "pb", "bone", "end", "the", "upper", "lower", "top", "bottom" };
        public static string Family(string symKey, string fallback)
        {
            var parts = (symKey ?? "").Split('_').Where(s => s.Length > 0).ToList();
            var word = parts.FirstOrDefault(s => !Generic.Contains(s)) ?? parts.FirstOrDefault();
            return string.IsNullOrEmpty(word) ? fallback : word;
        }

        // "Bow_Root_Elbow.L.034" -> "bow_root_elbow", "Ear1.002.R" -> "ear"
        public static string SymmetryKey(string name)
        {
            var s = name.ToLowerInvariant();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(left|right)", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(^|[\s._\-])(l|r)(?=$|[\s._\-\d])", "$1");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[\d]+", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[\s._\-]+", "_").Trim('_');
            return s;
        }

        // Пути объектов, у которых анимации FX включают/выключают физбон или сам объект.
        static HashSet<string> AnimatedPaths(GameObject avatar)
        {
            var set = new HashSet<string>();
            var desc = AvatarUtil.Descriptor(avatar);
            foreach (var field in new[] { "baseAnimationLayers", "specialAnimationLayers" })
            {
                if (!(Reflect.GetPath(desc, field) is Array layers)) continue;
                foreach (var l in layers)
                {
                    if (!(Reflect.GetPath(l, "animatorController") is RuntimeAnimatorController c) || c == null) continue;
                    foreach (var clip in c.animationClips)
                    {
                        if (clip == null) continue;
                        foreach (var b in AnimationUtility.GetCurveBindings(clip))
                            if ((b.propertyName == "m_Enabled" && b.type.Name == "VRCPhysBone") || b.propertyName == "m_IsActive")
                                set.Add(b.path);
                    }
                }
            }
            return set;
        }

        // Совпадают ли настройки двух физбонов (без корня и Ignore Transforms).
        public static bool SameSettings(Component a, Component b)
        {
            var sa = new SerializedObject(a);
            var sb = new SerializedObject(b);
            var it = sa.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (NotSettings.Contains(it.name)) continue;
                var other = sb.FindProperty(it.propertyPath);
                if (other == null || !SerializedProperty.DataEquals(it, other)) return false;
            }
            return true;
        }
    }
}
