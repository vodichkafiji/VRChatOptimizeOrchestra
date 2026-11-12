using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    // Уменьшает текстуры по ролям через Standalone-override импорта (с бэкапом).
    // MVP: меняет импорт исходных текстур. В v2 — копии текстур только для этого аватара.
    public static class TextureLever
    {
        enum Role { Face, Albedo, Normal, Mask }

        // Уровни сжатия: 0 — базовый, 1 — normal и маски меньше, 2 — и основные текстуры меньше.
        static readonly Dictionary<Role, int>[] Levels =
        {
            new Dictionary<Role, int> { [Role.Face] = 2048, [Role.Albedo] = 1024, [Role.Normal] = 1024, [Role.Mask] = 512 },
            new Dictionary<Role, int> { [Role.Face] = 2048, [Role.Albedo] = 1024, [Role.Normal] = 512, [Role.Mask] = 256 },
            new Dictionary<Role, int> { [Role.Face] = 1024, [Role.Albedo] = 512, [Role.Normal] = 512, [Role.Mask] = 256 },
        };
        public static int MaxLevel => Levels.Length - 1;
        static Dictionary<Role, int> Size = Levels[0];

        public static string BackupPath(GameObject avatar) => $"Assets/TurboOptimizer/Backups/{Safe(avatar.name)}/textures.txt";

        public static void Apply(GameObject avatar, List<string> log, int level = 0)
        {
            Size = Levels[Mathf.Clamp(level, 0, MaxLevel)];
            var face = AvatarUtil.FaceRenderer(avatar);
            var plan = new Dictionary<string, Role>();
            foreach (var r in AvatarUtil.MeshRenderers(avatar).Concat(avatar.GetComponentsInChildren<Renderer>(true).Where(x => !(x is SkinnedMeshRenderer || x is MeshRenderer))))
            {
                if (AvatarUtil.IsEditorOnly(r.transform)) continue;
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    foreach (var prop in mat.GetTexturePropertyNames())
                    {
                        var tex = mat.GetTexture(prop);
                        if (tex == null) continue;
                        var path = AssetDatabase.GetAssetPath(tex);
                        if (!path.StartsWith("Assets/")) continue; // текстуры из пакетов не трогаем
                        var role = RoleOf(prop, path, r == face);
                        // одна текстура в нескольких ролях — берём самую требовательную
                        if (!plan.TryGetValue(path, out var prev) || Size[role] > Size[prev]) plan[path] = role;
                    }
                }
            }

            var backupFile = BackupPath(avatar);
            var backup = ReadBackup(backupFile);
            int changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var kv in plan)
                {
                    var ti = AssetImporter.GetAtPath(kv.Key) as TextureImporter;
                    if (ti == null) continue;
                    var ps = ti.GetPlatformTextureSettings("Standalone");
                    int current = ps.overridden ? ps.maxTextureSize : ti.maxTextureSize;
                    int target = Size[kv.Value];
                    if (current <= target) continue;
                    if (!backup.ContainsKey(kv.Key)) backup[kv.Key] = $"{ps.overridden}|{ps.maxTextureSize}|{(int)ps.format}";
                    bool wasOverridden = ps.overridden;
                    ps.overridden = true;
                    ps.maxTextureSize = target;
                    if (!wasOverridden) ps.format = TextureImporterFormat.Automatic;
                    ti.SetPlatformTextureSettings(ps);
                    ti.SaveAndReimport();
                    changed++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            WriteBackup(backupFile, backup);
            log.Add(changed > 0 ? L.T($"+ Текстуры (уровень {level}): уменьшено {changed} (лицо {Size[Role.Face]}, основные {Size[Role.Albedo]}, normal {Size[Role.Normal]}, маски {Size[Role.Mask]}). Бэкап: {backupFile}", $"+ Textures (level {level}): reduced {changed} (face {Size[Role.Face]}, main {Size[Role.Albedo]}, normal {Size[Role.Normal]}, masks {Size[Role.Mask]}). Backup: {backupFile}")
                                : L.T("= Текстуры уже в нужных размерах", "= Textures are already the right size"));
        }

        // Режим «Good любой ценой»: у текстур одного свойства в группе склеиваемых материалов — одинаковые размер и формат,
        // чтобы d4rk сложил их в общий массив текстур (Merge MainTex и др.). Нормали получают тип Normal map.
        // Размер выбирается так, чтобы подошло как можно больше текстур; больше текущей настройки не делаем.
        public static void Unify(GameObject avatar, List<Material> group, List<string> log)
        {
            var backupFile = BackupPath(avatar);
            var backup = ReadBackup(backupFile);
            int changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var prop in group.SelectMany(m => m.GetTexturePropertyNames()).Distinct().ToList())
                {
                    var items = group.Where(m => m.HasProperty(prop)).Select(m => m.GetTexture(prop) as Texture2D).Where(t => t != null).Distinct()
                        .Select(t => (path: AssetDatabase.GetAssetPath(t), ti: AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter))
                        .Where(x => x.ti != null && x.path.StartsWith("Assets/")).ToList();
                    if (items.Count < 2) continue;
                    var lp = prop.ToLowerInvariant();
                    bool normal = lp.Contains("bump") || lp.Contains("normal") || items.Any(x => x.ti.textureType == TextureImporterType.NormalMap);
                    var src = items.Select(x => { x.ti.GetSourceTextureWidthAndHeight(out int w, out int h); return (x.path, x.ti, w, h); }).ToList();
                    foreach (var byAspect in src.GroupBy(x => Mathf.RoundToInt(100f * x.w / Mathf.Max(1, x.h))))
                    {
                        var list = byAspect.ToList();
                        if (list.Count < 2) continue;
                        int cap = list.Max(x => CurrentMax(x.ti));
                        int best = 0, bestCount = 0;
                        foreach (var size in new[] { 2048, 1024, 512, 256 })
                        {
                            if (size > cap) continue;
                            int c = list.Count(x => Mathf.Max(x.w, x.h) >= size);
                            if (c > bestCount) { best = size; bestCount = c; }
                        }
                        if (bestCount < 2) continue;
                        bool alpha = !normal && list.Any(x => x.ti.DoesSourceTextureHaveAlpha() && x.ti.alphaSource != TextureImporterAlphaSource.None);
                        var fmt = normal || alpha ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1;
                        foreach (var x in list.Where(x => Mathf.Max(x.w, x.h) >= best))
                        {
                            var ps = x.ti.GetPlatformTextureSettings("Standalone");
                            bool typeFix = normal && x.ti.textureType != TextureImporterType.NormalMap;
                            if (ps.overridden && ps.maxTextureSize == best && ps.format == fmt && !typeFix) continue;
                            if (!backup.TryGetValue(x.path, out var b)) backup[x.path] = $"{ps.overridden}|{ps.maxTextureSize}|{(int)ps.format}|{(int)x.ti.textureType}";
                            else if (b.Split('|').Length == 3) backup[x.path] = b + "|" + (int)x.ti.textureType;
                            ps.overridden = true;
                            ps.maxTextureSize = best;
                            ps.format = fmt;
                            x.ti.SetPlatformTextureSettings(ps);
                            if (typeFix) x.ti.textureType = TextureImporterType.NormalMap;
                            x.ti.SaveAndReimport();
                            changed++;
                        }
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            WriteBackup(backupFile, backup);
            if (changed > 0) log.Add(L.T($"+ Текстуры выровнены для склейки: {changed} (одинаковые размер и формат)", $"+ Textures aligned for merging: {changed} (same size and format)"));
        }

        static int CurrentMax(TextureImporter ti)
        {
            var ps = ti.GetPlatformTextureSettings("Standalone");
            return ps.overridden ? ps.maxTextureSize : ti.maxTextureSize;
        }

        public static int Restore(GameObject avatar)
        {
            var file = BackupPath(avatar);
            var backup = ReadBackup(file);
            foreach (var kv in backup)
            {
                var ti = AssetImporter.GetAtPath(kv.Key) as TextureImporter;
                if (ti == null) continue;
                var f = kv.Value.Split('|');
                var ps = ti.GetPlatformTextureSettings("Standalone");
                ps.overridden = bool.Parse(f[0]);
                ps.maxTextureSize = int.Parse(f[1]);
                ps.format = (TextureImporterFormat)int.Parse(f[2]);
                ti.SetPlatformTextureSettings(ps);
                if (f.Length > 3) ti.textureType = (TextureImporterType)int.Parse(f[3]);
                ti.SaveAndReimport();
            }
            if (File.Exists(file)) File.Delete(file);
            AssetDatabase.Refresh();
            return backup.Count;
        }

        static Role RoleOf(string prop, string path, bool onFace)
        {
            var p = prop.ToLowerInvariant();
            var f = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if ((ti != null && ti.textureType == TextureImporterType.NormalMap) || p.Contains("bump") || p.Contains("normal")) return Role.Normal;
            if (p == "_maintex" || p == "_basemap" || p == "_basecolormap") return onFace ? Role.Face : Role.Albedo;
            if (p.Contains("mask") || p.Contains("metallic") || p.Contains("occlusion") || p.Contains("matcap") || p.Contains("noise")
                || p.Contains("gradient") || p.Contains("ramp") || f.Contains("_ao") || f.Contains("metallic")) return Role.Mask;
            return Role.Albedo;
        }

        static Dictionary<string, string> ReadBackup(string file)
        {
            var d = new Dictionary<string, string>();
            if (!File.Exists(file)) return d;
            foreach (var line in File.ReadAllLines(file))
            {
                int i = line.IndexOf('\t');
                if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
            }
            return d;
        }

        static void WriteBackup(string file, Dictionary<string, string> d)
        {
            if (d.Count == 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllLines(file, d.Select(kv => kv.Key + "\t" + kv.Value));
        }

        static string Safe(string s) => AvatarUtil.SafeFileName(s);
    }
}
