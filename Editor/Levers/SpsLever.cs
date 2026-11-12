using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    // SPS (VRCFury): свет и контакты.
    // - «совместимость со старым DPS» даёт по 2 источника света на сокет, а свет в рангах допустим только в Poor (1 шт.);
    // - каждый сокет добавляет несколько контактов, их лимит: Good 16, Medium 24, Poor 32.
    public static class SpsLever
    {
        public const string OptionsHolder = "TurboOptimizer SPS Options";

        public class SocketInfo
        {
            public Component Socket;
            public string Name;
            public bool Safe; // можно пометить объект EditorOnly целиком (на нём нет ничего, кроме сокета)
        }

        public static List<SocketInfo> FindSockets(GameObject avatar)
        {
            var list = new List<SocketInfo>();
            foreach (var c in avatar.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.GetType().Name != "VRCFuryHapticSocket" || AvatarUtil.IsEditorOnly(c.transform)) continue;
                var custom = Reflect.GetPath(c, "name") as string;
                var go = c.gameObject;
                bool onlySocket = go.GetComponents<Component>().All(x => x is Transform || x == c || x.GetType().Name.StartsWith("VRCFury"))
                                  && go.GetComponentsInChildren<Renderer>(true).Length == 0;
                list.Add(new SocketInfo { Socket = c, Name = string.IsNullOrEmpty(custom) ? go.name : $"{custom} ({go.name})", Safe = onlySocket });
            }
            return list;
        }

        // Выключить «совместимость со старым DPS» (лампочки) в SPS Options VRCFury.
        public static void DisableLegacyLights(GameObject avatar, List<string> log)
        {
            var furyType = Reflect.FindType("VF.Model.VRCFury");
            var optType = Reflect.FindType("VF.Model.Feature.SpsOptions");
            if (furyType == null || optType == null) { log.Add(L.T("! VRCFury не найден — свет SPS не изменён", "! VRCFury not found — SPS lights unchanged")); return; }

            object options = null;
            Component owner = null;
            foreach (var c in avatar.GetComponentsInChildren(furyType, true))
            {
                var content = Reflect.GetPath(c, "content");
                if (content != null && content.GetType() == optType) { options = content; owner = c; break; }
            }
            if (options == null)
            {
                var holder = avatar.transform.Find(OptionsHolder);
                if (holder == null)
                {
                    var go = new GameObject(OptionsHolder);
                    Undo.RegisterCreatedObjectUndo(go, "Orchestra SPS");
                    go.transform.SetParent(avatar.transform, false);
                    holder = go.transform;
                }
                owner = Undo.AddComponent(holder.gameObject, furyType);
                var latest = Reflect.Call(owner, "GetLatestVersion");
                if (latest != null) Reflect.Set(owner, "Version", latest);
                options = Activator.CreateInstance(optType, true);
                Reflect.Set(owner, "content", options);
                log.Add(L.T("+ добавлен компонент VRCFury → SPS Options", "+ added VRCFury → SPS Options component"));
            }
            else Undo.RecordObject(owner, "Orchestra SPS");

            Reflect.Set(options, "legacyModeAvailable", false);
            Reflect.Set(options, "legacySocketCompatibility", false);
            Reflect.Set(options, "addDpsTipLight", false);
            EditorUtility.SetDirty(owner);
            log.Add(L.T("+ SPS: совместимость со старым DPS выключена (свет сокетов уйдёт, SPS и TPS работают как раньше)", "+ SPS: legacy DPS compatibility off (socket lights go away, SPS and TPS work as before)"));
        }

        // Отключить сокет: объект в EditorOnly, если на нём только сокет, иначе удалить сам компонент.
        public static void DisableSocket(SocketInfo s, List<string> log)
        {
            if (s.Socket == null) return;
            if (s.Safe)
            {
                Undo.RecordObject(s.Socket.gameObject, "Orchestra SPS");
                s.Socket.gameObject.tag = "EditorOnly";
                log.Add($"- {L.T("сокет", "socket")} {s.Name}: {L.T("объект помечен EditorOnly", "object tagged EditorOnly")}");
            }
            else
            {
                Undo.DestroyObjectImmediate(s.Socket);
                log.Add($"- {L.T("сокет", "socket")} {s.Name}: {L.T("компонент удалён", "component removed")}");
            }
        }

        public static bool HasSps(GameObject avatar) =>
            avatar.GetComponentsInChildren<Component>(true).Any(c => c != null && !AvatarUtil.IsEditorOnly(c.transform)
                && (c.GetType().Name == "VRCFuryHapticSocket" || c.GetType().Name == "VRCFuryHapticPlug"));

        // Выключить SPS целиком: все сокеты и плаги (сами меши плагов остаются, пропадает только SPS-логика и её контакты).
        public static void DisableAll(GameObject avatar, List<string> log)
        {
            foreach (var s in FindSockets(avatar)) DisableSocket(s, log);
            foreach (var c in avatar.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.GetType().Name != "VRCFuryHapticPlug" || AvatarUtil.IsEditorOnly(c.transform)) continue;
                log.Add($"- {L.T("SPS-плаг", "SPS plug")} {c.gameObject.name}: {L.T("компонент удалён", "component removed")}");
                Undo.DestroyObjectImmediate(c);
            }
            log.Add(L.T("+ SPS выключен целиком", "+ SPS fully disabled"));
        }

        // ---------- Другие контакты: плаги, системы автора, контакты на костях ----------

        public class ContactGroup
        {
            public string Name;
            public List<Component> Comps = new List<Component>();
            public int Contacts; // сколько контактов уйдёт при отключении (плаг — по замеру сборки)
        }

        static bool IsContactLike(Component c)
        {
            var n = c.GetType().Name;
            return n == "VRCContactSender" || n == "VRCContactReceiver" || n == "VRCFuryHapticPlug";
        }

        public static List<ContactGroup> FindContactGroups(GameObject avatar, PerfReport report)
        {
            var groups = new Dictionary<string, ContactGroup>();
            foreach (var c in avatar.GetComponentsInChildren<Component>(true))
            {
                if (c == null || !IsContactLike(c) || AvatarUtil.IsEditorOnly(c.transform)) continue;
                var top = c.transform;
                while (top.parent != null && top.parent != avatar.transform) top = top.parent;
                string key;
                if (top.name == "Armature" || top == c.transform)
                    key = "Кости: " + PhysBoneScan.Family(PhysBoneScan.SymmetryKey(c.gameObject.name), c.gameObject.name);
                else key = top.name;
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = new ContactGroup { Name = key };
                g.Comps.Add(c);
                if (c.GetType().Name == "VRCFuryHapticPlug")
                {
                    int plug = 12;
                    if (report?.Sources != null && report.Sources.TryGetValue("SPS плаг: " + c.gameObject.name, out var v)) plug = v.contacts;
                    g.Contacts += plug;
                }
                else g.Contacts += 1;
            }
            return groups.Values.OrderByDescending(g => g.Contacts).ToList();
        }

        // Отключить группу: объект с одними контактами — в EditorOnly, иначе удалить только компоненты контактов.
        public static void DisableContactGroup(ContactGroup g, List<string> log)
        {
            int tagged = 0, removed = 0;
            foreach (var c in g.Comps)
            {
                if (c == null) continue;
                var go = c.gameObject;
                bool onlyContacts = go.GetComponents<Component>().All(x => x is Transform || IsContactLike(x) || x.GetType().Name.StartsWith("VRCFury"))
                                    && go.GetComponentsInChildren<Renderer>(true).Length == 0;
                if (onlyContacts)
                {
                    if (!go.CompareTag("EditorOnly")) { Undo.RecordObject(go, "Orchestra contacts"); go.tag = "EditorOnly"; tagged++; }
                }
                else { Undo.DestroyObjectImmediate(c); removed++; }
            }
            log.Add(L.T($"- контакты «{g.Name}»: объектов в EditorOnly {tagged}, компонентов удалено {removed}", $"- contacts «{g.Name}»: objects tagged EditorOnly {tagged}, components removed {removed}"));
        }

        // Откуда в собранной копии берутся контакты и свет: сокет, плаг или объект аватара.
        public static Dictionary<string, (int contacts, int lights)> Sources(GameObject builtClone)
        {
            var result = new Dictionary<string, (int contacts, int lights)>();
            foreach (var c in builtClone.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var n = c.GetType().Name;
                bool contact = n == "VRCContactSender" || n == "VRCContactReceiver";
                bool light = c is Light;
                if (!contact && !light) continue;
                var src = SourceOf(c.transform, builtClone.transform);
                result.TryGetValue(src, out var v);
                result[src] = contact ? (v.contacts + 1, v.lights) : (v.contacts, v.lights + 1);
            }
            return result;
        }

        // Служебные объекты, которые VRCFury/MA вставляют в иерархию — их пропускаем, ища настоящее имя.
        static readonly HashSet<string> Generated = new HashSet<string>
        {
            "Original Object", "WorldSpace", "Senders", "Receivers", "LegacyLights", "Contacts", "Root", "Armature",
        };

        static string Meaningful(Transform x)
        {
            while (x != null && (Generated.Contains(x.name) || x.name.StartsWith("Baked") || x.name.StartsWith("vrcf_") || x.name.StartsWith("VRCF")))
                x = x.parent;
            // VRCFury при сборке добавляет к именам префикс вида "[VF248] " — номер случайный, убираем
            return x != null ? System.Text.RegularExpressions.Regex.Replace(x.name, @"^\[VF\d+\]\s*", "") : "?";
        }

        static string SourceOf(Transform t, Transform root)
        {
            for (var x = t; x != null && x != root; x = x.parent)
            {
                if (x.name.StartsWith("BakedSpsSocket")) return "SPS сокет: " + Meaningful(x.parent);
                if (x.name.StartsWith("BakedSpsPlug")) return "SPS плаг: " + Meaningful(x.parent);
                if (x.name == "Nonuniform SPS Sockets") return "SPS сокет (перенесён)";
            }
            // не SPS: верхний объект + сам объект с контактом, чтобы было видно, что это за система
            var top = t;
            while (top.parent != null && top.parent != root) top = top.parent;
            var own = Meaningful(t);
            return own == top.name ? "прочее: " + top.name : $"прочее: {top.name} / {own}";
        }
    }
}
