using System.Collections.Generic;
using UnityEditor;

namespace TurboOptimizer
{
    // Двуязычный интерфейс: каждая строка пишется сразу на русском и английском, язык выбирается в окне.
    public static class L
    {
        const string Pref = "TurboOptimizer.Lang";
        static bool? _en;

        public static bool En
        {
            get { if (_en == null) _en = EditorPrefs.GetString(Pref, DefaultLang()) == "en"; return _en.Value; }
            set { _en = value; EditorPrefs.SetString(Pref, value ? "en" : "ru"); }
        }

        static string DefaultLang() =>
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";

        public static string T(string ru, string en) => En ? en : ru;

        // ---------- категории ранга VRChat ----------
        static readonly Dictionary<string, (string ru, string en)> Names = new Dictionary<string, (string, string)>
        {
            ["PolyCount"] = ("Полигоны", "Polygons"),
            ["MaterialCount"] = ("Материалы", "Materials"),
            ["TextureMegabytes"] = ("Текстуры (МБ видеопамяти)", "Textures (MB of VRAM)"),
            ["PhysBoneComponentCount"] = ("Физбоны", "PhysBones"),
            ["PhysBoneTransformCount"] = ("Кости физбонов", "PhysBone transforms"),
            ["PhysBoneColliderCount"] = ("Коллайдеры физбонов", "PhysBone colliders"),
            ["PhysBoneCollisionCheckCount"] = ("Проверки столкновений", "PhysBone collision checks"),
            ["ContactCount"] = ("Контакты", "Contacts"),
            ["BoneCount"] = ("Кости", "Bones"),
            ["SkinnedMeshCount"] = ("Меши", "Skinned meshes"),
            ["MeshCount"] = ("Простые меши", "Basic meshes"),
            ["LightCount"] = ("Свет", "Lights"),
            ["AudioSourceCount"] = ("Источники звука", "Audio sources"),
            ["ParticleSystemCount"] = ("Системы частиц", "Particle systems"),
            ["ParticleTotalCount"] = ("Частицы", "Particles"),
            ["ParticleMaxMeshPolyCount"] = ("Полигоны частиц", "Particle mesh polygons"),
            ["TrailRendererCount"] = ("Следы (Trail)", "Trail renderers"),
            ["LineRendererCount"] = ("Линии (Line)", "Line renderers"),
            ["ClothCount"] = ("Ткань (Cloth)", "Cloths"),
            ["ClothMaxVertices"] = ("Вершины ткани", "Cloth vertices"),
            ["ConstraintsCount"] = ("Констрейнты", "Constraints"),
            ["ConstraintDepth"] = ("Глубина констрейнтов", "Constraint depth"),
            ["AnimatorCount"] = ("Аниматоры", "Animators"),
            ["PhysicsColliderCount"] = ("Коллайдеры", "Colliders"),
            ["PhysicsRigidbodyCount"] = ("Rigidbody", "Rigidbodies"),
        };

        static readonly Dictionary<string, (string ru, string en)> Explain = new Dictionary<string, (string, string)>
        {
            ["PolyCount"] = ("Сколько треугольников в модели. Уменьшаю автоматически (Meshia), сильнее всего — у мелких деталей.",
                             "How many triangles the model has. Reduced automatically (Meshia), mostly on small details."),
            ["MaterialCount"] = ("Каждый материал — отдельная отрисовка. Склеиваю похожие материалы (d4rk), а если не хватает — советую, какие вещи убрать.",
                                 "Each material is a separate draw call. Similar materials get merged (d4rk); if that's not enough, I suggest items to remove."),
            ["TextureMegabytes"] = ("Сколько видеопамяти занимают картинки. Уменьшаю размер текстур по их роли (лицо остаётся чётким).",
                                    "How much video memory the images take. Textures are downsized by role (the face stays sharp)."),
            ["PhysBoneComponentCount"] = ("Сколько компонентов физики (волосы, юбка, уши…). Сливаю похожие в один — качаются так же.",
                                          "How many physics components (hair, skirt, ears…). Similar ones get merged — they move the same."),
            ["PhysBoneTransformCount"] = ("Сколько костей двигает физика. Если нужно — делаю жёсткими самые кончики длинных цепочек.",
                                          "How many bones physics moves. If needed, the very tips of long chains become stiff."),
            ["PhysBoneColliderCount"] = ("Сколько «стенок», от которых отталкивается физика.", "How many colliders physics bounces off."),
            ["PhysBoneCollisionCheckCount"] = ("Сколько проверок «физика задела тело». Если нужно — убираю коллайдеры у самых дорогих цепочек (они начнут проходить сквозь тело).",
                                               "How many 'physics touched the body' checks. If needed, colliders are removed from the most expensive chains (they'll clip through the body)."),
            ["ContactCount"] = ("Точки касания: headpat, SPS, игрушки. SPS-сокеты дают больше всего.", "Touch points: headpat, SPS, toys. SPS sockets add the most."),
            ["BoneCount"] = ("Кости скелета и одежды. Лишние убирает AAO, остальное уходит вместе с вещами.", "Skeleton and clothing bones. AAO removes unused ones; the rest goes away with items."),
            ["SkinnedMeshCount"] = ("Отдельные меши. Склеиваю в один при загрузке (d4rk).", "Separate meshes. Merged into one on upload (d4rk)."),
            ["MeshCount"] = ("Простые (не skinned) меши. d4rk склеивает их с остальными.", "Basic (non-skinned) meshes. d4rk merges them with the rest."),
            ["LightCount"] = ("Источники света. Чаще всего это старая совместимость SPS с DPS — выключаю её.", "Light sources. Usually it's SPS's legacy DPS compatibility — I turn it off."),
            ["AudioSourceCount"] = ("Источники звука на аватаре. Можно убрать вещь со звуком.", "Audio sources on the avatar. Remove an item with sound."),
            ["ParticleSystemCount"] = ("Эффекты частиц. Можно убрать вещь с эффектом.", "Particle effects. Remove an item with an effect."),
        };

        public static string Cat(string category) => Names.TryGetValue(category, out var n) ? (En ? n.en : n.ru) : category;
        public static string CatExplain(string category) => Explain.TryGetValue(category, out var n) ? (En ? n.en : n.ru) : "";

        public static string RankName(Rank r) => r switch
        {
            Rank.Excellent => "Excellent", Rank.Good => "Good", Rank.Medium => "Medium", Rank.Poor => "Poor", Rank.VeryPoor => "Very Poor", _ => "?",
        };
    }
}
