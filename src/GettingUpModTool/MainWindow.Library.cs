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
    private async void DetectGame_Click(object sender, RoutedEventArgs e)
    {
        string? root = GameLocator.DetectGameRoot();
        if (root is null)
        {
            LocalizationService.Show(
                "Aucune installation Steam de Getting Up n'a été détectée automatiquement. Utilise « Parcourir… » pour sélectionner le dossier du jeu.",
                "Détection Steam",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        GameRootBox.Text = root;
        await ScanGameAssetsAsync(root);
    }

    private async void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.T("Sélectionner le dossier de Marc Ecko's Getting Up"),
            Multiselect = false
        };

        if (Directory.Exists(GameRootBox.Text))
            dialog.InitialDirectory = GameRootBox.Text;

        if (dialog.ShowDialog() != true)
            return;

        GameRootBox.Text = dialog.FolderName;
        await ScanGameAssetsAsync(dialog.FolderName);
    }

    private async void ScanGame_Click(object sender, RoutedEventArgs e)
    {
        await ScanGameAssetsAsync(GameRootBox.Text);
    }

    private async void IncludeOtherFiles_Click(object sender, RoutedEventArgs e)
    {
        _settings.IncludeOtherFiles = IncludeOtherFilesCheck.IsChecked == true;
        AppSettingsStore.Save(_settings);
        if (!string.IsNullOrWhiteSpace(_gameRoot))
            await ScanGameAssetsAsync(_gameRoot);
    }

    private async Task ScanGameAssetsAsync(string? root, IProgress<StartupStep>? startup = null)
    {
        if (_assetScanInProgress)
            return;

        string? resolvedRoot = GameLocator.ResolveGameRoot(root);
        if (resolvedRoot is null)
        {
            LocalizationService.Show(
                "Le dossier sélectionné ne se trouve pas dans une installation de Getting Up (dossier engine ou GettingUp.exe introuvable). Tu peux même sélectionner un sous-dossier comme engine\\Animation\\Dog : le tool remontera automatiquement à la racine du jeu.",
                "Dossier du jeu",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _assetScanInProgress = true;
        ScanGameButton.IsEnabled = false;
        AssetSummaryText.Text = LocalizationService.T("Scan des fichiers du jeu en cours…");
        StatusText.Text = LocalizationService.T("Indexation de la bibliothèque Getting Up…");

        try
        {
            string normalizedRoot = Path.GetFullPath(resolvedRoot);
            bool includeOther = IncludeOtherFilesCheck.IsChecked == true;

            int expected = _settings.IncludeOtherFiles == includeOther ? _settings.LastScanFileCount : 0;
            IProgress<int>? scanProgress = startup is null ? null : new Progress<int>(count =>
            {
                double done = expected > 0 ? Math.Min(1.0, (double)count / expected) : count / (count + 6000.0);
                startup.Report(new StartupStep(0.15 + 0.60 * done,
                    LocalizationService.T("Indexation des fichiers du jeu…"),
                    LocalizationService.F("{0:N0} fichiers trouvés", count)));
            });
            startup?.Report(new StartupStep(0.15, LocalizationService.T("Indexation des fichiers du jeu…")));
            IReadOnlyList<GameAsset> assets = await Task.Run(() => GameAssetScanner.Scan(normalizedRoot, includeOther, scanProgress));

            _gameRoot = normalizedRoot;
            _allGameAssets.Clear();
            _allGameAssets.AddRange(assets);

            _settings.GameRoot = normalizedRoot;
            _settings.IncludeOtherFiles = includeOther;
            _settings.LastScanFileCount = assets.Count;
            AppSettingsStore.Save(_settings);

            GameRootBox.Text = normalizedRoot;
            startup?.Report(new StartupStep(0.78, LocalizationService.T("Préparation de la bibliothèque…"),
                LocalizationService.F("{0:N0} fichiers trouvés", assets.Count)));
            await YieldToRender(startup);
            FillAssetFolderFilter();
            RefreshAssetGrid();
            startup?.Report(new StartupStep(0.86, LocalizationService.T("Analyse des animations…")));
            await YieldToRender(startup);
            RefreshFullGameDatabase();

            int msh = _allGameAssets.Count(a => a.Extension == ".msh");
            int bnm = _allGameAssets.Count(a => a.Extension == ".bnm");
            int st = _allGameAssets.Count(a => a.Extension == ".st");
            AssetSummaryText.Text = LocalizationService.F("{0:N0} fichiers indexés — {1:N0} MSH, {2:N0} BNM, {3:N0} ST. Le dossier est mémorisé pour les prochains lancements.", _allGameAssets.Count, msh, bnm, st);
            StatusText.Text = LocalizationService.F("Bibliothèque prête : {0:N0} assets — {1}", _allGameAssets.Count, normalizedRoot);
            RefreshExplorerDashboard();
        }
        catch (Exception ex)
        {
            AssetSummaryText.Text = LocalizationService.T("Échec du scan.");
            LocalizationService.Show(ex.Message, "Scan du jeu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _assetScanInProgress = false;
            ScanGameButton.IsEnabled = true;
        }
    }

    private static async Task YieldToRender(IProgress<StartupStep>? startup)
    {
        if (startup is not null)
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
    }

    private void FillAssetFolderFilter()
    {
        string? previous = AssetFolderFilterBox.SelectedItem as string;
        AssetFolderFilterBox.Items.Clear();
        AssetFolderFilterBox.Items.Add(LocalizationService.T("Tous les dossiers"));

        foreach (string folder in _allGameAssets
                     .Select(a => a.DirectoryRelativePath)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            AssetFolderFilterBox.Items.Add(folder);
        }

        if (previous is not null && AssetFolderFilterBox.Items.Contains(previous))
            AssetFolderFilterBox.SelectedItem = previous;
        else
            AssetFolderFilterBox.SelectedIndex = 0;
    }

    private void AssetFilter_Changed(object sender, RoutedEventArgs e)
    {
        RefreshAssetGrid();
    }

    private void RefreshAssetGrid()
    {
        if (AssetGrid is null || AssetTypeFilterBox is null || AssetFolderFilterBox is null || AssetSearchBox is null)
            return;

        IEnumerable<GameAsset> query = _allGameAssets;
        string search = AssetSearchBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            string[] terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(asset => terms.All(term =>
                asset.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                asset.RelativePath.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        if (AssetTypeFilterBox.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string typeTag)
        {
            query = typeTag == "other"
                ? query.Where(a => a.Kind == GameAssetKind.Other)
                : query.Where(a => string.Equals(a.Extension, typeTag, StringComparison.OrdinalIgnoreCase));
        }

        if (AssetFolderFilterBox.SelectedIndex > 0 && AssetFolderFilterBox.SelectedItem is string folder)
        {
            query = query.Where(a =>
                string.Equals(a.DirectoryRelativePath, folder, StringComparison.OrdinalIgnoreCase) ||
                a.DirectoryRelativePath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }

        List<GameAsset> filtered = query
            .OrderBy(a => a.DirectoryRelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AssetGrid.ItemsSource = filtered;
        AssetCountText.Text = filtered.Count == 1 ? "1 fichier" : $"{filtered.Count:N0} fichiers";
    }

    private void AssetGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssetGrid.SelectedItem is GameAsset asset)
            SelectedAssetText.Text = $"{LocalizationService.T(asset.KindLabel)} — {asset.RelativePath} — {asset.SizeLabel}";
        else
            SelectedAssetText.Text = LocalizationService.T("Aucun asset sélectionné.");
    }

    private void AssetGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AssetGrid.SelectedItem is GameAsset asset)
            LoadAsset(asset);
    }

    private void LoadSelectedAsset_Click(object sender, RoutedEventArgs e)
    {
        if (AssetGrid.SelectedItem is not GameAsset asset)
        {
            LocalizationService.Show("Sélectionne d'abord un fichier dans la bibliothèque.", "Bibliothèque", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoadAsset(asset);
    }

    private void LoadAsset(GameAsset asset)
    {
        try
        {
            switch (asset.Extension.ToLowerInvariant())
            {
                case ".msh":
                    LoadMsh(asset.FullPath);
                    break;
                case ".bnm":
                    LoadBnm(asset.FullPath);
                    break;
                case ".st":
                    LoadSt(asset.FullPath, selectTab: true);
                    break;
                case ".gat":
                    LoadGat(asset.FullPath, selectTab: true);
                    break;
                case ".ban":
                    LoadBan(asset.FullPath, selectTab: true);
                    break;
                case ".mtm":
                    LoadMtm(asset.FullPath);
                    break;
                default:
                    if (asset.Size <= 8 * 1024 * 1024)
                    {
                        HexTextBox.Text = HexDumper.Dump(File.ReadAllBytes(asset.FullPath));
                        MainTabs.SelectedItem = HexTab;
                        StatusText.Text = LocalizationService.F("Hex : {0}", asset.RelativePath);
                    }
                    else
                    {
                        LocalizationService.Show("Ce format n'a pas encore de lecteur intégré. Utilise « Afficher dans Explorer ».", "Format non pris en charge", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, $"Ouverture de {asset.Name}", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenSelectedAssetFolder_Click(object sender, RoutedEventArgs e)
    {
        if (AssetGrid.SelectedItem is not GameAsset asset)
            return;

        OpenInExplorerSafe(asset.FullPath);
    }

    private void CopySelectedAssetPath_Click(object sender, RoutedEventArgs e)
    {
        if (AssetGrid.SelectedItem is GameAsset asset)
        {
            Clipboard.SetText(asset.FullPath);
            StatusText.Text = LocalizationService.T("Chemin copié dans le presse-papiers.");
        }
    }

    private string? GetPreferredOpenDirectory(string? relative = null)
    {
        if (string.IsNullOrWhiteSpace(_gameRoot) || !Directory.Exists(_gameRoot))
            return null;

        if (string.IsNullOrWhiteSpace(relative))
            return _gameRoot;

        string candidate = Path.Combine(_gameRoot, relative);
        return Directory.Exists(candidate) ? candidate : _gameRoot;
    }
}
