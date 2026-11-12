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
