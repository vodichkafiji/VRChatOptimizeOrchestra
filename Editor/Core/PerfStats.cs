using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    public enum Rank { None = 0, Excellent = 1, Good = 2, Medium = 3, Poor = 4, VeryPoor = 5 }

    public class CategoryRow
    {
        public string Category;     // имя категории из SDK (PolyCount, MaterialCount, ...)
        public double? Value;       // текущее значение
        public Rank Rating;         // рейтинг этой категории
        public double? Limit;       // лимит для выбранной цели
    }

    public class PerfReport
    {
        public Rank Overall;
        public List<CategoryRow> Rows = new List<CategoryRow>();
        public string Error;
        public Dictionary<string, (int contacts, int lights)> Sources = new Dictionary<string, (int contacts, int lights)>();
        public List<(string name, int transforms, int colliders, int checks)> PhysBones = new List<(string, int, int, int)>();
        public string Materials;
        public List<List<string>> MaterialGroups; // как d4rk склеит материалы (имена)

        public IEnumerable<CategoryRow> Blockers(Rank target) => Rows.Where(r => r.Rating > target);

        public string ToText(Rank target)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Overall: {Overall}   target: {target}");
            foreach (var r in Rows)
                sb.AppendLine($"{(r.Rating > target ? "FAIL" : " ok ")}  {r.Category,-28} {Fmt(r.Value),10}  limit {Fmt(r.Limit),8}  {r.Rating}");
            if (Sources.Count > 0)
            {
                sb.AppendLine("\nКонтакты / свет по источникам:");
                foreach (var kv in Sources.OrderByDescending(k => k.Value.contacts + k.Value.lights))
                    sb.AppendLine($"  {kv.Key}: контактов {kv.Value.contacts}, света {kv.Value.lights}");
            }
            if (!string.IsNullOrEmpty(Materials)) sb.AppendLine("\nМатериалы:\n" + Materials);
            if (PhysBones.Count > 0)
            {
                sb.AppendLine($"\nФизбоны в сборке ({PhysBones.Count}; трансформов ~{PhysBones.Sum(p => p.transforms)}, проверок ~{PhysBones.Sum(p => p.checks)}):");
                foreach (var p in PhysBones.OrderByDescending(p => p.checks).ThenByDescending(p => p.transforms))
                    sb.AppendLine($"  {p.name}: трансформов {p.transforms}, коллайдеров {p.colliders}, проверок {p.checks}");
            }
            return sb.ToString();
        }

        public static string Fmt(double? v) => v == null ? "-" : (Math.Abs(v.Value % 1) < 1e-6 ? v.Value.ToString("0") : v.Value.ToString("0.0"));
    }

    // Статистика производительности через сам VRChat SDK — те же числа, что считает игра.
    public static class PerfStats
    {
        // Категории, которые не влияют на ранг или не имеют смысла для показа.
        static readonly HashSet<string> Skip = new HashSet<string> { "None", "Overall", "DownloadSize", "UncompressedSize", "AABB" };

        public static PerfReport Measure(GameObject avatar, Rank target, bool quest = false)
        {
            var report = new PerfReport();
            var perfType = Reflect.FindTypeByName("AvatarPerformance", t => t.GetMethod("CalculatePerformanceStats", BindingFlags.Public | BindingFlags.Static) != null);
            var statsType = Reflect.FindTypeByName("AvatarPerformanceStats", t => t.IsClass && t.GetMethod("GetPerformanceRatingForCategory") != null);
            if (perfType == null || statsType == null) { report.Error = L.T("Не найден VRChat SDK (AvatarPerformance).", "VRChat SDK not found (AvatarPerformance)."); return report; }

            var ctor = statsType.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
            var stats = ctor.Invoke(ctor.GetParameters().Select(p => p.ParameterType == typeof(bool) ? (object)quest : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)).ToArray());

            var calc = perfType.GetMethod("CalculatePerformanceStats", BindingFlags.Public | BindingFlags.Static);
            var args = calc.GetParameters().Select(p =>
                p.ParameterType == typeof(string) ? avatar.name :
                p.ParameterType == typeof(GameObject) ? avatar :
                p.ParameterType == statsType ? stats :
                p.ParameterType == typeof(bool) ? (object)quest :
                (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)).ToArray();
            try { calc.Invoke(null, args); }
            catch (Exception e) { report.Error = L.T("Ошибка подсчёта статистики: ", "Stats calculation error: ") + (e.InnerException ?? e).Message; return report; }

            var getRating = statsType.GetMethod("GetPerformanceRatingForCategory");
            var catType = getRating.GetParameters()[0].ParameterType;
            var limits = Thresholds.Load(target, quest);

            foreach (var cat in Enum.GetValues(catType))
            {
                var name = cat.ToString();
                if (Skip.Contains(name) && name != "Overall") continue;
                Rank rating;
                // часть категорий SDK не считает (например, новые) и бросает исключение — пропускаем их
                try { rating = ParseRank(getRating.Invoke(stats, new[] { cat })); }
                catch { continue; }
                if (name == "Overall") { report.Overall = rating; continue; }
                if (Skip.Contains(name)) continue;
                var path = FieldPath(name);
                report.Rows.Add(new CategoryRow
                {
                    Category = name,
                    Value = Reflect.ToNumber(Reflect.GetPath(stats, path)),
                    Rating = rating,
                    Limit = limits?.Get(path),
                });
            }
            // сначала то, что мешает, потом остальное
            report.Rows = report.Rows.OrderByDescending(r => r.Rating > target).ThenBy(r => r.Category).ToList();
            return report;
        }

        // PhysBoneComponentCount -> physBone.componentCount, PolyCount -> polyCount
        public static string FieldPath(string category)
        {
            if (category.StartsWith("PhysBone")) return "physBone." + LowerFirst(category.Substring("PhysBone".Length));
            return LowerFirst(category);
        }

        static string LowerFirst(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        static Rank ParseRank(object v) => Enum.TryParse(v?.ToString(), out Rank r) ? r : Rank.None;
    }

    // Пороги рангов из ассетов самого SDK (Resources/Validation/Performance/StatsLevels).
    public class Thresholds
    {
        SerializedObject _so;

        public static Thresholds Load(Rank level, bool quest)
        {
            if (level < Rank.Excellent || level > Rank.Poor) return null;
            var platform = quest ? "Quest" : "Windows";
            var asset = Resources.Load<ScriptableObject>($"Validation/Performance/StatsLevels/{platform}/{level}_{platform}");
            return asset == null ? null : new Thresholds { _so = new SerializedObject(asset) };
        }

        public double? Get(string path)
        {
            var p = _so.FindProperty(path);
            if (p == null) return null;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.intValue;
                case SerializedPropertyType.Float: return p.floatValue;
                case SerializedPropertyType.Boolean: return p.boolValue ? 1 : 0;
                default: return null;
            }
        }
    }
}
