using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GettingUpModTool;

public static class LocalizationService
{
    public static string CurrentLanguage { get; private set; } = "en";

    private static readonly Dictionary<string, Dictionary<string, string>> Phrases = new(StringComparer.Ordinal)
    {
        ["BIBLIOTHÈQUE"] = new() { ["fr"] = "BIBLIOTHÈQUE", ["en"] = "LIBRARY", ["ru"] = "БИБЛИОТЕКА", ["es"] = "BIBLIOTECA" },
        ["OUTILS TECHNIQUES"] = new() { ["fr"] = "OUTILS TECHNIQUES", ["en"] = "TECHNICAL TOOLS", ["ru"] = "ТЕХНИЧЕСКИЕ ИНСТРУМЕНТЫ", ["es"] = "HERRAMIENTAS TÉCNICAS" },
        ["Raccourci"] = new() { ["fr"] = "Raccourci", ["en"] = "Shortcut", ["ru"] = "Быстрый доступ", ["es"] = "Acceso rápido" },
        ["Glisse un .msh, .st ou .bnm n’importe où dans la fenêtre pour l’ouvrir."] = new() { ["fr"] = "Glisse un .msh, .st ou .bnm n’importe où dans la fenêtre pour l’ouvrir.", ["en"] = "Drop a .msh, .st or .bnm anywhere in the window to open it.", ["ru"] = "Перетащи .msh, .st или .bnm в окно, чтобы открыть файл.", ["es"] = "Arrastra un .msh, .st o .bnm a la ventana para abrirlo." },
        ["Préparation…"] = new() { ["fr"] = "Préparation…", ["en"] = "Preparing…", ["ru"] = "Подготовка…", ["es"] = "Preparando…" },
        ["Que veux-tu modifier ?"] = new() { ["fr"] = "Que veux-tu modifier ?", ["en"] = "What do you want to edit?", ["ru"] = "Что ты хочешь изменить?", ["es"] = "¿Qué quieres modificar?" },
        ["Commence par une catégorie. Le reste de l’interface s’adapte ensuite à ce que tu sélectionnes."] = new() { ["fr"] = "Commence par une catégorie. Le reste de l’interface s’adapte ensuite à ce que tu sélectionnes.", ["en"] = "Start with a category. The rest of the interface adapts to your selection.", ["ru"] = "Начни с категории. Остальной интерфейс адаптируется к выбору.", ["es"] = "Empieza por una categoría. El resto de la interfaz se adapta a tu selección." },
        ["Configuration du jeu"] = new() { ["fr"] = "Configuration du jeu", ["en"] = "Game setup", ["ru"] = "Настройка игры", ["es"] = "Configuración del juego" },
        ["Recherche de l'installation…"] = new() { ["fr"] = "Recherche de l'installation…", ["en"] = "Searching for installation…", ["ru"] = "Поиск установки…", ["es"] = "Buscando instalación…" },
        ["Dossier du jeu"] = new() { ["fr"] = "Dossier du jeu", ["en"] = "Game folder", ["ru"] = "Папка игры", ["es"] = "Carpeta del juego" },
        ["Bibliothèque de ressources"] = new() { ["fr"] = "Bibliothèque de ressources", ["en"] = "Resource Library", ["ru"] = "Библиотека ресурсов", ["es"] = "Biblioteca de recursos" },
        ["Personnages"] = new() { ["fr"] = "Personnages", ["en"] = "Characters", ["ru"] = "Персонажи", ["es"] = "Personajes" },
        ["Textures"] = new() { ["fr"] = "Textures", ["en"] = "Textures", ["ru"] = "Текстуры", ["es"] = "Texturas" },
        ["Objets"] = new() { ["fr"] = "Objets", ["en"] = "Objects", ["ru"] = "Объекты", ["es"] = "Objetos" },
        ["Animations"] = new() { ["fr"] = "Animations", ["en"] = "Animations", ["ru"] = "Анимации", ["es"] = "Animaciones" },
        ["Vue 3D"] = new() { ["fr"] = "Vue 3D", ["en"] = "3D View", ["ru"] = "3D-вид", ["es"] = "Vista 3D" },
        ["Bibliothèque complète"] = new() { ["fr"] = "Bibliothèque complète", ["en"] = "Full library", ["ru"] = "Полная библиотека", ["es"] = "Biblioteca completa" },
        ["Diagnostic du mesh"] = new() { ["fr"] = "Diagnostic du mesh", ["en"] = "Mesh diagnostics", ["ru"] = "Диагностика mesh", ["es"] = "Diagnóstico del mesh" },
        ["Matériaux"] = new() { ["fr"] = "Matériaux", ["en"] = "Materials", ["ru"] = "Материалы", ["es"] = "Materiales" },
        ["Squelette"] = new() { ["fr"] = "Squelette", ["en"] = "Skeleton", ["ru"] = "Скелет", ["es"] = "Esqueleto" },
        ["MSH avancé"] = new() { ["fr"] = "MSH avancé", ["en"] = "Advanced MSH", ["ru"] = "Эксперт MSH", ["es"] = "MSH avanzado" },
        ["Choisir un MSH"] = new() { ["fr"] = "Choisir un MSH", ["en"] = "Choose an MSH", ["ru"] = "Выбрать MSH", ["es"] = "Elegir un MSH" },
        ["Texture ST"] = new() { ["fr"] = "Texture ST", ["en"] = "ST texture", ["ru"] = "Текстура ST", ["es"] = "Textura ST" },
        ["Vue hex"] = new() { ["fr"] = "Vue hex", ["en"] = "Hex view", ["ru"] = "Hex-просмотр", ["es"] = "Vista hex" },
        ["Vue d’ensemble"] = new() { ["fr"] = "Vue d’ensemble", ["en"] = "Overview", ["ru"] = "Обзор", ["es"] = "Vista general" },
        ["Voir les modèles, variantes et textures"] = new() { ["fr"] = "Voir les modèles, variantes et textures", ["en"] = "Browse models, variants and textures", ["ru"] = "Модели, варианты и текстуры", ["es"] = "Ver modelos, variantes y texturas" },
        ["Rechercher et prévisualiser les textures"] = new() { ["fr"] = "Rechercher et prévisualiser les textures", ["en"] = "Search and preview textures", ["ru"] = "Поиск и просмотр текстур", ["es"] = "Buscar y previsualizar texturas" },
        ["Parcourir les objets par catégorie"] = new() { ["fr"] = "Parcourir les objets par catégorie", ["en"] = "Browse objects by category", ["ru"] = "Просмотр объектов по категориям", ["es"] = "Explorar objetos por categoría" },
        ["Trouver et tester les animations"] = new() { ["fr"] = "Trouver et tester les animations", ["en"] = "Find and test animations", ["ru"] = "Поиск и тест анимаций", ["es"] = "Buscar y probar animaciones" },
        ["Accès rapides · personnages"] = new() { ["fr"] = "Accès rapides · personnages", ["en"] = "Quick access · characters", ["ru"] = "Быстрый доступ · персонажи", ["es"] = "Acceso rápido · personajes" },
        ["Clique une carte pour filtrer la fenêtre Personnages"] = new() { ["fr"] = "Clique une carte pour filtrer la fenêtre Personnages", ["en"] = "Click a card to filter the Characters window", ["ru"] = "Нажми карточку, чтобы отфильтровать персонажей", ["es"] = "Haz clic en una tarjeta para filtrar Personajes" },
        ["Récemment ouverts"] = new() { ["fr"] = "Récemment ouverts", ["en"] = "Recently opened", ["ru"] = "Недавно открытые", ["es"] = "Abiertos recientemente" },
        ["session actuelle"] = new() { ["fr"] = "session actuelle", ["en"] = "current session", ["ru"] = "текущий сеанс", ["es"] = "sesión actual" },
        ["Aucun fichier ouvert pour le moment."] = new() { ["fr"] = "Aucun fichier ouvert pour le moment.", ["en"] = "No file opened yet.", ["ru"] = "Файлы пока не открыты.", ["es"] = "Aún no hay archivos abiertos." },
        ["Bibliothèque du jeu"] = new() { ["fr"] = "Bibliothèque du jeu", ["en"] = "Game library", ["ru"] = "Библиотека игры", ["es"] = "Biblioteca del juego" },
        ["Recherche un fichier par nom. Double-clique pour le charger."] = new() { ["fr"] = "Recherche un fichier par nom. Double-clique pour le charger.", ["en"] = "Search for a file by name. Double-click to load it.", ["ru"] = "Найди файл по имени. Двойной щелчок — загрузить.", ["es"] = "Busca un archivo por nombre. Doble clic para cargarlo." },
        ["Aucun asset sélectionné."] = new() { ["fr"] = "Aucun asset sélectionné.", ["en"] = "No asset selected.", ["ru"] = "Ресурс не выбран.", ["es"] = "Ningún recurso seleccionado." },
        ["Aucun mesh chargé."] = new() { ["fr"] = "Aucun mesh chargé.", ["en"] = "No mesh loaded.", ["ru"] = "Mesh не загружен.", ["es"] = "Ningún mesh cargado." },
        // Splash screen
        ["Démarrage…"] = new() { ["fr"] = "Démarrage…", ["en"] = "Starting…", ["ru"] = "Запуск…", ["es"] = "Iniciando…" },
        ["Chargement des réglages…"] = new() { ["fr"] = "Chargement des réglages…", ["en"] = "Loading settings…", ["ru"] = "Загрузка настроек…", ["es"] = "Cargando ajustes…" },
        ["Application de la langue…"] = new() { ["fr"] = "Application de la langue…", ["en"] = "Applying language…", ["ru"] = "Применение языка…", ["es"] = "Aplicando idioma…" },
        ["Recherche de l'installation du jeu…"] = new() { ["fr"] = "Recherche de l'installation du jeu…", ["en"] = "Looking for the game installation…", ["ru"] = "Поиск установки игры…", ["es"] = "Buscando la instalación del juego…" },
        ["Indexation des fichiers du jeu…"] = new() { ["fr"] = "Indexation des fichiers du jeu…", ["en"] = "Indexing game files…", ["ru"] = "Индексация файлов игры…", ["es"] = "Indexando archivos del juego…" },
        ["Préparation de la bibliothèque…"] = new() { ["fr"] = "Préparation de la bibliothèque…", ["en"] = "Preparing the library…", ["ru"] = "Подготовка библиотеки…", ["es"] = "Preparando la biblioteca…" },
        ["Analyse des animations…"] = new() { ["fr"] = "Analyse des animations…", ["en"] = "Analyzing animations…", ["ru"] = "Анализ анимаций…", ["es"] = "Analizando animaciones…" },
        ["Préparation de l'accueil…"] = new() { ["fr"] = "Préparation de l'accueil…", ["en"] = "Preparing the overview…", ["ru"] = "Подготовка обзора…", ["es"] = "Preparando la vista general…" },
        ["Prêt."] = new() { ["fr"] = "Prêt.", ["en"] = "Ready.", ["ru"] = "Готово.", ["es"] = "Listo." },
        ["＋ Ouvrir un mesh"] = new() { ["fr"] = "＋ Ouvrir un mesh", ["en"] = "＋ Open mesh", ["ru"] = "＋ Открыть mesh", ["es"] = "＋ Abrir mesh" },
        ["Effacer"] = new() { ["fr"] = "Effacer", ["en"] = "Clear", ["ru"] = "Очистить", ["es"] = "Limpiar" },
        ["Détecter Steam"] = new() { ["fr"] = "Détecter Steam", ["en"] = "Detect Steam", ["ru"] = "Найти Steam", ["es"] = "Detectar Steam" },
        ["Parcourir…"] = new() { ["fr"] = "Parcourir…", ["en"] = "Browse…", ["ru"] = "Обзор…", ["es"] = "Examinar…" },
        ["Scanner"] = new() { ["fr"] = "Scanner", ["en"] = "Scan", ["ru"] = "Сканировать", ["es"] = "Escanear" },
        ["Rafraîchir"] = new() { ["fr"] = "Rafraîchir", ["en"] = "Refresh", ["ru"] = "Обновить", ["es"] = "Actualizar" },
        ["Trouver les meshes"] = new() { ["fr"] = "Trouver les meshes", ["en"] = "Find meshes", ["ru"] = "Найти mesh", ["es"] = "Buscar meshes" },
        ["Meshes associés"] = new() { ["fr"] = "Meshes associés", ["en"] = "Associated meshes", ["ru"] = "Связанные mesh", ["es"] = "Meshes asociados" },
        ["Associé"] = new() { ["fr"] = "Associé", ["en"] = "Associated", ["ru"] = "Связан", ["es"] = "Asociado" },
        ["Nom correspondant"] = new() { ["fr"] = "Nom correspondant", ["en"] = "Name match", ["ru"] = "Совпадение имени", ["es"] = "Coincidencia de nombre" },
        ["Compatible squelette"] = new() { ["fr"] = "Compatible squelette", ["en"] = "Skeleton compatible", ["ru"] = "Совместимый скелет", ["es"] = "Esqueleto compatible" },
        ["Associé · à vérifier"] = new() { ["fr"] = "Associé · à vérifier", ["en"] = "Associated · check skeleton", ["ru"] = "Связан · проверить скелет", ["es"] = "Asociado · revisar esqueleto" },
        ["Exact"] = new() { ["fr"] = "Exact", ["en"] = "Exact", ["ru"] = "Точно", ["es"] = "Exacto" },
        ["EntityRoot"] = new() { ["fr"] = "EntityRoot", ["en"] = "EntityRoot", ["ru"] = "EntityRoot", ["es"] = "EntityRoot" },
        ["Non confirmé"] = new() { ["fr"] = "Non confirmé", ["en"] = "Unconfirmed", ["ru"] = "Не подтверждено", ["es"] = "No confirmado" },
        ["Probable (+{0} os)"] = new() { ["fr"] = "Probable (+{0} os)", ["en"] = "Likely (+{0} bones)", ["ru"] = "Вероятно (+{0} кост.)", ["es"] = "Probable (+{0} huesos)" },
        ["Autres compatibles"] = new() { ["fr"] = "Autres compatibles", ["en"] = "Other compatible meshes", ["ru"] = "Другие совместимые mesh", ["es"] = "Otros meshes compatibles" },
        ["Diagnostic"] = new() { ["fr"] = "Diagnostic", ["en"] = "Diagnostics", ["ru"] = "Диагностика", ["es"] = "Diagnóstico" },
        ["🖼 Textures"] = new() { ["fr"] = "🖼 Textures", ["en"] = "🖼 Textures", ["ru"] = "🖼 Текстуры", ["es"] = "🖼 Texturas" },
        ["▶ Charger automatiquement"] = new() { ["fr"] = "▶ Charger automatiquement", ["en"] = "▶ Load automatically", ["ru"] = "▶ Загрузить автоматически", ["es"] = "▶ Cargar automáticamente" },
        ["Charger le mesh sélectionné"] = new() { ["fr"] = "Charger le mesh sélectionné", ["en"] = "Load selected mesh", ["ru"] = "Загрузить выбранный mesh", ["es"] = "Cargar mesh seleccionado" },
        ["Charger"] = new() { ["fr"] = "Charger", ["en"] = "Load", ["ru"] = "Загрузить", ["es"] = "Cargar" },
        ["Afficher dans Explorer"] = new() { ["fr"] = "Afficher dans Explorer", ["en"] = "Show in Explorer", ["ru"] = "Показать в Проводнике", ["es"] = "Mostrar en el Explorador" },
        ["Copier chemin"] = new() { ["fr"] = "Copier chemin", ["en"] = "Copy path", ["ru"] = "Копировать путь", ["es"] = "Copiar ruta" },
        ["▶ Play"] = new() { ["fr"] = "▶ Play", ["en"] = "▶ Play", ["ru"] = "▶ Воспроизвести", ["es"] = "▶ Reproducir" },
        ["⏸ Pause"] = new() { ["fr"] = "⏸ Pause", ["en"] = "⏸ Pause", ["ru"] = "⏸ Пауза", ["es"] = "⏸ Pausa" },
        ["■ Stop"] = new() { ["fr"] = "■ Stop", ["en"] = "■ Stop", ["ru"] = "■ Стоп", ["es"] = "■ Detener" },
        ["Voir le résultat 3D →"] = new() { ["fr"] = "Voir le résultat 3D →", ["en"] = "View 3D result →", ["ru"] = "Посмотреть результат в 3D →", ["es"] = "Ver resultado 3D →" },
        ["Tout afficher"] = new() { ["fr"] = "Tout afficher", ["en"] = "Show all", ["ru"] = "Показать всё", ["es"] = "Mostrar todo" },
        ["Appliquer visibilité"] = new() { ["fr"] = "Appliquer visibilité", ["en"] = "Apply visibility", ["ru"] = "Применить видимость", ["es"] = "Aplicar visibilidad" },
        ["Fermer"] = new() { ["fr"] = "Fermer", ["en"] = "Close", ["ru"] = "Закрыть", ["es"] = "Cerrar" },
        ["Rechercher"] = new() { ["fr"] = "Rechercher", ["en"] = "Search", ["ru"] = "Поиск", ["es"] = "Buscar" },
        ["Afficher"] = new() { ["fr"] = "Afficher", ["en"] = "Display", ["ru"] = "Отображение", ["es"] = "Mostrar" },
        ["Vue"] = new() { ["fr"] = "Vue", ["en"] = "View", ["ru"] = "Вид", ["es"] = "Vista" },
        ["Liste"] = new() { ["fr"] = "Liste", ["en"] = "List", ["ru"] = "Список", ["es"] = "Lista" },
        ["Image entière"] = new() { ["fr"] = "Image entière", ["en"] = "Full image", ["ru"] = "Всё изображение", ["es"] = "Imagen completa" },
        ["Choisis une texture dans la liste"] = new() { ["fr"] = "Choisis une texture dans la liste", ["en"] = "Choose a texture from the list", ["ru"] = "Выбери текстуру из списка", ["es"] = "Elige una textura de la lista" },
        ["Aucune texture sélectionnée"] = new() { ["fr"] = "Aucune texture sélectionnée", ["en"] = "No texture selected", ["ru"] = "Текстура не выбрана", ["es"] = "Ninguna textura seleccionada" },
        ["☰ Liste"] = new() { ["fr"] = "☰ Liste", ["en"] = "☰ List", ["ru"] = "☰ Список", ["es"] = "☰ Lista" },
        ["▦ Miniatures"] = new() { ["fr"] = "▦ Miniatures", ["en"] = "▦ Thumbnails", ["ru"] = "▦ Миниатюры", ["es"] = "▦ Miniaturas" },
        ["Ajuster image entière"] = new() { ["fr"] = "Ajuster image entière", ["en"] = "Fit entire image", ["ru"] = "Вписать изображение", ["es"] = "Ajustar imagen completa" },
        ["Charger dans le tool"] = new() { ["fr"] = "Charger dans le tool", ["en"] = "Load into tool", ["ru"] = "Загрузить в программу", ["es"] = "Cargar en la herramienta" },
        ["Bibliothèque d’objets"] = new() { ["fr"] = "Bibliothèque d’objets", ["en"] = "Object library", ["ru"] = "Библиотека объектов", ["es"] = "Biblioteca de objetos" },
        ["Choisis une catégorie, trouve ton objet, puis ouvre-le en 3D ou inspecte ses textures."] = new() { ["fr"] = "Choisis une catégorie, trouve ton objet, puis ouvre-le en 3D ou inspecte ses textures.", ["en"] = "Choose a category, find an object, then open it in 3D or inspect its textures.", ["ru"] = "Выбери категорию и объект, затем открой его в 3D или изучи текстуры.", ["es"] = "Elige una categoría, encuentra un objeto y ábrelo en 3D o inspecciona sus texturas." },
        ["1 · Catégorie"] = new() { ["fr"] = "1 · Catégorie", ["en"] = "1 · Category", ["ru"] = "1 · Категория", ["es"] = "1 · Categoría" },
        ["Filtre la bibliothèque"] = new() { ["fr"] = "Filtre la bibliothèque", ["en"] = "Filter the library", ["ru"] = "Фильтр библиотеки", ["es"] = "Filtra la biblioteca" },
        ["2 · Trouve un objet"] = new() { ["fr"] = "2 · Trouve un objet", ["en"] = "2 · Find an object", ["ru"] = "2 · Найди объект", ["es"] = "2 · Encuentra un objeto" },
        ["Tape un nom ou parcours la liste"] = new() { ["fr"] = "Tape un nom ou parcours la liste", ["en"] = "Type a name or browse the list", ["ru"] = "Введи имя или просмотри список", ["es"] = "Escribe un nombre o explora la lista" },
        ["3 · Inspecte et ouvre"] = new() { ["fr"] = "3 · Inspecte et ouvre", ["en"] = "3 · Inspect and open", ["ru"] = "3 · Просмотри и открой", ["es"] = "3 · Inspecciona y abre" },
        ["Choisis un objet"] = new() { ["fr"] = "Choisis un objet", ["en"] = "Choose an object", ["ru"] = "Выбери объект", ["es"] = "Elige un objeto" },
        ["Ses textures et informations apparaîtront ici."] = new() { ["fr"] = "Ses textures et informations apparaîtront ici.", ["en"] = "Its textures and information will appear here.", ["ru"] = "Здесь появятся его текстуры и сведения.", ["es"] = "Sus texturas e información aparecerán aquí." },
        ["Textures de l'objet"] = new() { ["fr"] = "Textures de l'objet", ["en"] = "Object textures", ["ru"] = "Текстуры объекта", ["es"] = "Texturas del objeto" },
        ["Sélectionne une texture"] = new() { ["fr"] = "Sélectionne une texture", ["en"] = "Select a texture", ["ru"] = "Выбери текстуру", ["es"] = "Selecciona una textura" },
        ["Ouvrir en 3D →"] = new() { ["fr"] = "Ouvrir en 3D →", ["en"] = "Open in 3D →", ["ru"] = "Открыть в 3D →", ["es"] = "Abrir en 3D →" },
        ["🖼 Toutes ses textures"] = new() { ["fr"] = "🖼 Toutes ses textures", ["en"] = "🖼 All textures", ["ru"] = "🖼 Все текстуры", ["es"] = "🖼 Todas sus texturas" },
        ["Uniquement les meshes de personnages et leurs textures. Aucune animation dans cette fenêtre."] = new() { ["fr"] = "Uniquement les meshes de personnages et leurs textures. Aucune animation dans cette fenêtre.", ["en"] = "Character meshes and their textures only. No animations in this window.", ["ru"] = "Только mesh персонажей и их текстуры. В этом окне нет анимаций.", ["es"] = "Solo meshes de personajes y sus texturas. No hay animaciones en esta ventana." },
        ["Rechercher un personnage / mesh"] = new() { ["fr"] = "Rechercher un personnage / mesh", ["en"] = "Search character / mesh", ["ru"] = "Поиск персонажа / mesh", ["es"] = "Buscar personaje / mesh" },
        ["Choisis un mesh"] = new() { ["fr"] = "Choisis un mesh", ["en"] = "Choose a mesh", ["ru"] = "Выбери mesh", ["es"] = "Elige un mesh" },
        ["Les textures liées apparaîtront ici."] = new() { ["fr"] = "Les textures liées apparaîtront ici.", ["en"] = "Related textures will appear here.", ["ru"] = "Связанные текстуры появятся здесь.", ["es"] = "Las texturas relacionadas aparecerán aquí." },
        ["Textures du mesh"] = new() { ["fr"] = "Textures du mesh", ["en"] = "Mesh textures", ["ru"] = "Текстуры mesh", ["es"] = "Texturas del mesh" },
        ["Double-clique un mesh pour l'ouvrir directement en 3D."] = new() { ["fr"] = "Double-clique un mesh pour l'ouvrir directement en 3D.", ["en"] = "Double-click a mesh to open it directly in 3D.", ["ru"] = "Дважды щёлкни mesh, чтобы открыть его в 3D.", ["es"] = "Haz doble clic en un mesh para abrirlo directamente en 3D." },
        ["Voir le mesh en 3D"] = new() { ["fr"] = "Voir le mesh en 3D", ["en"] = "View mesh in 3D", ["ru"] = "Открыть mesh в 3D", ["es"] = "Ver mesh en 3D" },
        ["Sélectionne une texture : l'image se met à jour immédiatement."] = new() { ["fr"] = "Sélectionne une texture : l'image se met à jour immédiatement.", ["en"] = "Select a texture: the image updates immediately.", ["ru"] = "Выбери текстуру: изображение обновится сразу.", ["es"] = "Selecciona una textura: la imagen se actualiza inmediatamente." },
        ["Aucune animation"] = new() { ["fr"] = "Aucune animation", ["en"] = "No animation", ["ru"] = "Нет анимации", ["es"] = "Sin animación" },
        ["Vitesse"] = new() { ["fr"] = "Vitesse", ["en"] = "Speed", ["ru"] = "Скорость", ["es"] = "Velocidad" },
        ["Type"] = new() { ["fr"] = "Type", ["en"] = "Type", ["ru"] = "Тип", ["es"] = "Tipo" },
        ["Nom"] = new() { ["fr"] = "Nom", ["en"] = "Name", ["ru"] = "Имя", ["es"] = "Nombre" },
        ["Dossier"] = new() { ["fr"] = "Dossier", ["en"] = "Folder", ["ru"] = "Папка", ["es"] = "Carpeta" },
        ["Taille"] = new() { ["fr"] = "Taille", ["en"] = "Size", ["ru"] = "Размер", ["es"] = "Tamaño" },
        ["Chemin"] = new() { ["fr"] = "Chemin", ["en"] = "Path", ["ru"] = "Путь", ["es"] = "Ruta" },
        ["État"] = new() { ["fr"] = "État", ["en"] = "Status", ["ru"] = "Состояние", ["es"] = "Estado" },
        ["Visible"] = new() { ["fr"] = "Visible", ["en"] = "Visible", ["ru"] = "Видимый", ["es"] = "Visible" },
        ["Section"] = new() { ["fr"] = "Section", ["en"] = "Section", ["ru"] = "Секция", ["es"] = "Sección" },
        ["Animation"] = new() { ["fr"] = "Animation", ["en"] = "Animation", ["ru"] = "Анимация", ["es"] = "Animación" },
        ["Résultat"] = new() { ["fr"] = "Résultat", ["en"] = "Result", ["ru"] = "Результат", ["es"] = "Resultado" },
        ["Niveau"] = new() { ["fr"] = "Niveau", ["en"] = "Level", ["ru"] = "Уровень", ["es"] = "Nivel" },
        ["Explication"] = new() { ["fr"] = "Explication", ["en"] = "Explanation", ["ru"] = "Описание", ["es"] = "Explicación" },
    };

