using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TurboOptimizer
{
    // ПРО-режим: каждый инструмент отдельно, со всеми настройками.
    public partial class TurboWindow
    {
        void DrawPro()
        {
            using (Card("1", L.T("Аватар и цель", "Avatar and target")))
            {
                DrawAvatarAndTarget(explain: false);
                EditorGUILayout.LabelField(L.T("Платформа", "Platform"), L.T("PC (Quest — позже)", "PC (Quest — later)"));
                DrawToolsLine();
            }
            using (new EditorGUI.DisabledScope(!Ready))
            {
                // 2. диагноз
                using (Card("2", L.T("Диагноз", "Diagnosis"), L.T("Ранг считается на собранной копии — так же, как при загрузке в VRChat.", "Rank is measured on a built copy — exactly like an upload.")))
                {
                    _keepClone = EditorGUILayout.ToggleLeft(L.T("Оставить собранную копию в сцене (посмотреть, как выглядит)", "Keep the built copy in the scene (to see how it looks)"), _keepClone);
                    if (GUILayout.Button(L.T("Проверить ранг после сборки", "Check rank after build"), GUILayout.Height(28))) { Diagnose(); GUIUtility.ExitGUI(); }
                    DrawReport();
                }

                // 3. безопасная оптимизация
                using (Card("3", L.T("Безопасная оптимизация", "Safe optimization")))
                {
                    GUILayout.Label(L.T("• AAO Trace And Optimize — лишние кости, физбоны, блендшейпы", "• AAO Trace And Optimize — unused bones, PhysBones, blendshapes"), Styles.Small);
                    GUILayout.Label(L.T("• Текстуры по ролям: лицо 2048, основные 1024, normal 1024, маски 512 (с бэкапом)", "• Textures by role: face 2048, main 1024, normal 1024, masks 512 (with backup)"), Styles.Small);
                    GUILayout.Label(L.T("• Meshia: бюджет треугольников под цель, лицу 30%", "• Meshia: triangle budget for the target, 30% for the face"), Styles.Small);
                    GUILayout.Label(L.T("• d4rk Shader Toggles: склейка мешей и материалов при сборке", "• d4rk Shader Toggles: merges meshes and materials on build"), Styles.Small);
                    if (GUILayout.Button(L.T("Применить и проверить", "Apply and check"), GUILayout.Height(30))) { ApplySafe(); GUIUtility.ExitGUI(); }
                }

                // 4. физбоны
                using (Card("4", L.T("Физбоны", "PhysBones"), L.T("Сначала сливаются физбоны с общим родителем (AAO Merge PhysBone), потом — только если не влезли — то, что меняет поведение.",
                                                                  "First PhysBones with a common parent are merged (AAO Merge PhysBone); only if that's not enough — things that change behavior.")))
                {
                    _pbOpt.UnifySettings = EditorGUILayout.ToggleLeft(L.T("Сливать симметричные (L/R) даже с разными настройками", "Merge symmetric (L/R) ones even with different settings"), _pbOpt.UnifySettings);
                    _pbOpt.TrimTips = EditorGUILayout.ToggleLeft(L.T("Жёсткие кончики у длинных цепочек (только если это даёт ранг)", "Stiff tips on long chains (only if it gains a rank)"), _pbOpt.TrimTips);
                    _pbOpt.DropColliders = EditorGUILayout.ToggleLeft(L.T("Убирать коллайдеры у самых «дорогих» цепочек (могут проходить сквозь тело)", "Remove colliders from the most expensive chains (may clip through the body)"), _pbOpt.DropColliders);
                    if (_pbItems != null && _pbItems.Count > 0)
                    {
                        GUILayout.Label(L.T("Физика по вещам — сними галочку, чтобы отключить физику вещи:", "Physics per item — untick to turn an item's physics off:"), EditorStyles.miniBoldLabel);
                        foreach (var it in _pbItems)
                        {
                            bool keep = !_pbOpt.DropItems.Contains(it.Name);
                            bool suggested = _pbSuggest != null && _pbSuggest.Contains(it.Name);
                            bool nk = EditorGUILayout.ToggleLeft($"{it.Name} — {it.Count} {L.T("физбонов", "PhysBones")}, {it.Transforms} {L.T("костей", "bones")}{(suggested ? L.T("   ← совет: отключить", "   ← suggested: turn off") : "")}", keep);
                            if (nk != keep) { if (nk) _pbOpt.DropItems.Remove(it.Name); else _pbOpt.DropItems.Add(it.Name); }
                        }
                        if (_pbSuggestByRank != null && _pbSuggestByRank.Count > 0)
                            using (new EditorGUILayout.HorizontalScope())
                                foreach (var kv in _pbSuggestByRank.OrderBy(k => k.Key))
                                    if (GUILayout.Button(L.T($"Совет для {kv.Key} (−{kv.Value.Count})", $"Advice for {kv.Key} (−{kv.Value.Count})")))
                                    {
                                        _pbOpt.DropItems.Clear();
                                        foreach (var s in kv.Value) _pbOpt.DropItems.Add(s);
                                    }
                        if (_pbOpt.DropItems.Count > 0 && GUILayout.Button(L.T("Вернуть всю физику (снять отметки)", "Keep all physics (clear marks)"))) _pbOpt.DropItems.Clear();
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(L.T("Показать план", "Show plan"), GUILayout.Height(26))) { ShowPbPlan(); GUIUtility.ExitGUI(); }
                        if (GUILayout.Button(L.T("Применить план и проверить", "Apply plan and check"), GUILayout.Height(26))) { ApplyPbPlan(); GUIUtility.ExitGUI(); }
                    }
                    if (!string.IsNullOrEmpty(_pbPlanText)) EditorGUILayout.HelpBox(_pbPlanText, MessageType.None);
                }

                // 5. SPS
                DrawSps();

                // 6. материалы
                using (Card("6", L.T("Материалы", "Materials"),
                    L.T("Выравнивает настройки Poiyomi, чтобы d4rk склеил материалы: пустые функции выключаются, недостающие включаются с нулевой силой, непрозрачным ставится общая очередь. Вид не меняется. Работает на копиях материалов.",
                        "Aligns Poiyomi settings so d4rk can merge materials: empty features are turned off, missing ones are enabled at zero strength, opaque materials get a common queue. The look doesn't change. Works on material copies.")))
                {
                    _unifyTransparent = EditorGUILayout.ToggleLeft(L.T("Общая очередь и для прозрачных вещей (больше склеится, но может поменяться, какой прозрачный слой виден поверх)",
                                                                        "Common queue for transparent items too (more merges, but transparent layers may change draw order)"), _unifyTransparent);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(L.T("Выровнять материалы и проверить", "Align materials and check"), GUILayout.Height(26))) { ApplyMaterials(); GUIUtility.ExitGUI(); }
                        if (GUILayout.Button(L.T("Вернуть оригиналы", "Restore originals"), GUILayout.Height(26)))
                        {
                            int n = MaterialHarmonizer.Restore(_avatar);
                            EditorSceneManager.MarkSceneDirty(_avatar.scene);
                            _log.Add(L.T($"Материалы: вернул оригиналы на {n} слотов. Сохрани сцену (Ctrl+S).", $"Materials: restored originals on {n} slots. Save the scene (Ctrl+S)."));
                        }
                    }
                }

                // 7. вещи
                using (Card("7", L.T("Вещи", "Items"),
                    L.T("Что можно убрать с аватара и сколько это даёт по каждой категории. Части общего меша убираются через AAO Remove Mesh By Mask, объекты — тегом EditorOnly.",
                        "What can be removed and how much each gives per category. Parts of a shared mesh are removed via AAO Remove Mesh By Mask, objects via the EditorOnly tag.")))
                {
                    if (GUILayout.Button(L.T("Посоветовать, что убрать (по последней проверке)", "Suggest what to remove (from the last check)"))) { ScanItems(); GUIUtility.ExitGUI(); }
                    DrawItems();
                    int n = _itemDrop.Count(i => !i.Removed);
                    if (n > 0 && GUILayout.Button(L.T($"Убрать отмеченное ({n}) и проверить", $"Remove selected ({n}) and check"), GUILayout.Height(26))) { RemoveItemsPro(); GUIUtility.ExitGUI(); }
                }

                // служебное
                EditorGUILayout.Space(6);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L.T("Откатить текстуры", "Restore textures")))
                    {
                        int n = TextureLever.Restore(_avatar);
                        _log.Add(L.T($"Откат текстур: восстановлено {n}", $"Textures restored: {n}"));
                    }
                    if (GUILayout.Button(L.T("Удалить тестовую копию", "Delete test copy"))) Verifier.RemoveOldClones(_avatar);
                }
                var junk = Verifier.FindJunk(_avatar);
                if (junk.Count > 0)
                {
                    var names = string.Join(", ", junk.Select(j => j.name).Distinct());
                    EditorGUILayout.HelpBox(L.T($"В корне сцены {junk.Count} служебных объектов от прошлых проверок: {names}. Это копии частей аватара, вынесенные сборкой, — их можно удалить.",
                                                $"{junk.Count} leftover objects from previous checks are in the scene root: {names}. They're copies made by the build — safe to delete."), MessageType.Warning);
                    if (GUILayout.Button(L.T($"Убрать мусор ({junk.Count})", $"Clean up ({junk.Count})"))
                        && EditorUtility.DisplayDialog(Brand.Name, L.T($"Удалить из корня сцены:\n{names}\n\nОткатить можно через Edit → Undo.", $"Delete from the scene root:\n{names}\n\nUndo via Edit → Undo."), L.T("Удалить", "Delete"), L.T("Отмена", "Cancel")))
                    {
                        foreach (var go in junk) Undo.DestroyObjectImmediate(go);
                        EditorSceneManager.MarkSceneDirty(_avatar.scene);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        void DrawReport()
        {
            if (_report == null) return;
            if (_report.Error != null) { EditorGUILayout.HelpBox(_report.Error, MessageType.Error); return; }
            RankBadge(_report.Overall, L.T("Ранг после сборки: ", "Rank after build: "));
            GUILayout.Label(L.T($"цель {_target}, проверено {_reportTime:HH:mm:ss}", $"target {_target}, checked {_reportTime:HH:mm:ss}"), Styles.Small);

            var ok = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.35f, 0.8f, 0.4f) } };
            var bad = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.95f, 0.4f, 0.35f) } };
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L.T("Категория", "Category"), EditorStyles.miniBoldLabel, GUILayout.Width(200));
                GUILayout.Label(L.T("Сейчас", "Now"), EditorStyles.miniBoldLabel, GUILayout.Width(70));
                GUILayout.Label(L.T($"Лимит {_target}", $"{_target} limit"), EditorStyles.miniBoldLabel, GUILayout.Width(90));
                GUILayout.Label(L.T("Рейтинг", "Rating"), EditorStyles.miniBoldLabel);
            }
            foreach (var r in _report.Rows.Where(r => r.Rating > Rank.Excellent || r.Rating > _target))
            {
                var style = r.Rating > _target ? bad : ok;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(L.Cat(r.Category), style, GUILayout.Width(200));
                    GUILayout.Label(PerfReport.Fmt(r.Value), style, GUILayout.Width(70));
                    GUILayout.Label(PerfReport.Fmt(r.Limit), style, GUILayout.Width(90));
                    GUILayout.Label(r.Rating.ToString(), style);
                }
            }
            GUILayout.Label(L.T("Категории в Excellent не показаны.", "Excellent categories are hidden."), EditorStyles.miniLabel);
            foreach (var r in _report.Blockers(_target))
            {
                var ex = L.CatExplain(r.Category);
                if (ex.Length > 0) EditorGUILayout.HelpBox($"{L.Cat(r.Category)}: {ex}", MessageType.None);
            }
        }

        void ApplyMaterials()
        {
            _log.Clear();
            try { MaterialHarmonizer.Apply(_avatar, _log, _unifyTransparent); }
            catch (Exception e) { _log.Add("! " + e.Message); Debug.LogException(e); }
            EditorSceneManager.MarkSceneDirty(_avatar.scene);
            _log.Add(L.T("Сохрани сцену (Ctrl+S).", "Save the scene (Ctrl+S)."));
            Diagnose();
        }

        void ApplySafe()
        {
            if (!EditorUtility.DisplayDialog(Brand.Name,
                L.T("Будут добавлены компоненты AAO, Meshia и d4rk на аватар и уменьшен импорт текстур (с бэкапом).\n\nИсходные FBX не меняются. Продолжить?",
                    "AAO, Meshia and d4rk components will be added to the avatar and texture import size reduced (with a backup).\n\nSource FBX files aren't changed. Continue?"),
                L.T("Применить", "Apply"), L.T("Отмена", "Cancel"))) return;

            _log.Clear();
            try
            {
                EditorUtility.DisplayProgressBar(Brand.Name, "AAO…", 0.1f);
                ToolLevers.AddTraceAndOptimize(_avatar, _log);
                EditorUtility.DisplayProgressBar(Brand.Name, L.T("Текстуры…", "Textures…"), 0.3f);
                TextureLever.Apply(_avatar, _log);
                EditorUtility.DisplayProgressBar(Brand.Name, L.T("Бюджет треугольников…", "Triangle budget…"), 0.6f);
                var limit = Thresholds.Load(_target, false)?.Get("polyCount") ?? 70000;
                MeshiaLever.Apply(_avatar, (int)(limit * 0.97), _log);
                EditorUtility.DisplayProgressBar(Brand.Name, "d4rk…", 0.8f);
                ToolLevers.AddD4rk(_avatar, _log);
            }
            catch (Exception e) { _log.Add("! " + e.Message); Debug.LogException(e); }
            finally { EditorUtility.ClearProgressBar(); }

            EditorSceneManager.MarkSceneDirty(_avatar.scene);
            _log.Add(L.T("Сохрани сцену (Ctrl+S).", "Save the scene (Ctrl+S)."));
            Diagnose();
            for (int level = 1; level <= TextureLever.MaxLevel; level++)
            {
                var tex = _report?.Rows.FirstOrDefault(r => r.Category == "TextureMegabytes");
                if (tex == null || tex.Rating <= _target) break;
                TextureLever.Apply(_avatar, _log, level);
                Diagnose();
            }
        }

        PbPlan MakePbPlan()
        {
            EditorUtility.DisplayProgressBar(Brand.Name, L.T("Планирую физбоны…", "Planning PhysBones…"), 0.5f);
            try
            {
                var plan = PhysBonePlanner.Plan(_avatar, _target, _pbOpt);
                _pbPlanText = plan.ToText();
                _pbItems = plan.Items;
                _pbSuggest = plan.SuggestDrop;
                _pbSuggestByRank = plan.SuggestByRank;
                return plan;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        void DrawSps()
        {
            if (_sockets == null || _socketsOf != _avatar)
            {
                _sockets = SpsLever.FindSockets(_avatar);
                _contactGroups = SpsLever.FindContactGroups(_avatar, _report);
                _socketsOf = _avatar;
            }
            if (_sockets.Count == 0 && _contactGroups.Count == 0 && !SpsLever.HasSps(_avatar)) return;
            using (Card("5", L.T($"SPS и контакты (сокетов: {_sockets.Count})", $"SPS and contacts ({_sockets.Count} sockets)"),
                L.T("Свет в рангах допустим только в Poor (1 шт.), а «совместимость со старым DPS» даёт по 2 лампы на сокет. Каждый сокет ещё добавляет контакты: лимит Good 16, Medium 24, Poor 32.",
                    "Lights are only allowed in Poor (1), and 'legacy DPS compatibility' adds 2 lights per socket. Each socket also adds contacts: Good 16, Medium 24, Poor 32.")))
            {
                _spsLegacyOff = EditorGUILayout.ToggleLeft(L.T("Выключить совместимость со старым DPS (убирает свет; SPS и TPS работают)", "Turn off legacy DPS compatibility (removes lights; SPS and TPS still work)"), _spsLegacyOff);
                _spsOff = EditorGUILayout.ToggleLeft(L.T("Выключить SPS полностью (все сокеты и плаги)", "Turn SPS off completely (all sockets and plugs)"), _spsOff);
                if (!_spsOff)
                {
                    GUILayout.Label(L.T("Сокеты — сними галочку, чтобы отключить:", "Sockets — untick to disable:"), EditorStyles.miniBoldLabel);
                    foreach (var s in _sockets)
                    {
                        if (s.Socket == null) continue;
                        string cost = "";
                        if (_report?.Sources != null && _report.Sources.TryGetValue("SPS сокет: " + s.Socket.gameObject.name, out var v))
                            cost = L.T($" — контактов {v.contacts}, света {v.lights}", $" — {v.contacts} contacts, {v.lights} lights");
                        bool keep = !_spsDrop.Contains(s.Socket);
                        bool nk = EditorGUILayout.ToggleLeft(s.Name + cost, keep);
                        if (nk != keep) { if (nk) _spsDrop.Remove(s.Socket); else _spsDrop.Add(s.Socket); }
                    }
                    if (_report == null || _report.Sources.Count == 0)
                        GUILayout.Label(L.T("Нажми «Проверить ранг», чтобы увидеть, сколько стоит каждый сокет.", "Press \"Check rank\" to see what each socket costs."), EditorStyles.miniLabel);
                }
                if (_contactGroups.Count > 0)
                {
                    GUILayout.Label(L.T("Другие контакты (плаги, системы, контакты на костях) — сними галочку, чтобы отключить:", "Other contacts (plugs, systems, bone contacts) — untick to disable:"), EditorStyles.miniBoldLabel);
                    foreach (var g in _contactGroups)
                    {
                        bool keep = !_contactDrop.Contains(g.Name);
                        bool nk = EditorGUILayout.ToggleLeft($"{g.Name} — {g.Contacts} {L.T("контактов", "contacts")}", keep);
                        if (nk != keep) { if (nk) _contactDrop.Remove(g.Name); else _contactDrop.Add(g.Name); }
                    }
                }

                var contactRow = _report?.Rows.FirstOrDefault(r => r.Category == "ContactCount");
                if (contactRow?.Value != null && !_spsOff)
                {
                    double contacts = contactRow.Value.Value;
                    foreach (var s in _sockets.Where(s => s.Socket != null && _spsDrop.Contains(s.Socket)))
                        if (_report.Sources.TryGetValue("SPS сокет: " + s.Socket.gameObject.name, out var v)) contacts -= v.contacts;
                    foreach (var g in _contactGroups.Where(g => _contactDrop.Contains(g.Name))) contacts -= g.Contacts;
                    var lightsLeft = _spsLegacyOff ? 0 : (_report.Rows.FirstOrDefault(r => r.Category == "LightCount")?.Value ?? 0);
                    string rank = "Very Poor";
                    foreach (var r in new[] { Rank.Good, Rank.Medium, Rank.Poor })
                    {
                        var lim = Thresholds.Load(r, false)?.Get("contactCount");
                        if (lim != null && contacts <= lim) { rank = r.ToString(); break; }
                    }
                    EditorGUILayout.HelpBox(L.T($"Прогноз: контактов ≈ {contacts:0} → {rank} по контактам. Света: {lightsLeft:0}{(lightsLeft > 1 ? " → Very Poor" : "")}.",
                                                $"Forecast: ≈ {contacts:0} contacts → {rank} for contacts. Lights: {lightsLeft:0}{(lightsLeft > 1 ? " → Very Poor" : "")}."), MessageType.None);
                }
                if (GUILayout.Button(L.T("Применить SPS и контакты, проверить", "Apply SPS and contacts, check"), GUILayout.Height(26))) { ApplySps(); GUIUtility.ExitGUI(); }
            }
        }

        void ApplySps()
        {
            var drop = _sockets.Where(s => s.Socket != null && _spsDrop.Contains(s.Socket)).ToList();
            var dropGroups = _contactGroups.Where(g => _contactDrop.Contains(g.Name)).ToList();
            var msg = (_spsOff ? L.T("SPS будет выключен ПОЛНОСТЬЮ (все сокеты и плаги).\n", "SPS will be turned off COMPLETELY (all sockets and plugs).\n") : "") +
                      (_spsLegacyOff && !_spsOff ? L.T("Совместимость со старым DPS будет выключена.\n", "Legacy DPS compatibility will be turned off.\n") : "") +
                      (!_spsOff ? L.T($"Отключить сокетов: {drop.Count}", $"Sockets to disable: {drop.Count}") + (drop.Count > 0 ? ": " + string.Join(", ", drop.Select(d => d.Name)) : "") + ".\n" : "") +
                      L.T($"Отключить групп контактов: {dropGroups.Count}", $"Contact groups to disable: {dropGroups.Count}") + (dropGroups.Count > 0 ? ": " + string.Join(", ", dropGroups.Select(g => g.Name)) : "") +
                      L.T(".\n\nОткатить можно через Edit → Undo. Применить?", ".\n\nUndo via Edit → Undo. Apply?");
            if (!EditorUtility.DisplayDialog(Brand.Short + " — SPS", msg, L.T("Применить", "Apply"), L.T("Отмена", "Cancel"))) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Orchestra SPS");
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                if (_spsOff) SpsLever.DisableAll(_avatar, _log);
                else
                {
                    if (_spsLegacyOff) SpsLever.DisableLegacyLights(_avatar, _log);
                    foreach (var s in drop) SpsLever.DisableSocket(s, _log);
                }
                foreach (var g in dropGroups) SpsLever.DisableContactGroup(g, _log);
            }
            catch (Exception e) { _log.Add("! SPS: " + e.Message); Debug.LogException(e); }
            Undo.CollapseUndoOperations(undoGroup);
            _spsDrop.Clear();
            _contactDrop.Clear();
            _sockets = null;
            EditorSceneManager.MarkSceneDirty(_avatar.scene);
            _log.Add(L.T("Сохрани сцену (Ctrl+S).", "Save the scene (Ctrl+S)."));
            Diagnose();
        }

        void ShowPbPlan()
        {
            MakePbPlan();
            SaveReport();
        }

        void ApplyPbPlan()
        {
            var plan = MakePbPlan();
            if (!EditorUtility.DisplayDialog(Brand.Short + " — PhysBones",
                L.T($"Ранг физбонов после плана: {plan.Achieved} (цель {_target}).\n\nСлияний: {plan.Merges.Count}, жёстких кончиков: {plan.Trims.Sum(t => t.Value.Count)}, без коллайдеров: {plan.DropColliders.Count}, удалить физбонов: {plan.Remove.Count}.\n\nОткатить можно через Edit → Undo. Применить?",
                    $"PhysBone rank after the plan: {plan.Achieved} (target {_target}).\n\nMerges: {plan.Merges.Count}, stiff tips: {plan.Trims.Sum(t => t.Value.Count)}, colliders removed: {plan.DropColliders.Count}, PhysBones to delete: {plan.Remove.Count}.\n\nUndo via Edit → Undo. Apply?"),
                L.T("Применить", "Apply"), L.T("Отмена", "Cancel"))) return;
            try { PhysBoneApply.Apply(_avatar, plan, _log); }
            catch (Exception e) { _log.Add("! PhysBones: " + e.Message); Debug.LogException(e); }
            _log.Add(L.T("Сохрани сцену (Ctrl+S).", "Save the scene (Ctrl+S)."));
            Diagnose();
        }

        void RemoveItemsPro()
        {
            var removing = _itemDrop.Where(i => !i.Removed).ToList();
            if (!EditorUtility.DisplayDialog(Brand.Short + " — " + L.T("вещи", "items"),
                L.T("Убрать:\n", "Remove:\n") + string.Join("\n", removing.Select(i => "• " + i.Name)) +
                L.T("\n\nФизбоны этих вещей уйдут при следующем «Применить план» (раздел 4). Откатить: кнопка «Вернуть» у вещи или Edit → Undo.",
                    "\n\nThese items' PhysBones go away on the next \"Apply plan\" (section 4). Undo: the \"Restore\" button next to the item or Edit → Undo."),
                L.T("Убрать", "Remove"), L.T("Отмена", "Cancel"))) return;
            foreach (var it in removing) ItemScan.Remove(_avatar, it, _log);
            _itemDrop.Clear();
            EditorSceneManager.MarkSceneDirty(_avatar.scene);
            _log.Add(L.T("Сохрани сцену (Ctrl+S).", "Save the scene (Ctrl+S)."));
            Diagnose();
            ScanItems();
        }
    }
}
