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

public partial class MainWindow
{
    private void OpenMsh_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.T("Ouvrir un fichier MSH"),
            Filter = LocalizationService.T("Getting Up mesh (*.msh)|*.msh|Tous les fichiers (*.*)|*.*"),
            InitialDirectory = GetPreferredOpenDirectory() ?? string.Empty
        };
        if (dialog.ShowDialog() != true) return;
        LoadMsh(dialog.FileName);
    }

    private void LoadMsh(string path)
    {
        _currentMshPath = path;
        AddRecentAsset(path);
        _currentMshBytes = File.ReadAllBytes(path);
        _currentMesh = null;
        _currentSkeleton = null;
        _currentAnimationBinding = null;
        _currentMaterialAnalysis = null;
        _sectionTextures.Clear();
        StopPlayback(resetFrame: false);
        ClearAnimationPose();
        _currentSt = null;
        _currentTexture = null;
        _currentGat = null;
        _currentBan = null;
        StPreviewImage.Source = null;
        StSummaryText.Text = LocalizationService.T("Aucune texture ST compagne trouvée.");
        GatGrid.ItemsSource = null;
        GatSummaryText.Text = LocalizationService.T("Aucun GAT compagnon trouvé.");
        BanGrid.ItemsSource = null;
        BanSummaryText.Text = LocalizationService.T("Aucun BAN compagnon trouvé.");
        HexTextBox.Text = HexDumper.Dump(_currentMshBytes);
        CandidatesGrid.ItemsSource = null;
        MshSectionsGrid.ItemsSource = null;
        MshIssuesGrid.ItemsSource = null;
        MshDiagnosticSummaryText.Text = LocalizationService.T("Ouvre un MSH puis lance le diagnostic.");
        MaterialsGrid.ItemsSource = null;
        MaterialsSummaryText.Text = LocalizationService.T("Aucun matériau analysé.");
        SkeletonGrid.ItemsSource = null;
        SkinningGrid.ItemsSource = null;
        AutoDetectText.Text = LocalizationService.T("Analyse automatique en cours...");
        SkeletonSummaryText.Text = LocalizationService.T("Recherche du squelette...");
        StatusText.Text = LocalizationService.F("MSH : {0} — {1:N0} octets", Path.GetFileName(path), _currentMshBytes.Length);

        
        TryDetectSkeleton();
        AnalyzeCurrentMshStructure(selectTab: false);
        AnalyzeCurrentMaterials();
        bool meshDetected = TryAutoAnalyze(preview: false);
        TryAutoLoadCompanions(path);

        if ((_currentMshAnalysis?.Sections.Count ?? 0) > 0 || meshDetected)
            PreviewCurrentMsh();
        else
            MainTabs.SelectedItem = MeshDiagnosticTab;

        ResolveCurrentAnimationBinding();
        RefreshBnmTables();
    }

    private void OpenSt_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.T("Ouvrir une texture ST"),
            Filter = LocalizationService.T("Getting Up texture (*.st)|*.st|Tous les fichiers (*.*)|*.*"),
            InitialDirectory = GetPreferredOpenDirectory() ?? string.Empty
        };
        if (dialog.ShowDialog() != true) return;
        LoadSt(dialog.FileName, selectTab: true);
    }

    private void OpenCharacterBrowser_Click(object sender, RoutedEventArgs e)
    {
        ShowCharacterBrowser();
    }

    private void OpenCharacterQuick_Click(object sender, RoutedEventArgs e)
    {
        string? search = (sender as FrameworkElement)?.Tag as string;
        ShowCharacterBrowser(search);
    }

    private void ShowCharacterBrowser(string? initialSearch = null)
    {
        var dialog = new CharacterBrowserWindow(_allGameAssets, initialSearch)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true &&
            !string.IsNullOrWhiteSpace(dialog.SelectedMeshPath) &&
            File.Exists(dialog.SelectedMeshPath))
        {
            _requestedHeadVariantTexturePath = dialog.SelectedHeadVariantTexturePath;
            LoadMsh(dialog.SelectedMeshPath);
            ApplyRequestedHeadVariantIfAny();
            _requestedHeadVariantTexturePath = null;
            MainTabs.SelectedItem = View3DTab;
            StatusText.Text = LocalizationService.F("Personnage : {0}", Path.GetFileName(dialog.SelectedMeshPath));
        }
    }

    private void OpenPropsBrowser_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PropsBrowserWindow(_allGameAssets)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true &&
            !string.IsNullOrWhiteSpace(dialog.SelectedMeshPath) &&
            File.Exists(dialog.SelectedMeshPath))
        {
            LoadMsh(dialog.SelectedMeshPath);
            MainTabs.SelectedItem = View3DTab;
            StatusText.Text = LocalizationService.F("Objet : {0}", Path.GetFileName(dialog.SelectedMeshPath));
        }
    }

    private void OpenTextureBrowser_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TextureBrowserWindow(_allGameAssets, _currentMshPath, _currentMaterialAnalysis)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true &&
            !string.IsNullOrWhiteSpace(dialog.SelectedTexturePath) &&
            File.Exists(dialog.SelectedTexturePath))
        {
            LoadSt(dialog.SelectedTexturePath, selectTab: false);
            StatusText.Text = LocalizationService.F("Texture sélectionnée : {0}", Path.GetFileName(dialog.SelectedTexturePath));
        }
    }

    private void RefreshExplorerDashboard()
    {
        if (HomeReadyText is null)
            return;

        int characterMeshes = _allGameAssets.Count(FullGameDatabase.IsCharacterMesh);
        int meshes = _allGameAssets.Count(PropCatalog.IsPropMesh);
        int textures = _allGameAssets.Count(a => a.Kind == GameAssetKind.Texture);
        int animations = _allGameAssets.Count(a => a.Kind == GameAssetKind.Animation);

        HomeReadyText.Text = _allGameAssets.Count == 0 ? "Aucun jeu indexé" : "✓ Bibliothèque prête";
        HomeCharacterCountText.Text = LocalizationService.F("{0:N0} mesh(es) personnage", characterMeshes);
        HomeTextureCountText.Text = LocalizationService.F("{0:N0} texture(s)", textures);
        HomeMeshCountText.Text = LocalizationService.F("{0:N0} objet(s) MSH", meshes);
        HomeAnimationCountText.Text = LocalizationService.F("{0:N0} animation(s)", animations);

        var groups = _allGameAssets
            .Where(FullGameDatabase.IsCharacterMesh)
            .GroupBy(a => Path.GetFileName(Path.GetDirectoryName(a.FullPath) ?? a.DirectoryRelativePath) ?? "Personnage", StringComparer.OrdinalIgnoreCase)
            .Select(g => new CharacterQuickCard
            {
                SearchKey = g.Key,
                DisplayName = HumanizeFamilyName(g.Key),
                MeshCount = g.Count()
            })
            .OrderBy(x => CharacterPriority(x.SearchKey))
            .ThenByDescending(x => x.MeshCount)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        CharacterQuickCards.ItemsSource = groups;
        RefreshRecentAssets();
    }

    private static int CharacterPriority(string name)
    {
        string n = name.ToLowerInvariant();
        string[] priority = ["trane", "gabe", "dog", "shannaray", "dip", "cck", "kry", "spleen", "rat", "seagull"];
        for (int i = 0; i < priority.Length; i++)
        {
            if (n.Contains(priority[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 100;
    }

    private static string HumanizeFamilyName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Personnage";

        string cleaned = value.Replace('_', ' ').Replace('-', ' ').Trim();
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
    }

    private void AddRecentAsset(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch { return; }

        _recentAssets.RemoveAll(x => string.Equals(x.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        _recentAssets.Insert(0, new RecentAssetItem(fullPath));
        if (_recentAssets.Count > 8)
            _recentAssets.RemoveRange(8, _recentAssets.Count - 8);

        RefreshRecentAssets();
    }

    private void RefreshRecentAssets()
    {
        if (RecentAssetsList is null || RecentEmptyText is null)
            return;

        RecentAssetsList.ItemsSource = null;
        RecentAssetsList.ItemsSource = _recentAssets.ToList();
        RecentAssetsList.Visibility = _recentAssets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentEmptyText.Visibility = _recentAssets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RecentAssetsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentAssetsList.SelectedItem is not RecentAssetItem item || !File.Exists(item.Path))
            return;

        OpenPathByExtension(item.Path);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;

        string? supported = paths.FirstOrDefault(path =>
            File.Exists(path) && IsDirectOpenExtension(Path.GetExtension(path)));

        if (supported is null)
        {
            StatusText.Text = LocalizationService.T("Glisser-déposer : formats directs acceptés .msh, .st, .bnm, .gat, .ban, .mtm");
            return;
        }

        OpenPathByExtension(supported);
    }

    private static bool IsDirectOpenExtension(string extension)
    {
        return extension.Equals(".msh", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".st", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bnm", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gat", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ban", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mtm", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenPathByExtension(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".msh": LoadMsh(path); MainTabs.SelectedItem = View3DTab; break;
            case ".st": LoadSt(path, selectTab: true); break;
            case ".bnm": LoadBnm(path); break;
            case ".gat": LoadGat(path, selectTab: true); break;
            case ".ban": LoadBan(path, selectTab: true); break;
            case ".mtm": LoadMtm(path); break;
        }
    }

    private void OpenGat_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.T("Ouvrir un fichier GAT"),
            Filter = LocalizationService.T("Getting Up attachment (*.gat)|*.gat|Tous les fichiers (*.*)|*.*"),
            InitialDirectory = GetPreferredOpenDirectory() ?? string.Empty
        };
        if (dialog.ShowDialog() != true) return;
        LoadGat(dialog.FileName, selectTab: true);
    }

    private void OpenBan_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.T("Ouvrir un fichier BAN"),
            Filter = LocalizationService.T("Getting Up BAN (*.ban)|*.ban|Tous les fichiers (*.*)|*.*"),
            InitialDirectory = GetPreferredOpenDirectory() ?? string.Empty
        };
        if (dialog.ShowDialog() != true) return;
        LoadBan(dialog.FileName, selectTab: true);
    }
}
