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
    private void PreviewMsh_Click(object sender, RoutedEventArgs e) => PreviewCurrentMsh();

    private void PreviewCurrentMsh()
    {
        if (_currentMshPath is null)
        {
            LocalizationService.Show("Ouvre d'abord un fichier .msh.", "Getting Up Mod Tool", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (_currentSkeleton is null && _currentMshBytes is not null)
                TryDetectSkeleton();
            if (_currentMshAnalysis is null && _currentMshBytes is not null)
                AnalyzeCurrentMshStructure(selectTab: false);
            if (_currentMaterialAnalysis is null && _currentMshBytes is not null)
                AnalyzeCurrentMaterials();

            bool useAllSections = UseAllSectionsCheck.IsChecked != false && (_currentMshAnalysis?.Sections.Count ?? 0) > 0;
            if (useAllSections && _currentMshBytes is not null)
            {
                
                
                _currentMesh = MshMultiSectionReader.Read(
                    _currentMshBytes,
                    _currentMshAnalysis!.Sections,
                    GetKnownFlipVSections(),
                    GetKnownWrapVSections());
            }
            else
            {
                var layout = ReadLayoutFromUi();
                _currentMesh = MshReader.Read(_currentMshPath, layout);
            }

            ApplyMaterialBindingsToMesh(_currentMesh);
            UpdateSkeletonAndSkinningTables();
            ClearAnimationPose();
            if (CanAnimateCurrentPair())
                ApplyAnimationFrame(_currentAnimationFrame, fitCamera: true);
            else
                RefreshScene(fitCamera: true);
            RefreshViewportSectionPanel();

            int triangleCount = _currentMesh.Indices.Count / 3;
            string skin = _currentMesh.HasSkinning ? $"   Skin: {_currentMesh.Skinning.Count:N0}" : "";
            string bones = _currentSkeleton is not null ? $"   Bones: {_currentSkeleton.Bones.Count}" : "";
            string sections = UseAllSectionsCheck.IsChecked != false && _currentMshAnalysis is not null
                ? $"   Sections: {_currentMshAnalysis.Sections.Count}"
                : $"   Section: {LocalizationService.T("manuelle")}";
            string materials = _currentMaterialAnalysis is not null && _currentMaterialAnalysis.Bindings.Count > 0
                ? $"   Materials: {_currentMaterialAnalysis.Bindings.Count}   Textures: {_sectionTextures.Count}"
                : "";
            MeshStatsText.Text = $"Vertices: {_currentMesh.Positions.Count:N0}   Triangles: {triangleCount:N0}   UV: {_currentMesh.UVs.Count:N0}   Normals: {_currentMesh.Normals.Count:N0}{skin}{bones}{sections}{materials}";
            StatusText.Text = $"Aperçu : {Path.GetFileName(_currentMshPath)} — {(UseAllSectionsCheck.IsChecked != false ? "multi-section" : "manuel")}";
            MainTabs.SelectedItem = View3DTab;
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Layout MSH invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshScene(bool fitCamera = true)
    {
        if (_currentMesh is null)
            return;

        MeshData displayMesh = _animatedMesh ?? _currentMesh;
        double textureEmissiveBoost = _currentMshPath is not null &&
            string.Equals(Path.GetFileName(_currentMshPath), "Stake_01.msh", StringComparison.OrdinalIgnoreCase)
            ? 0.42
            : 0.0;

        _viewportController.ShowScene(
            displayMesh,
            _currentSkeleton,
            ShowMeshCheck.IsChecked != false,
            ShowSkeletonCheck.IsChecked != false,
            _currentTexture,
            _animatedBoneWorlds,
            fitCamera,
            _sectionTextures,
            textureEmissiveBoost,
            _selectedViewerBoneIndex);
    }

    private void RefreshViewportSectionPanel()
    {
        int? previouslySelectedSection = (ViewportSectionsList.SelectedItem as ViewportSectionRow)?.SectionIndex;
        if (_currentMesh is null || _currentMesh.Parts.Count == 0)
        {
            ViewportSectionsList.ItemsSource = null;
            ViewerSectionCountText.Text = LocalizationService.T("0");
            RefreshViewerPrototypeInfo();
            return;
        }

        var rows = _currentMesh.Parts
            .GroupBy(p => p.SectionIndex)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                string[] materials = g.Select(p => p.MaterialName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                string[] textures = g.Select(p => p.TextureReference)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new ViewportSectionRow
                {
                    SectionIndex = g.Key,
                    Title = $"Section {g.Key}",
                    Material = materials.Length == 0 ? "—" : string.Join(" · ", materials),
                    Texture = textures.Length == 0 ? LocalizationService.T("Texture non résolue") : string.Join(" · ", textures),
                    IsVisible = g.Any(p => p.IsVisible)
                };
            })
            .ToList();
        ViewportSectionsList.ItemsSource = rows;
        ViewerSectionCountText.Text = rows.Count.ToString();
        if (rows.Count > 0)
        {
            ViewportSectionRow? previous = previouslySelectedSection is int section
                ? rows.FirstOrDefault(r => r.SectionIndex == section)
                : null;
            ViewportSectionsList.SelectedItem = previous ?? rows[0];
        }
        RefreshViewerPrototypeInfo();
    }

    private void CameraPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string preset)
            _viewportController.SetPresetView(preset);
    }

    private void ViewportSectionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshViewerPrototypeInfo();
    }

    private void RefreshViewerPrototypeInfo()
    {
        if (_currentMesh is null)
        {
            ViewerAssetNameText.Text = LocalizationService.T("Aucun mesh");
            ViewerAssetPathText.Text = LocalizationService.T("—");
            ViewerInspectorText.Text = LocalizationService.T("Charge un mesh pour afficher ses informations.");
            ViewerDiagnosticText.Text = LocalizationService.T("—");
            ViewerTexturePreviewImage.Source = null;
            ViewerUvTextureImage.Source = null;
            ViewerUvOverlayImage.Source = null;
            ViewerCompareLeftImage.Source = null;
            ViewerCompareRightImage.Source = null;
            ViewerSkeletonList.ItemsSource = null;
            _viewerBoneListSignature = "";
            ViewerSkeletonList.SelectedIndex = -1;
            _selectedViewerBoneIndex = null;
            ViewerBoneDetailsText.Text = LocalizationService.T("Sélectionne un os.");
            FocusBoneButton.IsEnabled = false;
            ViewerSelectedSectionText.Text = LocalizationService.T("Sélectionne une section pour l'inspecter.");
            ViewerTextureInfoText.Text = LocalizationService.T("Aucune texture");
            ViewerUvInfoText.Text = LocalizationService.T("Sélectionne une section.");
            return;
        }

        ViewerAssetNameText.Text = _currentMshPath is null ? "Mesh" : Path.GetFileNameWithoutExtension(_currentMshPath);
        ViewerAssetPathText.Text = _currentMshPath ?? "—";
        int triangles = _currentMesh.Indices.Count / 3;
        int sections = _currentMesh.Parts.Select(p => p.SectionIndex).Distinct().Count();
        var bindings = _currentMaterialAnalysis?.Bindings ?? [];
        int materials = bindings.Select(b => b.MaterialName)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x != "<inconnu>")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        int expectedTexturedSections = bindings
            .Where(b => !string.IsNullOrWhiteSpace(b.ResolvedTexturePath))
            .Select(b => b.SectionIndex)
            .Distinct()
            .Count();
        int resolvedTexturedSections = bindings
            .Where(b => !string.IsNullOrWhiteSpace(b.ResolvedTexturePath) && _sectionTextures.ContainsKey(b.SectionIndex))
            .Select(b => b.SectionIndex)
            .Distinct()
            .Count();
        int bones = _currentSkeleton?.Bones.Count ?? 0;
        ViewerInspectorText.Text =
            $"{LocalizationService.T("Mesh")} : {Path.GetFileName(_currentMshPath) ?? "—"}\n" +
            $"{LocalizationService.T("Textures")} : {resolvedTexturedSections}/{expectedTexturedSections}\n" +
            $"{LocalizationService.T("Sections")} : {sections}\n" +
            $"{LocalizationService.T("Matériaux uniques")} : {materials}\n" +
            $"{LocalizationService.T("Triangles")} : {triangles:N0}\n" +
            $"{LocalizationService.T("Vertices")} : {_currentMesh.Positions.Count:N0}\n" +
            $"{LocalizationService.T("Bones")} : {bones}\n" +
            $"{LocalizationService.T("Skinning")} : {LocalizationService.T(_currentMesh.HasSkinning ? "Oui" : "Non")}";

        var diag = new List<string> { "✓ " + LocalizationService.T("Mesh valide") };
        if (expectedTexturedSections == 0)
            diag.Add("• " + LocalizationService.T("Aucune texture déclarée"));
        else if (resolvedTexturedSections == expectedTexturedSections)
            diag.Add($"✓ Textures résolues : {resolvedTexturedSections}/{expectedTexturedSections}");
        else
        {
            diag.Add($"⚠ Textures résolues : {resolvedTexturedSections}/{expectedTexturedSections}");
            string[] missingTextures = bindings
                .Where(b => !string.IsNullOrWhiteSpace(b.ResolvedTexturePath) && !_sectionTextures.ContainsKey(b.SectionIndex))
                .Select(b => Path.GetFileName(b.ResolvedTexturePath!))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();
            if (missingTextures.Length > 0)
                diag.Add("  ↳ " + string.Join(", ", missingTextures));
        }
        diag.Add(bones > 0 ? "✓ " + LocalizationService.T("Squelette détecté") : "• " + LocalizationService.T("Pas de squelette"));
        if ((_currentMaterialAnalysis?.HiddenCount ?? 0) > 0)
            diag.Add($"⚠ {_currentMaterialAnalysis!.HiddenCount} variante(s) masquée(s)");
        ViewerDiagnosticText.Text = string.Join("\n", diag);

        string boneSignature = _currentSkeleton is null
            ? ""
            : string.Join("\u001F", _currentSkeleton.Bones.Select(b => $"{b.Index}:{b.Name}:{b.ParentIndex}"));
        if (!string.Equals(_viewerBoneListSignature, boneSignature, StringComparison.Ordinal))
        {
            _viewerBoneListSignature = boneSignature;
            _updatingViewerBoneList = true;
            try
            {
                ViewerSkeletonList.ItemsSource = _currentSkeleton?.Bones
                    .Select(b => new ViewerBoneRow { BoneIndex = b.Index, Name = b.Name })
                    .ToList();
            }
            finally
            {
                _updatingViewerBoneList = false;
            }
        }
        if (_currentSkeleton is null || _currentSkeleton.Bones.Count == 0)
        {
            _selectedViewerBoneIndex = null;
            ViewerSkeletonList.SelectedIndex = -1;
            ViewerBoneDetailsText.Text = LocalizationService.T("Aucun squelette détecté.");
            FocusBoneButton.IsEnabled = false;
        }
        else if (_selectedViewerBoneIndex is int selectedBone && FindBoneByIndex(selectedBone) is not null)
        {
            if (ViewerSkeletonList.ItemsSource is IEnumerable<ViewerBoneRow> boneRows)
                ViewerSkeletonList.SelectedItem = boneRows.FirstOrDefault(row => row.BoneIndex == selectedBone);
            UpdateViewerBoneDetails(selectedBone);
        }

        int? selectedSection = (ViewportSectionsList.SelectedItem as ViewportSectionRow)?.SectionIndex;
        if (selectedSection is null && _currentMesh.Parts.Count > 0)
            selectedSection = _currentMesh.Parts[0].SectionIndex;

        ImageSource? texture = null;
        if (selectedSection is int si)
            _sectionTextures.TryGetValue(si, out texture);
        texture ??= _currentTexture;
        ViewerTexturePreviewImage.Source = texture;
        ViewerUvTextureImage.Source = texture;
        ViewerCompareLeftImage.Source = texture;
        ViewerCompareRightImage.Source = texture;

        if (selectedSection is int sectionIndex)
        {
            MeshPart? part = _currentMesh.Parts.FirstOrDefault(p => p.SectionIndex == sectionIndex);
            string material = part is null || string.IsNullOrWhiteSpace(part.MaterialName) ? "—" : part.MaterialName;
            string textureName = part is null || string.IsNullOrWhiteSpace(part.TextureReference) ? "Texture non résolue" : part.TextureReference;
            ViewerSelectedSectionText.Text = LocalizationService.F("Section {0:0} · {1}\n{2}", sectionIndex, material, textureName);
            ViewerTextureInfoText.Text = textureName;
            ViewerUvOverlayImage.Source = CreateUvOverlay(sectionIndex, 512, 512);
            var uvStats = GetSectionUvStats(sectionIndex);
            ViewerUvInfoText.Text = uvStats is null
                ? $"Section {sectionIndex} · UV indisponibles"
                : $"Section {sectionIndex} · U {uvStats.Value.MinU:0.###}..{uvStats.Value.MaxU:0.###} · V {uvStats.Value.MinV:0.###}..{uvStats.Value.MaxV:0.###}" +
                  (uvStats.Value.Outside01 ? " · Repeat / hors 0–1" : " · 0–1");
        }
        else
        {
            ViewerSelectedSectionText.Text = LocalizationService.T("Sélectionne une section pour l'inspecter.");
            ViewerTextureInfoText.Text = LocalizationService.T("Aucune texture");
            ViewerUvOverlayImage.Source = null;
            ViewerUvInfoText.Text = LocalizationService.T("Sélectionne une section.");
        }
    }

    private BitmapSource? CreateUvOverlay(int sectionIndex, int width, int height)
    {
        if (_currentMesh is null || _currentMesh.UVs.Count != _currentMesh.Positions.Count)
            return null;
        MeshPart[] parts = _currentMesh.Parts.Where(p => p.SectionIndex == sectionIndex && p.IndexCount >= 3).ToArray();
        if (parts.Length == 0)
            return null;

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 176, 32)), 1.0);
            foreach (MeshPart part in parts)
            {
                int start = Math.Clamp(part.IndexStart, 0, _currentMesh.Indices.Count);
                int end = Math.Clamp(start + part.IndexCount, 0, _currentMesh.Indices.Count);
                for (int i = start; i + 2 < end; i += 3)
                {
                    int ia = _currentMesh.Indices[i];
                    int ib = _currentMesh.Indices[i + 1];
                    int ic = _currentMesh.Indices[i + 2];
                    if ((uint)ia >= (uint)_currentMesh.UVs.Count || (uint)ib >= (uint)_currentMesh.UVs.Count || (uint)ic >= (uint)_currentMesh.UVs.Count)
                        continue;
                    Point a = UvPoint(_currentMesh.UVs[ia], width, height);
                    Point b = UvPoint(_currentMesh.UVs[ib], width, height);
                    Point c = UvPoint(_currentMesh.UVs[ic], width, height);
                    dc.DrawLine(pen, a, b);
                    dc.DrawLine(pen, b, c);
                    dc.DrawLine(pen, c, a);
                }
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private (float MinU, float MaxU, float MinV, float MaxV, bool Outside01)? GetSectionUvStats(int sectionIndex)
    {
        if (_currentMesh is null || _currentMesh.UVs.Count != _currentMesh.Positions.Count)
            return null;

        var used = new HashSet<int>();
        foreach (MeshPart part in _currentMesh.Parts.Where(p => p.SectionIndex == sectionIndex))
        {
            int start = Math.Clamp(part.IndexStart, 0, _currentMesh.Indices.Count);
            int end = Math.Clamp(start + part.IndexCount, 0, _currentMesh.Indices.Count);
            for (int i = start; i < end; i++)
            {
                int vertex = _currentMesh.Indices[i];
                if ((uint)vertex < (uint)_currentMesh.UVs.Count)
                    used.Add(vertex);
            }
        }
        if (used.Count == 0)
            return null;

        float minU = float.PositiveInfinity, minV = float.PositiveInfinity;
        float maxU = float.NegativeInfinity, maxV = float.NegativeInfinity;
        foreach (int vertex in used)
        {
            Vector2 uv = _currentMesh.UVs[vertex];
            minU = Math.Min(minU, uv.X); maxU = Math.Max(maxU, uv.X);
            minV = Math.Min(minV, uv.Y); maxV = Math.Max(maxV, uv.Y);
        }
        bool outside = minU < 0f || minV < 0f || maxU > 1f || maxV > 1f;
        return (minU, maxU, minV, maxV, outside);
    }

    private static Point UvPoint(Vector2 uv, int width, int height)
    {
        double u = uv.X - Math.Floor(uv.X);
        double v = uv.Y - Math.Floor(uv.Y);
        return new Point(u * (width - 1), v * (height - 1));
    }

    private void SetViewportSectionVisibility(int sectionIndex, bool visible)
    {
        if (_currentMesh is not null)
            foreach (MeshPart part in _currentMesh.Parts.Where(p => p.SectionIndex == sectionIndex))
                part.IsVisible = visible;
        if (_animatedMesh is not null)
            foreach (MeshPart part in _animatedMesh.Parts.Where(p => p.SectionIndex == sectionIndex))
                part.IsVisible = visible;
        if (_currentMaterialAnalysis is not null)
            foreach (MshSectionMaterialBinding binding in _currentMaterialAnalysis.Bindings.Where(b => b.SectionIndex == sectionIndex))
                binding.IsVisible = visible;
    }

    private void ViewportSectionVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox check || check.DataContext is not ViewportSectionRow row)
            return;
        row.IsVisible = check.IsChecked == true;
        SetViewportSectionVisibility(row.SectionIndex, row.IsVisible);
        RefreshScene(fitCamera: false);
    }

    private void ViewportSoloSection_Click(object sender, RoutedEventArgs e)
    {
        if (ViewportSectionsList.SelectedItem is not ViewportSectionRow selected || _currentMesh is null)
            return;
        foreach (int sectionIndex in _currentMesh.Parts.Select(p => p.SectionIndex).Distinct())
            SetViewportSectionVisibility(sectionIndex, sectionIndex == selected.SectionIndex);
        RefreshViewportSectionPanel();
        RefreshScene(fitCamera: false);
    }

    private void ViewportShowAllSections_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMesh is null)
            return;
        foreach (int sectionIndex in _currentMesh.Parts.Select(p => p.SectionIndex).Distinct())
            SetViewportSectionVisibility(sectionIndex, true);
        RefreshViewportSectionPanel();
        RefreshScene(fitCamera: false);
    }

    private void ViewerSkeletonList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingViewerBoneList)
            return;

        if (_currentSkeleton is null || ViewerSkeletonList.SelectedItem is not ViewerBoneRow row ||
            FindBoneByIndex(row.BoneIndex) is null)
        {
            _selectedViewerBoneIndex = null;
            FocusBoneButton.IsEnabled = false;
            ViewerBoneDetailsText.Text = LocalizationService.T("Sélectionne un os.");
            return;
        }

        _selectedViewerBoneIndex = row.BoneIndex;
        ShowSkeletonCheck.IsChecked = true;
        UpdateViewerBoneDetails(row.BoneIndex);
        RefreshScene(fitCamera: false);
    }

    private BoneData? FindBoneByIndex(int boneIndex)
        => _currentSkeleton?.Bones.FirstOrDefault(b => b.Index == boneIndex);

    private BoneData? FindParentBone(BoneData bone)
    {
        if (_currentSkeleton is null || bone.ParentIndex < 0)
            return null;
        return _currentSkeleton.Bones.FirstOrDefault(b => b.Index == bone.ParentIndex)
            ?? (bone.ParentIndex < _currentSkeleton.Bones.Count ? _currentSkeleton.Bones[bone.ParentIndex] : null);
    }

    private void UpdateViewerBoneDetails(int boneIndex)
    {
        if (_currentSkeleton is null)
            return;

        BoneData? bone = FindBoneByIndex(boneIndex);
        if (bone is null)
            return;

        BoneData? parentBone = FindParentBone(bone);
        string parent = parentBone is not null
            ? $"{parentBone.Index:00}  {parentBone.Name}"
            : "<root>";
        string[] children = _currentSkeleton.Bones
            .Where(b => b.ParentIndex == bone.Index)
            .Select(b => $"{b.Index:00} {b.Name}")
            .ToArray();

        int influencedVertices = 0;
        float strongestWeight = 0f;
        MeshData? mesh = _animatedMesh ?? _currentMesh;
        if (mesh is not null && mesh.HasSkinning)
        {
            foreach (VertexSkin skin in mesh.Skinning)
            {
                float total = 0f;
                foreach (var influence in skin.Influences())
                {
                    if (influence.Joint == bone.Index)
                        total += influence.Weight;
                }
                if (total > 0.0001f)
                {
                    influencedVertices++;
                    strongestWeight = Math.Max(strongestWeight, total);
                }
            }
        }

        Vector3 position = bone.BindPosition;
        if (_animatedBoneWorlds is not null && bone.Index >= 0 && bone.Index < _animatedBoneWorlds.Count)
        {
            Matrix4x4 m = _animatedBoneWorlds[bone.Index];
            position = new Vector3(m.M41, m.M42, m.M43);
        }

        ViewerBoneDetailsText.Text =
            $"{bone.Index:00}  {bone.Name}\n" +
            $"{LocalizationService.T("Parent")} : {parent}\n" +
            $"{LocalizationService.T("Enfants")} : {(children.Length == 0 ? "—" : string.Join(", ", children))}\n" +
            $"{LocalizationService.T("Position")} : X {position.X:0.###}  Y {position.Y:0.###}  Z {position.Z:0.###}\n" +
            $"{LocalizationService.T("Vertices influencés")} : {influencedVertices:N0}" +
            (influencedVertices > 0 ? $"  · {LocalizationService.T("Poids max").ToLowerInvariant()} {strongestWeight:0.##}" : "");
        FocusBoneButton.IsEnabled = true;
    }

    private void FocusSelectedBone_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSkeleton is null || _selectedViewerBoneIndex is not int boneIndex ||
            FindBoneByIndex(boneIndex) is null)
            return;

        ShowSkeletonCheck.IsChecked = true;
        RefreshScene(fitCamera: false);
        _viewportController.FocusBone(_currentSkeleton, _animatedBoneWorlds, boneIndex);
    }

    private void ShowSkeletonFromBones_Click(object sender, RoutedEventArgs e)
    {
        ShowSkeletonCheck.IsChecked = true;
        RefreshScene(fitCamera: false);
    }

    private void SceneVisibility_Click(object sender, RoutedEventArgs e) => RefreshScene(fitCamera: false);
}
