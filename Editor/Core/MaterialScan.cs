using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    // Разбор материалов: что осталось в собранной копии и что мешает d4rk их склеить.
    public static class MaterialScan
    {
        // Собранная копия: слоты материалов по шейдерам.
        public static string Built(GameObject clone)
        {
            var sb = new StringBuilder();
            var slots = new List<(string renderer, Material mat)>();
            foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                foreach (var m in r.sharedMaterials) slots.Add((r.name, m));
            }
            sb.AppendLine($"Слотов материалов в сборке: {slots.Count}");
            foreach (var byShader in slots.GroupBy(s => s.mat == null ? "(пусто)" : s.mat.shader.name).OrderByDescending(g => g.Count()))
            {
                sb.AppendLine($"  [{byShader.Count()}] {byShader.Key}");
                foreach (var byRenderer in byShader.GroupBy(s => s.renderer))
                    sb.AppendLine($"      {byRenderer.Key}: {string.Join(", ", byRenderer.Select(s => s.mat == null ? "-" : s.mat.name))}");
            }
            return sb.ToString();
        }

        // Исходный аватар: материалы, их состояние (Locked) и анимации, которые меняют материалы.
        public static string Source(GameObject avatar)
        {
            var sb = new StringBuilder();
            var mats = new Dictionary<Material, List<string>>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (AvatarUtil.IsEditorOnly(r.transform) || r is ParticleSystemRenderer) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (!mats.TryGetValue(m, out var l)) mats[m] = l = new List<string>();
                    if (!l.Contains(r.name)) l.Add(r.name);
                }
            }
            sb.AppendLine($"Разных материалов в аватаре: {mats.Count}");
            foreach (var kv in mats.OrderBy(k => k.Key.shader.name).ThenBy(k => k.Key.name))
            {
                var m = kv.Key;
                bool locked = m.shader.name.StartsWith("Hidden/Locked") || m.shader.name.Contains("/Locked/");
                var path = AssetDatabase.GetAssetPath(m);
                sb.AppendLine($"  {(locked ? "[LOCKED] " : "")}{m.name}  | {ShortShader(m.shader.name)} | rq {m.renderQueue} | {string.Join(", ", kv.Value)}");
            }

            // анимации, которые подменяют материалы (material swap) — d4rk такие слоты не склеивает
            var swaps = new Dictionary<string, HashSet<string>>();
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
                        foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                        {
                            if (!b.propertyName.StartsWith("m_Materials")) continue;
                            var key = $"{b.path} {b.propertyName}";
                            if (!swaps.TryGetValue(key, out var set)) swaps[key] = set = new HashSet<string>();
                            foreach (var k in AnimationUtility.GetObjectReferenceCurve(clip, b))
                                if (k.value != null) set.Add(k.value.name);
                        }
                    }
                }
            }
            if (swaps.Count > 0)
            {
                sb.AppendLine($"\nПодмена материалов анимацией ({swaps.Count} слотов):");
                foreach (var kv in swaps) sb.AppendLine($"  {kv.Key}: {string.Join(", ", kv.Value)}");
            }
            else sb.AppendLine("\nПодмены материалов анимацией нет.");
            return sb.ToString();
        }

        static string ShortShader(string s) => s.Length > 70 ? s.Substring(0, 70) + "…" : s;
    }
}
