using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TurboOptimizer
{
    // Окно плагина. Разбито на части: общее (этот файл), простой режим, ПРО и справка.
    public partial class TurboWindow : EditorWindow
    {
        GameObject _avatar;
        Rank _target = Rank.Medium;
        bool _keepClone;
        bool _unifyTransparent;
        bool _anyCost;
        bool _keepFace;
        PerfReport _report;
        DateTime _reportTime;
        readonly List<string> _log = new List<string>();
        Vector2 _scroll;
        int _tab; // 0 — простой, 1 — ПРО, 2 — справка
        bool _showLog;

        // SPS
        bool _spsLegacyOff = true;
        bool _spsOff;
        readonly HashSet<Component> _spsDrop = new HashSet<Component>();
        List<SpsLever.SocketInfo> _sockets;
        GameObject _socketsOf;
        List<SpsLever.ContactGroup> _contactGroups;
        readonly HashSet<string> _contactDrop = new HashSet<string>();

        // физбоны (ПРО)
        readonly PbOptions _pbOpt = new PbOptions();
        string _pbPlanText;
        List<ItemStat> _pbItems;
        List<string> _pbSuggest;
        Dictionary<Rank, List<string>> _pbSuggestByRank;

        // простой режим и вещи
        bool _optimized;
        int _undoGroup = -1;
        Rank _rankBefore = Rank.None;
        readonly List<string> _changes = new List<string>();
        List<AvatarItem> _items;
        ItemScan.Advice _advice;
        readonly HashSet<AvatarItem> _itemDrop = new HashSet<AvatarItem>();
        bool _showAllItems;

        [MenuItem("Tools/" + Brand.Name)]
        public static void Open() => GetWindow<TurboWindow>(Brand.Short);

        void OnEnable()
        {
            if (_avatar == null) _avatar = FindAvatar();
            _tab = EditorPrefs.GetInt("TurboOptimizer.Tab", 0);
            titleContent = new GUIContent(Brand.Short);
        }

        static GameObject FindAvatar()
        {
            if (Selection.activeGameObject != null && AvatarUtil.IsAvatar(Selection.activeGameObject)) return Selection.activeGameObject;
            var scene = EditorSceneManager.GetActiveScene();
            return scene.GetRootGameObjects().FirstOrDefault(g => g.activeInHierarchy && AvatarUtil.IsAvatar(g) && !g.name.EndsWith(Verifier.CloneSuffix));
        }

        bool Ready => _avatar != null && AvatarUtil.IsAvatar(_avatar) && !EditorApplication.isPlayingOrWillChangePlaymode;

        void OnGUI()
        {
            DrawHeader();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 0: DrawEasy(); break;
                case 1: DrawPro(); break;
                default: DrawHelp(); break;
            }
            DrawLog();
            DrawReportLink();
            EditorGUILayout.Space(10);
            EditorGUILayout.EndScrollView();
        }

        // ---------- шапка: название, язык, режим ----------
        void DrawHeader()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Brand.Name, Styles.Title);
                GUILayout.FlexibleSpace();
                int lang = GUILayout.Toolbar(L.En ? 1 : 0, new[] { "RU", "EN" }, GUILayout.Width(80));
                if ((lang == 1) != L.En) { L.En = lang == 1; if (_items != null) ScanItems(); }
            }
            GUILayout.Label(L.T("Оркестр для оптимизаторов: сам решает, когда и как применить AAO, d4rk, Meshia и VRCFury, чтобы поднять ранг аватара.",
                                "An orchestra for optimizers: decides when and how to use AAO, d4rk, Meshia and VRCFury to raise your avatar's rank."), Styles.Sub);
            EditorGUILayout.Space(4);
            int tab = GUILayout.Toolbar(_tab, new[] { L.T("Простой", "Easy"), L.T("ПРО", "PRO"), L.T("Справка", "Help") }, GUILayout.Height(24));
            if (tab != _tab) { _tab = tab; EditorPrefs.SetInt("TurboOptimizer.Tab", _tab); }
            EditorGUILayout.Space(4);
        }

        // ---------- общие элементы оформления ----------
        static class Styles
        {
            public static readonly GUIStyle Title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
            public static readonly GUIStyle Sub = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            public static readonly GUIStyle Step = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            public static readonly GUIStyle Text = new GUIStyle(EditorStyles.wordWrappedLabel);
            public static readonly GUIStyle Small = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            public static readonly GUIStyle Big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, wordWrap = true };
            public static readonly GUIStyle Card = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 10) };
        }

        // Карточка шага: номер, заголовок, пояснение.
        static IDisposable Card(string number, string title, string explain = null)
        {
            EditorGUILayout.Space(6);
            var scope = new EditorGUILayout.VerticalScope(Styles.Card);
            GUILayout.Label(string.IsNullOrEmpty(number) ? title : $"{number}  {title}", Styles.Step);
            if (!string.IsNullOrEmpty(explain)) GUILayout.Label(explain, Styles.Small);
            EditorGUILayout.Space(2);
            return scope;
        }

        static Color RankColor(Rank r) => r switch
        {
            Rank.Excellent => new Color(0.30f, 0.80f, 0.95f),
            Rank.Good => new Color(0.35f, 0.82f, 0.40f),
            Rank.Medium => new Color(0.95f, 0.80f, 0.25f),
            Rank.Poor => new Color(0.98f, 0.55f, 0.20f),
            Rank.VeryPoor => new Color(0.95f, 0.30f, 0.30f),
            _ => Color.gray,
        };

        static void RankBadge(Rank r, string prefix)
        {
            var style = new GUIStyle(Styles.Big) { normal = { textColor = RankColor(r) } };
            GUILayout.Label($"{prefix}{L.RankName(r)}", style);
        }

        static void Bullet(string text) => GUILayout.Label("•  " + text, Styles.Text);

        // ---------- какие инструменты установлены ----------
        static List<(string name, bool ok, bool required)> _tools;
        static List<(string name, bool ok, bool required)> Tools()
        {
            if (_tools != null) return _tools;
            _tools = new List<(string, bool, bool)>
            {
                ("NDMF", Reflect.FindType("nadena.dev.ndmf.BuildContext") != null, true),
                ("Avatar Optimizer (AAO)", Reflect.FindType("Anatawa12.AvatarOptimizer.TraceAndOptimize") != null, true),
                ("d4rkAvatarOptimizer", Reflect.FindTypeByName("d4rkAvatarOptimizer", x => typeof(MonoBehaviour).IsAssignableFrom(x)) != null, true),
                ("Meshia", Reflect.FindType("Meshia.MeshSimplification.Ndmf.MeshiaCascadingAvatarMeshSimplifier") != null, true),
                ("VRCFury", Reflect.FindType("VF.Model.VRCFury") != null, false),
            };
            return _tools;
        }

        static void DrawToolsLine()
        {
            var tools = Tools();
            var missing = tools.Where(t => !t.ok && t.required).Select(t => t.name).ToList();
            GUILayout.Label(string.Join("    ", tools.Select(t => (t.ok ? "✓ " : "✗ ") + t.name)), Styles.Small);
            if (missing.Count > 0)
                EditorGUILayout.HelpBox(L.T($"Не установлено: {string.Join(", ", missing)}. Добавь через VRChat Creator Companion (Manage Project), иначе часть оптимизаций не сработает.",
                                            $"Not installed: {string.Join(", ", missing)}. Add them in VRChat Creator Companion (Manage Project), otherwise some optimizations won't run."), MessageType.Warning);
        }

        // ---------- аватар и цель (общие для режимов) ----------
        void DrawAvatarAndTarget(bool explain)
        {
            var picked = (GameObject)EditorGUILayout.ObjectField(L.T("Аватар", "Avatar"), _avatar, typeof(GameObject), true);
            if (picked != _avatar) { _avatar = picked; _report = null; _items = null; _advice = null; _itemDrop.Clear(); _userDrop.Clear(); _userKeep.Clear(); _optimized = false; _changes.Clear(); _rankBefore = Rank.None; }
            if (_avatar != null && !AvatarUtil.IsAvatar(_avatar))
                EditorGUILayout.HelpBox(L.T("На этом объекте нет VRC Avatar Descriptor — выбери корень аватара.", "This object has no VRC Avatar Descriptor — pick the avatar root."), MessageType.Error);
            var targets = new[] { Rank.Poor, Rank.Medium, Rank.Good };
            int t = EditorGUILayout.Popup(L.T("Цель", "Target"), Mathf.Max(0, Array.IndexOf(targets, _target)), new[] { "Poor", "Medium", "Good" });
            var newTarget = targets[Mathf.Clamp(t, 0, 2)];
            if (newTarget != _target) { _target = newTarget; _advice = null; if (_report != null && _items != null) { _advice = ItemScan.Recommend(_items, _report, _target); ResetPicks(); } }
            if (!explain) return;
            var desc = _target switch
            {
                Rank.Poor => L.T("Poor — минимум: аватар перестанет быть Very Poor, который многие игроки скрывают. Почти всё остаётся на месте.",
                                 "Poor — the minimum: the avatar stops being Very Poor, which many players hide. Almost everything stays."),
                Rank.Medium => L.T("Medium — хороший баланс: аватар виден почти всем. Возможно, придётся убрать пару вещей.",
                                   "Medium — a good balance: visible to almost everyone. You may need to drop a couple of items."),
                _ => L.T("Good — строго: мало материалов, физики и контактов. Для насыщенных аватаров придётся убрать многое.",
                         "Good — strict: few materials, little physics and contacts. Busy avatars will need to lose a lot."),
            };
            GUILayout.Label(desc, Styles.Small);
        }

        // Выбор игрока переживает пересканирование: помним по ключу «вид:имя».
        readonly HashSet<string> _userDrop = new HashSet<string>(); // отметил сам
        readonly HashSet<string> _userKeep = new HashSet<string>(); // снял галочку с совета
        static string KeyOf(AvatarItem it) => $"{it.Kind}:{it.Name}";

        void ResetPicks()
        {
            _itemDrop.Clear();
            if (_items == null) return;
            foreach (var it in _items.Where(i => !i.Removed && _userDrop.Contains(KeyOf(i)))) _itemDrop.Add(it);
            // совет подставляем только после первой оптимизации: до неё он завышен (склейка ещё не сделана)
            if (_advice != null && (_optimized || _tab == 1))
                foreach (var it in _advice.Pick.Where(i => !_userKeep.Contains(KeyOf(i)))) _itemDrop.Add(it);
        }

        void SetDrop(AvatarItem it, bool drop)
        {
            var k = KeyOf(it);
            if (drop) { _itemDrop.Add(it); _userDrop.Add(k); _userKeep.Remove(k); }
            else { _itemDrop.Remove(it); _userDrop.Remove(k); _userKeep.Add(k); }
        }

        // ---------- журнал ----------
        void DrawLog()
        {
            if (_log.Count == 0) return;
            EditorGUILayout.Space(8);
            _showLog = EditorGUILayout.Foldout(_showLog || _tab == 1, L.T("Подробный журнал", "Detailed log"), true);
            if (!_showLog && _tab != 1) return;
            foreach (var l in _log) GUILayout.Label(l, Styles.Small);
        }

        // Строка «где отчёт» внизу окна: путь и кнопка, открывающая папку с выделенным файлом.
        void DrawReportLink()
        {
            if (!File.Exists(ReportPath)) return;
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L.T("Отчёт: ", "Report: ") + ReportPath, Styles.Small);
                if (GUILayout.Button(L.T("Показать в папке", "Show in folder"), GUILayout.Width(140))) EditorUtility.RevealInFinder(ReportPath);
            }
        }

        static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TurboOptimizerReport.txt"));

        // ---------- проверка ранга ----------
        void Diagnose()
        {
            try { _report = Verifier.BuildAndMeasure(_avatar, _target, _keepClone); }
            catch (Exception e)
            {
                _report = new PerfReport { Error = L.T("Ошибка проверки: ", "Check failed: ") + (e.InnerException ?? e).Message };
                Debug.LogException(e);
            }
            _reportTime = DateTime.Now;
            _sockets = null;
            SaveReport();
        }

        // ---------- вещи ----------
        void ScanItems()
        {
            if (_avatar == null) return;
            try
            {
                _items = ItemScan.Scan(_avatar, _report);
                _advice = ItemScan.Recommend(_items, _report, _target);
                ResetPicks();
            }
            catch (Exception e) { _log.Add("! " + L.T("Ошибка анализа вещей: ", "Item analysis failed: ") + e.Message); Debug.LogException(e); }
            SaveReport();
        }

        void DrawItems()
        {
            if (_items == null) return;
            // цель слишком далеко: честно говорим и предлагаем реалистичную
            if (_advice != null && _report != null && (_advice.Left.Count > 0 || _advice.Pick.Count >= 8))
            {
                var realistic = _report.Overall > _target ? _report.Overall : _target;
                EditorGUILayout.HelpBox(_advice.Left.Count > 0
                    ? L.T($"Цель {_target} для этого аватара недостижима даже без {_advice.Pick.Count} вещей. Реалистичная цель — {L.RankName(realistic)}: поставь её вверху, и я не буду ничего советовать убрать.",
                          $"{_target} isn't reachable for this avatar even after removing {_advice.Pick.Count} items. A realistic target is {L.RankName(realistic)}: set it at the top and I won't suggest removing anything.")
                    : L.T($"Для {_target} придётся убрать {_advice.Pick.Count} вещей — аватар заметно изменится. Подумай, может, хватит {L.RankName(realistic)}.",
                          $"{_target} needs {_advice.Pick.Count} items removed — the avatar will change noticeably. Maybe {L.RankName(realistic)} is enough."),
                    MessageType.Warning);
            }
            if (_advice != null && _advice.Pick.Count > 0)
            {
                GUILayout.Label(L.T($"Чтобы дойти до {_target}, советую убрать:", $"To reach {_target}, I suggest removing:"), EditorStyles.boldLabel);
                foreach (var it in _advice.Pick) ItemToggle(it);
            }
            else if (_advice != null && _advice.Need.Count > 0)
                GUILayout.Label(L.T("Убирать вещи тут не поможет.", "Removing items won't help here."), EditorStyles.boldLabel);
            if (_advice != null && _advice.Left.Count > 0)
                EditorGUILayout.HelpBox(L.T("Даже без вещей не хватит: ", "Not enough even without items: ") +
                    string.Join(", ", _advice.Left.Select(kv => $"{L.Cat(kv.Key)} {L.T("ещё на", "by")} {PerfReport.Fmt(Math.Round(kv.Value, 1))}")) +
                    L.T(". Попробуй цель на ступень ниже.", ". Try a lower target."), MessageType.None);

            _showAllItems = EditorGUILayout.Foldout(_showAllItems, L.T($"Все вещи аватара ({_items.Count})", $"All avatar items ({_items.Count})"), true);
            if (_showAllItems)
                foreach (var it in _items.Where(i => _advice == null || !_advice.Pick.Contains(i)).OrderBy(i => i.Protected).ThenBy(i => i.Name))
                    ItemToggle(it);
        }

        void ItemToggle(AvatarItem it)
        {
            if (it.Removed)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"✕ {it.Name} — {L.T("убрано", "removed")}", Styles.Small);
                    if (GUILayout.Button(L.T("Вернуть", "Restore"), GUILayout.Width(80)))
                    {
                        ItemScan.Restore(_avatar, it, _log);
                        EditorSceneManager.MarkSceneDirty(_avatar.scene);
                        _items = null; _advice = null;
                        GUIUtility.ExitGUI();
                    }
                }
                return;
            }
            var tags = new List<string> { it.KindText };
            if (it.InMenu) tags.Add(L.T("в меню", "in menu"));
            if (it.HiddenByDefault) tags.Add(L.T("выключено по умолчанию", "off by default"));
            if (it.Protected) tags.Add(L.T("основа аватара", "core of the avatar"));
            bool on = _itemDrop.Contains(it);
            bool nv = EditorGUILayout.ToggleLeft($"{it.Name}  [{string.Join(", ", tags)}]", on, EditorStyles.boldLabel);
            GUILayout.Label("      " + it.CostText(), Styles.Small);
            if (nv != on) SetDrop(it, nv);
        }

        // Один шаг оптимизации: ошибка в нём попадает в журнал, но не отменяет остальные шаги.
        void Step(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { _log.Add($"! {name}: " + (e.InnerException ?? e).Message); Debug.LogException(e); }
        }

        // ---------- всё за одну кнопку ----------
        // Каждый шаг идемпотентен, поэтому повторное нажатие безопасно.
        void Optimize()
        {
            var removing = _itemDrop.Where(i => !i.Removed).ToList();
            bool sps = SpsLever.FindSockets(_avatar).Count > 0 || SpsLever.HasSps(_avatar);
            var msg = L.T("Будет сделано:\n", "This will:\n") +
                      L.T("• склейка мешей и материалов, уменьшение текстур и полигонов (AAO, d4rk, Meshia)\n",
                          "• merge meshes and materials, shrink textures and polygons (AAO, d4rk, Meshia)\n") +
                      L.T("• слияние физбонов, а если нужно для ранга — жёсткие кончики и меньше коллайдеров\n",
                          "• merge PhysBones; if the rank needs it — stiff tips and fewer colliders\n") +
                      (sps ? (_spsOff ? L.T("• SPS выключится целиком\n", "• SPS will be turned off completely\n")
                                      : L.T("• старая совместимость SPS с DPS выключится (уберёт свет)\n", "• SPS legacy DPS compatibility will be turned off (removes lights)\n")) : "") +
                      (removing.Count > 0 ? L.T("• убрать: ", "• remove: ") + string.Join(", ", removing.Select(i => i.Name)) + "\n" : "") +
                      (_anyCost ? L.T("• ЛЮБОЙ ЦЕНОЙ: выключить физику мелких вещей, общий режим освещения, одинаковые текстуры, сильнее упростить меши (кроме лица)\n",
                                      "• AT ANY COST: turn off physics of small things, one lighting mode, same-size textures, stronger mesh simplification (except the face)\n") : "") +
                      L.T("\nИсходные модели и текстуры не портятся. Всё можно вернуть кнопкой «Вернуть всё как было».",
                          "\nSource models and textures aren't damaged. Everything can be undone with \"Revert everything\".");
            if (!EditorUtility.DisplayDialog(Brand.Name, msg, L.T("Оптимизировать", "Optimize"), L.T("Отмена", "Cancel"))) return;

            if (_report != null && _report.Error == null && _rankBefore == Rank.None) _rankBefore = _report.Overall;
            if (_undoGroup < 0) { Undo.IncrementCurrentGroup(); _undoGroup = Undo.GetCurrentGroup(); }
            _log.Clear();
            _changes.Clear();
            try
            {
                Step("tools", () =>
                {
                    EditorUtility.DisplayProgressBar(Brand.Name, L.T("Инструменты оптимизации…", "Optimization tools…"), 0.1f);
                    ToolLevers.AddTraceAndOptimize(_avatar, _log);
                    TextureLever.Apply(_avatar, _log);
                    ToolLevers.AddD4rk(_avatar, _log);
                    Step("Meshia", () =>
                    {
                        var limit = Thresholds.Load(_target, false)?.Get("polyCount") ?? 70000;
                        MeshiaLever.Apply(_avatar, (int)(limit * 0.97), _log, _anyCost ? 0.50f : 0.45f, !_keepFace);
                    });
                    _changes.Add(L.T("Меши и материалы склеиваются при загрузке, текстуры и полигоны уменьшены под цель. Вид почти не меняется.",
                                     "Meshes and materials are merged on upload; textures and polygons are reduced for the target. The look barely changes."));
                });

                Step("SPS", () =>
                {
                    EditorUtility.DisplayProgressBar(Brand.Name, "SPS…", 0.3f);
                    if (sps)
                    {
                        if (_spsOff) { SpsLever.DisableAll(_avatar, _log); _changes.Add(L.T("SPS выключен целиком.", "SPS is turned off completely.")); }
                        else
                        {
                            SpsLever.DisableLegacyLights(_avatar, _log);
                            _changes.Add(L.T("Выключена старая совместимость SPS с DPS: пропал свет сокетов, SPS и TPS работают.",
                                             "SPS legacy DPS compatibility is off: socket lights are gone, SPS and TPS still work."));
                        }
                    }
                });

                Step("items", () =>
                {
                    EditorUtility.DisplayProgressBar(Brand.Name, L.T("Вещи…", "Items…"), 0.4f);
                    foreach (var it in removing) ItemScan.Remove(_avatar, it, _log);
                    var removedNow = (_items ?? new List<AvatarItem>()).Where(i => i.Removed).Select(i => i.Name).Concat(removing.Select(i => i.Name)).Distinct().ToList();
                    if (removedNow.Count > 0) _changes.Add(L.T("Убрано с аватара: ", "Removed from the avatar: ") + string.Join(", ", removedNow) + ".");
                });

                Step("materials", () =>
                {
                    EditorUtility.DisplayProgressBar(Brand.Name, L.T("Материалы…", "Materials…"), 0.5f);
                    MaterialHarmonizer.Apply(_avatar, _log, _unifyTransparent, _anyCost);
                    if (MaterialHarmonizer.LastSacrificed.Count > 0)
                        _changes.Add(L.T("Перестанут работать анимации материалов (переключатели меню, которые меняют цвет/рисунки): ",
                                         "Material animations stop working (menu toggles that change colors/decals): ")
                                     + string.Join(", ", MaterialHarmonizer.LastSacrificed.Take(12)) + (MaterialHarmonizer.LastSacrificed.Count > 12 ? " …" : "") + ".");
                    _changes.Add(_unifyTransparent
                        ? L.T("Материалы выровнены для склейки, включая прозрачные — проверь, как лежат прозрачные слои.", "Materials aligned for merging, including transparent ones — check how transparent layers stack.")
                        : L.T("Материалы выровнены для склейки (вид не меняется).", "Materials aligned for merging (the look doesn't change)."));
                });

                Step("physbones", () =>
                {
                    EditorUtility.DisplayProgressBar(Brand.Name, L.T("Физбоны…", "PhysBones…"), 0.6f);
                    var plan = PhysBonePlanner.Plan(_avatar, _target, new PbOptions { Conservative = _anyCost });
                    if (_anyCost && plan.Achieved > _target && plan.SuggestDrop.Count > 0)
                    {
                        // любой ценой: выключаем физику вещей, которые планировщик выбрал как наименее заметные
                        var opt = new PbOptions { Conservative = true };
                        opt.DropItems.UnionWith(plan.SuggestDrop);
                        plan = PhysBonePlanner.Plan(_avatar, _target, opt);
                        _changes.Add(L.T("Выключена физика: ", "Physics turned off: ") + string.Join(", ", opt.DropItems) + ".");
                    }
                    _pbPlanText = plan.ToText();
                    if (plan.Merges.Count > 0 || plan.Trims.Count > 0 || plan.DropColliders.Count > 0 || plan.Remove.Count > 0)
                    {
                        PhysBoneApply.Apply(_avatar, plan, _log);
                        if (plan.Merges.Count > 0) _changes.Add(L.T($"Слиты похожие физбоны ({plan.Merges.Count} групп) — качаются так же.", $"Similar PhysBones merged ({plan.Merges.Count} groups) — they move the same."));
                        if (plan.Trims.Count > 0) _changes.Add(L.T("Стали жёсткими кончики: ", "Tips became stiff: ") + string.Join(", ", plan.Trims.Select(kv => kv.Key.Name)) + ".");
                        if (plan.DropColliders.Count > 0) _changes.Add(L.T("Больше не отталкиваются от тела (проходят сквозь): ", "No longer collide with the body (clip through): ") + string.Join(", ", plan.DropColliders.Select(p => p.Name)) + ".");
                    }
                });
            }
            finally { EditorUtility.ClearProgressBar(); }

            EditorSceneManager.MarkSceneDirty(_avatar.scene);
            Diagnose();
            for (int level = 1; level <= TextureLever.MaxLevel; level++)
            {
                var tex = _report?.Rows.FirstOrDefault(r => r.Category == "TextureMegabytes");
                if (tex == null || tex.Rating <= _target) break;
                TextureLever.Apply(_avatar, _log, level);
                _changes.Add(L.T($"Текстуры уменьшены сильнее (уровень {level}).", $"Textures reduced further (level {level})."));
                Diagnose();
            }
            _optimized = true;
            ScanItems();
            SaveReport();
        }

        void RevertAll()
        {
            if (!EditorUtility.DisplayDialog(Brand.Name, L.T("Вернуть аватар к состоянию до оптимизации? Текстуры и материалы тоже вернутся.",
                    "Revert the avatar to how it was before optimizing? Textures and materials will be restored too."), L.T("Вернуть", "Revert"), L.T("Отмена", "Cancel"))) return;
            if (_undoGroup >= 0) { Undo.RevertAllDownToGroup(_undoGroup); _undoGroup = -1; }
            else
                EditorUtility.DisplayDialog(Brand.Name, L.T("Полный откат компонентов работает только до перезапуска Unity. Текстуры и материалы верну, остальное можно откатить через Edit → Undo.",
                    "A full revert of components only works until Unity restarts. Textures and materials will be restored; undo the rest via Edit → Undo."), "OK");
            int tex = TextureLever.Restore(_avatar);
            int mats = MaterialHarmonizer.Restore(_avatar);
            _log.Add(L.T($"Откат: текстур {tex}, материалов {mats}.", $"Reverted: {tex} textures, {mats} materials."));
            _changes.Clear();
            _optimized = false;
            _rankBefore = Rank.None;
            _itemDrop.Clear();
            _userDrop.Clear();
            _userKeep.Clear();
            EditorSceneManager.MarkSceneDirty(_avatar.scene);
            Diagnose();
            ScanItems();
        }

        // ---------- отчёт для разработчика ----------
        void SaveReport()
        {
            try
            {
                var sockets = SpsLever.FindSockets(_avatar);
                var groups = SpsLever.FindContactGroups(_avatar, _report);
                var sps = new System.Text.StringBuilder();
                foreach (var s in sockets)
                {
                    var cost = _report?.Sources != null && _report.Sources.TryGetValue("SPS сокет: " + s.Socket.gameObject.name, out var v) ? $"контактов {v.contacts}, света {v.lights}" : "цена неизвестна";
                    sps.AppendLine($"  сокет {s.Name}: {cost}{(s.Safe ? "" : " (на объекте есть ещё что-то)")}");
                }
                foreach (var g in groups) sps.AppendLine($"  группа {g.Name}: контактов {g.Contacts} ({g.Comps.Count} компонентов)");
                var items = new System.Text.StringBuilder();
                if (_advice != null)
                {
                    items.AppendLine($"Нужно для {_target}: " + string.Join(", ", _advice.Need.Select(kv => $"{kv.Key} {PerfReport.Fmt(Math.Round(kv.Value, 1))}")));
                    items.AppendLine("Совет убрать: " + string.Join("; ", _advice.Pick.Select(i => $"{i.Name} [{i.CostText()}]")));
                    if (_advice.Left.Count > 0) items.AppendLine("Не покрыто вещами: " + string.Join(", ", _advice.Left.Select(kv => $"{kv.Key} {PerfReport.Fmt(Math.Round(kv.Value, 1))}")));
                }
                if (_items != null)
                    foreach (var i in _items.OrderByDescending(i => i.Cost.Sum(kv => kv.Value / ItemScan.Scale(kv.Key))))
                        items.AppendLine($"  {(i.Removed ? "[убрано] " : "")}{(i.Protected ? "[основа] " : "")}{i.Name} ({i.KindText}{(i.InMenu ? ", в меню" : "")}{(i.HiddenByDefault ? ", скрыто" : "")}): {i.CostText()}");
                var text = $"Avatar: {_avatar.name}\nРежим: {(_tab == 0 ? "простой" : "ПРО")}, язык {(L.En ? "en" : "ru")}\n{_report?.Error}\n{_report?.ToText(_target)}\nЧто изменилось:\n{string.Join("\n", _changes)}\n\nВещи:\n{items}\nSPS и контакты в окне:\n{sps}\nPhysBone plan:\n{_pbPlanText}\nLog:\n{string.Join("\n", _log)}";
                File.WriteAllText(ReportPath, text);
                Debug.Log($"[{Brand.Short}] " + L.T("отчёт сохранён: ", "report saved: ") + ReportPath);
            }
            catch (Exception e) { Debug.LogWarning($"[{Brand.Short}] " + L.T("не удалось сохранить отчёт: ", "couldn't save the report: ") + e.Message); }
        }
    }
}
