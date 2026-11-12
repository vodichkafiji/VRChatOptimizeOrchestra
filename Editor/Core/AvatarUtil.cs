using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TurboOptimizer
{
    public static class AvatarUtil
    {
        public static bool IsAvatar(GameObject go) =>
            go != null && go.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "VRCAvatarDescriptor");

        public static Component Descriptor(GameObject go) =>
            go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "VRCAvatarDescriptor");

        static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                                              "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

        // Имя, которое Windows примет как имя папки/файла: без запрещённых символов, без пробелов и точек на краях
        // (Windows молча срезает их, и Unity потом не находит путь), не зарезервированное.
        public static string SafeFileName(string s)
        {
            var bad = System.IO.Path.GetInvalidFileNameChars();
            var r = string.Concat((s ?? "").Select(c => bad.Contains(c) || c < 32 ? '_' : c)).Trim().TrimEnd('.', ' ');
            if (r.Length == 0) r = "_";
            if (Reserved.Contains(r.Split('.')[0].ToUpperInvariant())) r = "_" + r;
            return r;
        }

        public static bool IsEditorOnly(Transform t)
        {
            for (var x = t; x != null; x = x.parent) if (x.CompareTag("EditorOnly")) return true;
            return false;
        }

        public static Mesh GetMesh(Renderer r)
        {
            if (r is SkinnedMeshRenderer s) return s.sharedMesh;
            if (r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                return mf != null ? mf.sharedMesh : null;
            }
            return null;
        }

        public static int Triangles(Mesh m)
        {
            if (m == null) return 0;
            long n = 0;
            for (int i = 0; i < m.subMeshCount; i++) if (m.GetTopology(i) == MeshTopology.Triangles) n += m.GetIndexCount(i) / 3;
            return (int)n;
        }

        // Все MeshRenderer/SkinnedMeshRenderer аватара, включая выключенные, без EditorOnly.
        public static List<Renderer> MeshRenderers(GameObject avatar) =>
            avatar.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && !IsEditorOnly(r.transform) && GetMesh(r) != null)
                .ToList();

        // Меш лица (визем) из дескриптора — ему отдаём больше треугольников и текстур.
        public static SkinnedMeshRenderer FaceRenderer(GameObject avatar) =>
            Reflect.GetPath(Descriptor(avatar), "VisemeSkinnedMesh") as SkinnedMeshRenderer;

        // SPS / DPS / TPS: такие меши нельзя упрощать и склеивать.
        public static bool IsPenetrator(Renderer r)
        {
            foreach (var m in r.sharedMaterials)
                if (m != null && m.shader != null && (m.shader.name.Contains("SPS") || m.shader.name.Contains("DPS") || m.shader.name.Contains("TPS"))) return true;
            for (var t = r.transform; t != null; t = t.parent)
                if (t.GetComponents<Component>().Any(c => c != null && (c.GetType().Name.Contains("HapticPlug") || c.GetType().Name.Contains("Penetrator")))) return true;
            return false;
        }
    }
}
