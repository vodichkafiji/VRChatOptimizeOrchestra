using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TurboOptimizer
{
    public class PbOptions
    {
        public bool UnifySettings = true;   // сливать симметричные физбоны (L/R, .001/.002) даже с разными настройками
        public bool TrimTips = true;        // делать жёсткими кончики длинных цепочек
        public bool DropColliders = true;   // убирать коллайдеры у самых «дорогих» цепочек
        public HashSet<string> DropItems = new HashSet<string>(); // вещи, чью физику игрок отключил
        // считать все кости цепочки, а не только с весами: AAO убирает «пустые» концы не всегда (Hecate: прогноз 51, по факту 182)
        public bool Conservative;
    }

    public class PbLimits
    {
        public int Components = 8, Transforms = 64, Colliders = 8, Checks = 64;

        public static PbLimits For(Rank rank)
        {
            var t = Thresholds.Load(rank, false);
            if (t == null) return null;
            return new PbLimits
            {
                Components = (int)(t.Get("physBone.componentCount") ?? 8),
                Transforms = (int)(t.Get("physBone.transformCount") ?? 64),
                Colliders = (int)(t.Get("physBone.colliderCount") ?? 8),
                Checks = (int)(t.Get("physBone.collisionCheckCount") ?? 64),
            };
        }
    }

    public class MergeGroup
    {
        public List<PbInfo> Members = new List<PbInfo>();
        public bool NeedsUnify;   // настройки разные — будут выровнены по первому
        public string Name => $"{Members[0].Parent?.name}: {string.Join(" + ", Members.Select(m => m.Name))}";
    }

    public class ItemStat
    {
        public string Name;
        public int Count, Transforms;
    }

    public class PbPlan
    {
        public Rank Target, Achieved = Rank.VeryPoor;
        public PbLimits Limits;
        public List<PbInfo> All;
        public List<PbInfo> Useless = new List<PbInfo>();
        public List<ItemStat> Items = new List<ItemStat>();
        public List<PbInfo> Remove = new List<PbInfo>();
        public List<MergeGroup> Merges = new List<MergeGroup>();
        public Dictionary<PbInfo, List<Transform>> Trims = new Dictionary<PbInfo, List<Transform>>();
        public List<PbInfo> DropColliders = new List<PbInfo>();
        public int Components, Transforms, Checks;
        public List<string> SuggestDrop = new List<string>(); // какие вещи отключить, чтобы получить цель
        public Dictionary<Rank, List<string>> SuggestByRank = new Dictionary<Rank, List<string>>();
        public List<string> Warnings = new List<string>();

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine(L.T($"Физбонов: {All.Count} (без костей с весами, удалю: {Useless.Count}). Отключено вещей: {Remove.Except(Useless).Select(r => r.Item).Distinct().Count()}", $"PhysBones: {All.Count} (no weighted bones, will remove: {Useless.Count}). Items turned off: {Remove.Except(Useless).Select(r => r.Item).Distinct().Count()}"));
            sb.AppendLine(L.T($"Ранг физбонов после плана: {Achieved} (цель {Target})", $"PhysBone rank after the plan: {Achieved} (target {Target})"));
            sb.AppendLine(L.T($"Прогноз: компонентов {Components}, трансформов {Transforms}, collision checks ~{Checks}  (лимиты {Target}: {Limits?.Components}/{Limits?.Transforms}/{Limits?.Checks})", $"Forecast: components {Components}, transforms {Transforms}, collision checks ~{Checks}  ({Target} limits: {Limits?.Components}/{Limits?.Transforms}/{Limits?.Checks})"));
            foreach (var kv in SuggestByRank.OrderBy(k => k.Key))
                sb.AppendLine(L.T($"\nДля {kv.Key} по физбонам отключи ({kv.Value.Count}): ", $"\nFor {kv.Key} on PhysBones turn off ({kv.Value.Count}): ") + string.Join(", ", kv.Value));
            if (Achieved == Rank.VeryPoor)
                sb.AppendLine(L.T("\nДаже Poor не получается без отключения физики — кончики и коллайдеры не трогаю, чтобы не портить вид зря.", "\nEven Poor isn't reachable without turning physics off — tips and colliders are left alone so the look isn't spoiled for nothing."));
            if (Merges.Count > 0)
            {
                sb.AppendLine($"\n{L.T("Слияния", "Merges")} ({Merges.Count}):");
                foreach (var g in Merges) sb.AppendLine($"  {(g.NeedsUnify ? L.T("[настройки выровняются] ", "[settings will be aligned] ") : "")}{g.Name}");
            }
            if (Trims.Count > 0)
            {
                sb.AppendLine(L.T($"\nЖёсткие кончики ({Trims.Sum(t => t.Value.Count)} костей):", $"\nStiff tips ({Trims.Sum(t => t.Value.Count)} bones):"));
                foreach (var kv in Trims.OrderByDescending(k => k.Value.Count)) sb.AppendLine($"  {kv.Key.Name}: {kv.Value.Count}");
            }
            if (DropColliders.Count > 0)
            {
                sb.AppendLine($"\n{L.T("Убрать коллайдеры", "Remove colliders")} ({DropColliders.Count}):");
                foreach (var p in DropColliders) sb.AppendLine($"  {p.Name}: {p.Colliders} {L.T("коллайдеров", "colliders")}");
            }
            if (Remove.Count > 0)
            {
                sb.AppendLine($"\n{L.T("Удаляемые физбоны отключённых вещей", "PhysBones removed with turned-off items")} ({Remove.Count}):");
                foreach (var g in Remove.GroupBy(r => Useless.Contains(r) ? L.T("без весов: ", "no weights: ") + r.Name : r.Item)) sb.AppendLine($"  {g.Key}: {g.Count()}");
            }
            sb.AppendLine(L.T("\nФизика по вещам (физбонов / трансформов):", "\nPhysics per item (PhysBones / transforms):"));
            foreach (var it in Items) sb.AppendLine($"  {it.Name}: {it.Count} / {it.Transforms}");
            foreach (var w in Warnings) sb.AppendLine("! " + w);
            return sb.ToString();
        }
    }

    // Планировщик физбонов.
    // 1) отключённые игроком вещи → их физбоны удаляются;
    // 2) слияние с общим родителем (одинаковые настройки, либо симметричные при UnifySettings);
    // 3) ищется лучший ДОСТИЖИМЫЙ ранг от цели вниз, и только под него режутся кончики и коллайдеры;
    // 4) если цель не достигнута — подсказка, физику каких вещей отключить.
    public static class PhysBonePlanner
    {
        const int MinWeightedLeft = 2; // меньше — физбон перестаёт что-то двигать и AAO его удалит
        static readonly Rank[] Ranks = { Rank.Excellent, Rank.Good, Rank.Medium, Rank.Poor };

        class Ctx
        {
            public HashSet<Transform> Weighted;
            public Dictionary<(Component, Component), bool> Same = new Dictionary<(Component, Component), bool>();
            public PbOptions Opt;
        }

        class Sim
        {
            public List<MergeGroup> Merges;
            public Dictionary<PbInfo, List<Transform>> Trims = new Dictionary<PbInfo, List<Transform>>();
            public List<PbInfo> Drops = new List<PbInfo>();
            public int Components, Transforms, Checks;
            public bool Fits;
        }

        public static PbPlan Plan(GameObject avatar, Rank target, PbOptions opt)
        {
            var all = PhysBoneScan.Scan(avatar);
            if (opt.Conservative) foreach (var p in all) p.Effective = new HashSet<Transform>(p.Affected);
            var ctx = new Ctx { Weighted = PhysBoneScan.WeightedBones(avatar), Opt = opt };
            var plan = new PbPlan { All = all, Target = target, Limits = PbLimits.For(target) };
            // физбоны без костей с весами ничего видимого не двигают. AAO убирает их не всегда (проверено на Full SPS),
            // поэтому удаляем сами — кроме тех, у кого есть параметр или кого включает анимация.
            plan.Useless = all.Where(p => p.Useless && !p.HasParameter && !p.Animated).ToList();
            var live = all.Except(plan.Useless).ToList();
            plan.Items = live.GroupBy(p => p.Item)
                .Select(g => new ItemStat { Name = g.Key, Count = g.Count(), Transforms = g.Sum(p => p.Effective.Count) })
                .OrderByDescending(i => i.Count).ToList();

            var dropped = live.Where(p => opt.DropItems.Contains(p.Item)).ToList();
            var kept = live.Except(dropped).ToList();
            plan.Remove = dropped.Concat(plan.Useless).ToList();

            // лучший достижимый ранг, начиная с цели
            Sim chosen = null;
            foreach (var rank in Ranks.Where(r => r >= target))
            {
                var limits = PbLimits.For(rank);
                if (limits == null) continue;
                var sim = Simulate(kept, limits, ctx, allowCuts: true);
                if (sim.Fits) { chosen = sim; plan.Achieved = rank; break; }
            }
            if (chosen == null) chosen = Simulate(kept, PbLimits.For(Rank.Poor) ?? new PbLimits(), ctx, allowCuts: false);

            plan.Merges = chosen.Merges;
            plan.Trims = chosen.Trims;
            plan.DropColliders = chosen.Drops;
            plan.Components = chosen.Components;
            plan.Transforms = chosen.Transforms;
            plan.Checks = chosen.Checks;

            // подсказка для каждого ранга между целью и достигнутым: физику каких вещей отключить
            foreach (var rank in Ranks.Where(r => r >= target && r < plan.Achieved))
            {
                var limits = PbLimits.For(rank);
                if (limits == null) continue;
                plan.SuggestByRank[rank] = SuggestFor(kept, limits, ctx);
            }
            plan.SuggestDrop = plan.SuggestByRank.TryGetValue(target, out var forTarget) ? forTarget : new List<string>();
            return plan;
        }

        // Какие группы физики отключить, чтобы уложиться в лимиты, потеряв как можно меньше «видимой» физики.
        // Цена группы ≈ число её костей (чем больше двигается, тем заметнее потеря).
        static List<string> SuggestFor(List<PbInfo> kept, PbLimits limits, Ctx ctx)
        {
            var pool = kept.ToList();
            var dropped = new List<string>();
            int guard = 100;
            while (!Simulate(pool, limits, ctx, allowCuts: true).Fits && guard-- > 0)
            {
                var baseSim = Simulate(pool, limits, ctx, allowCuts: false);
                var items = pool.Select(p => p.Item).Distinct().ToList();
                if (items.Count == 0) break;
                string best = null; double bestScore = double.MinValue;
                foreach (var item in items)
                {
                    var rest = pool.Where(p => p.Item != item).ToList();
                    var s = Simulate(rest, limits, ctx, allowCuts: false);
                    int cost = pool.Where(p => p.Item == item).Sum(p => p.Effective.Count) + 1;
                    double score = baseSim.Components > limits.Components
                        ? (baseSim.Components - s.Components) / (double)cost      // сначала добиваемся числа компонентов
                        : (baseSim.Transforms - s.Transforms) / Math.Sqrt(cost);  // потом — трансформов
                    if (score > bestScore) { bestScore = score; best = item; }
                }
                pool = pool.Where(p => p.Item != best).ToList();
                dropped.Add(best);
            }
            // проход назад: возвращаем самые «ценные» группы, если ранг сохраняется
            foreach (var item in dropped.OrderByDescending(i => kept.Where(p => p.Item == i).Sum(p => p.Effective.Count)).ToList())
            {
                var withIt = pool.Concat(kept.Where(p => p.Item == item)).ToList();
                if (Simulate(withIt, limits, ctx, allowCuts: true).Fits) { pool = withIt; dropped.Remove(item); }
            }
            return dropped;
        }

        static Sim Simulate(List<PbInfo> kept, PbLimits limits, Ctx ctx, bool allowCuts)
        {
            var sim = new Sim { Merges = BuildGroups(kept, ctx) };
            Recount(sim, kept);
            if (!allowCuts || sim.Components > limits.Components)
            {
                sim.Fits = !allowCuts && sim.Components <= limits.Components && sim.Transforms <= limits.Transforms && sim.Checks <= limits.Checks;
                return sim;
            }

            if (sim.Transforms > limits.Transforms && ctx.Opt.TrimTips)
            {
                int guard = 5000;
                while (sim.Transforms > limits.Transforms && guard-- > 0)
                {
                    var best = kept.Select(p => (p, left: Left(sim, p)))
                        .Where(x => x.left.Count(ctx.Weighted.Contains) > MinWeightedLeft)
                        .OrderByDescending(x => x.left.Count).FirstOrDefault();
                    if (best.p == null) break;
                    var leaf = best.left.Where(t => t != best.p.Root && !t.Cast<Transform>().Any(best.left.Contains)).OrderByDescending(Depth).First();
                    if (!sim.Trims.TryGetValue(best.p, out var l)) sim.Trims[best.p] = l = new List<Transform>();
                    l.Add(leaf);
                    Recount(sim, kept);
                }
            }
            if (sim.Checks > limits.Checks && ctx.Opt.DropColliders)
            {
                // кандидаты — «единицы»: отдельный физбон или первый участник слияния (коллайдеры уйдут у всей группы)
                var units = kept.Where(p => { var g = GroupOf(sim, p); return g == null || g.Members[0] == p; })
                                .Where(p => ChecksOf(sim, p) > 0)
                                // сначала цепочки, где коллайдеров больше всего на кость (хвост с 15 коллайдерами),
                                // а не длинные с парой коллайдеров (юбка без них проваливается в ноги — это заметнее)
                                .OrderByDescending(p => ChecksOf(sim, p) / (double)UnitTransforms(sim, p)).ToList();
                foreach (var p in units)
                {
                    if (sim.Checks <= limits.Checks) break;
                    sim.Drops.Add(p);
                    Recount(sim, kept);
                }
            }
            sim.Fits = sim.Components <= limits.Components && sim.Transforms <= limits.Transforms && sim.Checks <= limits.Checks;
            return sim;
        }

        static List<MergeGroup> BuildGroups(List<PbInfo> kept, Ctx ctx)
        {
            var result = new List<MergeGroup>();
            foreach (var byParent in kept.Where(p => !p.Animated && !p.HasParameter && p.Parent != null).GroupBy(p => p.Parent))
            {
                var clusters = new List<MergeGroup>();
                foreach (var p in byParent)
                {
                    var same = clusters.FirstOrDefault(c => SameSettings(ctx, c.Members[0], p));
                    if (same != null) { same.Members.Add(p); continue; }
                    var sym = ctx.Opt.UnifySettings ? clusters.FirstOrDefault(c => c.Members.Any(m => m.SymKey == p.SymKey && !string.IsNullOrEmpty(p.SymKey))) : null;
                    if (sym != null) { sym.Members.Add(p); sym.NeedsUnify = true; continue; }
                    var g = new MergeGroup(); g.Members.Add(p); clusters.Add(g);
                }
                result.AddRange(clusters.Where(c => c.Members.Count > 1));
            }
            return result;
        }

        static bool SameSettings(Ctx ctx, PbInfo a, PbInfo b)
        {
            var key = (a.Pb, b.Pb);
            if (!ctx.Same.TryGetValue(key, out var v)) ctx.Same[key] = v = PhysBoneScan.SameSettings(a.Pb, b.Pb);
            return v;
        }

        static HashSet<Transform> Left(Sim sim, PbInfo p)
        {
            var set = new HashSet<Transform>(p.Effective);
            if (sim.Trims.TryGetValue(p, out var trims)) foreach (var t in trims) RemoveSubtree(set, t);
            return set;
        }

        static void RemoveSubtree(HashSet<Transform> set, Transform t)
        {
            set.Remove(t);
            foreach (Transform c in t) RemoveSubtree(set, c);
        }

        static MergeGroup GroupOf(Sim sim, PbInfo p) => sim.Merges.FirstOrDefault(g => g.Members.Contains(p));

        // Проверки столкновений «единицы» (отдельный физбон или целое слияние), в которую входит p.
        // Слитый физбон — одна цепочка: все кости + добавленные AAO, коллайдеры — первого участника
        // (при слиянии настройки, включая коллайдеры, копируются с него).
        static int ChecksOf(Sim sim, PbInfo p)
        {
            var g = GroupOf(sim, p);
            if (g == null) return sim.Drops.Contains(p) ? 0 : Math.Max(0, Left(sim, p).Count - 1) * p.Colliders;
            if (g.Members.Any(sim.Drops.Contains)) return 0;
            int transforms = g.Members.Sum(m => Left(sim, m).Count) + g.Members.Count + 1;
            return (transforms - 1) * g.Members[0].Colliders;
        }

        static int UnitTransforms(Sim sim, PbInfo p)
        {
            var g = GroupOf(sim, p);
            return Math.Max(1, g == null ? Left(sim, p).Count : g.Members.Sum(m => Left(sim, m).Count) + g.Members.Count + 1);
        }

        static void Recount(Sim sim, List<PbInfo> kept)
        {
            sim.Components = kept.Count - sim.Merges.Sum(g => g.Members.Count - 1);
            // слияние AAO добавляет по кости на цепочку и общий корень (проверено на TRXSH)
            sim.Transforms = kept.Sum(p => Left(sim, p).Count) + sim.Merges.Sum(g => g.Members.Count + 1);
            sim.Checks = kept.Where(p => GroupOf(sim, p) == null).Sum(p => ChecksOf(sim, p))
                       + sim.Merges.Sum(g => ChecksOf(sim, g.Members[0]));
        }

        static int Depth(Transform t) { int d = 0; for (var x = t; x != null; x = x.parent) d++; return d; }
    }
}
