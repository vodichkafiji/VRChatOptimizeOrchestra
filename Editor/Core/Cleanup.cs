using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TurboOptimizer
{
    // Уборка после отката: удаляет копии материалов и файлы бэкапов, которые плагин создал для аватара,
    // и пустые папки Assets/TurboOptimizer/…. Копию материала не трогаем, если на неё ещё ссылается
    // какой-то рендерер в открытых сценах (например, дубликат аватара с тем же именем).
    public static class Cleanup
    {
        const string Root = "Assets/TurboOptimizer";

        public static int Run(GameObject avatar, List<string> log)
        {
            int deleted = 0;
            var used = UsedMaterialPaths();

            // 1. копии материалов
            var folder = MaterialHarmonizer.FolderOf(avatar);
            if (AssetDatabase.IsValidFolder(folder))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (used.Contains(path)) continue;
                    if (AssetDatabase.DeleteAsset(path)) deleted++;
                }
                DeleteIfEmpty(folder);
            }

            // 2. бэкапы: список материалов — только если копий не осталось; список убранных объектов — чистим от вернувшихся
            var backups = $"{Root}/Backups/{AvatarUtil.SafeFileName(avatar.name)}";
            if (!AssetDatabase.IsValidFolder(folder)) deleted += DeleteFile($"{backups}/materials.txt");
            ItemScan.PruneRemovedList(avatar);
            DeleteIfEmpty(backups);

            // 3. пустые общие папки
            DeleteIfEmpty($"{Root}/Materials");
            DeleteIfEmpty($"{Root}/Backups");
            AssetDatabase.Refresh();
            if (deleted > 0) log?.Add(L.T($"Уборка: удалено файлов плагина {deleted} (копии материалов и бэкапы).", $"Cleanup: removed {deleted} plugin files (material copies and backups)."));
            return deleted;
        }

        static HashSet<string> UsedMaterialPaths()
        {
            var set = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                            if (m != null) set.Add(AssetDatabase.GetAssetPath(m));
            }
            return set;
        }

        public static int DeleteFile(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null) return AssetDatabase.DeleteAsset(assetPath) ? 1 : 0;
            if (!File.Exists(assetPath)) return 0;
            File.Delete(assetPath);
            if (File.Exists(assetPath + ".meta")) File.Delete(assetPath + ".meta");
            return 1;
        }

        static void DeleteIfEmpty(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any(e => !e.EndsWith(".meta")))
                AssetDatabase.DeleteAsset(folder);
        }
    }
}
