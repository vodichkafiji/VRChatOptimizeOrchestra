using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TurboOptimizer
{
    // Выравнивает материалы Poiyomi, чтобы d4rk смог их склеить, не меняя внешний вид:
    // - функции, которые включены, но ничего не делают (сила 0), выключаются;
    // - функции, которые есть у одних материалов группы, включаются у остальных с нулевой силой;
    // - непрозрачным/cutout материалам ставится общая очередь рендера.
    // Работает на КОПИЯХ материалов (Assets/TurboOptimizer/Materials/<аватар>), оригиналы не меняются.
    public static class MaterialHarmonizer
    {
        class Feature
        {
            public string Toggle, Keyword, Name;
            public string NeedsEmpty; // функция по наличию текстуры: включать с нулевой силой можно только без неё
            public string OnlyIfNeutral; // под-настройка другой функции: включать, только если та выключена или «пустая»
            public System.Func<Material, bool> Safe; // своя проверка, можно ли включить без изменения вида
            public (string prop, float value)[] Neutral; // значения, при которых функция ничего не меняет
        }

        static (string, float)[] P(params (string, float)[] p) => p;

        // Только функции, для которых точно известно «нулевое» состояние.
        static readonly Feature[] Features =
        {
            new Feature { Name = "Adjust Colors", Toggle = "_MainColorAdjustToggle", Keyword = "COLOR_GRADING_HDR",
                Neutral = P(("_Saturation", 0), ("_MainChromatize", 0), ("_MainBrightness", 0), ("_MainGamma", 1), ("_MainHueShiftToggle", 0)) },
            new Feature { Name = "UV Tile Discard", Toggle = "_EnableUDIMDiscardOptions", Keyword = "POI_UDIMDISCARD",
                Neutral = Udim() },
            new Feature { Name = "Detail Texture", Toggle = "_DetailEnabled", Keyword = "FINALPASS",
                Neutral = P(("_DetailTexIntensity", 0), ("_DetailNormalMapScale", 0)) },
            new Feature { Name = "AudioLink", Toggle = "_EnableAudioLink", Keyword = "POI_AUDIOLINK",
                Neutral = P() }, // сам по себе ничего не рисует — эффекты включаются отдельно
            new Feature { Name = "Matcap 1", Toggle = "_Matcap2Enable", Keyword = "COLOR_GRADING_HDR_3D",
                Neutral = P(("_Matcap2Intensity", 0), ("_Matcap2EmissionStrength", 0)) },
            new Feature { Name = "Matcap 2", Toggle = "_Matcap3Enable", Keyword = "POI_MATCAP2",
                Neutral = P(("_Matcap3Intensity", 0), ("_Matcap3EmissionStrength", 0)) },
            new Feature { Name = "Rim Lighting", Toggle = "_EnableRimLighting", Keyword = "_GLOSSYREFLECTIONS_OFF",
                Neutral = P(("_RimBlendStrength", 0), ("_RimStrength", 0)) },
            new Feature { Name = "Emission", Toggle = "_EnableEmission", Keyword = "_EMISSION",
                Neutral = P(("_EmissionStrength", 0)) },
            new Feature { Name = "Decal 0", Toggle = "_DecalEnabled", Keyword = "GEOM_TYPE_BRANCH",
                Neutral = P(("_DecalBlendAlpha", 0), ("_DecalEmissionStrength", 0), ("_DecalOverrideAlpha", 0)) },
            new Feature { Name = "Decal 1", Toggle = "_DecalEnabled1", Keyword = "POI_DECAL_1",
                Neutral = P(("_DecalBlendAlpha1", 0), ("_DecalEmissionStrength1", 0), ("_DecalOverrideAlpha1", 0)) },
            new Feature { Name = "Decal 2", Toggle = "_DecalEnabled2", Keyword = "POI_DECAL_2",
                Neutral = P(("_DecalBlendAlpha2", 0), ("_DecalEmissionStrength2", 0), ("_DecalOverrideAlpha2", 0)) },
            new Feature { Name = "Emission 2", Toggle = "_EnableEmission2", Keyword = "POI_EMISSION_2",
                Neutral = P(("_EmissionStrength2", 0)) },
            // своя нормаль матcapа: безопасно включить, только если сам матcap у материала выключен или «пустой»
            // (_Matcap3CustomNormal стоит в разделе _Matcap4 = «Matcap 3», _Matcap1CustomNormal — в разделе _Matcap2 = «Matcap 1»)
            new Feature { Name = "Matcap 3 Custom Normal", Toggle = "_Matcap3CustomNormal", Keyword = "POI_MATCAP3_CUSTOM_NORMAL",
                Neutral = P(), OnlyIfNeutral = "Matcap 3" },
            new Feature { Name = "Matcap 1 Custom Normal", Toggle = "_Matcap1CustomNormal", Keyword = "POI_MATCAP1_CUSTOM_NORMAL",
                Neutral = P(), OnlyIfNeutral = "Matcap 1" },
            new Feature { Name = "Environmental Rim", Toggle = "_EnableEnvironmentalRim", Keyword = "POI_ENVIRORIM",
                Neutral = P(("_RimEnviroIntensity", 0)) },
            // глобальные маски сами ничего не рисуют; безопасно, если у материала нет своих текстур масок
            // и ни одна функция не ссылается на глобальную маску (все *GlobalMask / *GlobalMaskIndex = Off)
            new Feature { Name = "Global Mask Textures", Toggle = "_GlobalMaskTexturesEnable", Keyword = "POI_GLOBALMASK_TEXTURES",
                Neutral = P(), Safe = GlobalMasksUnused },
            new Feature { Name = "Decal 3", Toggle = "_DecalEnabled3", Keyword = "POI_DECAL_3",
                Neutral = P(("_DecalBlendAlpha3", 0), ("_DecalEmissionStrength3", 0), ("_DecalOverrideAlpha3", 0)) },
            new Feature { Name = "Stylized Specular", Toggle = "_StylizedSpecular", Keyword = "POI_STYLIZED_StylizedSpecular",
                Neutral = P(("_StylizedSpecularStrength", 0)) },
            new Feature { Name = "Dissolve", Toggle = "_EnableDissolve", Keyword = "DISTORT",
                Neutral = P(("_DissolveAlpha", 0), ("_DissolveEdgeWidth", 0), ("_DissolveEdgeEmission", 0)) },
            new Feature { Name = "AO Maps", Toggle = null, Keyword = "PROP_LIGHTINGAOMAPS", NeedsEmpty = "_LightingAOMaps",
                Neutral = P(("_LightDataAOStrengthR", 0), ("_LightDataAOStrengthG", 0), ("_LightDataAOStrengthB", 0), ("_LightDataAOStrengthA", 0)) },
            new Feature { Name = "Rim Lighting 2", Toggle = "_EnableRim2Lighting", Keyword = "POI_RIM2",
                Neutral = P(("_Rim2BlendStrength", 0), ("_Rim2Strength", 0)) },
            new Feature { Name = "Anisotropics", Toggle = "_EnableAniso", Keyword = "POI_ANISOTROPICS",
                Neutral = P(("_Aniso0Strength", 0), ("_Aniso1Strength", 0)) },
            new Feature { Name = "Matcap 0", Toggle = "_MatcapEnable", Keyword = "POI_MATCAP0",
                Neutral = P(("_MatcapIntensity", 0), ("_MatcapEmissionStrength", 0)) },
            new Feature { Name = "Matcap 3", Toggle = "_Matcap4Enable", Keyword = "POI_MATCAP3",
                Neutral = P(("_Matcap4Intensity", 0), ("_Matcap4EmissionStrength", 0)) },
            new Feature { Name = "Emission 1", Toggle = "_EnableEmission1", Keyword = "POI_EMISSION_1",
                Neutral = P(("_EmissionStrength1", 0)) },
            new Feature { Name = "Glitter", Toggle = "_GlitterEnable", Keyword = "_SUNDISK_SIMPLE",
                Neutral = P(("_GlitterBrightness", 0), ("_GlitterBlendType", 0)) },
            // Reflections & Specular (Mochie): при нулевой металличности, отражениях и бликах ничего не меняет
            new Feature { Name = "Reflections & Specular", Toggle = "_MochieBRDF", Keyword = "MOCHIE_PBR",
                Neutral = P(("_MochieReflectionStrength", 0), ("_MochieSpecularStrength", 0), ("_MochieMetallicMultiplier", 0)) },
            new Feature { Name = "CubeMap", Toggle = "_CubeMapEnabled", Keyword = "_CUBEMAP",
                Neutral = P(("_CubeMapBlendAmount", 0), ("_CubeMapEmissionStrength", 0)) },
        };

        // Кубмапы, которые видны только при работающей функции: у «пустых» материалов группы ставим тот же кубмап,
        // что у «живых» (или убираем), чтобы d4rk не спотыкался о разные кубмапы.
        static readonly (string prop, string feature)[] FeatureCubes = { ("_MochieReflCube", "Reflections & Specular"), ("_CubeMap", "CubeMap") };

        // Режимы освещения Poiyomi (KeywordEnum _LightingMode)
        static readonly string[] LightingModes = { "TEXTURERAMP", "MULTILAYER_MATH", "WRAPPED", "SKIN", "SHADEMAP", "FLAT", "REALISTIC", "CLOTH", "SDF" };

        static bool GlobalMasksUnused(Material m)
        {
            for (int i = 0; i < 4; i++)
                if (m.HasProperty($"_GlobalMaskTexture{i}") && m.GetTexture($"_GlobalMaskTexture{i}") != null) return false;
            var s = m.shader;
            for (int i = 0; i < s.GetPropertyCount(); i++)
            {
                var t = s.GetPropertyType(i);
                if (t != UnityEngine.Rendering.ShaderPropertyType.Float && t != UnityEngine.Rendering.ShaderPropertyType.Range
                    && t != UnityEngine.Rendering.ShaderPropertyType.Int) continue;
                var n = s.GetPropertyName(i);
                if ((n.EndsWith("GlobalMask") || n.EndsWith("GlobalMaskIndex") || n.EndsWith("GlobalMaskChannel")) && m.GetFloat(n) != 0) return false;
            }
            return true;
        }

        static (string, float)[] Udim()
        {
            var l = new List<(string, float)> { ("_EnableUDIMFaceDiscardOptions", 0) };
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) l.Add(($"_UDIMDiscardRow{r}_{c}", 0));
            return l.ToArray();
        }

        const int CommonQueue = 2450; // AlphaTest — очередь большинства материалов Poiyomi

        public static string FolderOf(GameObject avatar) => $"Assets/TurboOptimizer/Materials/{Safe(avatar.name)}";
        static string BackupFile(GameObject avatar) => $"Assets/TurboOptimizer/Backups/{Safe(avatar.name)}/materials.txt";

        // aggressive — режим «Good любой ценой»: общий режим освещения в группе и одинаковые размеры/форматы текстур
        // (вид может немного измениться).
        public static void Apply(GameObject avatar, List<string> log, bool unifyTransparent = false, bool aggressive = false)
        {
            var swapped = SwappedMaterials(avatar);
            var backup = ReadBackup(BackupFile(avatar)); // путь копии → путь оригинала

            // 1. кандидаты: Poiyomi без Lock, не подменяются анимацией, не на частицах
            var slots = new List<(Renderer r, int i, Material m)>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (AvatarUtil.IsEditorOnly(r.transform) || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var arr0 = r.sharedMaterials;
                for (int i = 0; i < arr0.Length; i++) if (arr0[i] != null) slots.Add((r, i, arr0[i]));
            }
            // Залоченные (Lock) материалы берём, если можем снять Lock с их копии: исходный шейдер найден,
            // и анимации не меняют свойства материала на их рендерерах (у залоченных такие свойства переименованы).
            // Свойства, которые анимации меняют на рендерерах этого материала. Если у залоченного материала такое свойство
            // переименовано (Thry «Renamed»), после снятия Lock анимация перестанет работать — такие не берём.
            // Если имя совпадает с исходным шейдером — анимация продолжит работать, Lock снимать можно.
            var animatedAll = AnimatedProps(avatar);
            var animByOrig = new Dictionary<Material, HashSet<string>>();
            foreach (var s0 in slots)
            {
                var pth = AnimationUtility.CalculateTransformPath(s0.r.transform, avatar.transform);
                if (!animatedAll.TryGetValue(pth, out var props0)) continue;
                if (!animByOrig.TryGetValue(s0.m, out var set0)) animByOrig[s0.m] = set0 = new HashSet<string>();
                set0.UnionWith(props0);
            }
            var blockedBy = new Dictionary<Material, List<string>>();
            bool AnimationsSurviveUnlock(Material m, Shader original)
            {
                if (!animByOrig.TryGetValue(m, out var props1)) return true;
                var missing = props1.Where(pr => original.FindPropertyIndex(pr) < 0).ToList();
                if (missing.Count > 0) blockedBy[m] = missing;
                return missing.Count == 0;
            }
            bool unlockAvailable = ThryUnlock != null;
            bool Candidate(Material m)
            {
                if (swapped.Contains(m) || swapped.Contains(OriginalOf(m, backup))) return false;
                var orig = OriginalOf(m, backup) ?? m;
                if (!IsLocked(orig)) return m.shader.name.StartsWith(".poiyomi/");
                if (!unlockAvailable) return false;
                var os = OriginalShader(orig);
                return os != null && os.name.StartsWith(".poiyomi/") && AnimationsSurviveUnlock(m, os);
            }
            var mats0 = slots.Select(s => s.m).Distinct().Where(Candidate).ToList();
            var skipped = slots.Select(s => s.m).Distinct().Where(m => !Candidate(m)).Select(m => m.name).ToList();
            if (skipped.Count > 0) log.Add(L.T("= не трогаю (Lock с анимацией, подмена анимацией или другой шейдер): ", "= skipped (locked with animations, swapped by animation or other shader): ") + string.Join(", ", skipped));
            foreach (var kv in blockedBy)
                log.Add($"  {kv.Key.name}: {L.T("Lock не снять — анимации меняют", "can't unlock — animations change")} {string.Join(", ", kv.Value.Take(8))}{(kv.Value.Count > 8 ? " …" : "")}");

            // 2. копии материалов
            Directory.CreateDirectory(FolderOf(avatar));
            var copyOf = new Dictionary<Material, Material>();
            var toUnlock = new List<Material>();
            foreach (var m in mats0)
            {
                var path = AssetDatabase.GetAssetPath(m);
                if (backup.ContainsKey(path))
                {
                    // уже наша копия — пересобираем с оригинала, чтобы повторный запуск давал тот же результат
                    var orig = AssetDatabase.LoadAssetAtPath<Material>(backup[path]);
                    if (orig != null)
                    {
                        if (IsLocked(orig))
                        {
                            var name = m.name;
                            EditorUtility.CopySerialized(orig, m); // целиком, с тегами Lock — чтобы снова снять Lock
                            m.name = name;
                            toUnlock.Add(m);
                        }
                        else
                        {
                            m.CopyPropertiesFromMaterial(orig);
                            m.shaderKeywords = orig.shaderKeywords;
                            m.renderQueue = orig.renderQueue;
                        }
                    }
                    copyOf[m] = m; continue;
                }
                var copyPath = AssetDatabase.GenerateUniqueAssetPath($"{FolderOf(avatar)}/{Safe(m.name)}.mat");
                var copy = new Material(m) { name = m.name };
                AssetDatabase.CreateAsset(copy, copyPath);
                backup[copyPath] = path;
                copyOf[m] = copy;
                if (IsLocked(m)) toUnlock.Add(copy);
            }
            WriteBackup(BackupFile(avatar), backup);
            if (toUnlock.Count > 0)
            {
                // Снимаем Lock только с копий; оригиналы и их залоченные шейдеры остаются как были.
                // Перед загрузкой Thry залочит всё снова, но уже после того, как d4rk склеит материалы.
                try
                {
                    var bar = ThryUnlock.GetParameters()[1].ParameterType;
                    ThryUnlock.Invoke(null, new object[] { toUnlock, System.Enum.ToObject(bar, 0) });
                }
                catch (System.Exception e) { log.Add("! Thry unlock: " + (e.InnerException ?? e).Message); }
                var still = toUnlock.Where(IsLocked).ToList();
                foreach (var m in still)
                {
                    // не получилось — оставляем на слотах оригинал, копию не используем
                    var orig = copyOf.First(kv => kv.Value == m).Key;
                    copyOf.Remove(orig);
                }
                int ok = toUnlock.Count - still.Count;
                if (ok > 0) log.Add(L.T($"+ Lock снят с копий {ok} материалов (оригиналы не тронуты): ", $"+ Unlocked copies of {ok} materials (originals untouched): ") + string.Join(", ", toUnlock.Except(still).Select(m => m.name)));
                if (still.Count > 0) log.Add(L.T("! не удалось снять Lock: ", "! couldn't unlock: ") + string.Join(", ", still.Select(m => m.name)));
            }
            foreach (var s in slots.Where(s => copyOf.ContainsKey(s.m)))
            {
                var arr = s.r.sharedMaterials;
                if (arr[s.i] == copyOf[s.m]) continue;
                Undo.RecordObject(s.r, "Orchestra materials");
                arr[s.i] = copyOf[s.m];
                s.r.sharedMaterials = arr;
                PrefabUtility.RecordPrefabInstancePropertyModifications(s.r);
            }
            var mats = copyOf.Values.Distinct().ToList();

            // Перевод старых версий Poiyomi (9.3) на текущую пробовали — кожа на Hecate стала чёрной: настройки версий
            // не совпадают по смыслу. Версии шейдера не смешиваем.

            // 3. очередь рендера: непрозрачным и cutout (без смешивания) ставим общую
            int rq = 0;
            foreach (var m in mats)
            {
                if (m.renderQueue == CommonQueue || !Opaque(m) || m.renderQueue < 2000 || m.renderQueue >= 2500 || UsesStencil(m)) continue;
                log.Add($"  {m.name}: {L.T("очередь", "queue")} {m.renderQueue} → {CommonQueue}");
                m.renderQueue = CommonQueue; rq++;
                EditorUtility.SetDirty(m);
            }
            // по желанию: общая очередь и для прозрачных вещей (2451–2499). Может поменять,
            // какой прозрачный слой рисуется поверх другого — поэтому только по галочке.
            if (unifyTransparent)
            {
                var trans = mats.Where(m => !Opaque(m) && m.renderQueue > CommonQueue && m.renderQueue < 2500 && !UsesStencil(m)).ToList();
                if (trans.Count > 1)
                {
                    int q = trans.GroupBy(m => m.renderQueue).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
                    foreach (var m in trans.Where(m => m.renderQueue != q))
                    {
                        log.Add($"  {m.name} ({L.T("прозрачный", "transparent")}): {L.T("очередь", "queue")} {m.renderQueue} → {q}");
                        m.renderQueue = q; rq++;
                    }
                }
            }

            // 4. функции: выключить «пустые», затем в каждой группе (очередь + Cull + режим) включить общие с нулевой силой
            // свойства, которые анимации меняют на рендерерах с этим материалом: такие функции не выключаем
            var animated = AnimatedProps(avatar);
            var animByMat = new Dictionary<Material, HashSet<string>>();
            foreach (var s in slots)
            {
                var path = AnimationUtility.CalculateTransformPath(s.r.transform, avatar.transform);
                if (!copyOf.TryGetValue(s.m, out var cm) || !animated.TryGetValue(path, out var props)) continue;
                if (!animByMat.TryGetValue(cm, out var set)) animByMat[cm] = set = new HashSet<string>();
                set.UnionWith(props);
            }
            bool Animated(Material m, Feature f) => animByMat.TryGetValue(m, out var set)
                && ((f.Toggle != null && set.Contains(f.Toggle)) || f.Neutral.Any(p => set.Contains(p.prop)));

            int off = 0, on = 0;
            foreach (var m in mats)
                foreach (var f in Features)
                    if (IsOn(m, f) && IsNeutral(m, f) && !Animated(m, f) && f.NeedsEmpty == null) { SetOn(m, f, false); off++; }

            foreach (var g in mats.GroupBy(GroupKey))
            {
                var group = g.ToList();
                if (group.Count < 2) continue;
                foreach (var f in Features)
                {
                    int with = group.Count(m => IsOn(m, f));
                    if (with == 0 || with == group.Count) continue;
                    bool CanEnable(Material m)
                    {
                        if (f.NeedsEmpty != null && m.HasProperty(f.NeedsEmpty) && m.GetTexture(f.NeedsEmpty) != null) return false;
                        if (f.Safe != null && !f.Safe(m)) return false;
                        if (f.OnlyIfNeutral != null)
                        {
                            var parent = Features.First(x => x.Name == f.OnlyIfNeutral);
                            if (IsOn(m, parent) && !IsNeutral(m, parent)) return false;
                        }
                        return true;
                    }
                    var lacking = group.Where(m => !IsOn(m, f)).ToList();
                    // если хоть одному материалу функцию включить нельзя — не включаем никому:
                    // иначе группа расколется по-новому и склеится хуже, чем было (проверено на Full SPS)
                    if (!lacking.All(CanEnable))
                    {
                        log.Add(L.T($"  {f.Name}: не выравниваю в группе «{g.Key}» — у {lacking.Count(m => !CanEnable(m))} материалов её нельзя включить без изменения вида", $"  {f.Name}: not aligned in group «{g.Key}» — {lacking.Count(m => !CanEnable(m))} materials can't enable it without changing the look"));
                        continue;
                    }
                    foreach (var m in lacking)
                    {
                        foreach (var (prop, v) in f.Neutral) if (m.HasProperty(prop)) m.SetFloat(prop, v);
                        SetOn(m, f, true); on++;
                    }
                    log.Add(L.T($"  {f.Name}: включено с нулевой силой у {lacking.Count} материалов группы «{g.Key}»", $"  {f.Name}: enabled at zero strength on {lacking.Count} materials of group «{g.Key}»"));
                }
            }
            foreach (var g in mats.GroupBy(GroupKey).Where(g => g.Count() > 1))
                foreach (var (prop, featName) in FeatureCubes)
                {
                    var f = Features.First(x => x.Name == featName);
                    var live = g.Where(m => m.HasProperty(prop) && IsOn(m, f) && !IsNeutral(m, f)).Select(m => m.GetTexture(prop)).Distinct().ToList();
                    if (live.Count > 1) continue; // у «живых» разные кубмапы — тут ничего не сделать без изменения вида
                    var cube = live.FirstOrDefault();
                    int n = 0;
                    foreach (var m in g.Where(m => m.HasProperty(prop) && (!IsOn(m, f) || IsNeutral(m, f)) && m.GetTexture(prop) != cube)) { m.SetTexture(prop, cube); n++; }
                    if (n > 0) log.Add($"  {prop}: {L.T("общий кубмап у", "same cubemap on")} {n} {L.T("материалов группы", "materials of group")} «{g.Key}»");
                }

            if (aggressive)
            {
                // у непрозрачных материалов операция смешивания альфы ни на что не влияет — ставим общую
                foreach (var g in mats.Where(m => m.HasProperty("_BlendOpAlpha") && Opaque(m)).GroupBy(GroupKey))
                {
                    var op = g.GroupBy(m => m.GetFloat("_BlendOpAlpha")).OrderByDescending(x => x.Count()).First().Key;
                    foreach (var m in g) m.SetFloat("_BlendOpAlpha", op);
                }
                // общий режим освещения в группе — самый частый (вид теней может измениться)
                foreach (var g in mats.Where(m => m.HasProperty("_LightingMode")).GroupBy(GroupKey))
                {
                    var mode = g.GroupBy(m => Mathf.RoundToInt(m.GetFloat("_LightingMode"))).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key;
                    if (mode < 0 || mode >= LightingModes.Length) continue;
                    foreach (var m in g.Where(m => Mathf.RoundToInt(m.GetFloat("_LightingMode")) != mode))
                    {
                        log.Add($"  {m.name}: {L.T("режим освещения", "lighting mode")} {LightingName(m.GetFloat("_LightingMode"))} → {LightingModes[mode]}");
                        m.SetFloat("_LightingMode", mode);
                        foreach (var k in LightingModes) m.DisableKeyword("_LIGHTINGMODE_" + k);
                        m.EnableKeyword("_LIGHTINGMODE_" + LightingModes[mode]);
                    }
                }
                // одинаковые размеры и форматы текстур в группе — иначе d4rk не соберёт их в общий массив
                foreach (var g in mats.GroupBy(GroupKey).Where(g => g.Count() > 1))
                    TextureLever.Unify(avatar, g.ToList(), log);
            }

            // запасной шейдер VRChat (виден, только когда шейдеры у зрителя скрыты) — один на группу
            foreach (var g in mats.GroupBy(GroupKey))
            {
                var tag = g.GroupBy(m => m.GetTag("VRCFallback", false, "None")).OrderByDescending(x => x.Count()).First().Key;
                foreach (var m in g.Where(m => m.GetTag("VRCFallback", false, "None") != tag))
                {
                    log.Add($"  {m.name}: {L.T("запасной шейдер VRChat", "VRChat fallback shader")} {m.GetTag("VRCFallback", false, "None")} → {tag}");
                    m.SetOverrideTag("VRCFallback", tag == "None" ? "" : tag);
                }
            }
            foreach (var m in mats) EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            log.Add(L.T($"+ Материалы: копий {mats.Count} в {FolderOf(avatar)}, очередь выровнена у {rq}, пустых функций выключено {off}, включено с нулевой силой {on}", $"+ Materials: {mats.Count} copies in {FolderOf(avatar)}, queue aligned on {rq}, unused features off {off}, enabled at zero strength {on}"));
        }

        // Вернуть оригинальные материалы на все слоты.
        public static int Restore(GameObject avatar)
        {
            var backup = ReadBackup(BackupFile(avatar));
            int n = 0;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                var arr = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] == null || !backup.TryGetValue(AssetDatabase.GetAssetPath(arr[i]), out var orig)) continue;
                    var m = AssetDatabase.LoadAssetAtPath<Material>(orig);
                    if (m == null) continue;
                    arr[i] = m; changed = true; n++;
                }
                if (!changed) continue;
                Undo.RecordObject(r, "Orchestra materials restore");
                r.sharedMaterials = arr;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }
            return n;
        }

        static string GroupKey(Material m) =>
            // Cull d4rk склеивать не мешает (в отчёте нет такой причины) — в ключ группы не входит
            $"{m.shader.name.Replace(".poiyomi/", "")}, rq {m.renderQueue}";

        static bool IsOn(Material m, Feature f) => m.IsKeywordEnabled(f.Keyword) || (f.Toggle != null && m.HasProperty(f.Toggle) && m.GetFloat(f.Toggle) > 0.5f);

        // путь рендерера → свойства материала, которые меняют анимации
        static Dictionary<string, HashSet<string>> AnimatedProps(GameObject avatar)
        {
            var map = new Dictionary<string, HashSet<string>>();
            var desc = AvatarUtil.Descriptor(avatar);
            foreach (var field in new[] { "baseAnimationLayers", "specialAnimationLayers" })
            {
                if (!(Reflect.GetPath(desc, field) is System.Array layers)) continue;
                foreach (var l in layers)
                {
                    if (!(Reflect.GetPath(l, "animatorController") is RuntimeAnimatorController c) || c == null) continue;
                    foreach (var clip in c.animationClips)
                    {
                        if (clip == null) continue;
                        foreach (var b in AnimationUtility.GetCurveBindings(clip))
                        {
                            if (!b.propertyName.StartsWith("material.")) continue;
                            var prop = b.propertyName.Substring("material.".Length);
                            int dot = prop.IndexOf('.');
                            if (dot > 0) prop = prop.Substring(0, dot); // material._Color.r → _Color
                            if (!map.TryGetValue(b.path, out var set)) map[b.path] = set = new HashSet<string>();
                            set.Add(prop);
                        }
                    }
                }
            }
            return map;
        }

        static bool IsNeutral(Material m, Feature f) =>
            f.Neutral.Length > 0 && f.Neutral.All(p => !m.HasProperty(p.prop) || Mathf.Approximately(m.GetFloat(p.prop), p.value));

        static void SetOn(Material m, Feature f, bool on)
        {
            if (f.Toggle != null && m.HasProperty(f.Toggle)) m.SetFloat(f.Toggle, on ? 1 : 0);
            if (on) m.EnableKeyword(f.Keyword); else m.DisableKeyword(f.Keyword);
        }

        // без смешивания цветов: DstBlend = Zero и пишет глубину
        static bool Opaque(Material m) =>
            (!m.HasProperty("_DstBlend") || Mathf.Approximately(m.GetFloat("_DstBlend"), 0))
            && (!m.HasProperty("_ZWrite") || m.GetFloat("_ZWrite") > 0.5f);

        static bool UsesStencil(Material m) =>
            (m.HasProperty("_StencilPassOp") && m.GetFloat("_StencilPassOp") != 0)
            || (m.HasProperty("_StencilCompareFunction") && m.GetFloat("_StencilCompareFunction") != 8 && m.GetFloat("_StencilCompareFunction") != 0);

        static string LightingName(float v) { int i = Mathf.RoundToInt(v); return i >= 0 && i < LightingModes.Length ? LightingModes[i] : i.ToString(); }

        static bool IsLocked(Material m) => m != null && m.shader != null && m.shader.name.StartsWith("Hidden/Locked/");

        // Исходный (незалоченный) шейдер по тегам, которые оставляет Thry при Lock.
        static Shader OriginalShader(Material m)
        {
            var name = m.GetTag("OriginalShader", false, "");
            var s = string.IsNullOrEmpty(name) ? null : Shader.Find(name);
            if (s != null) return s;
            var guid = m.GetTag("OriginalShaderGUID", false, "");
            return string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
        }

        static System.Reflection.MethodInfo _thryUnlock; static bool _thrySearched;
        static System.Reflection.MethodInfo ThryUnlock
        {
            get
            {
                if (_thrySearched) return _thryUnlock;
                _thrySearched = true;
                var t = Reflect.FindType("Thry.ThryEditor.ShaderOptimizer") ?? Reflect.FindType("Thry.ShaderOptimizer");
                _thryUnlock = t?.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "UnlockMaterials" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType.IsEnum);
                return _thryUnlock;
            }
        }

        static Material OriginalOf(Material m, Dictionary<string, string> backup) =>
            backup.TryGetValue(AssetDatabase.GetAssetPath(m), out var p) ? AssetDatabase.LoadAssetAtPath<Material>(p) : null;

        static HashSet<Material> SwappedMaterials(GameObject avatar)
        {
            var set = new HashSet<Material>();
            var desc = AvatarUtil.Descriptor(avatar);
            foreach (var field in new[] { "baseAnimationLayers", "specialAnimationLayers" })
            {
                if (!(Reflect.GetPath(desc, field) is System.Array layers)) continue;
                foreach (var l in layers)
                {
                    if (!(Reflect.GetPath(l, "animatorController") is RuntimeAnimatorController c) || c == null) continue;
                    foreach (var clip in c.animationClips)
                    {
                        if (clip == null) continue;
                        foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                        {
                            if (!b.propertyName.StartsWith("m_Materials")) continue;
                            foreach (var k in AnimationUtility.GetObjectReferenceCurve(clip, b))
                                if (k.value is Material mm) set.Add(mm);
                        }
                    }
                }
            }
            return set;
        }

        static Dictionary<string, string> ReadBackup(string file)
        {
            var d = new Dictionary<string, string>();
            if (!File.Exists(file)) return d;
            foreach (var line in File.ReadAllLines(file))
            {
                int i = line.IndexOf('\t');
                if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
            }
            return d;
        }

        static void WriteBackup(string file, Dictionary<string, string> d)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllLines(file, d.Select(kv => kv.Key + "\t" + kv.Value));
        }

        static string Safe(string s) => AvatarUtil.SafeFileName(s);
    }
}
