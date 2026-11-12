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
        const float FaceKeep = 0.80f;      // меш лица/тела: не меньше 80% треугольников (иначе ломаются лицо и шейпы)
        const float MinKeep = 0.45f;       // остальные меши: не меньше 45%

        public static int CurrentTriangles(GameObject avatar) =>
            AvatarUtil.MeshRenderers(avatar).Sum(r => AvatarUtil.Triangles(AvatarUtil.GetMesh(r)));

        public static void Apply(GameObject avatar, int budget, List<string> log, float minKeep = MinKeep)
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
                // меш лица не упрощаем совсем: мимика (блендшейпы) ломается даже при лёгком упрощении (проверено на Mishi)
                if (tris < KeepSmallerThan || AvatarUtil.IsPenetrator(r) || r == face)
                {
                    fEnabled.SetValue(e, false); fTarget.SetValue(e, tris); left -= tris;
                    if (AvatarUtil.IsPenetrator(r)) log.Add($"  {r.name}: SPS/DPS, {L.T("не упрощается", "not simplified")}");
                    if (r == face) log.Add($"  {r.name}: {L.T("лицо, не упрощается", "face, not simplified")}");
                }
                else adjustable.Add((e, r, tris));
            }

            // Пределы «без порчи вида»: меш лица (часто это и всё тело) ужимаем не сильнее чем до FaceKeep,
            // остальные — не сильнее чем до MinKeep. Если так в бюджет не влезть — не ломаем, а просим убрать вещи.
            var targets = new Dictionary<object, int>();
            var faceItem = adjustable.FirstOrDefault(x => x.r == face);
            if (faceItem.e != null)
            {
                int f = Mathf.Min(faceItem.tris, Mathf.Max(Mathf.CeilToInt(faceItem.tris * FaceKeep), Mathf.RoundToInt(budget * FaceShare)));
                targets[faceItem.e] = f; left -= f; adjustable.Remove(faceItem);
            }
            long rest = adjustable.Sum(x => (long)x.tris);
            float ratio = rest > 0 ? Mathf.Clamp(Mathf.Max(0, left) / (float)rest, minKeep, 1f) : 1f;
            foreach (var x in adjustable) targets[x.e] = Mathf.Max(64, Mathf.FloorToInt(x.tris * ratio));
            foreach (var kv in targets) { fEnabled.SetValue(kv.Key, true); fFixed.SetValue(kv.Key, false); fTarget.SetValue(kv.Key, kv.Value); }

            int result = 0;
            foreach (var r in renderers)
            {
                var e = map[r];
                int t = (bool)fEnabled.GetValue(e) ? (int)fTarget.GetValue(e) : AvatarUtil.Triangles(AvatarUtil.GetMesh(r));
                result += t;
            }
            EditorUtility.SetDirty(comp);
            log.Add(L.T($"+ Meshia: треугольники {total} → ~{result} (бюджет {budget}, лицу {FaceShare:P0})", $"+ Meshia: triangles {total} → ~{result} (budget {budget}, face {FaceShare:P0})"));
            if (result > budget)
                log.Add(L.T($"! Полигоны: без порчи вида можно ужать только до ~{result} (лимит {budget}). Остальное — убрать вещи, совет появится после проверки.",
                            $"! Polygons: without spoiling the look they can only go down to ~{result} (limit {budget}). Remove items for the rest — advice appears after the check."));
        }
    }
}
