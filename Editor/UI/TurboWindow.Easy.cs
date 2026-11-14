using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    // Простой режим: шаги-карточки, каждое действие объяснено.
    public partial class TurboWindow
    {
        void DrawEasy()
        {
            DrawToolsLine();

            // ---- шаг 1
            using (Card("1", L.T("Аватар и цель", "Avatar and target"),
                L.T("Ранг — оценка «тяжести» аватара в VRChat: Excellent → Good → Medium → Poor → Very Poor. Многие игроки скрывают тяжёлые аватары — вместо тебя они видят заглушку.",
                    "Rank is VRChat's 'heaviness' rating: Excellent → Good → Medium → Poor → Very Poor. Many players hide heavy avatars and see a placeholder instead of you.")))
                DrawAvatarAndTarget(explain: true);

            using (new EditorGUI.DisabledScope(!Ready))
            {
                // ---- шаг 2
                using (Card("2", L.T("Проверка", "Check"),
                    L.T("Соберу копию аватара так же, как при загрузке в VRChat, и посчитаю настоящий ранг. Сам аватар не меняется. Окно VRChat SDK считает до сборки — там цифры хуже, это нормально.",
                        "I build a copy of the avatar exactly like an upload does and measure the real rank. The avatar itself isn't changed. The VRChat SDK window measures before the build — its numbers look worse, that's normal.")))
                {
                    if (GUILayout.Button(L.T("Проверить", "Check"), GUILayout.Height(28))) { Diagnose(); ScanItems(); GUIUtility.ExitGUI(); }
                    DrawEasyStatus();
                }

                // ---- шаг 3
                bool hasSps = _avatar != null && (SpsLever.HasSps(_avatar) || SpsLever.FindSockets(_avatar).Count > 0);
                using (Card("3", L.T("Настройки", "Settings"), L.T("Решения, которые может принять только владелец аватара.", "Choices only the avatar's owner can make.")))
                {
                    if (hasSps)
                    {
                        bool keep = EditorGUILayout.ToggleLeft(L.T("Оставить SPS (сокеты и плаги для взаимодействий 18+)", "Keep SPS (sockets and plugs for 18+ interactions)"), !_spsOff, EditorStyles.boldLabel);
                        _spsOff = !keep;
                        GUILayout.Label(_spsOff
                            ? L.T("SPS выключится целиком: уйдут все его контакты (это сильно помогает рангу). Меши остаются, пропадает только SPS-логика.",
                                  "SPS will be turned off completely: all its contacts go away (helps the rank a lot). Meshes stay, only the SPS logic is gone.")
                            : L.T("SPS останется. Уберу только старую совместимость с DPS: она добавляет по 2 источника света на сокет, а свет почти сразу делает аватар Very Poor.",
                                  "SPS stays. I'll only remove the legacy DPS compatibility: it adds 2 lights per socket, and lights almost instantly make an avatar Very Poor."), Styles.Small);
                        EditorGUILayout.Space(4);
                    }
                    _unifyTransparent = EditorGUILayout.ToggleLeft(L.T("Склеивать и прозрачные вещи", "Merge transparent items too"), _unifyTransparent, EditorStyles.boldLabel);
                    GUILayout.Label(L.T("Даст ещё несколько склеенных материалов. Но прозрачные слои (сетка, кружево, стекло) могут начать рисоваться в другом порядке — проверь глазами.",
                                        "Merges a few more materials. But transparent layers (mesh, lace, glass) may start drawing in a different order — check by eye."), Styles.Small);
                    EditorGUILayout.Space(4);
                    _keepFace = EditorGUILayout.ToggleLeft(L.T("Не упрощать лицо", "Don't simplify the face"), _keepFace, EditorStyles.boldLabel);
                    GUILayout.Label(L.T("Обычно лицо упрощается бережно (не меньше 85%, края глаз, рта и ресниц сохраняются). Если лицо всё же выглядит сломанным — включи и нажми «Оптимизировать» ещё раз.",
                                        "Normally the face is simplified gently (at least 85%, eye, mouth and lash edges are kept). If the face still looks broken, turn this on and press \"Optimize\" again."), Styles.Small);
                    EditorGUILayout.Space(4);
                    _anyCost = EditorGUILayout.ToggleLeft(L.T("Дожать до цели любой ценой", "Reach the target at any cost"), _anyCost, EditorStyles.boldLabel);
                    if (_anyCost)
                        EditorGUILayout.HelpBox(L.T(
                            "Вид и поведение изменятся заметно:\n• физика мелких вещей (ушки, хвост, прыгающие части тела) выключится, длинные пряди волос станут жёстче\n• у склеиваемых материалов станет общий режим освещения (тени могут выглядеть иначе)\n• текстуры в группе получат одинаковый размер и формат\n• все меши, кроме лица, упростятся сильнее (до 30%)\nВсё откатывается кнопкой «Вернуть всё как было».",
                            "The look and behavior will noticeably change:\n• physics of small things (ears, tail, jiggle bones) turns off, long hair strands get stiffer\n• merged materials get one lighting mode (shadows may look different)\n• textures in a group get the same size and format\n• all meshes except the face are simplified harder (down to 30%)\nEverything can be undone with \"Revert everything\"."), MessageType.Warning);
                    else
                        GUILayout.Label(L.T("Для Good на сложных аватарах обычно нужно: без этого плагин меняет только то, что не видно.",
                                            "Usually needed for Good on complex avatars: without it the plugin only changes what isn't visible."), Styles.Small);
                }

                // ---- шаг 3б: вещи — отметить, что убрать сразу
                using (Card("", L.T("Что убрать с аватара (по желанию)", "What to remove from the avatar (optional)"),
                    L.T("Отметь галочкой вещи, которые тебе не нужны, — они уйдут при оптимизации. Вещь пропадает только в загружаемой версии: в сцене и в файлах она остаётся, вернуть можно в любой момент. Рядом показано, что даёт её удаление.",
                        "Tick items you don't need — they'll be removed when optimizing. An item disappears only from the uploaded version: it stays in the scene and files and can be restored any time. Next to each is what removing it gains.")))
                    DrawEasyItems();

                // ---- шаг 4
                using (Card("4", L.T("Оптимизация", "Optimize"),
                    L.T("Сначала делаю всё, что не меняет вид. Только если этого мало для цели — меняю поведение физики. Исходные модели и текстуры не портятся.",
                        "First I do everything that doesn't change the look. Only if that's not enough for the target do I change how physics behaves. Source models and textures aren't damaged.")))
                {
                    if (!_optimized)
                    {
                        Bullet(L.T("склею меши и материалы (d4rk) — меньше отрисовок", "merge meshes and materials (d4rk) — fewer draw calls"));
                        Bullet(L.T("уменьшу текстуры по роли: лицо остаётся чётким", "shrink textures by role: the face stays sharp"));
                        Bullet(L.T("уменьшу полигоны у мелких деталей (Meshia)", "reduce polygons on small details (Meshia)"));
                        Bullet(L.T("уберу лишние кости и блендшейпы (AAO)", "remove unused bones and blendshapes (AAO)"));
                        Bullet(L.T("солью похожие физбоны; если нужно — жёсткие кончики и меньше коллайдеров", "merge similar PhysBones; if needed — stiff tips and fewer colliders"));
                        EditorGUILayout.Space(4);
                    }
                    var label = !_optimized ? L.T("Оптимизировать", "Optimize")
                              : _itemDrop.Any(i => !i.Removed) ? L.T($"Убрать отмеченное ({_itemDrop.Count(i => !i.Removed)}) и проверить", $"Remove selected ({_itemDrop.Count(i => !i.Removed)}) and check")
                              : L.T("Оптимизировать ещё раз", "Optimize again");
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.55f, 0.85f, 0.6f);
                    if (GUILayout.Button(label, GUILayout.Height(40))) { GUI.backgroundColor = old; Optimize(); GUIUtility.ExitGUI(); }
                    GUI.backgroundColor = old;

                    if (_changes.Count > 0)
                    {
                        EditorGUILayout.Space(6);
                        GUILayout.Label(L.T("Что изменилось:", "What changed:"), EditorStyles.boldLabel);
                        foreach (var c in _changes) Bullet(c);
                    }
                }

                // ---- шаг 5: если не хватило
                bool reached = _report != null && _report.Error == null && _report.Overall != Rank.None && _report.Overall <= _target;
                if (_optimized && _report != null && _report.Error == null && !reached)
                {
                    using (Card("5", L.T("Если не хватило — убрать вещи", "Not enough? Remove items"),
                        L.T("Я посчитал, сколько «стоит» каждая вещь, и выбрал самый маленький набор, который доводит до цели. В первую очередь — то, что выключено по умолчанию. Тело, лицо и волосы не предлагаю. Вещь убирается только из загружаемой версии: в сцене и в файлах она остаётся, вернуть можно кнопкой «Вернуть».",
                            "I measured what each item 'costs' and picked the smallest set that reaches the target, preferring things that are off by default. Body, face and hair are never suggested. An item is removed only from the uploaded version: it stays in the scene and files and can be restored with \"Restore\".")))
                    {
                        DrawItems();
                        GUILayout.Label(L.T("Сними галочку, если вещь нужна, затем нажми зелёную кнопку в шаге 4.", "Untick anything you want to keep, then press the green button in step 4."), Styles.Small);
                    }
                }

                // ---- шаг 6: загрузка
                if (reached || _optimized)
                {
                    using (Card(reached ? "5" : "6", L.T("Загрузка в VRChat", "Upload to VRChat"), null))
                    {
                        Bullet(L.T("Сохрани сцену: Ctrl+S.", "Save the scene: Ctrl+S."));
                        Bullet(L.T("Посмотри на аватар в сцене: прозрачные вещи, переключатели в меню (можно в Play Mode).", "Look over the avatar in the scene: transparent items, menu toggles (Play Mode works too)."));
                        Bullet(L.T("VRChat SDK → Builder → Build and Publish. Чтобы не перезаписать оригинал, загрузи как новый аватар: в Pipeline Manager нажми Detach (отвязать Blueprint ID).",
                                   "VRChat SDK → Builder → Build and Publish. To keep the original, upload as a new avatar: press Detach in Pipeline Manager (unlinks the Blueprint ID)."));
                        Bullet(L.T("Ранг в игре совпадёт с тем, что показал шаг 2.", "The in-game rank will match what step 2 showed."));
                    }
                }

                // ---- откат
                EditorGUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L.T("Вернуть всё как было", "Revert everything"), GUILayout.Width(200))) { RevertAll(); GUIUtility.ExitGUI(); }
                    GUILayout.Label(L.T("Снимает все изменения плагина, возвращает текстуры и материалы.", "Undoes all plugin changes and restores textures and materials."), Styles.Small);
                }
            }
        }

        // Все вещи аватара по группам, с галочкой «убрать».
        readonly System.Collections.Generic.Dictionary<string, bool> _groupOpen = new System.Collections.Generic.Dictionary<string, bool>();

        void DrawEasyItems()
        {
            if (_items == null)
            {
                GUILayout.Label(L.T("Нажми «Проверить» в шаге 2 — тут появится список вещей.", "Press \"Check\" in step 2 — the item list will appear here."), Styles.Small);
                return;
            }
            int marked = _itemDrop.Count(i => !i.Removed);
            GUILayout.Label(marked > 0 ? L.T($"Отмечено к удалению: {marked}", $"Marked for removal: {marked}") : L.T("Ничего не отмечено — все вещи останутся.", "Nothing marked — all items stay."), EditorStyles.boldLabel);

            double Weight(AvatarItem i) => i.Cost.Sum(kv => kv.Value / ItemScan.Scale(kv.Key));
            var groups = new (string key, string title, System.Func<AvatarItem, bool> filter, bool open)[]
            {
                ("clothes", L.T("Одежда и аксессуары", "Clothing and accessories"), i => !i.Protected && !i.Removed && (i.Kind == ItemKind.Slot || i.Kind == ItemKind.Object), true),
                ("sps", L.T("SPS-сокеты", "SPS sockets"), i => !i.Removed && i.Kind == ItemKind.Socket, true),
                ("contacts", L.T("Контакты", "Contacts"), i => !i.Removed && i.Kind == ItemKind.Contacts, false),
                ("core", L.T("Основа аватара (тело, лицо, волосы) — лучше не трогать", "Avatar core (body, face, hair) — better leave alone"), i => i.Protected && !i.Removed, false),
                ("removed", L.T("Уже убрано", "Already removed"), i => i.Removed, true),
            };
            foreach (var g in groups)
            {
                var list = _items.Where(g.filter).OrderByDescending(Weight).ToList();
                if (list.Count == 0) continue;
                if (!_groupOpen.TryGetValue(g.key, out var open)) open = g.open;
                int sel = list.Count(i => _itemDrop.Contains(i));
                open = EditorGUILayout.Foldout(open, $"{g.title} ({list.Count}{(sel > 0 ? L.T($", отмечено {sel}", $", {sel} marked") : "")})", true, EditorStyles.foldoutHeader);
                _groupOpen[g.key] = open;
                if (!open) continue;
                EditorGUI.indentLevel++;
                foreach (var it in list) ItemToggle(it);
                EditorGUI.indentLevel--;
            }
            if (_tab == 0 && _items.Any(i => i.Kind == ItemKind.Socket) && !_spsOff)
                GUILayout.Label(L.T("Совет: если SPS не нужен совсем, проще снять галочку «Оставить SPS» в настройках выше.", "Tip: if you don't need SPS at all, just untick \"Keep SPS\" in the settings above."), Styles.Small);
        }

        // Итог проверки: ранг цветом и что мешает — с объяснениями.
        void DrawEasyStatus()
        {
            if (_report == null) return;
            EditorGUILayout.Space(4);
            if (_report.Error != null) { EditorGUILayout.HelpBox(_report.Error, MessageType.Error); return; }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (_rankBefore != Rank.None && _rankBefore != _report.Overall)
                {
                    RankBadge(_rankBefore, L.T("Было: ", "Before: "));
                    GUILayout.Label("→", Styles.Big, GUILayout.Width(20));
                    RankBadge(_report.Overall, L.T("стало: ", "now: "));
                }
                else RankBadge(_report.Overall, L.T("Сейчас: ", "Now: "));
                GUILayout.FlexibleSpace();
            }
            GUILayout.Label(L.T($"проверено в {_reportTime:HH:mm}", $"checked at {_reportTime:HH:mm}"), Styles.Small);

            if (_report.Overall != Rank.None && _report.Overall <= _target)
            {
                EditorGUILayout.HelpBox(L.T($"Цель {_target} достигнута! Можно загружать.", $"Target {_target} reached! Ready to upload."), MessageType.Info);
                return;
            }
            var limits = Thresholds.Load(_target, false);
            GUILayout.Label(L.T($"До {_target} мешает:", $"Blocking {_target}:"), EditorStyles.boldLabel);
            foreach (var r in _report.Blockers(_target))
            {
                var lim = limits?.Get(PerfStats.FieldPath(r.Category));
                var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = RankColor(r.Rating) } };
                GUILayout.Label($"{L.Cat(r.Category)}: {PerfReport.Fmt(r.Value)}" + (lim != null ? L.T($"  (нужно ≤ {PerfReport.Fmt(lim)})", $"  (needs ≤ {PerfReport.Fmt(lim)})") : ""), style);
                var ex = L.CatExplain(r.Category);
                if (ex.Length > 0) GUILayout.Label("      " + ex, Styles.Small);
            }
            if (!_optimized)
                GUILayout.Label(L.T("Нажми «Оптимизировать» в шаге 4 — большую часть этого я исправлю сам.", "Press \"Optimize\" in step 4 — I'll fix most of this myself."), Styles.Small);
        }
    }
}
