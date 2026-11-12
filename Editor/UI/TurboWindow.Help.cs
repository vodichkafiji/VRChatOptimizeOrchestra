using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    // Справка прямо в плагине: что такое ранг, что делает каждая кнопка, как откатить.
    public partial class TurboWindow
    {
        void DrawHelp()
        {
            using (Card("", L.T("Что такое ранг", "What rank is")))
            {
                GUILayout.Label(L.T(
                    "VRChat оценивает, насколько аватар нагружает компьютеры других игроков: Excellent → Good → Medium → Poor → Very Poor. Ранг = худшая из категорий (полигоны, материалы, физбоны, контакты и т.д.). Многие игроки в настройках скрывают аватары ниже выбранного ранга — тогда вместо тебя они видят заглушку.",
                    "VRChat rates how much an avatar loads other players' computers: Excellent → Good → Medium → Poor → Very Poor. The rank is the worst of all categories (polygons, materials, PhysBones, contacts, etc.). Many players hide avatars below a chosen rank — they see a placeholder instead of you."), Styles.Text);
            }

            using (Card("", L.T("Как пользоваться (простой режим)", "How to use (Easy mode)")))
            {
                Bullet(L.T("Выбери аватар и цель. Начни с Medium — это хороший баланс.", "Pick the avatar and a target. Start with Medium — a good balance."));
                Bullet(L.T("«Проверить» — узнаешь настоящий ранг и что мешает.", "\"Check\" — see the real rank and what's blocking it."));
                Bullet(L.T("Реши про SPS и прозрачные вещи.", "Decide about SPS and transparent items."));
                Bullet(L.T("«Оптимизировать» — плагин сделает всё, что не меняет вид, и покажет, что изменилось.", "\"Optimize\" — the plugin does everything that keeps the look and shows what changed."));
                Bullet(L.T("Если не хватило — отметь вещи из совета и нажми зелёную кнопку ещё раз.", "If it's not enough — tick items from the advice and press the green button again."));
                Bullet(L.T("Ctrl+S и загрузи через VRChat SDK.", "Ctrl+S and upload with the VRChat SDK."));
            }

            using (Card("", L.T("Что плагин меняет", "What the plugin changes")))
            {
                Bullet(L.T("Добавляет на аватар компоненты AAO, d4rk, Meshia — они срабатывают только при загрузке. В сцене аватар выглядит как раньше.",
                           "Adds AAO, d4rk and Meshia components — they only run on upload. In the scene the avatar looks the same."));
                Bullet(L.T("Уменьшает размер импорта текстур для ПК (сами картинки не трогает, бэкап в Assets/TurboOptimizer/Backups).",
                           "Lowers texture import size for PC (image files aren't touched; backup in Assets/TurboOptimizer/Backups)."));
                Bullet(L.T("Делает копии материалов (Assets/TurboOptimizer/Materials), снимает с копий Lock Poiyomi и выравнивает их настройки для склейки. Оригиналы остаются залоченными.",
                           "Makes material copies (Assets/TurboOptimizer/Materials), unlocks locked Poiyomi copies and aligns their settings for merging. Originals stay locked."));
                Bullet(L.T("Сливает физбоны (AAO Merge PhysBone); если нужно для ранга — делает кончики жёсткими и убирает часть коллайдеров.",
                           "Merges PhysBones (AAO Merge PhysBone); if the rank needs it — stiffens tips and removes some colliders."));
                Bullet(L.T("Убранные вещи: части меша — через AAO Remove Mesh By Mask, объекты — тег EditorOnly. Файлы моделей не меняются.",
                           "Removed items: mesh parts via AAO Remove Mesh By Mask, objects via the EditorOnly tag. Model files aren't changed."));
                Bullet(L.T("При загрузке из меню пропадают переключатели, которые управляли только убранными вещами (на копии меню — исходное меню не меняется).",
                           "On upload, menu toggles that only controlled removed items disappear (on a copy of the menu — the original menu isn't changed)."));
            }

            using (Card("", L.T("Как откатить", "How to revert")))
            {
                Bullet(L.T("Кнопка «Вернуть всё как было» в простом режиме — полный откат (до перезапуска Unity).", "\"Revert everything\" in Easy mode — a full revert (until Unity restarts)."));
                Bullet(L.T("Отдельная вещь — кнопка «Вернуть» рядом с ней.", "A single item — the \"Restore\" button next to it."));
                Bullet(L.T("В ПРО: «Откатить текстуры», «Вернуть оригиналы» материалов, или Edit → Undo.", "In PRO: \"Restore textures\", material \"Restore originals\", or Edit → Undo."));
                Bullet(L.T("Самый надёжный способ — сделать копию сцены или аватара перед оптимизацией.", "The safest way is to duplicate the scene or avatar before optimizing."));
            }

            using (Card("", L.T("Частые вопросы", "FAQ")))
            {
                GUILayout.Label(L.T("Почему VRChat SDK показывает ранг хуже?", "Why does the VRChat SDK show a worse rank?"), EditorStyles.boldLabel);
                GUILayout.Label(L.T("SDK считает аватар до сборки, а оптимизаторы работают во время загрузки. Верь рангу из шага «Проверка» — он совпадает с тем, что увидишь в игре.",
                                    "The SDK measures before the build, while optimizers run during upload. Trust the rank from the \"Check\" step — it matches what you'll see in game."), Styles.Text);
                EditorGUILayout.Space(4);
                GUILayout.Label(L.T("Во время проверки выскакивают окна VRCFury или Thry.", "VRCFury or Thry dialogs pop up during the check."), EditorStyles.boldLabel);
                GUILayout.Label(L.T("Это нормально: сборка идёт как при загрузке. У VRCFury жми Auto-Fix, у Thry — Yes.",
                                    "That's normal: the build runs like an upload. Press Auto-Fix for VRCFury and Yes for Thry."), Styles.Text);
                EditorGUILayout.Space(4);
                GUILayout.Label(L.T("Что такое SPS и зачем его выключать?", "What is SPS and why turn it off?"), EditorStyles.boldLabel);
                GUILayout.Label(L.T("SPS (VRCFury) — система взаимодействий 18+. Каждый сокет добавляет много контактов, а старая совместимость с DPS — источники света. Если SPS не нужен, выключить его — самый быстрый способ поднять ранг.",
                                    "SPS (VRCFury) is an 18+ interaction system. Each socket adds many contacts, and its legacy DPS compatibility adds lights. If you don't need SPS, turning it off is the quickest rank boost."), Styles.Text);
                EditorGUILayout.Space(4);
                GUILayout.Label(L.T("Нужен Quest?", "What about Quest?"), EditorStyles.boldLabel);
                GUILayout.Label(L.T("Пока только ПК. Quest — в планах.", "PC only for now. Quest is planned."), Styles.Text);
            }

            using (Card("", L.T("Нужные инструменты", "Required tools")))
            {
                DrawToolsLine();
                GUILayout.Label(L.T("Устанавливаются через VRChat Creator Companion → Manage Project.", "Install them via VRChat Creator Companion → Manage Project."), Styles.Small);
            }
        }
    }
}
