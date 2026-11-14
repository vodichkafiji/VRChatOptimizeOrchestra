# fiji's Optimize Orchestra ✩✩✩

[![Buy Me A Coffee](https://img.shields.io/badge/Buy%20Me%20A%20Coffee-FFDD00?style=for-the-badge&logo=buy-me-a-coffee&logoColor=black)](https://buymeacoffee.com/vodichkafiji)

**English** · [Русский](#русский)

A Unity editor tool that brings a VRChat avatar to **Medium** or **Good** performance rank — without you having to understand how AAO, d4rkAvatarOptimizer, Meshia and VRCFury work. It's an orchestra conductor for optimizers: it checks the avatar, decides which tool to apply and how hard, and shows the real rank you'll get in game.

- **Real rank, not a guess.** The check builds a copy of the avatar exactly like an upload does (with every optimizer running) and measures it with the VRChat SDK. What you see is what VRChat will show.
- **Easy mode.** Pick the avatar and a target, press *Check*, press *Optimize*. Every step explains what it does.
- **Remove what you don't need.** A list of all clothes, accessories, SPS sockets and contacts with checkboxes — each shows what removing it gains (polygons, PhysBones, materials, texture memory…). Removed items vanish only from the uploaded version; the scene and files stay intact.
- **PRO mode.** Every lever separately: PhysBone plan, material merge analysis, SPS, textures, per-item removal.
- **Non-destructive.** Model and texture files aren't modified; materials are edited on copies; everything can be reverted.
- **English / Русский** switch right in the window.

> PC only for now. Quest support is planned.

## Requirements

- Unity **2022.3** with **VRChat SDK – Avatars 3.10+** (a normal VRChat Creator Companion project)
- These packages in the project (all free):

| Tool | Version | Add to VCC (one click) | Repository URL (manual) |
|---|---|---|---|
| [NDMF](https://github.com/bdunderscore/ndmf) | 1.14+ | [**Add to VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/ndmf.html) | `https://vpm.nadena.dev/vpm.json` |
| [Avatar Optimizer (AAO)](https://vpm.anatawa12.com/avatar-optimizer/en/) | 1.9+ | [**Add to VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/aao.html) | `https://vpm.anatawa12.com/vpm.json` |
| [d4rkAvatarOptimizer](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer) | 4.7+ | [**Add to VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/d4rk.html) | `https://d4rkc0d3r.github.io/vpm-repos/main.json` |
| [Meshia Mesh Simplification](https://github.com/RamType0/Meshia.MeshSimplification) | 3.3+ | [**Add to VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/meshia.html) | `https://RamType0.github.io/VpmRepository/index.json` |
| [VRCFury](https://vrcfury.com) — only if the avatar uses it | any | [**Add to VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/vrcfury.html) | `https://vcc.vrcfury.com` |

All links on one page: **[vodichkafiji.github.io/VRChatOptimizeOrchestra](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/)**

The plugin window shows a green/red line with which of these are installed.

## Installation

**Option A — VCC (recommended, no Git needed)**

1. Add the plugin's repository: [**Add to VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/orchestra.html) → VCC opens → confirm *I understand, Add Repository*.
2. Add the repository of every tool from the table above the same way (**Add to VCC** in each row).
3. In VCC open your project → *Manage Project* → press **+** next to **fiji's Optimize Orchestra**. NDMF, AAO, d4rk and Meshia are installed with it.

Manual way: VCC → *Settings → Packages → Add Repository* → paste `https://vodichkafiji.github.io/VRChatOptimizeOrchestra/vpm.json` (and the tool addresses from the table).

> The `.json` addresses are for **VCC only** — Unity's Package Manager can't use them.

**Option B — Unity Package Manager (git URL)**

Needs [Git](https://git-scm.com/download/win) installed on the computer (restart Unity after installing it) and the tools from the table already in the project.

1. In Unity: *Window → Package Manager → + → Add package from git URL…*
2. Paste `https://github.com/vodichkafiji/VRChatOptimizeOrchestra.git` and press *Add*.

**Option C — download**

1. *Code → Download ZIP* on this page.
2. Unpack it into your project's `Packages` folder, so you get `Packages/VRChatOptimizeOrchestra/package.json`.

> Install only one way. If the plugin is already in the project, adding it again fails.

Then open **Tools → fiji's Optimize Orchestra**.

## How to use (Easy mode)

> Tip: duplicate your avatar (or the scene) before optimizing — the safest undo there is.

1. **Avatar and target.** Drag the avatar root (the object with *VRC Avatar Descriptor*) into the field. Choose a target: *Poor*, *Medium* (a good start) or *Good*.
2. **Check.** Press *Check*. The tool builds a test copy and shows the real rank and what's holding it back, with an explanation for every category. VRCFury or Thry dialogs may pop up — press *Auto-Fix* / *Yes*, that's normal.
3. **Settings.** Keep SPS or turn it off completely; allow merging transparent materials (can change how see-through layers stack). **Reach the target at any cost** — for Good on complex avatars: turns off physics of small things and stiffens long hair, gives merged materials one lighting mode, makes textures the same size and format, simplifies all meshes except the face harder, and unlocks locked materials even when animations change them (those menu toggles — e.g. eye color, decals — stop working). The look changes noticeably; everything can be reverted.
4. **What to remove (optional).** Tick items you don't need. Each one shows what removing it gains.
5. **Optimize.** One button applies everything that keeps the look: mesh and material merging, smaller textures, polygon reduction, PhysBone merging, and — only if the rank needs it — stiff PhysBone tips and fewer colliders. Then it checks again and shows *before → after* and a list of what changed.
6. **Not enough?** The tool suggests which items to remove to reach the target and ticks them. Untick anything you want to keep and press *Optimize* again. If the target isn't realistic for this avatar, it says so.
7. **Upload.** *Ctrl+S*, then upload with the VRChat SDK as usual. The SDK window may show a worse rank — it measures *before* the optimizers run. Trust the rank from step 2.

## What it changes

| What | How | Undo |
|---|---|---|
| Meshes, materials, unused bones/blendshapes | Adds AAO *Trace And Optimize* and d4rkAvatarOptimizer components; they run only on upload | Remove the components / *Revert everything* |
| Polygons | Meshia component with a triangle budget for the target (30% kept for the face) | Same |
| Textures | Lowers the PC import size; image files untouched. Backup in `Assets/TurboOptimizer/Backups` | *Restore textures* (PRO) |
| Materials | Copies in `Assets/TurboOptimizer/Materials`; locked Poiyomi copies are unlocked (originals stay locked) and settings aligned so d4rk can merge them. *At any cost:* one lighting mode per group, same-size textures | *Restore originals* (PRO) |
| PhysBones | AAO *Merge PhysBone*; stiff tips / fewer colliders only when needed | Edit → Undo / *Revert everything* |
| Removed items | Mesh parts via AAO *Remove Mesh By Mask*, objects via the `EditorOnly` tag | *Restore* next to the item |
| Menu | On upload, toggles that only controlled removed items are dropped from a copy of the menu | Restore the item |
| SPS | Turns off legacy DPS compatibility (lights), or the whole SPS if you choose | *Revert everything* |

*Revert everything* in Easy mode does a full rollback until Unity is restarted.

## FAQ

**Why does my avatar look slightly different?** Polygon reduction and smaller textures are tuned to be hard to notice, but you can raise the target to *Poor* for a lighter touch or skip items in PRO mode.

**Medium/Good isn't reached even after optimizing.** Some avatars simply have too much (many materials on separate clothes, lots of PhysBones). The tool tells you what to remove; if even that's not enough, it warns that the target isn't realistic.

**Something broke during the check.** Open the Console — usually one of the tools (AAO, VRCFury, d4rk) reports the reason. After every check the tool writes `TurboOptimizerReport.txt` to the project root — attach it to an issue.

## Credits

Built on top of the great work of [anatawa12 (AAO)](https://github.com/anatawa12/AvatarOptimizer), [d4rkc0d3r](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer), [RamType0 (Meshia)](https://github.com/RamType0/Meshia.MeshSimplification), [bd_ (NDMF)](https://github.com/bdunderscore/ndmf) and [VRCFury](https://vrcfury.com). This tool only configures them — all the heavy lifting is theirs.

---

## Русский

[English](#readme) · **Русский**

Инструмент для Unity, который доводит VRChat-аватар до ранга **Medium** или **Good** — без нужды разбираться, как работают AAO, d4rkAvatarOptimizer, Meshia и VRCFury. Это дирижёр оркестра оптимизаторов: проверяет аватар, решает, какой инструмент применить и насколько сильно, и показывает настоящий ранг, который будет в игре.

- **Настоящий ранг, а не прогноз.** Проверка собирает копию аватара точно так же, как при загрузке (со всеми оптимизаторами), и меряет её VRChat SDK. Что видишь здесь — то и покажет VRChat.
- **Простой режим.** Выбери аватар и цель, нажми «Проверить», нажми «Оптимизировать». Каждый шаг объясняет, что делает.
- **Убери ненужное.** Список всей одежды, аксессуаров, SPS-сокетов и контактов с галочками — у каждой вещи видно, что даст её удаление (полигоны, физбоны, материалы, память текстур…). Вещи пропадают только из загружаемой версии; сцена и файлы остаются.
- **ПРО-режим.** Каждый рычаг отдельно: план физбонов, разбор склейки материалов, SPS, текстуры, удаление вещей.
- **Ничего не портит.** Файлы моделей и текстур не меняются; материалы правятся на копиях; всё можно откатить.
- **Переключатель English / Русский** прямо в окне.

> Пока только ПК. Quest — в планах.

## Что нужно

- Unity **2022.3** и **VRChat SDK – Avatars 3.10+** (обычный проект из VRChat Creator Companion)
- В проекте должны стоять пакеты (все бесплатные):

| Инструмент | Версия | Добавить в VCC (в один клик) | Адрес репозитория (вручную) |
|---|---|---|---|
| [NDMF](https://github.com/bdunderscore/ndmf) | 1.14+ | [**Добавить в VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/ndmf.html) | `https://vpm.nadena.dev/vpm.json` |
| [Avatar Optimizer (AAO)](https://vpm.anatawa12.com/avatar-optimizer/en/) | 1.9+ | [**Добавить в VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/aao.html) | `https://vpm.anatawa12.com/vpm.json` |
| [d4rkAvatarOptimizer](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer) | 4.7+ | [**Добавить в VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/d4rk.html) | `https://d4rkc0d3r.github.io/vpm-repos/main.json` |
| [Meshia Mesh Simplification](https://github.com/RamType0/Meshia.MeshSimplification) | 3.3+ | [**Добавить в VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/meshia.html) | `https://RamType0.github.io/VpmRepository/index.json` |
| [VRCFury](https://vrcfury.com) — только если он есть на аватаре | любая | [**Добавить в VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/vrcfury.html) | `https://vcc.vrcfury.com` |

Все ссылки на одной странице: **[vodichkafiji.github.io/VRChatOptimizeOrchestra](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/)**

В окне плагина есть строка, где зелёным/красным видно, что из этого установлено.

## Установка

**Вариант А — VCC (рекомендуется, Git не нужен)**

1. Добавь репозиторий плагина: [**Добавить в VCC**](https://vodichkafiji.github.io/VRChatOptimizeOrchestra/add/orchestra.html) → откроется VCC → подтверди *I understand, Add Repository*.
2. Так же добавь репозиторий каждого инструмента из таблицы выше (**Добавить в VCC** в каждой строке).
3. В VCC открой проект → *Manage Project* → нажми **+** у **fiji's Optimize Orchestra**. NDMF, AAO, d4rk и Meshia установятся вместе с ним.

Вручную: VCC → *Settings → Packages → Add Repository* → вставь `https://vodichkafiji.github.io/VRChatOptimizeOrchestra/vpm.json` (и адреса инструментов из таблицы).

> Адреса `.json` — **только для VCC**, Package Manager в Unity их не понимает.

**Вариант Б — Unity Package Manager (git URL)**

Нужен установленный на компьютере [Git](https://git-scm.com/download/win) (после установки перезапусти Unity) и инструменты из таблицы уже в проекте.

1. В Unity: *Window → Package Manager → + → Add package from git URL…*
2. Вставь `https://github.com/vodichkafiji/VRChatOptimizeOrchestra.git` и нажми *Add*.

**Вариант В — скачать**

1. На этой странице *Code → Download ZIP*.
2. Распакуй в папку `Packages` своего проекта, чтобы получилось `Packages/VRChatOptimizeOrchestra/package.json`.

> Ставь только одним способом. Если плагин уже есть в проекте, повторное добавление выдаст ошибку.

Потом открой **Tools → fiji's Optimize Orchestra**.

## Как пользоваться (простой режим)

> Совет: перед оптимизацией сделай копию аватара (или сцены) — это самый надёжный откат.

1. **Аватар и цель.** Перетащи корень аватара (объект с *VRC Avatar Descriptor*) в поле. Выбери цель: *Poor*, *Medium* (хорошее начало) или *Good*.
2. **Проверка.** Нажми «Проверить». Плагин соберёт тестовую копию и покажет настоящий ранг и что ему мешает — с объяснением каждой категории. Могут выскочить окна VRCFury или Thry — жми *Auto-Fix* / *Yes*, это нормально.
3. **Настройки.** Оставить SPS или выключить его целиком; разрешить склейку прозрачных материалов (может поменять, как лежат полупрозрачные слои). **Дожать до цели любой ценой** — для Good на сложных аватарах: выключает физику мелких вещей и делает жёстче длинные пряди, даёт склеиваемым материалам общий режим освещения, делает текстуры одинакового размера и формата, сильнее упрощает все меши, кроме лица, и снимает Lock с материалов даже при анимациях (такие переключатели — цвет глаз, рисунки — перестают работать). Вид заметно меняется; всё откатывается.
4. **Что убрать (по желанию).** Отметь ненужные вещи. У каждой видно, что даст её удаление.
5. **Оптимизация.** Одна кнопка делает всё, что не меняет вид: склейку мешей и материалов, уменьшение текстур и полигонов, слияние физбонов, а если нужно для ранга — жёсткие кончики физбонов и меньше коллайдеров. Потом проверяет ещё раз и показывает «было → стало» и список изменений.
6. **Не хватило?** Плагин подскажет, какие вещи убрать до цели, и сам их отметит. Сними галочки с того, что хочешь оставить, и нажми «Оптимизировать» ещё раз. Если цель для аватара нереальна — он об этом скажет.
7. **Загрузка.** *Ctrl+S*, потом загрузка через VRChat SDK как обычно. Окно SDK может показать ранг хуже — оно считает *до* оптимизаторов. Верь рангу из шага 2.

## Что меняется

| Что | Как | Откат |
|---|---|---|
| Меши, материалы, лишние кости/блендшейпы | Добавляет компоненты AAO *Trace And Optimize* и d4rkAvatarOptimizer; они срабатывают только при загрузке | Удалить компоненты / «Вернуть всё как было» |
| Полигоны | Компонент Meshia с бюджетом треугольников под цель (30% оставлено лицу) | Так же |
| Текстуры | Уменьшает размер импорта для ПК; файлы картинок не трогаются. Бэкап в `Assets/TurboOptimizer/Backups` | «Откатить текстуры» (ПРО) |
| Материалы | Копии в `Assets/TurboOptimizer/Materials`; с залоченных копий Poiyomi снимается Lock (оригиналы остаются залоченными), настройки выровнены, чтобы d4rk мог их склеить. *Любой ценой:* общий режим освещения в группе, текстуры одного размера | «Вернуть оригиналы» (ПРО) |
| Физбоны | AAO *Merge PhysBone*; жёсткие кончики / меньше коллайдеров — только когда нужно | Edit → Undo / «Вернуть всё как было» |
| Убранные вещи | Части меша — через AAO *Remove Mesh By Mask*, объекты — тег `EditorOnly` | «Вернуть» рядом с вещью |
| Меню | При загрузке из копии меню убираются переключатели, которые управляли только убранными вещами | Вернуть вещь |
| SPS | Выключает старую совместимость с DPS (свет) или весь SPS, если выберешь | «Вернуть всё как было» |

«Вернуть всё как было» в простом режиме делает полный откат (до перезапуска Unity).

## Частые вопросы

**Почему аватар выглядит чуть иначе?** Уменьшение полигонов и текстур настроено так, чтобы его было трудно заметить, но можно выбрать цель *Poor* для более мягкой оптимизации или пропустить отдельные шаги в ПРО-режиме.

**Medium/Good не получается даже после оптимизации.** На некоторых аватарах просто слишком много всего (много материалов на отдельной одежде, много физбонов). Плагин подскажет, что убрать; если и этого не хватит — предупредит, что цель нереальна.

**Во время проверки что-то сломалось.** Открой Console — обычно там видно, какой инструмент (AAO, VRCFury, d4rk) пожаловался. После каждой проверки плагин пишет `TurboOptimizerReport.txt` в корень проекта — приложи его к issue.

## Благодарности

Плагин построен на отличной работе [anatawa12 (AAO)](https://github.com/anatawa12/AvatarOptimizer), [d4rkc0d3r](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer), [RamType0 (Meshia)](https://github.com/RamType0/Meshia.MeshSimplification), [bd_ (NDMF)](https://github.com/bdunderscore/ndmf) и [VRCFury](https://vrcfury.com). Он только настраивает их — вся тяжёлая работа на них.
