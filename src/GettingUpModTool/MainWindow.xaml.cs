using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GettingUpModTool.Core.Animation;
using GettingUpModTool.Core.Game;
using GettingUpModTool.Core.Formats.BAN;
using GettingUpModTool.Core.Formats.BNM;
using GettingUpModTool.Core.Formats.GAT;
using GettingUpModTool.Core.Formats.MSH;
using GettingUpModTool.Core.Formats.MTM;
using GettingUpModTool.Core.Formats.ST;
using GettingUpModTool.Core.IO;
using GettingUpModTool.Core.Models;
using GettingUpModTool.Views;

namespace GettingUpModTool;

public partial class MainWindow : Window
{
    private string? _currentMshPath;
    private MeshData? _currentMesh;
    private SkeletonData? _currentSkeleton;
    private byte[]? _currentMshBytes;
    private MshStructureAnalysis? _currentMshAnalysis;
    private MshMaterialAnalysis? _currentMaterialAnalysis;
    private readonly Dictionary<int, ImageSource> _sectionTextures = [];
    private string? _currentBnmPath;
    private BnmAnimation? _currentBnm;
    private AnimationSkeletonBinding? _currentAnimationBinding;
    private StTexture? _currentSt;
    private BitmapSource? _currentTexture;
    private GatDocument? _currentGat;
    private BanDocument? _currentBan;
    private MeshData? _animatedMesh;
    private IReadOnlyList<Matrix4x4>? _animatedBoneWorlds;
    private int? _selectedViewerBoneIndex;
    private bool _updatingViewerBoneList;
    private string _viewerBoneListSignature = "";
    private bool _exportInProgress;
    private double _currentAnimationFrame;
    private bool _updatingAnimationUi;
    private bool _isPlaying;
    private DateTime _lastAnimationTick;
    private readonly DispatcherTimer _animationTimer;
    private readonly ViewportController _viewportController;
    private readonly List<GameAsset> _allGameAssets = [];
    private AppSettingsData _settings = new();
    private string? _gameRoot;
    private bool _assetScanInProgress;
    private IReadOnlyList<AnimationFamilyInfo> _animationFamilies = Array.Empty<AnimationFamilyInfo>();
    private IReadOnlyList<CompatibleMeshInfo> _compatibleMeshes = Array.Empty<CompatibleMeshInfo>();
    private bool _fullGameAnalysisInProgress;
    private readonly List<RecentAssetItem> _recentAssets = [];
    private string? _requestedHeadVariantTexturePath;

    private static readonly LanguageOption[] LanguageOptions =
    [
        new("fr", "Français", new Uri("pack://application:,,,/Resources/Flags/fr.png", UriKind.Absolute)),
        new("en", "English", new Uri("pack://application:,,,/Resources/Flags/en.png", UriKind.Absolute)),
        new("ru", "Русский", new Uri("pack://application:,,,/Resources/Flags/ru.png", UriKind.Absolute)),
        new("es", "Español", new Uri("pack://application:,,,/Resources/Flags/es.png", UriKind.Absolute)),
    ];

    private sealed record LanguageOption(string Code, string NativeName, Uri FlagUri);

    private sealed class ViewportSectionRow
    {
        public int SectionIndex { get; init; }
        public string Title { get; init; } = "";
        public string Material { get; init; } = "";
        public string Texture { get; init; } = "";
        public bool IsVisible { get; set; }
    }

