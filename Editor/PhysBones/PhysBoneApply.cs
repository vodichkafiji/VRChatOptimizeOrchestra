using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    // Применяет план физбонов. Всё одной группой Undo; изменения физбонов — обычные overrides
    // (Revert в инспекторе), слияния — компоненты AAO Merge PhysBone в объекте HolderName.
    public static class PhysBoneApply
    {
        public const string HolderName = "TurboOptimizer PhysBones";

        public static void Apply(GameObject avatar, PbPlan plan, List<string> log)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Orchestra PhysBones");
            int undoGroup = Undo.GetCurrentGroup();

            var mergeType = Reflect.FindType("Anatawa12.AvatarOptimizer.MergePhysBone");
            if (plan.Merges.Count > 0 && mergeType == null) { log.Add(L.T("! AAO не установлен — слияния пропущены", "! AAO isn't installed — merges skipped")); }

            // старые слияния пересобираем заново
            var old = avatar.transform.Find(HolderName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // 1. удаление
            foreach (var p in plan.Remove)
            {
                log.Add($"- {L.T("удалён физбон", "removed PhysBone")} {p.Name}");
                Undo.DestroyObjectImmediate(p.Pb);
            }

            // 2. коллайдеры (в группе слияния — у всех, иначе AAO не сольёт)
            var drop = new HashSet<PbInfo>(plan.DropColliders);
            foreach (var g in plan.Merges)
                if (g.Members.Any(drop.Contains)) foreach (var m in g.Members) drop.Add(m);
            foreach (var p in drop)
            {
                if (p.Pb == null) continue;
                var so = new SerializedObject(p.Pb);
                var c = so.FindProperty("colliders");
                if (c == null || c.arraySize == 0) continue;
                c.arraySize = 0;
                so.ApplyModifiedProperties();
                log.Add($"  {p.Name}: {L.T("коллайдеры убраны", "colliders removed")}");
            }

            // 3. жёсткие кончики
            foreach (var kv in plan.Trims)
            {
                if (kv.Key.Pb == null) continue;
                var so = new SerializedObject(kv.Key.Pb);
                var ign = so.FindProperty("ignoreTransforms");
                var set = PhysBoneScan.Ignored(so);
                foreach (var t in kv.Value) set.Add(t);
                var top = set.Where(t => !set.Contains(t.parent)).ToList();
                ign.arraySize = top.Count;
                for (int i = 0; i < top.Count; i++) ign.GetArrayElementAtIndex(i).objectReferenceValue = top[i];
                so.ApplyModifiedProperties();
                log.Add($"  {kv.Key.Name}: {kv.Value.Count} {L.T("кончиков стали жёсткими", "tips made stiff")}");
            }

            // 4. слияния
            if (mergeType != null && plan.Merges.Count > 0)
            {
                var holder = new GameObject(HolderName);
                Undo.RegisterCreatedObjectUndo(holder, "Orchestra PhysBones");
                holder.transform.SetParent(avatar.transform, false);
                int i = 0;
                foreach (var g in plan.Merges)
                {
                    var members = g.Members.Where(m => m.Pb != null).ToList();
                    if (members.Count < 2) continue;
                    var first = new SerializedObject(members[0].Pb);
                    foreach (var m in members.Skip(1))
                    {
                        var so = new SerializedObject(m.Pb);
                        var it = first.GetIterator();
                        bool enter = true;
                        while (it.NextVisible(enter))
                        {
                            enter = false;
                            if (PhysBoneScan.NotSettings.Contains(it.name)) continue;
                            var other = so.FindProperty(it.propertyPath);
                            if (other != null && !SerializedProperty.DataEquals(it, other)) so.CopyFromSerializedProperty(it);
                        }
                        so.ApplyModifiedProperties();
                    }
                    foreach (var m in members)
                    {
                        var so = new SerializedObject(m.Pb);
                        var mct = so.FindProperty("multiChildType");
                        if (mct != null && mct.enumValueIndex != 0) { mct.enumValueIndex = 0; so.ApplyModifiedProperties(); } // 0 = Ignore
                    }

                    var go = new GameObject($"Merge {++i}: {members[0].Parent.name}");
                    Undo.RegisterCreatedObjectUndo(go, "Orchestra PhysBones");
                    go.transform.SetParent(holder.transform, false);
                    var merge = Undo.AddComponent(go, mergeType);
                    var acc = mergeType.GetProperty("PhysBones").GetValue(merge);
                    var add = acc.GetType().GetMethod("Add");
                    foreach (var m in members) add.Invoke(acc, new object[] { m.Pb });
                    EditorUtility.SetDirty(merge);
                    log.Add($"+ {L.T("слияние", "merge")} {g.Name}{(g.NeedsUnify ? L.T(" (настройки выровнены)", " (settings aligned)") : "")}");
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(avatar.scene);
        }
    }
}