    private sealed class HookMarker { }
    private static readonly ConditionalWeakTable<DependencyObject, HookMarker> HookedObjects = new();
    private static bool _isUpdatingText;

    private static IEnumerable<KeyValuePair<string, Dictionary<string, string>>> AllPhrases
        => Phrases.Concat(LocalizationCatalog.Extras).Concat(LocalizationTemplates.Entries);

    public static void SetLanguage(string? language)
    {
        CurrentLanguage = language is "fr" or "ru" or "es" ? language : "en";
        foreach (Window window in Application.Current.Windows)
            Apply(window);
    }

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> ExactIndex = new(BuildExactIndex);
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> CandidateMap = new(BuildCandidateMap);
    private static readonly Lazy<Regex> CompositeRegex = new(BuildCompositeRegex);
    private static readonly Lazy<IReadOnlyList<TemplatePattern>> Templates = new(BuildTemplates);

    private static readonly Regex PlaceholderRegex = new(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed record TemplatePattern(Regex Pattern, Dictionary<string, string> Variants);

    public static string F(string template, params object?[] args)
        => string.Format(System.Globalization.CultureInfo.CurrentCulture, T(template), args);

    public static string T(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

        if (ExactIndex.Value.TryGetValue(text, out var exact) && exact.TryGetValue(CurrentLanguage, out string? exactTranslation))
            return exactTranslation;

        if (TryTranslateTemplate(text, out string templated))
            return templated;

        if (LooksLikeAssetPath(text))
            return text;

        
        
        
        return CompositeRegex.Value.Replace(text, match =>
        {
            
            
            bool pathOnLeft = match.Index > 0 && (text[match.Index - 1] == '\\' || text[match.Index - 1] == '/');
            int after = match.Index + match.Length;
            bool pathOnRight = after < text.Length && (text[after] == '\\' || text[after] == '/');
            if (pathOnLeft || pathOnRight)
                return match.Value;

            if (!CandidateMap.Value.TryGetValue(match.Value, out var variants))
                return match.Value;
            return variants.TryGetValue(CurrentLanguage, out string? translated) ? translated : match.Value;
        });
    }

    private static bool LooksLikeAssetPath(string text)
    {
        bool hasSlash = text.Contains('\\');
        bool looksLikeSentence = text.Contains(" : ", StringComparison.Ordinal)
                                 || text.Contains(" — ", StringComparison.Ordinal)
                                 || text.Contains(" · ", StringComparison.Ordinal)
                                 || text.Contains(" → ", StringComparison.Ordinal)
                                 || text.Contains('«') || text.Contains('»')
                                 || text.Contains('!') || text.Contains('?');

        
        
        
        if (hasSlash && !looksLikeSentence)
        {
            if (Regex.IsMatch(text, @"^[A-Za-z]:\\") || text.StartsWith(@"\\", StringComparison.Ordinal))
                return true;

            
            if (!text.Contains(':') && !text.Contains('!') && !text.Contains('?')
                && Regex.IsMatch(text, @"^[^\r\n\\]+(?:\\[^\r\n\\]+)+$"))
                return true;
        }

        return Regex.IsMatch(text,
            @"^[^\r\n]+\.(msh|st|bnm|gat|ban|mtm|gin|cin|plr|cap|smf|fts|obj|gltf)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static Dictionary<string, Dictionary<string, string>> BuildExactIndex()
    {
        var index = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var pair in AllPhrases)
            index.TryAdd(pair.Key, pair.Value);
        foreach (var pair in AllPhrases)
            foreach (string variant in pair.Value.Values)
                if (!string.IsNullOrWhiteSpace(variant))
                    index.TryAdd(variant, pair.Value);
        return index;
    }

    private static Dictionary<string, Dictionary<string, string>> BuildCandidateMap()
    {
        var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var pair in AllPhrases.OrderByDescending(p => p.Key.Length))
        {
            foreach (string candidate in pair.Value.Values.Append(pair.Key).Distinct(StringComparer.Ordinal))
            {

                if (candidate.Length < 4 || PlaceholderRegex.IsMatch(candidate))
                    continue;
                map.TryAdd(candidate, pair.Value);
            }
        }
        return map;
    }

    private static Regex BuildCompositeRegex()
    {
        const string wordChar = @"[\p{L}\p{N}_]";
        IEnumerable<string> alternatives = CandidateMap.Value.Keys
            .OrderByDescending(v => v.Length)
            .Select(candidate =>
            {

                string prefix = char.IsLetterOrDigit(candidate[0]) ? $"(?<!{wordChar})" : "";
                string suffix = char.IsLetterOrDigit(candidate[^1]) ? $"(?!{wordChar})" : "";
                return prefix + Regex.Escape(candidate) + suffix;
            });

        return new Regex(string.Join("|", alternatives), RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private static IReadOnlyList<TemplatePattern> BuildTemplates()
    {
        var templates = new List<(TemplatePattern Template, int LiteralLength)>();
        foreach (var pair in AllPhrases)
        {
            foreach (string variant in pair.Value.Values.Append(pair.Key).Distinct(StringComparer.Ordinal))
            {
                if (!PlaceholderRegex.IsMatch(variant))
                    continue;

                var pattern = new System.Text.StringBuilder("^");
                var seen = new HashSet<string>();
                int last = 0;
                foreach (Match placeholder in PlaceholderRegex.Matches(variant))
                {
                    pattern.Append(Regex.Escape(variant[last..placeholder.Index]));
                    string group = "p" + placeholder.Groups[1].Value;

                    string capture = placeholder.Value.Contains(':') ? @"[-+]?\d[\d\p{Zs},.%]*?" : ".+?";
                    pattern.Append(seen.Add(group) ? $"(?<{group}>{capture})" : $@"\k<{group}>");
                    last = placeholder.Index + placeholder.Length;
                }
                pattern.Append(Regex.Escape(variant[last..])).Append('$');

                int literalLength = PlaceholderRegex.Replace(variant, "").Length;
                var regex = new Regex(pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline);
                templates.Add((new TemplatePattern(regex, pair.Value), literalLength));
            }
        }

        return templates.OrderByDescending(t => t.LiteralLength).Select(t => t.Template).ToList();
    }

    private static bool TryTranslateTemplate(string text, out string translated)
    {
        foreach (TemplatePattern template in Templates.Value)
        {
            Match match = template.Pattern.Match(text);
            if (!match.Success || !template.Variants.TryGetValue(CurrentLanguage, out string? target))
                continue;

            translated = PlaceholderRegex.Replace(target, placeholder =>
            {
                Group value = match.Groups["p" + placeholder.Groups[1].Value];
                return value.Success ? value.Value : placeholder.Value;
            });
            return true;
        }

        translated = text;
        return false;
    }

    public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        => MessageBox.Show(T(message), T(caption), buttons, icon);

    public static void Apply(Window window)
    {
        LocalizeProperty(window, Window.TitleProperty);
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        ApplyElement(window, visited);
    }

    private static void ApplyElement(DependencyObject obj, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(obj)) return;

        Hook(obj);

        if (obj is TextBlock tb)
            LocalizeProperty(tb, TextBlock.TextProperty);

        if (obj is HeaderedContentControl hcc)
            LocalizeProperty(hcc, HeaderedContentControl.HeaderProperty);

        if (obj is HeaderedItemsControl hic)
            LocalizeProperty(hic, HeaderedItemsControl.HeaderProperty);

        if (obj is ContentControl cc)
            LocalizeProperty(cc, ContentControl.ContentProperty);

        if (obj is FrameworkElement fe)
            LocalizeProperty(fe, FrameworkElement.ToolTipProperty);

        if (obj is DataGrid grid)
            foreach (DataGridColumn column in grid.Columns)
                LocalizeProperty(column, DataGridColumn.HeaderProperty);

        if (obj is Visual || obj is System.Windows.Media.Media3D.Visual3D)
        {
            int visualCount = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < visualCount; i++)
                ApplyElement(VisualTreeHelper.GetChild(obj, i), visited);
        }

        
        
        
        if (obj is FrameworkElement || obj is FrameworkContentElement)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(obj))
                if (child is DependencyObject dependencyChild)
                    ApplyElement(dependencyChild, visited);
        }
    }

    private static void Hook(DependencyObject obj)
    {
        if (HookedObjects.TryGetValue(obj, out _)) return;
        HookedObjects.Add(obj, new HookMarker());

        if (obj is TextBlock)
            AddHook(obj, TextBlock.TextProperty);
        if (obj is HeaderedContentControl)
            AddHook(obj, HeaderedContentControl.HeaderProperty);
        if (obj is HeaderedItemsControl)
            AddHook(obj, HeaderedItemsControl.HeaderProperty);
        if (obj is ContentControl)
            AddHook(obj, ContentControl.ContentProperty);
        if (obj is FrameworkElement)
            AddHook(obj, FrameworkElement.ToolTipProperty);
        if (obj is Window)
            AddHook(obj, Window.TitleProperty);
    }

    private static void AddHook(DependencyObject obj, DependencyProperty property)
    {
        DependencyPropertyDescriptor? descriptor = DependencyPropertyDescriptor.FromProperty(property, obj.GetType());
        descriptor?.AddValueChanged(obj, (_, _) => LocalizeProperty(obj, property));
    }

    private static void LocalizeProperty(DependencyObject obj, DependencyProperty property)
    {
        if (_isUpdatingText || obj.GetValue(property) is not string source || string.IsNullOrWhiteSpace(source))
            return;

        string translated = T(source);
        if (string.Equals(source, translated, StringComparison.Ordinal))
            return;

        try
        {
            _isUpdatingText = true;
            
            
            obj.SetCurrentValue(property, translated);
        }
        finally
        {
            _isUpdatingText = false;
        }
    }
}
