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
    private void RefreshFullGameDatabase()
    {
        _animationFamilies = FullGameDatabase.BuildAnimationFamilies(_allGameAssets);

        CompatibleMeshGrid.ItemsSource = null;
        FullGameAnimationGrid.ItemsSource = null;
        _compatibleMeshes = Array.Empty<CompatibleMeshInfo>();
        ApplyAnimationSearchFilter(preserveSelection: false);

        int bnm = _allGameAssets.Count(a => a.Kind == GameAssetKind.Animation);
        int msh = _allGameAssets.Count(a => a.Kind == GameAssetKind.Mesh);
        int characterMsh = _allGameAssets.Count(FullGameDatabase.IsCharacterMesh);
        int st = _allGameAssets.Count(a => a.Kind == GameAssetKind.Texture);
        int gat = _allGameAssets.Count(a => a.Kind == GameAssetKind.Attachment);
        int ban = _allGameAssets.Count(a => a.Kind == GameAssetKind.Ban);

        FullGameSummaryText.Text =
            $"{_animationFamilies.Count:N0} familles d'animations — {bnm:N0} BNM — {msh:N0} MSH ({characterMsh:N0} dans Meshes\\Chars) — {st:N0} ST — {gat:N0} GAT — {ban:N0} BAN. " +
            "Sélectionne une animation puis clique « Trouver les meshes » : les meshes associés à la famille du jeu sont affichés en premier, puis les autres squelettes compatibles.";
    }

    private void AnimationSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyAnimationSearchFilter(preserveSelection: true);
    }

    private void ClearAnimationSearch_Click(object sender, RoutedEventArgs e)
    {
        if (AnimationSearchBox is null)
            return;

        AnimationSearchBox.Clear();
        AnimationSearchBox.Focus();
    }

    private void ApplyAnimationSearchFilter(bool preserveSelection)
    {
        if (FullGameFamilyGrid is null || FullGameAnimationGrid is null)
            return;

        string query = AnimationSearchBox?.Text?.Trim() ?? string.Empty;
        AnimationFamilyInfo? previousFamily = preserveSelection ? FullGameFamilyGrid.SelectedItem as AnimationFamilyInfo : null;
        GameAsset? previousAnimation = preserveSelection ? FullGameAnimationGrid.SelectedItem as GameAsset : null;

        IReadOnlyList<AnimationFamilyInfo> filteredFamilies;
        if (string.IsNullOrWhiteSpace(query))
        {
            filteredFamilies = _animationFamilies;
        }
        else
        {
            filteredFamilies = _animationFamilies
                .Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            f.Animations.Any(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }

        FullGameFamilyGrid.ItemsSource = filteredFamilies;

        AnimationFamilyInfo? familyToSelect = previousFamily is not null && filteredFamilies.Contains(previousFamily)
            ? previousFamily
            : filteredFamilies.FirstOrDefault();

        FullGameFamilyGrid.SelectedItem = familyToSelect;

        if (familyToSelect is null)
        {
            FullGameAnimationGrid.ItemsSource = null;
            CompatibleMeshGrid.ItemsSource = null;
            _compatibleMeshes = Array.Empty<CompatibleMeshInfo>();
            return;
        }

        IReadOnlyList<GameAsset> filteredAnimations = string.IsNullOrWhiteSpace(query) ||
            familyToSelect.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            ? familyToSelect.Animations
            : familyToSelect.Animations
                .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        FullGameAnimationGrid.ItemsSource = filteredAnimations;
        if (previousAnimation is not null && filteredAnimations.Contains(previousAnimation))
            FullGameAnimationGrid.SelectedItem = previousAnimation;
        else if (filteredAnimations.Count > 0)
            FullGameAnimationGrid.SelectedIndex = 0;
    }

    private void RefreshFullGameDatabase_Click(object sender, RoutedEventArgs e)
    {
        RefreshFullGameDatabase();
        MainTabs.SelectedItem = FullGameDbTab;
    }

    private void FullGameFamilyGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FullGameFamilyGrid.SelectedItem is not AnimationFamilyInfo family)
        {
            FullGameAnimationGrid.ItemsSource = null;
            return;
        }

        string query = AnimationSearchBox?.Text?.Trim() ?? string.Empty;
        IReadOnlyList<GameAsset> animations = string.IsNullOrWhiteSpace(query) ||
            family.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            ? family.Animations
            : family.Animations.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

        FullGameAnimationGrid.ItemsSource = animations;
        CompatibleMeshGrid.ItemsSource = null;
        _compatibleMeshes = Array.Empty<CompatibleMeshInfo>();
        CompatibilitySummaryText.Text = LocalizationService.F("{0} : {1:N0} animation(s). Sélectionne un BNM pour rechercher les MSH qui ont le même squelette.", family.Name, family.AnimationCount);
        if (animations.Count > 0)
            FullGameAnimationGrid.SelectedIndex = 0;
    }

    private void BindCompatibleMeshes(IReadOnlyList<CompatibleMeshInfo> matches)
    {
        var view = new ListCollectionView(matches.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CompatibleMeshInfo.MatchGroupLabel)));
        CompatibleMeshGrid.ItemsSource = view;
    }

    private async void FindCompatibleMeshes_Click(object sender, RoutedEventArgs e)
    {
        if (_fullGameAnalysisInProgress)
            return;
        if (FullGameAnimationGrid.SelectedItem is not GameAsset animation)
        {
            LocalizationService.Show("Sélectionne d'abord une animation BNM.", "Compatibilité", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _fullGameAnalysisInProgress = true;
        FindCompatibleMeshesButton.IsEnabled = false;
        CompatibilitySummaryText.Text = LocalizationService.F("Analyse des squelettes pour {0}…", animation.Name);
        StatusText.Text = LocalizationService.T("Recherche des meshes compatibles…");

        try
        {
            IReadOnlyList<CompatibleMeshInfo> matches = await Task.Run(() =>
                FullGameDatabase.FindCompatibleMeshes(animation, _allGameAssets));
            _compatibleMeshes = matches;
            BindCompatibleMeshes(matches);

            BnmAnimation clip = await Task.Run(() => BnmReader.Read(animation.FullPath));
            int expectedMinusRoot = Math.Max(0, clip.TrackCount - 1);
            int recommendedCount = matches.Count(x => x.IsRecommended);
            CompatibilitySummaryText.Text = matches.Count == 0
                ? $"{animation.Name} : {clip.TrackCount} tracks → correspondance exacte attendue : {expectedMinusRoot} ou {clip.TrackCount} os. Aucun MSH compatible détecté dans Meshes\\Chars."
                : $"{animation.Name} : {clip.TrackCount} tracks. Exact : {expectedMinusRoot}/{clip.TrackCount} os ; un mesh associé peut aussi être marqué Probable s'il possède quelques os supplémentaires. {recommendedCount:N0} associé(s), {matches.Count - recommendedCount:N0} autre(s).";
            StatusText.Text = LocalizationService.F("Compatibilité : {0:N0} mesh(es) pour {1}", matches.Count, animation.Name);

            if (matches.Count > 0)
                CompatibleMeshGrid.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Compatibilité BNM/MSH", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _fullGameAnalysisInProgress = false;
            FindCompatibleMeshesButton.IsEnabled = true;
        }
    }

    private async void AutoLoadBestMatch_Click(object sender, RoutedEventArgs e)
    {
        if (_fullGameAnalysisInProgress)
            return;

        if (FullGameAnimationGrid.SelectedItem is not GameAsset animation)
        {
            LocalizationService.Show("Choisis d'abord une animation.", "Chargement automatique", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _fullGameAnalysisInProgress = true;
        FindCompatibleMeshesButton.IsEnabled = false;
        CompatibilitySummaryText.Text = LocalizationService.F("Recherche automatique du meilleur mesh pour {0}…", animation.Name);
        StatusText.Text = LocalizationService.T("Recherche du meilleur mesh compatible…");

        try
        {
            IReadOnlyList<CompatibleMeshInfo> matches = await Task.Run(() =>
                FullGameDatabase.FindCompatibleMeshes(animation, _allGameAssets));

            _compatibleMeshes = matches;
            BindCompatibleMeshes(matches);

            if (matches.Count == 0)
            {
                CompatibilitySummaryText.Text = LocalizationService.F("Aucun mesh compatible détecté pour {0}. Ouvre Diagnostic du mesh ou la Bibliothèque pour chercher manuellement.", animation.Name);
                LocalizationService.Show("Aucun mesh compatible n'a été détecté automatiquement pour cette animation.", "Chargement automatique", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            CompatibleMeshInfo? bestAssociated = matches.FirstOrDefault(x => x.IsRecommended && x.IsSkeletonCompatible);
            CompatibleMeshInfo? bestCompatible = matches.FirstOrDefault(x => x.IsSkeletonCompatible);

            if (matches.Any(x => x.IsRecommended) && bestAssociated is null)
            {
                CompatibleMeshInfo associated = matches.First(x => x.IsRecommended);
                CompatibleMeshGrid.SelectedItem = associated;
                CompatibilitySummaryText.Text = LocalizationService.F("Mesh associé trouvé : {0} — {1:N0} os / {2:N0} tracks / écart {3}. Squelette non confirmé.", associated.Asset.Name, associated.BoneCount, associated.AnimationTrackCount, associated.BoneTrackDeltaLabel);
                LocalizationService.Show($"Le mesh associé au personnage a été trouvé, mais son squelette n'est pas confirmé.\n\n{associated.Asset.Name}\n{associated.BoneCount} os / {associated.AnimationTrackCount} tracks / écart {associated.BoneTrackDeltaLabel}\n\nLe chargement automatique d'un autre personnage a été évité.", "Compatibilité", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CompatibleMeshInfo? best = bestAssociated ?? bestCompatible;
            if (best is null)
            {
                LocalizationService.Show("Aucun mesh avec un squelette compatible n'a été trouvé pour le chargement automatique.", "Compatibilité", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            CompatibleMeshGrid.SelectedItem = best;
            LoadMsh(best.Asset.FullPath);
            LoadBnm(animation.FullPath);
            MainTabs.SelectedItem = View3DTab;
            CompatibilitySummaryText.Text = LocalizationService.F("Chargé automatiquement : {0} + {1}. Si le personnage semble incorrect, utilise Diagnostic du mesh.", best.Asset.Name, animation.Name);
            StatusText.Text = LocalizationService.F("Auto : {0} + {1}", best.Asset.Name, animation.Name);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Chargement automatique", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _fullGameAnalysisInProgress = false;
            FindCompatibleMeshesButton.IsEnabled = true;
        }
    }

    private void FullGameAnimationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FullGameAnimationGrid.SelectedItem is GameAsset animation)
            LoadBnm(animation.FullPath);
    }

    private void CompatibleMeshGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CompatibleMeshGrid.SelectedItem is CompatibleMeshInfo mesh)
            LoadMsh(mesh.Asset.FullPath);
    }

    private void LoadFullGameAnimation_Click(object sender, RoutedEventArgs e)
    {
        if (FullGameAnimationGrid.SelectedItem is GameAsset animation)
            LoadBnm(animation.FullPath);
        else
            LocalizationService.Show("Sélectionne une animation.", "Base complète", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadFullGameMesh_Click(object sender, RoutedEventArgs e)
    {
        if (CompatibleMeshGrid.SelectedItem is CompatibleMeshInfo mesh)
            LoadMsh(mesh.Asset.FullPath);
        else
            LocalizationService.Show("Lance d'abord la recherche de meshes compatibles puis sélectionne un MSH.", "Base complète", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DiagnoseCompatibleMesh_Click(object sender, RoutedEventArgs e)
    {
        if (CompatibleMeshGrid.SelectedItem is not CompatibleMeshInfo mesh)
        {
            LocalizationService.Show("Sélectionne d'abord un mesh compatible.", "Diagnostic MSH", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoadMsh(mesh.Asset.FullPath);
        MainTabs.SelectedItem = MeshDiagnosticTab;
    }

    private void BatchValidationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BatchValidationGrid.SelectedItem is not BatchValidationRow row)
            return;

        GameAsset? asset = _allGameAssets.FirstOrDefault(a =>
            string.Equals(a.RelativePath, row.RelativePath, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
            return;

        LoadAsset(asset);
        if (asset.Kind == GameAssetKind.Mesh)
            MainTabs.SelectedItem = MeshDiagnosticTab;
    }

    private void LoadFullGamePair_Click(object sender, RoutedEventArgs e)
    {
        if (FullGameAnimationGrid.SelectedItem is not GameAsset animation || CompatibleMeshGrid.SelectedItem is not CompatibleMeshInfo mesh)
        {
            LocalizationService.Show("Sélectionne une animation et un mesh compatible.", "Base complète", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        
        LoadMsh(mesh.Asset.FullPath);
        LoadBnm(animation.FullPath);
        MainTabs.SelectedItem = View3DTab;
        StatusText.Text = LocalizationService.F("Paire chargée : {0} + {1}", mesh.Asset.Name, animation.Name);
    }

    private async void ValidateBnms_Click(object sender, RoutedEventArgs e)
    {
        if (_fullGameAnalysisInProgress)
            return;
        _fullGameAnalysisInProgress = true;
        try
        {
            FullGameSummaryText.Text = LocalizationService.T("Validation de tous les BNM en cours…");
            StatusText.Text = LocalizationService.T("Validation BNM…");
            IReadOnlyList<BatchValidationRow> rows = await Task.Run(() => FullGameDatabase.ValidateBnms(_allGameAssets));
            BatchValidationGrid.ItemsSource = rows.OrderBy(r => r.Success).ThenBy(r => r.RelativePath).ToList();
            int ok = rows.Count(r => r.Success);
            FullGameSummaryText.Text = LocalizationService.F("Validation BNM terminée : {0:N0}/{1:N0} reconnus; {2:N0} variante(s)/erreur(s). Les échecs sont affichés en premier.", ok, rows.Count, rows.Count - ok);
            StatusText.Text = LocalizationService.F("BNM validés : {0:N0}/{1:N0}", ok, rows.Count);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Validation BNM", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _fullGameAnalysisInProgress = false;
        }
    }

    private async void ValidateCharacterMeshes_Click(object sender, RoutedEventArgs e)
    {
        if (_fullGameAnalysisInProgress)
            return;
        _fullGameAnalysisInProgress = true;
        try
        {
            FullGameSummaryText.Text = LocalizationService.T("Validation des MSH de personnages en cours…");
            StatusText.Text = LocalizationService.T("Validation MSH personnages…");
            IReadOnlyList<BatchValidationRow> rows = await Task.Run(() => FullGameDatabase.ValidateCharacterMeshes(_allGameAssets));
            BatchValidationGrid.ItemsSource = rows.OrderBy(r => r.Success).ThenBy(r => r.RelativePath).ToList();
            int ok = rows.Count(r => r.Success);
            FullGameSummaryText.Text = LocalizationService.F("Validation MSH personnages : {0:N0}/{1:N0} reconnus par le détecteur squelette et/ou mesh automatique.", ok, rows.Count);
            StatusText.Text = LocalizationService.F("MSH personnages validés : {0:N0}/{1:N0}", ok, rows.Count);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Validation MSH", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _fullGameAnalysisInProgress = false;
        }
    }

    private async void ValidateCharacterMaterials_Click(object sender, RoutedEventArgs e)
    {
        if (_fullGameAnalysisInProgress)
            return;
        _fullGameAnalysisInProgress = true;
        try
        {
            FullGameSummaryText.Text = LocalizationService.T("Validation matériaux/textures des personnages en cours…");
            StatusText.Text = LocalizationService.T("Validation matériaux personnages…");
            IReadOnlyList<BatchValidationRow> rows = await Task.Run(() => FullGameDatabase.ValidateCharacterMaterials(_allGameAssets));
            BatchValidationGrid.ItemsSource = rows.OrderBy(r => r.Success).ThenBy(r => r.RelativePath).ToList();
            int ok = rows.Count(r => r.Success);
            FullGameSummaryText.Text = LocalizationService.F("Matériaux personnages : {0:N0}/{1:N0} MSH ont toutes leurs sections mappées vers une texture trouvée. Les autres sont listés en premier pour diagnostic.", ok, rows.Count);
            StatusText.Text = LocalizationService.F("Validation matériaux : {0:N0}/{1:N0} complets", ok, rows.Count);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Validation matériaux", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _fullGameAnalysisInProgress = false;
        }
    }
}