    private sealed class ViewerBoneRow
    {
        public int BoneIndex { get; init; }
        public string Name { get; init; } = "";
        public string Display => $"{BoneIndex:00}  {Name}";
        public override string ToString() => Display;
    }

    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.DisplayName;
        VersionText.Text = $"MOD TOOL · v{AppInfo.Version}";
        LanguageBox.ItemsSource = LanguageOptions;
        _viewportController = new ViewportController(ModelViewport);
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _animationTimer.Tick += AnimationTimer_Tick;
        ConfigureAnimationUi();
        Loaded += MainWindow_Loaded;
    }

    /// <summary>
    /// Loads settings and indexes the game while the window is still hidden, so the splash
    /// screen can show real progress. Called once by <see cref="App"/> before <c>Show()</c>.
    /// </summary>
    public async Task InitializeAsync(IProgress<StartupStep>? startup = null)
    {
        startup?.Report(new StartupStep(0.04, LocalizationService.T("Chargement des réglages…")));
        _settings = AppSettingsStore.Load();
        if (!string.IsNullOrWhiteSpace(AppSettingsStore.LastWarning))
            StatusText.Text = "⚠ " + AppSettingsStore.LastWarning;

        startup?.Report(new StartupStep(0.08, LocalizationService.T("Application de la langue…")));
        LocalizationService.SetLanguage(_settings.Language);
        SelectLanguageItem(_settings.Language);
        IncludeOtherFilesCheck.IsChecked = _settings.IncludeOtherFiles;

        startup?.Report(new StartupStep(0.12, LocalizationService.T("Recherche de l'installation du jeu…")));
        string? root = GameLocator.LooksLikeGameRoot(_settings.GameRoot)
            ? _settings.GameRoot
            : await Task.Run(() => GameLocator.DetectGameRoot());

        if (!string.IsNullOrWhiteSpace(root))
        {
            GameRootBox.Text = root;
            await ScanGameAssetsAsync(root, startup);
        }
        else
        {
            AssetSummaryText.Text = LocalizationService.T("Installation non détectée. Clique « Détecter Steam » ou « Parcourir… ».");
            StatusText.Text = LocalizationService.T("Installation de Getting Up non détectée.");
            startup?.Report(new StartupStep(0.9, LocalizationService.T("Installation de Getting Up non détectée.")));
        }

        startup?.Report(new StartupStep(0.96, LocalizationService.T("Préparation de l'accueil…")));
        RefreshExplorerDashboard();
        MainTabs.SelectedItem = HomeTab;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Templates (and their texts) only exist once the window is shown.
        _ = Dispatcher.BeginInvoke(() => LocalizationService.Apply(this), DispatcherPriority.Loaded);
    }

    private void SelectLanguageItem(string? language)
    {
        string code = language is "fr" or "ru" or "es" ? language : "en";
        LanguageBox.SelectedItem = LanguageOptions.First(option => option.Code == code);
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is not LanguageOption option) return;

        
        
        
        LocalizationService.SetLanguage(option.Code);
        _settings.Language = option.Code;
        AppSettingsStore.Save(_settings);
        _ = Dispatcher.BeginInvoke(() => LocalizationService.Apply(this), DispatcherPriority.Loaded);
    }

    private void NavigateTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string target)
            return;

        MainTabs.SelectedItem = target switch
        {
            "HomeTab" => HomeTab,
            "FullGameDbTab" => FullGameDbTab,
            "AssetBrowserTab" => AssetBrowserTab,
            "View3DTab" => View3DTab,
            "MaterialsTab" => MaterialsTab,
            "MeshDiagnosticTab" => MeshDiagnosticTab,
            "MshExpertTab" => MshExpertTab,
            "SkeletonTab" => SkeletonTab,
            "SkinningTab" => SkinningTab,
            "BnmSummaryTab" => BnmSummaryTab,
            "BnmKeysTab" => BnmKeysTab,
            "AnimationDebugTab" => AnimationDebugTab,
            "BnmDiffTab" => BnmDiffTab,
            "StTab" => StTab,
            "GatTab" => GatTab,
            "BanTab" => BanTab,
            "HexTab" => HexTab,
            "MtmStringsTab" => MtmStringsTab,
            "MtmNumbersTab" => MtmNumbersTab,
            "MtmDiffTab" => MtmDiffTab,
            _ => MainTabs.SelectedItem
        };
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles up from every DataGrid/ComboBox/ListBox inside the tabs.
        if (!ReferenceEquals(e.OriginalSource, MainTabs) || MainTabs.SelectedItem is not TabItem tab)
            return;

        // Secondary tabs without their own sidebar entry highlight their parent entry.
        string target = tab.Name switch
        {
            nameof(SkinningTab) => nameof(SkeletonTab),
            nameof(BnmKeysTab) or nameof(AnimationDebugTab) or nameof(BnmDiffTab) => nameof(BnmSummaryTab),
            _ => tab.Name
        };

        foreach (Button button in FindNavButtons(NavPanel))
            NavItem.SetIsActive(button, button.Tag is string tag && tag == target);
    }

    private static IEnumerable<Button> FindNavButtons(DependencyObject parent)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is Button button)
                yield return button;
            else if (child is DependencyObject element)
                foreach (Button nested in FindNavButtons(element))
                    yield return nested;
        }
    }

    private void ShowFullGameDb_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedItem = FullGameDbTab;
    }

    private void ShowAssetBrowser_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedItem = AssetBrowserTab;
    }

    private void ShowMeshesProps_Click(object sender, RoutedEventArgs e)
    {
        AssetSearchBox.Text = string.Empty;
        AssetFolderFilterBox.SelectedIndex = 0;

        for (int i = 0; i < AssetTypeFilterBox.Items.Count; i++)
        {
            if (AssetTypeFilterBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag as string, ".msh", StringComparison.OrdinalIgnoreCase))
            {
                AssetTypeFilterBox.SelectedIndex = i;
                break;
            }
        }

        RefreshAssetGrid();
        MainTabs.SelectedItem = AssetBrowserTab;
        SelectedAssetText.Text = LocalizationService.T("Mode Meshes / Props : tous les fichiers .msh du jeu. Double-clique pour ouvrir.");
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _currentMshPath = null;
        _currentMshBytes = null;
        _currentMshAnalysis = null;
        _currentMaterialAnalysis = null;
        _sectionTextures.Clear();
        _currentMesh = null;
        _currentSkeleton = null;
        _currentBnmPath = null;
        _currentBnm = null;
        StopPlayback(resetFrame: false);
        ClearAnimationPose();
        _currentAnimationFrame = 0;
        _currentSt = null;
        _currentTexture = null;
        _currentGat = null;
        _currentBan = null;
        HexTextBox.Clear();
        CandidatesGrid.ItemsSource = null;
        MshSectionsGrid.ItemsSource = null;
        MshIssuesGrid.ItemsSource = null;
        MshDiagnosticSummaryText.Text = LocalizationService.T("Ouvre un MSH puis lance le diagnostic.");
        MaterialsGrid.ItemsSource = null;
        MaterialsSummaryText.Text = LocalizationService.T("Aucun matériau analysé.");
        SkeletonGrid.ItemsSource = null;
        SkinningGrid.ItemsSource = null;
        MtmStringsGrid.ItemsSource = null;
        MtmNumbersGrid.ItemsSource = null;
        MtmDiffGrid.ItemsSource = null;
        BnmTracksGrid.ItemsSource = null;
        BnmKeysGrid.ItemsSource = null;
        BnmDiffGrid.ItemsSource = null;
        AnimationDebugGrid.ItemsSource = null;
        AnimationDebugSummaryText.Text = LocalizationService.T("Charge un MSH skinné puis son BNM pour prévisualiser l'animation.");
        StPreviewImage.Source = null;
        StSummaryText.Text = LocalizationService.T("Aucune texture ST chargée.");
        GatGrid.ItemsSource = null;
        GatSummaryText.Text = LocalizationService.T("Aucun GAT chargé.");
        BanGrid.ItemsSource = null;
        BanSummaryText.Text = LocalizationService.T("Aucun BAN chargé.");
        BnmSummaryText.Text = LocalizationService.T("Aucune animation BNM chargée.");
        AutoDetectText.Text = LocalizationService.T("Aucune analyse automatique.");
        SkeletonSummaryText.Text = LocalizationService.T("Aucun squelette détecté.");
        MeshStatsText.Text = LocalizationService.T("Aucun mesh chargé.");
        ConfigureAnimationUi();
        StatusText.Text = LocalizationService.T("Prêt.");
    }
    private sealed class CharacterQuickCard
    {
        public required string SearchKey { get; init; }
        public required string DisplayName { get; init; }
        public int MeshCount { get; init; }
        public string CountLabel => LocalizationService.F(MeshCount == 1 ? "{0:N0} variante" : "{0:N0} variantes", MeshCount);
    }

    private sealed class RecentAssetItem
    {
        public RecentAssetItem(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            KindLabel = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".msh" => "Mesh",
                ".st" => "Texture",
                ".bnm" => "Animation",
                ".gat" => "GAT",
                ".ban" => "BAN",
                ".mtm" => "MTM",
                _ => "Fichier"
            };
        }

        public string Path { get; }
        public string Name { get; }
        public string KindLabel { get; }
    }

    private void OpenInExplorerSafe(string fullPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{fullPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

}
