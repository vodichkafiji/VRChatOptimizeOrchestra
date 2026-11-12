using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

[assembly: ExportsPlugin(typeof(TurboOptimizer.Ndmf.MenuCleanupPlugin))]

namespace TurboOptimizer.Ndmf
{
    // При загрузке убирает из меню переключатели, которые управляют только тем, чего в загружаемой версии нет
    // (вещи, убранные через EditorOnly). Работает на копии меню — исходные ассеты не меняются.
    // Запускается после VRCFury (его меню к этому моменту уже вписано в дескриптор) и до AAO.
    public class MenuCleanupPlugin : Plugin<MenuCleanupPlugin>
    {
        public override string QualifiedName => "dev.turbooptimizer.menu-cleanup";
        public override string DisplayName => "Optimize Orchestra: menu cleanup";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Remove menu toggles of removed items", Execute);
        }

        static void Execute(BuildContext ctx)
        {
            var root = ctx.AvatarRootObject;
            var desc = root.GetComponent<VRCAvatarDescriptor>();
            if (desc == null || desc.expressionsMenu == null) return;

            var dead = DeadParameters(root.transform, desc);
            if (dead.Count == 0) return;

            var removed = new List<string>();
            var menu = Clean(desc.expressionsMenu, dead, removed, new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>());
            if (removed.Count == 0) return;
            desc.expressionsMenu = menu;
            Debug.Log($"[{Brand.Short}] menu: removed {removed.Count} toggles of removed items: {string.Join(", ", removed)}");
        }

        // ---------- какие параметры больше ничего не делают ----------

        class LayerInfo { public bool Live; public bool HitsRemoved; public HashSet<string> Params = new HashSet<string>(); }

        static HashSet<string> DeadParameters(Transform root, VRCAvatarDescriptor desc)
        {
            var layers = new List<LayerInfo>();
            var controllers = desc.baseAnimationLayers.Concat(desc.specialAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
                .Where(l => !l.isDefault && l.animatorController != null).Select(l => l.animatorController).Distinct();

            foreach (var rc in controllers)
            {
                if (!(rc is AnimatorController ac)) return new HashSet<string>(); // неизвестный тип контроллера — ничего не трогаем
                foreach (var layer in ac.layers)
                {
                    var info = new LayerInfo();
                    if (layer.syncedLayerIndex >= 0 || layer.stateMachine == null) info.Live = true;
                    else Walk(layer.stateMachine, root, info);
                    layers.Add(info);
                }
            }

            var used = layers.SelectMany(l => l.Params).Distinct();
            return new HashSet<string>(used.Where(p =>
            {
                var users = layers.Where(l => l.Params.Contains(p)).ToList();
                return users.Count > 0 && users.All(l => !l.Live && l.HitsRemoved);
            }));
        }

        static void Walk(AnimatorStateMachine sm, Transform root, LayerInfo info)
        {
            if (sm.behaviours != null && sm.behaviours.Length > 0) info.Live = true;
            foreach (var t in sm.anyStateTransitions) Conditions(t, info);
            foreach (var t in sm.entryTransitions) Conditions(t, info);
            foreach (var cs in sm.states)
            {
                var s = cs.state;
                if (s.behaviours != null && s.behaviours.Length > 0) info.Live = true;
                if (s.timeParameterActive) info.Params.Add(s.timeParameter);
                if (s.speedParameterActive) info.Params.Add(s.speedParameter);
                if (s.mirrorParameterActive) info.Params.Add(s.mirrorParameter);
                if (s.cycleOffsetParameterActive) info.Params.Add(s.cycleOffsetParameter);
                foreach (var t in s.transitions) Conditions(t, info);
                Motion(s.motion, root, info);
            }
            foreach (var child in sm.stateMachines)
            {
                foreach (var t in sm.GetStateMachineTransitions(child.stateMachine)) Conditions(t, info);
                Walk(child.stateMachine, root, info);
            }
        }

        static void Conditions(AnimatorTransitionBase t, LayerInfo info)
        {
            foreach (var c in t.conditions) info.Params.Add(c.parameter);
        }

        static void Motion(Motion m, Transform root, LayerInfo info)
        {
            if (m == null) return;
            if (m is BlendTree bt)
            {
                info.Params.Add(bt.blendParameter);
                if (bt.blendType != BlendTreeType.Simple1D) info.Params.Add(bt.blendParameterY);
                foreach (var ch in bt.children)
                {
                    if (bt.blendType == BlendTreeType.Direct) info.Params.Add(ch.directBlendParameter);
                    Motion(ch.motion, root, info);
                }
                return;
            }
            if (!(m is AnimationClip clip)) { info.Live = true; return; }
            var paths = UnityEditor.AnimationUtility.GetCurveBindings(clip).Select(b => b.path)
                .Concat(UnityEditor.AnimationUtility.GetObjectReferenceCurveBindings(clip).Select(b => b.path));
            foreach (var path in paths)
            {
                if (Exists(root, path)) info.Live = true;
                else info.HitsRemoved = true;
            }
        }

        static bool Exists(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            var t = root.Find(path);
            for (var x = t; x != null && x != root; x = x.parent)
                if (x.CompareTag("EditorOnly")) return false;
            return t != null;
        }

        // ---------- копия меню без мёртвых переключателей ----------

        static VRCExpressionsMenu Clean(VRCExpressionsMenu menu, HashSet<string> dead, List<string> removed,
                                        Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> done)
        {
            if (menu == null) return null;
            if (done.TryGetValue(menu, out var known)) return known;
            done[menu] = menu; // защита от циклов: пока обрабатываем — ссылка на себя

            var controls = new List<VRCExpressionsMenu.Control>();
            bool changed = false;
            foreach (var c in menu.controls)
            {
                var p = c.parameter?.name;
                if ((c.type == VRCExpressionsMenu.Control.ControlType.Toggle || c.type == VRCExpressionsMenu.Control.ControlType.Button)
                    && !string.IsNullOrEmpty(p) && dead.Contains(p))
                {
                    removed.Add(c.name); changed = true; continue;
                }
                if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null)
                {
                    var before = removed.Count;
                    var sub = Clean(c.subMenu, dead, removed, done);
                    if (sub != c.subMenu)
                    {
                        changed = true;
                        bool hadControls = c.subMenu.controls.Count > 0;
                        bool ownParamDead = !string.IsNullOrEmpty(p) && dead.Contains(p);
                        if (hadControls && sub.controls.Count == 0 && (string.IsNullOrEmpty(p) || ownParamDead)) continue; // папка опустела
                        var copy = Copy(c); copy.subMenu = sub; controls.Add(copy); continue;
                    }
                }
                controls.Add(c);
            }

            if (!changed) return menu;
            var clone = Object.Instantiate(menu);
            clone.name = menu.name;
            clone.controls = controls;
            done[menu] = clone;
            return clone;
        }

        static VRCExpressionsMenu.Control Copy(VRCExpressionsMenu.Control c) => new VRCExpressionsMenu.Control
        {
            name = c.name, icon = c.icon, type = c.type, parameter = c.parameter, value = c.value,
            style = c.style, subMenu = c.subMenu, subParameters = c.subParameters, labels = c.labels,
        };
    }
}
