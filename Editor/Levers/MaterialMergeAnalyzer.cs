using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TurboOptimizer
{
    // Спрашивает у самого d4rk (CanCombineMaterialsError), какие материалы он склеит, а какие нет и почему.
    public static class MaterialMergeAnalyzer
    {
        public class Result
        {
            public List<List<Material>> Groups = new List<List<Material>>();   // как d4rk склеит сейчас
            public Dictionary<string, List<(Material a, Material b)>> Reasons = new Dictionary<string, List<(Material, Material)>>();
            public string Error;
        }

        public static Result Analyze(GameObject avatar)
        {
            var res = new Result();
            var optType = Reflect.FindTypeByName("d4rkAvatarOptimizer", x => typeof(MonoBehaviour).IsAssignableFrom(x));
            var opt = optType == null ? null : avatar.GetComponent(optType);
            if (opt == null) { res.Error = L.T("На аватаре нет d4rkAvatarOptimizer — сначала примени безопасную оптимизацию.", "The avatar has no d4rkAvatarOptimizer — apply the safe optimization first."); return res; }
            var slotType = optType.GetNestedType("MaterialSlot");
            var check = optType.GetMethod("CanCombineMaterialsError");
            if (slotType == null || check == null) { res.Error = L.T("Эта версия d4rk не даёт проверить склейку материалов.", "This d4rk version can't check material merging."); return res; }
            try { Reflect.Call(opt, "ClearCaches"); } catch { }

            // по одному слоту на материал
            var slots = new List<(Material mat, object slot)>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (AvatarUtil.IsEditorOnly(r.transform) || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && !slots.Any(s => s.mat == mats[i]))
                        slots.Add((mats[i], Activator.CreateInstance(slotType, r, i)));
            }

            var listType = typeof(List<>).MakeGenericType(slotType);
            string Why(List<object> group, object candidate)
            {
                var list = (IList)Activator.CreateInstance(listType);
                foreach (var g in group) list.Add(g);
                try { return check.Invoke(opt, new[] { list, candidate }) as string; }
                catch (Exception e) { return L.T("ошибка проверки: ", "check error: ") + (e.InnerException ?? e).Message; }
            }

            // жадная склейка, как у d4rk: кандидат идёт в первую группу, с которой совместим
            var groups = new List<List<(Material mat, object slot)>>();
            foreach (var s in slots)
            {
                var g = groups.FirstOrDefault(x => Why(x.Select(y => y.slot).ToList(), s.slot) == null);
                if (g != null) g.Add(s); else groups.Add(new List<(Material, object)> { s });
            }
            res.Groups = groups.Select(g => g.Select(x => x.mat).ToList()).ToList();

            // почему не склеиваются: каждая пара «первых» материалов групп
            for (int i = 0; i < groups.Count; i++)
                for (int j = i + 1; j < groups.Count; j++)
                {
                    var a = groups[i][0]; var b = groups[j][0];
                    var why = Why(new List<object> { a.slot }, b.slot);
                    if (why == null) continue;
                    var key = why.Split('\n')[0];
                    if (!res.Reasons.TryGetValue(key, out var l)) res.Reasons[key] = l = new List<(Material, Material)>();
                    l.Add((a.mat, b.mat));
                }
            return res;
        }

        public static string ToText(Result r)
        {
            if (r.Error != null) return r.Error;
            var sb = new StringBuilder();
            sb.AppendLine(L.T($"d4rk склеит {r.Groups.Sum(g => g.Count)} материалов в {r.Groups.Count} слотов:", $"d4rk will merge {r.Groups.Sum(g => g.Count)} materials into {r.Groups.Count} slots:"));
            foreach (var g in r.Groups.Where(g => g.Count > 1)) sb.AppendLine($"  [{g.Count}] {string.Join(", ", g.Select(m => m.name))}");
            sb.AppendLine($"  {L.T("поодиночке", "alone")}: {string.Join(", ", r.Groups.Where(g => g.Count == 1).Select(g => g[0].name))}");
            sb.AppendLine(L.T("\nПочему не склеиваются (причина: число пар, примеры):", "\nWhy they don't merge (reason: pairs, examples):"));
            foreach (var kv in r.Reasons.OrderByDescending(k => k.Value.Count))
                sb.AppendLine($"  {kv.Key}: {kv.Value.Count}  — {string.Join("; ", kv.Value.Take(4).Select(p => p.a.name + " + " + p.b.name))}");
            return sb.ToString();
        }
    }
}
