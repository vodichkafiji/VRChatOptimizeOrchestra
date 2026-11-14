using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    // Бюджет треугольников через Meshia Cascading Avatar Mesh Simplifier.
    // Упрощение происходит только при сборке, FBX не меняются.
    public static class MeshiaLever
    {
        public const string HolderName = "TurboOptimizer Mesh Budget";
        const float FaceShare = 0.30f;     // доля бюджета для меша лица/тела
        const int KeepSmallerThan = 1500;  // мелкие меши не трогаем
        const float FaceKeep = 0.85f;      // меш лица: не меньше 85% (на Mishi при 60% лицо исказилось даже с защитой)
        const float MinKeep = 0.45f;       // остальные меши: не меньше 45%
        const float HandsKeep = 0.60f;     // меши с кистями и пальцами: не меньше 60% (на Mishi при 30% «поломались» пальцы)

        public static int CurrentTriangles(GameObject avatar) =>
            AvatarUtil.MeshRenderers(avatar).Sum(r => AvatarUtil.Triangles(AvatarUtil.GetMesh(r)));

        // simplifyFace = false — меш лица не трогается совсем (галочка «Не упрощать лицо»).
        public static void Apply(GameObject avatar, int budget, List<string> log, float minKeep = MinKeep, bool simplifyFace = true)
        {
            var type = Reflect.FindType("Meshia.MeshSimplification.Ndmf.MeshiaCascadingAvatarMeshSimplifier");
            if (type == null) { log.Add(L.T("! Meshia не установлен — треугольники не уменьшаются", "! Meshia isn't installed — triangles won't be reduced")); return; }

            var renderers = AvatarUtil.MeshRenderers(avatar);
            int total = renderers.Sum(r => AvatarUtil.Triangles(AvatarUtil.GetMesh(r)));
            var holder = avatar.transform.Find(HolderName);
            if (total <= budget)
            {
                if (holder != null) Undo.DestroyObjectImmediate(holder.gameObject);
                log.Add(L.T($"= треугольников {total} ≤ {budget}, упрощение не нужно", $"= triangles {total} ≤ {budget}, no simplification needed"));
                return;
            }

            if (holder == null)
            {
                var go = new GameObject(HolderName);
                Undo.RegisterCreatedObjectUndo(go, "Orchestra Meshia");
                go.transform.SetParent(avatar.transform, false);
                holder = go.transform;
            }
            var comp = holder.GetComponent(type);
            if (comp == null) comp = Undo.AddComponent(holder.gameObject, type);
            Undo.RecordObject(comp, "Orchestra Meshia");
            type.GetField("TargetTriangleCount").SetValue(comp, budget);
            try { type.GetMethod("RefreshEntries").Invoke(comp, null); } catch (Exception e) { log.Add("! Meshia RefreshEntries: " + (e.InnerException ?? e).Message); }

            var entries = (IList)type.GetField("Entries").GetValue(comp);
            var entryType = entries.GetType().GetGenericArguments()[0];
            var getRenderer = entryType.GetMethod("GetTargetRenderer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var fTarget = entryType.GetField("TargetTriangleCount");
            var fEnabled = entryType.GetField("Enabled");
            var fFixed = entryType.GetField("Fixed");

            // Meshia сама не видит выключенные объекты — добавляем их.
            var map = new Dictionary<Renderer, object>();
            foreach (var e in entries) { var r = getRenderer.Invoke(e, new object[] { comp }) as Renderer; if (r != null) map[r] = e; }
            foreach (var r in renderers)
                if (!map.ContainsKey(r)) { var e = Activator.CreateInstance(entryType, new object[] { r }); entries.Add(e); map[r] = e; }

            var face = AvatarUtil.FaceRenderer(avatar);
            int left = budget;
            var adjustable = new List<(object e, Renderer r, int tris)>();
            foreach (var r in renderers)
            {
                var e = map[r];
                int tris = AvatarUtil.Triangles(AvatarUtil.GetMesh(r));
                if (tris < KeepSmallerThan || AvatarUtil.IsPenetrator(r) || (r == face && !simplifyFace))
                {
                    fEnabled.SetValue(e, false); fTarget.SetValue(e, tris); left -= tris;
                    if (AvatarUtil.IsPenetrator(r)) log.Add($"  {r.name}: SPS/DPS, {L.T("не упрощается", "not simplified")}");
                    if (r == face) log.Add($"  {r.name}: {L.T("лицо, не упрощается", "face, not simplified")}");
                }
                else adjustable.Add((e, r, tris));
            }

            // Одна общая доля для всех, но с пределами «без порчи вида»: лицо не меньше FaceKeep, остальные не меньше minKeep.
            // Если кто-то упёрся в свой предел, остальным достаётся меньше. Не влезает — не ломаем, а просим убрать вещи.
            var targets = new Dictionary<Renderer, int>(); // ключ — рендерер: записи Meshia — record, их хэш меняется при правке
            var anim = avatar.GetComponent<Animator>();
            var handMeshes = new HashSet<Renderer>(adjustable.Where(x => x.r != face && HasHands(x.r, anim)).Select(x => x.r));
            float Floor(Renderer r) => r == face ? FaceKeep : handMeshes.Contains(r) ? Mathf.Max(minKeep, HandsKeep) : minKeep;
            var open = adjustable.ToList();
            int budgetLeft = left;
            for (int it = 0; it < 5 && open.Count > 0; it++)
            {
                long sum = open.Sum(x => (long)x.tris);
                float ratio = Mathf.Clamp(Mathf.Max(0, budgetLeft) / (float)sum, 0f, 1f);
                var floored = open.Where(x => ratio < Floor(x.r)).ToList();
                if (floored.Count == 0 || it == 4)
                {
                    foreach (var x in open) targets[x.r] = Mathf.Max(64, Mathf.FloorToInt(x.tris * Mathf.Max(ratio, Floor(x.r))));
                    break;
                }
                foreach (var x in floored) { int t = Mathf.CeilToInt(x.tris * Floor(x.r)); targets[x.r] = t; budgetLeft -= t; open.Remove(x); }
            }
            var faceEntry = adjustable.FirstOrDefault(x => x.r == face);
            if (faceEntry.e != null)
            {
                ProtectFace(faceEntry.e, entryType);
                log.Add($"  {face.name}: {L.T("лицо упрощается бережно", "face simplified gently")} ({faceEntry.tris} → {targets[faceEntry.r]}, {L.T("края глаз, рта и ресниц сохраняются", "eye, mouth and lash edges kept")})");
            }
            foreach (var r in handMeshes)
            {
                ProtectShape(map[r], entryType);
                log.Add($"  {r.name}: {L.T("кисти и пальцы — упрощается бережно", "hands and fingers — simplified gently")} ({AvatarUtil.Triangles(AvatarUtil.GetMesh(r))} → {targets[r]})");
            }
            foreach (var kv in targets) { var en = map[kv.Key]; fEnabled.SetValue(en, true); fFixed.SetValue(en, false); fTarget.SetValue(en, kv.Value); }

            int result = 0;
            foreach (var r in renderers)
            {
                var e = map[r];
                int t = (bool)fEnabled.GetValue(e) ? (int)fTarget.GetValue(e) : AvatarUtil.Triangles(AvatarUtil.GetMesh(r));
                result += t;
            }
            EditorUtility.SetDirty(comp);
            log.Add(L.T($"+ Meshia: треугольники {total} → ~{result} (бюджет {budget})", $"+ Meshia: triangles {total} → ~{result} (budget {budget})"));
            if (result > budget)
                log.Add(L.T($"! Полигоны: без порчи вида можно ужать только до ~{result} (лимит {budget}). Остальное — убрать вещи, совет появится после проверки.",
                            $"! Polygons: without spoiling the look they can only go down to ~{result} (limit {budget}). Remove items for the rest — advice appears after the check."));
        }

        static readonly HumanBodyBones[] HandBones =
        {
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
            HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
        };

        // Меш «с руками»: заметная доля вершин (≥ 3%) висит на костях кистей и пальцев.
        static bool HasHands(Renderer r, Animator anim)
        {
            if (!(r is SkinnedMeshRenderer smr) || smr.sharedMesh == null || anim == null || !anim.isHuman) return false;
            var hand = new HashSet<Transform>(HandBones.Select(b => anim.GetBoneTransform(b)).Where(t => t != null));
            if (hand.Count == 0) return false;
            var bones = smr.bones;
            var perVertex = smr.sharedMesh.GetBonesPerVertex();
            var weights = smr.sharedMesh.GetAllBoneWeights();
            int onHands = 0;
            for (int v = 0, w = 0; v < perVertex.Length; v++)
            {
                bool any = false;
                for (int k = 0; k < perVertex[v]; k++, w++)
                {
                    var bw = weights[w];
                    if (!any && bw.weight > 0.3f && bw.boneIndex < bones.Length && hand.Contains(bones[bw.boneIndex])) any = true;
                }
                if (any) onHands++;
            }
            return perVertex.Length > 0 && onHands >= perVertex.Length * 0.03f;
        }

        // Бережное упрощение формы (пальцы): сохраняем кривизну и точнее переносим UV.
        static void ProtectShape(object entry, Type entryType)
        {
            try
            {
                var fOpt = entryType.GetField("Options");
                object opt = fOpt.GetValue(entry);
                var ot = opt.GetType();
                ot.GetField("UseBarycentricCoordinateInterpolation")?.SetValue(opt, true);
                ot.GetField("PreserveSurfaceCurvature")?.SetValue(opt, true);
                fOpt.SetValue(entry, opt);
            }
            catch (Exception) { }
        }

        // Защита лица в Meshia: сохраняем края (вырезы глаз, рот, ресницы) у вершин на костях головы, точная интерполяция UV
        // (без неё текстура лица «плывёт» белыми пятнами — так было на Mishi) и сохранение кривизны.
        static void ProtectFace(object entry, Type entryType)
        {
            try
            {
                var fOpt = entryType.GetField("Options");
                object opt = fOpt.GetValue(entry); // boxed struct
                var ot = opt.GetType();
                ot.GetField("UseBarycentricCoordinateInterpolation")?.SetValue(opt, true);
                ot.GetField("PreserveSurfaceCurvature")?.SetValue(opt, true);
                fOpt.SetValue(entry, opt);
                var fBones = entryType.GetField("PreserveBorderEdgesBones");
                if (fBones != null)
                {
                    ulong mask = (ulong)fBones.GetValue(entry);
                    foreach (var b in new[] { HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.LeftEye, HumanBodyBones.RightEye, HumanBodyBones.Jaw })
                        mask |= 1ul << (int)b;
                    fBones.SetValue(entry, mask);
                }
            }
            catch (Exception) { /* другая версия Meshia — без защиты, но и без ошибки */ }
        }
    }
}
