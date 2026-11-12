using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    // Рычаги, которые просто добавляют компоненты готовых инструментов.
    // Они срабатывают только при сборке, исходники аватара не меняются.
    public static class ToolLevers
    {
        public static void AddTraceAndOptimize(GameObject avatar, List<string> log)
        {
            var t = Reflect.FindType("Anatawa12.AvatarOptimizer.TraceAndOptimize");
            if (t == null) { log.Add(L.T("! AAO не установлен — пропуск Trace And Optimize", "! AAO isn't installed — skipping Trace And Optimize")); return; }
            if (avatar.GetComponent(t) != null) { log.Add(L.T("= AAO Trace And Optimize уже есть", "= AAO Trace And Optimize is already there")); return; }
            Undo.AddComponent(avatar, t);
            log.Add(L.T("+ AAO Trace And Optimize: лишние кости, физбоны и блендшейпы уберутся при сборке", "+ AAO Trace And Optimize: unused bones, PhysBones and blendshapes are removed on build"));
        }

        public static void AddD4rk(GameObject avatar, List<string> log)
        {
            var t = Reflect.FindTypeByName("d4rkAvatarOptimizer", x => typeof(MonoBehaviour).IsAssignableFrom(x));
            if (t == null) { log.Add(L.T("! d4rkAvatarOptimizer не установлен — пропуск склейки мешей", "! d4rkAvatarOptimizer isn't installed — skipping mesh merging")); return; }
            var c = avatar.GetComponent(t);
            if (c == null) c = Undo.AddComponent(avatar, t);
            else Undo.RecordObject(c, "Orchestra d4rk");
            Reflect.Call(c, "SetPreset", "Shader Toggles");
            Reflect.Set(c, "MergeMainTex", true);
            Reflect.Set(c, "ApplyOnUpload", true);
            EditorUtility.SetDirty(c);
            log.Add(L.T("+ d4rk: пресет Shader Toggles + Merge MainTex (склейка мешей и материалов при сборке)", "+ d4rk: Shader Toggles preset + Merge MainTex (meshes and materials merged on build)"));
        }
    }
}
