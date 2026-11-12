using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    public enum ItemKind { Slot, Object, Socket, Contacts }

    // «Вещь» аватара, которую можно убрать: часть меша (слот материала), объект, SPS-сокет или группа контактов.
    public class AvatarItem
    {
        public string Name;
        public ItemKind Kind;
        public Renderer Renderer;
        public int Slot = -1;
        public GameObject Object;
        public SpsLever.SocketInfo Socket;
        public SpsLever.ContactGroup Group;
        public bool Protected;       // тело, лицо, глаза, волосы — не советуем
        public bool Removed;         // уже убрано нами
        public bool InMenu;          // включается/выключается из меню
        public bool HiddenByDefault; // по умолчанию выключено
        public Dictionary<string, double> Cost = new Dictionary<string, double>(); // категория SDK → насколько уменьшится
        public HashSet<Transform> OwnedBones = new HashSet<Transform>();
        public List<PbInfo> OwnedPbs = new List<PbInfo>();

        public string KindText => Kind switch
        {
            ItemKind.Slot => L.T("часть меша", "mesh part"),
            ItemKind.Object => L.T("объект", "object"),
            ItemKind.Socket => L.T("SPS-сокет", "SPS socket"),
            _ => L.T("контакты", "contacts"),
        };

        public string CostText()
        {
            var parts = new List<string>();
            foreach (var kv in Cost.Where(k => k.Value > 0.05).OrderByDescending(k => k.Value / Math.Max(1, ItemScan.Scale(k.Key))))
                parts.Add($"{L.Cat(kv.Key)} −{PerfReport.Fmt(Math.Round(kv.Value, 1))}");
            return parts.Count == 0 ? L.T("почти ничего не даёт", "gives almost nothing") : string.Join(", ", parts);
        }
    }

    public static class ItemScan
    {
        public const string MaskPath = "Assets/TurboOptimizer/RemoveAll.asset";
        static string ListFile(GameObject avatar) => $"Assets/TurboOptimizer/Backups/{Safe(avatar.name)}/removed_objects.txt";

        // ---------- названия категорий по-русски ----------
        static readonly Dictionary<string, string> RuNames = new Dictionary<string, string>
        {
            ["PolyCount"] = "полигоны", ["MaterialCount"] = "материалы", ["TextureMegabytes"] = "текстуры МБ",
            ["PhysBoneComponentCount"] = "физбоны", ["PhysBoneTransformCount"] = "кости физбонов",
            ["PhysBoneColliderCount"] = "коллайдеры физбонов", ["PhysBoneCollisionCheckCount"] = "проверки столкновений",
            ["ContactCount"] = "контакты", ["BoneCount"] = "кости", ["SkinnedMeshCount"] = "меши", ["MeshCount"] = "простые меши",
            ["LightCount"] = "свет", ["AudioSourceCount"] = "звуки", ["ParticleSystemCount"] = "системы частиц",
            ["ParticleTotalCount"] = "частицы", ["TrailRendererCount"] = "следы", ["LineRendererCount"] = "линии",
            ["ClothCount"] = "ткань", ["ConstraintsCount"] = "констрейнты",
        };
        public static string Ru(string cat) => RuNames.TryGetValue(cat, out var s) ? s : cat;

        // чтобы сравнивать вклад в разные категории: 1 материал ≈ 1000 полигонов и т.д.
        public static double Scale(string cat) => cat switch
        {
            "PolyCount" => 2000, "PhysBoneTransformCount" => 8, "PhysBoneCollisionCheckCount" => 32, "BoneCount" => 20,
            "TextureMegabytes" => 4, "ParticleTotalCount" => 50, _ => 1,
        };

        static readonly System.Text.RegularExpressions.Regex ProtectedName = new System.Text.RegularExpressions.Regex(
            @"(body|face|head|eye|skin|mouth|teeth|tongue|brow|lash|hair|тело|лицо|глаз|волос)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // ---------- скан ----------
        public static List<AvatarItem> Scan(GameObject avatar, PerfReport report)
        {
            var items = new List<AvatarItem>();
            var face = AvatarUtil.FaceRenderer(avatar);
            var toggled = ToggledPaths(avatar);
            var votes = BoneVotes(avatar);           // кость → (ключ вещи → вершин)
            var pbs = PhysBoneScan.Scan(avatar);
            var singleton = SingletonMaterials(report);

            // 1. меши с несколькими материалами — по слотам
            var renderers = avatar.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && !AvatarUtil.IsEditorOnly(r.transform) && AvatarUtil.GetMesh(r) != null).ToList();
            var multi = new HashSet<Renderer>(renderers.Where(r => r.sharedMaterials.Length > 1 && r is SkinnedMeshRenderer));
            foreach (var r in multi)
            {
                var removed = RemovedSlots(r);
                var mesh = AvatarUtil.GetMesh(r);
                for (int i = 0; i < r.sharedMaterials.Length && i < mesh.subMeshCount; i++)
                {
                    var mat = r.sharedMaterials[i];
                    var it = new AvatarItem { Kind = ItemKind.Slot, Renderer = r, Slot = i, Name = $"{(mat != null ? mat.name : L.T("слот ", "slot ") + i)} ({r.name})" };
                    it.Removed = removed.Contains(i);
                    it.Protected = r == face && (mat == null || ProtectedName.IsMatch(mat.name)) || (mat != null && ProtectedName.IsMatch(mat.name));
                    it.Cost["PolyCount"] = mesh.GetTopology(i) == MeshTopology.Triangles ? mesh.GetIndexCount(i) / 3 : 0;
                    if (mat != null && singleton.Contains(mat.name) && !r.sharedMaterials.Where((m, j) => j != i).Contains(mat)) it.Cost["MaterialCount"] = 1;
                    it.Cost["TextureMegabytes"] = UniqueTextureMb(new[] { mat }, renderers, r, i);
                    AddOwned(it, Key(r, i), votes, pbs, avatar);
                    items.Add(it);
                }
            }

            // 2. объекты: включаемые из меню и отдельные меши (без вложенных друг в друга)
            var candidates = new HashSet<GameObject>();
            foreach (var path in toggled)
            {
                var t = avatar.transform.Find(path);
                if (t != null && t != avatar.transform && !AvatarUtil.IsEditorOnly(t)) candidates.Add(t.gameObject);
            }
            foreach (var r in renderers.Where(r => !multi.Contains(r))) candidates.Add(r.gameObject);
            var skinned = renderers.OfType<SkinnedMeshRenderer>().ToList();
            candidates.RemoveWhere(go =>
                go.GetComponentsInChildren<Renderer>(true).Any(r => multi.Contains(r) || r == face)   // целиком тело/одежду не предлагаем
                || go.name == "Armature" || go.transform.parent == null
                // кость чужого меша (например, корень физбона волос или хвоста): EditorOnly сломал бы меш
                || skinned.Any(r => !r.transform.IsChildOf(go.transform) && r.bones.Any(b => b != null && b.IsChildOf(go.transform))));
            var top = candidates.Where(go => !candidates.Any(o => o != go && go.transform.IsChildOf(o.transform))).ToList();
            foreach (var go in top)
            {
                var it = new AvatarItem { Kind = ItemKind.Object, Object = go, Name = go.name };
                it.InMenu = toggled.Contains(AnimationUtility.CalculateTransformPath(go.transform, avatar.transform));
                it.HiddenByDefault = !go.activeSelf;
                it.Protected = ProtectedName.IsMatch(go.name);
                var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => r is SkinnedMeshRenderer || r is MeshRenderer).ToList();
                it.Cost["PolyCount"] = rs.Sum(r => AvatarUtil.Triangles(AvatarUtil.GetMesh(r)));
                it.Cost["MaterialCount"] = rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count(m => singleton.Contains(m.name));
                it.Cost["TextureMegabytes"] = UniqueTextureMb(rs.SelectMany(r => r.sharedMaterials).ToArray(), renderers, null, -1, go);
                it.Cost["ParticleSystemCount"] = go.GetComponentsInChildren<ParticleSystem>(true).Length;
                it.Cost["AudioSourceCount"] = go.GetComponentsInChildren<AudioSource>(true).Length;
                it.Cost["LightCount"] = go.GetComponentsInChildren<Light>(true).Length;
                it.Cost["SkinnedMeshCount"] = 0; // d4rk всё равно склеит
                foreach (var r in rs) AddOwned(it, Key(r, -1), votes, pbs, avatar, sumAllSlots: true);
                // физбоны и контакты внутри самого объекта
                var inner = pbs.Where(p => p.Pb.transform.IsChildOf(go.transform)).ToList();
                foreach (var p in inner.Where(p => !it.OwnedPbs.Contains(p))) it.OwnedPbs.Add(p);
                RecountPb(it);
                it.Cost["ContactCount"] = (it.Cost.TryGetValue("ContactCount", out var c0) ? c0 : 0)
                    + go.GetComponentsInChildren<Component>(true).Count(x => x != null && (x.GetType().Name == "VRCContactSender" || x.GetType().Name == "VRCContactReceiver"));
                items.Add(it);
            }
            foreach (var path in ReadList(avatar))
            {
                var t = avatar.transform.Find(path);
                if (t != null && t.CompareTag("EditorOnly")) items.Add(new AvatarItem { Kind = ItemKind.Object, Object = t.gameObject, Name = t.name, Removed = true });
            }

            // 3. SPS-сокеты и группы контактов
            foreach (var s in SpsLever.FindSockets(avatar))
            {
                var it = new AvatarItem { Kind = ItemKind.Socket, Socket = s, Name = "SPS " + s.Name };
                if (report?.Sources != null && report.Sources.TryGetValue("SPS сокет: " + s.Socket.gameObject.name, out var v))
                { it.Cost["ContactCount"] = v.contacts; it.Cost["LightCount"] = v.lights; }
                else it.Cost["ContactCount"] = 5;
                items.Add(it);
            }
            foreach (var g in SpsLever.FindContactGroups(avatar, report))
            {
                // контакты внутри объектов-вещей уже посчитаны у них
                if (g.Comps.All(c => top.Any(o => c.transform.IsChildOf(o.transform)))) continue;
                items.Add(new AvatarItem { Kind = ItemKind.Contacts, Group = g, Name = L.T("Контакты: ", "Contacts: ") + g.Name, Cost = { ["ContactCount"] = g.Contacts } });
            }
            return items;
        }

        // ---------- совет: что убрать, чтобы дойти до цели ----------
        public class Advice
        {
            public List<AvatarItem> Pick = new List<AvatarItem>();
            public Dictionary<string, double> Need = new Dictionary<string, double>();     // сколько не хватало
            public Dictionary<string, double> Left = new Dictionary<string, double>();     // сколько не покрыли вещами
        }

        public static Advice Recommend(List<AvatarItem> items, PerfReport report, Rank target)
        {
            var adv = new Advice();
            var limits = Thresholds.Load(target, false);
            if (report == null || limits == null) return adv;
            foreach (var row in report.Rows.Where(r => r.Rating > target && r.Value != null))
            {
                var lim = limits.Get(PerfStats.FieldPath(row.Category));
                if (lim == null) continue;
                var need = row.Value.Value - lim.Value;
                if (need > 0) adv.Need[row.Category] = need;
            }
            var left = new Dictionary<string, double>(adv.Need);
            var pool = items.Where(i => !i.Protected && !i.Removed).ToList();
            while (left.Values.Any(v => v > 0) && pool.Count > 0)
            {
                AvatarItem best = null; double bestScore = 0;
                foreach (var it in pool)
                {
                    double gain = 0;
                    foreach (var kv in left.Where(k => k.Value > 0))
                        if (it.Cost.TryGetValue(kv.Key, out var c) && c > 0) gain += Math.Min(c, kv.Value) / Scale(kv.Key);
                    if (gain <= 0) continue;
                    // заметность: выключенное по умолчанию и контакты — «дешевле»; большая видимая вещь — «дороже»
                    double visible = it.HiddenByDefault ? 0.3 : it.Kind == ItemKind.Contacts ? 0.5 : it.Kind == ItemKind.Socket ? 0.8 : 1.0;
                    visible *= 1 + (it.Cost.TryGetValue("PolyCount", out var p) ? p : 0) / 10000.0;
                    var score = gain / visible;
                    if (score > bestScore) { bestScore = score; best = it; }
                }
                if (best == null) break;
                adv.Pick.Add(best);
                pool.Remove(best);
                foreach (var k in left.Keys.ToList()) if (best.Cost.TryGetValue(k, out var c)) left[k] -= c;
            }
            // проход назад: вернуть то, без чего цель всё равно достигается
            foreach (var it in adv.Pick.ToList())
            {
                bool needed = it.Cost.Any(kv => left.TryGetValue(kv.Key, out var l) && kv.Value > 0 && l + kv.Value > 0);
                if (!needed) { adv.Pick.Remove(it); foreach (var kv in it.Cost) if (left.ContainsKey(kv.Key)) left[kv.Key] += kv.Value; }
            }
            foreach (var kv in left.Where(k => k.Value > 0)) adv.Left[kv.Key] = kv.Value;
            return adv;
        }

        // ---------- применить / вернуть ----------
        public static void Remove(GameObject avatar, AvatarItem it, List<string> log)
        {
            switch (it.Kind)
            {
                case ItemKind.Slot:
                    SetSlotRemoved(it.Renderer, it.Slot, true);
                    log.Add($"- {L.T("убрана часть меша", "removed mesh part")} «{it.Name}» (AAO Remove Mesh By Mask)");
                    break;
                case ItemKind.Object:
                    Undo.RecordObject(it.Object, "Orchestra items");
                    it.Object.tag = "EditorOnly";
                    var list = ReadList(avatar);
                    var path = AnimationUtility.CalculateTransformPath(it.Object.transform, avatar.transform);
                    if (!list.Contains(path)) { list.Add(path); WriteList(avatar, list); }
                    log.Add($"- {L.T("убран объект", "removed object")} «{it.Name}» (EditorOnly)");
                    break;
                case ItemKind.Socket: SpsLever.DisableSocket(it.Socket, log); break;
                case ItemKind.Contacts: SpsLever.DisableContactGroup(it.Group, log); break;
            }
        }

        public static void Restore(GameObject avatar, AvatarItem it, List<string> log)
        {
            if (it.Kind == ItemKind.Slot) { SetSlotRemoved(it.Renderer, it.Slot, false); log.Add($"+ {L.T("возвращена часть меша", "restored mesh part")} «{it.Name}»"); }
            else if (it.Kind == ItemKind.Object && it.Object != null)
            {
                Undo.RecordObject(it.Object, "Orchestra items");
                it.Object.tag = "Untagged";
                var list = ReadList(avatar);
                list.Remove(AnimationUtility.CalculateTransformPath(it.Object.transform, avatar.transform));
                WriteList(avatar, list);
                log.Add($"+ {L.T("возвращён объект", "restored object")} «{it.Name}»");
            }
        }

        static void SetSlotRemoved(Renderer r, int slot, bool removed)
        {
            var type = Reflect.FindType("Anatawa12.AvatarOptimizer.RemoveMeshByMask");
            if (type == null) throw new Exception(L.T("Нужен AAO (Remove Mesh By Mask)", "AAO is required (Remove Mesh By Mask)"));
            var comp = r.GetComponent(type);
            if (comp == null)
            {
                if (!removed) return;
                comp = Undo.AddComponent(r.gameObject, type);
            }
            var so = new SerializedObject(comp);
            var arr = so.FindProperty("materials");
            int n = r.sharedMaterials.Length;
            if (arr.arraySize < n) arr.arraySize = n;
            var el = arr.GetArrayElementAtIndex(slot);
            el.FindPropertyRelative("enabled").boolValue = removed;
            if (removed)
            {
                el.FindPropertyRelative("mask").objectReferenceValue = BlackMask();
                el.FindPropertyRelative("mode").enumValueIndex = 0; // RemoveBlack
            }
            so.ApplyModifiedProperties();
        }

        // Какие слоты меша уже убраны нашей маской.
        public static HashSet<int> RemovedSlots(Renderer r)
        {
            var set = new HashSet<int>();
            var type = Reflect.FindType("Anatawa12.AvatarOptimizer.RemoveMeshByMask");
            var comp = type == null ? null : r.GetComponent(type);
            if (comp == null) return set;
            var arr = new SerializedObject(comp).FindProperty("materials");
            for (int i = 0; arr != null && i < arr.arraySize; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                var mask = el.FindPropertyRelative("mask").objectReferenceValue;
                if (el.FindPropertyRelative("enabled").boolValue && mask != null && AssetDatabase.GetAssetPath(mask) == MaskPath) set.Add(i);
            }
            return set;
        }

        // Вершины, которые остаются (null — ничего не убрано).
        public static bool[] KeptVertices(SkinnedMeshRenderer smr)
        {
            var removed = RemovedSlots(smr);
            var mesh = smr.sharedMesh;
            if (removed.Count == 0 || mesh == null) return null;
            var kept = new bool[mesh.vertexCount];
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (removed.Contains(i)) continue;
                foreach (var v in mesh.GetIndices(i)) kept[v] = true;
            }
            return kept;
        }

        static Texture2D BlackMask()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
            if (tex != null) return tex;
            Directory.CreateDirectory(Path.GetDirectoryName(MaskPath));
            tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "TurboOptimizer RemoveAll" };
            tex.SetPixels(Enumerable.Repeat(Color.black, 16).ToArray());
            tex.Apply();
            AssetDatabase.CreateAsset(tex, MaskPath);
            return tex;
        }

        // ---------- вклад в физбоны, кости и контакты ----------
        static string Key(Renderer r, int slot) => $"{r.GetInstanceID()}#{slot}";

        // кость → ключ вещи → число вершин (ключ «renderer#slot», для MeshRenderer — «renderer#-1»)
        static Dictionary<Transform, Dictionary<string, int>> BoneVotes(GameObject avatar)
        {
            var map = new Dictionary<Transform, Dictionary<string, int>>();
            void Add(Transform t, string key, int n)
            {
                if (t == null) return;
                if (!map.TryGetValue(t, out var d)) map[t] = d = new Dictionary<string, int>();
                d[key] = (d.TryGetValue(key, out var v) ? v : 0) + n;
            }
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (AvatarUtil.IsEditorOnly(r.transform)) continue;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    var mesh = smr.sharedMesh;
                    var bones = smr.bones;
                    var perVertex = mesh.GetBonesPerVertex();
                    var weights = mesh.GetAllBoneWeights();
                    var start = new int[perVertex.Length];
                    for (int v = 1; v < perVertex.Length; v++) start[v] = start[v - 1] + perVertex[v - 1];
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var key = Key(r, s);
                        var counted = new Dictionary<int, int>();
                        foreach (var v in new HashSet<int>(mesh.GetIndices(s)))
                            for (int k = 0; k < perVertex[v]; k++)
                            {
                                var w = weights[start[v] + k];
                                if (w.weight > 0.0001f && w.boneIndex < bones.Length) counted[w.boneIndex] = (counted.TryGetValue(w.boneIndex, out var c) ? c : 0) + 1;
                            }
                        foreach (var kv in counted) Add(bones[kv.Key], key, kv.Value);
                    }
                }
                else if (r is MeshRenderer)
                {
                    Add(r.transform, Key(r, -1), 100);
                    Add(r.transform.parent, Key(r, -1), 100);
                }
            }
            return map;
        }

        static void AddOwned(AvatarItem it, string key, Dictionary<Transform, Dictionary<string, int>> votes, List<PbInfo> pbs, GameObject avatar, bool sumAllSlots = false)
        {
            bool Mine(string k) => sumAllSlots ? k.StartsWith(key.Substring(0, key.IndexOf('#') + 1)) : k == key;
            // кости, которые двигают только эту вещь
            var owned = votes.Where(kv => kv.Value.Keys.Any(Mine) && kv.Value.Keys.All(Mine)).Select(kv => kv.Key).ToList();
            it.OwnedBones.UnionWith(owned);
            it.Cost["BoneCount"] = (it.Cost.TryGetValue("BoneCount", out var b) ? b : 0) + owned.Count;
            // физбоны, у которых ≥90% веса — на этой вещи
            foreach (var p in pbs)
            {
                int mine = 0, all = 0;
                foreach (var t in p.Effective)
                    if (votes.TryGetValue(t, out var d))
                        foreach (var kv in d) { all += kv.Value; if (Mine(kv.Key)) mine += kv.Value; }
                if (all > 0 && mine >= all * 0.9 && !it.OwnedPbs.Contains(p)) it.OwnedPbs.Add(p);
            }
            RecountPb(it);
            // контакты на собственных костях вещи
            int contacts = 0;
            foreach (var t in owned)
                foreach (var c in t.GetComponents<Component>())
                    if (c != null && (c.GetType().Name == "VRCContactSender" || c.GetType().Name == "VRCContactReceiver")) contacts++;
            it.Cost["ContactCount"] = (it.Cost.TryGetValue("ContactCount", out var c0) ? c0 : 0) + contacts;
        }

        static void RecountPb(AvatarItem it)
        {
            it.Cost["PhysBoneComponentCount"] = it.OwnedPbs.Count;
            it.Cost["PhysBoneTransformCount"] = it.OwnedPbs.Sum(p => p.Effective.Count);
            it.Cost["PhysBoneCollisionCheckCount"] = it.OwnedPbs.Sum(p => p.Checks);
        }

        // материалы, которые после склейки d4rk остаются в отдельном слоте: убрать вещь = −1 материал
        static HashSet<string> SingletonMaterials(PerfReport report)
        {
            var set = new HashSet<string>();
            if (report?.MaterialGroups == null) return set;
            foreach (var g in report.MaterialGroups) if (g.Count == 1) set.Add(g[0]);
            return set;
        }

        static double UniqueTextureMb(Material[] mats, List<Renderer> all, Renderer skipR, int skipSlot, GameObject skipObj = null)
        {
            var mine = new HashSet<Texture>();
            foreach (var m in mats) if (m != null) foreach (var p in m.GetTexturePropertyNames()) { var t = m.GetTexture(p); if (t != null) mine.Add(t); }
            // убираем текстуры, которые нужны другим вещам
            foreach (var r in all)
            {
                if (skipObj != null && r.transform.IsChildOf(skipObj.transform)) continue;
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    if (r == skipR && i == skipSlot) continue;
                    if (ms[i] == null || mats.Contains(ms[i]) && r != skipR) continue;
                    foreach (var p in ms[i].GetTexturePropertyNames()) { var t = ms[i].GetTexture(p); if (t != null) mine.Remove(t); }
                }
            }
            double mb = 0;
            foreach (var t in mine) mb += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t) / (1024.0 * 1024.0);
            return mb;
        }

        // пути объектов, которые включаются/выключаются анимациями FX (переключатели меню)
        static HashSet<string> ToggledPaths(GameObject avatar)
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
                        if (clip != null)
                            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                                if (b.propertyName == "m_IsActive" && b.type == typeof(GameObject)) set.Add(b.path);
                }
            }
            return set;
        }

        static List<string> ReadList(GameObject avatar) => File.Exists(ListFile(avatar)) ? File.ReadAllLines(ListFile(avatar)).Where(s => s.Length > 0).ToList() : new List<string>();
        static void WriteList(GameObject avatar, List<string> list)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ListFile(avatar)));
            File.WriteAllLines(ListFile(avatar), list);
        }
        static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
