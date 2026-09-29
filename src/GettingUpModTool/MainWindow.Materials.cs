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
    private void LoadSt(string path, bool selectTab, bool addRecent = true)
    {
        try
        {
            if (addRecent)
                AddRecentAsset(path);
            _currentSt = StReader.Read(path);
            _currentTexture = CreateBitmapSource(_currentSt);
            StPreviewImage.Source = _currentTexture;
            StSummaryText.Text =
                $"Texture : {_currentSt.FileName} — nom interne : {_currentSt.Name}\n" +
                $"{_currentSt.Width} × {_currentSt.Height} — {_currentSt.Codec}\n" +
                $"Payload : 0x{_currentSt.DataOffset:X} — {_currentSt.CompressedSize:N0} octets.\n" +
                "Lecteur ST : BGRA32, DXT1 et DXT5 avec prise en charge des payloads contenant des mipmaps.";
            StatusText.Text = LocalizationService.F("ST : {0} — {1:0}x{2:0} {3}", _currentSt.FileName, _currentSt.Width, _currentSt.Height, _currentSt.Codec);
            RefreshScene(fitCamera: false);
            if (selectTab) MainTabs.SelectedItem = StTab;
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "ST non reconnu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static BitmapSource CreateBitmapSource(StTexture texture, bool forceOpaque = false)
    {
        byte[] pixels = texture.Bgra32;
        if (forceOpaque)
        {
            pixels = (byte[])texture.Bgra32.Clone();
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
        }

        BitmapSource bitmap = BitmapSource.Create(
            texture.Width, texture.Height,
            96, 96, PixelFormats.Bgra32, null,
            pixels, texture.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private BitmapSource CreateDisplayBitmapSource(StTexture texture, MshSectionMaterialBinding binding)
    {
        string? meshFileName = _currentMshPath is null ? null : Path.GetFileName(_currentMshPath);
        bool usesMaskedAlphaAsMaterialData =
            string.Equals(meshFileName, "Tina_01.msh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(meshFileName, "ShannaRay_01.msh", StringComparison.OrdinalIgnoreCase);

        bool forceOpaque = usesMaskedAlphaAsMaterialData &&
            !binding.MaterialName.Contains("hair", StringComparison.OrdinalIgnoreCase);

        return CreateBitmapSource(texture, forceOpaque);
    }

    private void ApplyKnownMaterialOverrides(MshMaterialAnalysis analysis)
    {
        if (_currentMshPath is null)
            return;

        if (string.Equals(Path.GetFileName(_currentMshPath), "BizM_01.msh", StringComparison.OrdinalIgnoreCase))
        {
            ApplyBizM01MaterialOverride(analysis);
        }
    }

    private void ApplyBizM01MaterialOverride(MshMaterialAnalysis analysis)
    {
        string? bodyPath = FindTextureNearCurrentMesh("BizM_Body_01.st");
        string? headPath = FindTextureNearCurrentMesh("BizM_Head_01.st");
        if (bodyPath is null && headPath is null)
            return;

        if (bodyPath is not null)
            UpsertSectionBinding(analysis, 0, "Body", "BizM_Body_01", bodyPath);
        if (headPath is not null)
            UpsertSectionBinding(analysis, 1, "Head", "BizM_Head_01", headPath);

        const string note = "Correctif ciblé BizM_01 : association forcée section 0 = BizM_Body_01.st, section 1 = BizM_Head_01.st.";
        if (!analysis.Notes.Contains(note, StringComparer.Ordinal))
            analysis.Notes.Add(note);
    }

    private void UpsertSectionBinding(
        MshMaterialAnalysis analysis,
        int sectionIndex,
        string materialName,
        string textureReference,
        string resolvedTexturePath)
    {
        MshSectionMaterialBinding? binding = analysis.Bindings.FirstOrDefault(x => x.SectionIndex == sectionIndex);
        if (binding is null)
        {
            binding = new MshSectionMaterialBinding
            {
                SectionIndex = sectionIndex,
                RenderGroupName = "Base",
                MaterialName = materialName,
                TextureReference = textureReference,
                ResolvedTexturePath = resolvedTexturePath,
                IsVisible = true
            };
            analysis.Bindings.Add(binding);
        }
        else
        {
            binding.MaterialName = materialName;
            binding.TextureReference = textureReference;
            binding.ResolvedTexturePath = resolvedTexturePath;
            binding.IsVisible = true;
        }
    }

    private string? FindTextureNearCurrentMesh(string fileName)
    {
        if (_currentMshPath is null)
            return null;

        string? directory = Path.GetDirectoryName(Path.GetFullPath(_currentMshPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            string sibling = Path.Combine(directory, fileName);
            if (File.Exists(sibling))
                return sibling;
        }

        GameAsset? asset = _allGameAssets.FirstOrDefault(a =>
            a.Extension.Equals(".st", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Name, fileName, StringComparison.OrdinalIgnoreCase));
        return asset?.FullPath;
    }

    private IReadOnlySet<int>? GetKnownFlipVSections()
        => null;

    private IReadOnlySet<int>? GetKnownWrapVSections()
    {
        if (_currentMshPath is null)
            return null;

        
        
        if (string.Equals(Path.GetFileName(_currentMshPath), "WWAS_01.msh", StringComparison.OrdinalIgnoreCase))
            return new HashSet<int> { 1 };

        return null;
    }

    private void AnalyzeCurrentMaterials()
    {
        _sectionTextures.Clear();
        if (_currentMshBytes is null || _currentMshPath is null || _currentMshAnalysis is null)
            return;

        try
        {
            _currentMaterialAnalysis = MshMaterialAnalyzer.Analyze(
                _currentMshBytes,
                _currentMshAnalysis.Sections,
                _currentMshPath,
                _allGameAssets);
            ApplyKnownMaterialOverrides(_currentMaterialAnalysis);

            foreach (MshSectionMaterialBinding binding in _currentMaterialAnalysis.Bindings)
            {
                if (binding.ResolvedTexturePath is null)
                    continue;
                try
                {
                    StTexture texture = StReader.Read(binding.ResolvedTexturePath);
                    _sectionTextures[binding.SectionIndex] = CreateDisplayBitmapSource(texture, binding);
                    binding.TextureDecoded = true;
                }
                catch
                {
                    
                }
            }

            if (_currentTexture is null && _sectionTextures.OrderBy(x => x.Key).FirstOrDefault().Value is BitmapSource firstBitmap)
                _currentTexture = firstBitmap;

            
            
            _currentMshAnalysis.Issues.RemoveAll(x => x.Code.StartsWith("MATERIAL_", StringComparison.Ordinal));
            if (_currentMaterialAnalysis.Bindings.Count > 0)
            {
                _currentMshAnalysis.Issues.Add(new MshDiagnosticIssue
                {
                    Severity = MshDiagnosticSeverity.Info,
                    Code = "MATERIAL_MAPPING",
                    Message = $"{_currentMaterialAnalysis.Bindings.Count} matériau(x) de section reconnu(s), {_sectionTextures.Count} texture(s) ST décodée(s)."
                });
            }
            if (_currentMaterialAnalysis.HiddenCount > 0)
            {
                _currentMshAnalysis.Issues.Add(new MshDiagnosticIssue
                {
                    Severity = MshDiagnosticSeverity.Info,
                    Code = "MATERIAL_VARIANTS",
                    Message = $"{_currentMaterialAnalysis.HiddenCount} variante(s) alternative(s) masquée(s) automatiquement pour éviter les morceaux superposés."
                });
            }
            if (_currentMaterialAnalysis.UnmappedSectionCount > 0)
            {
                _currentMshAnalysis.Issues.Add(new MshDiagnosticIssue
                {
                    Severity = MshDiagnosticSeverity.Warning,
                    Code = "MATERIAL_UNMAPPED",
                    Message = $"{_currentMaterialAnalysis.UnmappedSectionCount} section(s) n'ont pas encore de mapping matériau reconnu. Elles restent visibles avec le rendu de secours."
                });
            }
            int missing = _currentMaterialAnalysis.Bindings.Count - _sectionTextures.Count;
            if (missing > 0)
            {
                _currentMshAnalysis.Issues.Add(new MshDiagnosticIssue
                {
                    Severity = MshDiagnosticSeverity.Warning,
                    Code = "MATERIAL_TEXTURE_MISSING",
                    Message = $"{missing} texture(s) de matériau ne sont pas encore résolues/décodées. Consulte Matériaux / textures."
                });
            }
            MshIssuesGrid.ItemsSource = null;
            MshIssuesGrid.ItemsSource = _currentMshAnalysis.Issues;
            MshDiagnosticSummaryText.Text +=
                $"\nMatériaux : {_currentMaterialAnalysis.Bindings.Count} associés — {_sectionTextures.Count} textures ST — {_currentMaterialAnalysis.HiddenCount} variante(s) masquée(s).";

            RefreshMaterialsUi();
        }
        catch (Exception ex)
        {
            _currentMaterialAnalysis = null;
            MaterialsGrid.ItemsSource = null;
            MaterialsSummaryText.Text = LocalizationService.F("Analyse matériaux non disponible : {0}", ex.Message);
        }
    }

    private void RefreshMaterialsUi()
    {
        if (_currentMaterialAnalysis is null)
        {
            MaterialsGrid.ItemsSource = null;
            MaterialsSummaryText.Text = LocalizationService.T("Aucun matériau analysé.");
            return;
        }

        MaterialsGrid.ItemsSource = null;
        MaterialsGrid.ItemsSource = _currentMaterialAnalysis.Bindings.OrderBy(x => x.SectionIndex).ToList();
        int total = _currentMaterialAnalysis.Bindings.Count;
        int resolved = _sectionTextures.Count;
        int hidden = _currentMaterialAnalysis.HiddenCount;
        string notes = _currentMaterialAnalysis.Notes.Count == 0
            ? ""
            : "\n" + string.Join(" ", _currentMaterialAnalysis.Notes);
        MaterialsSummaryText.Text =
            $"{Path.GetFileName(_currentMshPath)} — {_currentMshAnalysis?.Sections.Count ?? 0} section(s), " +
            $"{total} matériau(x) associé(s), {resolved}/{total} texture(s) résolue(s), {hidden} variante(s) masquée(s)." + notes;
    }

    private void ApplyRequestedHeadVariantIfAny()
    {
        if (string.IsNullOrWhiteSpace(_requestedHeadVariantTexturePath) || _currentMaterialAnalysis is null)
            return;

        string selectedPath = Path.GetFullPath(_requestedHeadVariantTexturePath);
        MshSectionMaterialBinding? selectedBinding = _currentMaterialAnalysis.Bindings.FirstOrDefault(b =>
            !string.IsNullOrWhiteSpace(b.ResolvedTexturePath) &&
            string.Equals(Path.GetFullPath(b.ResolvedTexturePath!), selectedPath, StringComparison.OrdinalIgnoreCase));

        if (selectedBinding is null)
            return;

        string family = GetHeadVariantFamily(selectedBinding.TextureReference, selectedBinding.ResolvedTexturePath);
        if (string.IsNullOrWhiteSpace(family))
            return;

        foreach (MshSectionMaterialBinding binding in _currentMaterialAnalysis.Bindings)
        {
            string bindingFamily = GetHeadVariantFamily(binding.TextureReference, binding.ResolvedTexturePath);
            if (!string.Equals(bindingFamily, family, StringComparison.OrdinalIgnoreCase))
                continue;

            bool isSelected = !string.IsNullOrWhiteSpace(binding.ResolvedTexturePath) &&
                string.Equals(Path.GetFullPath(binding.ResolvedTexturePath!), selectedPath, StringComparison.OrdinalIgnoreCase);
            binding.IsVisible = isSelected;
            binding.AutoHidden = !isSelected;
            binding.AutoRule = isSelected ? "selected head variant" : "hidden alternate head variant";
        }

        
        
        
        
        if (_currentMshPath is not null &&
            string.Equals(Path.GetFileName(_currentMshPath), "VanSq_01.msh", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFileName(selectedPath), "VanSq_Head_01.st", StringComparison.OrdinalIgnoreCase))
        {
            PreserveVanSqHands();
        }

        ApplyMaterialBindingsToMesh(_currentMesh);
        ApplyMaterialBindingsToMesh(_animatedMesh);
        RefreshMaterialsUi();
        RefreshScene(fitCamera: false);
        StatusText.Text = LocalizationService.F("Variante de tête sélectionnée : {0}", Path.GetFileName(selectedPath));
    }

    private void PreserveVanSqHands()
    {
        if (_currentMaterialAnalysis is null || _currentMesh is null)
            return;

        MshSectionMaterialBinding[] defaultHeadBindings = _currentMaterialAnalysis.Bindings
            .Where(b => !string.IsNullOrWhiteSpace(b.ResolvedTexturePath) &&
                string.Equals(Path.GetFileName(b.ResolvedTexturePath), "VanSq_Head_01.st", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (defaultHeadBindings.Length == 0)
            return;

        MshSectionMaterialBinding? handsBinding = defaultHeadBindings
            .Select(binding => new
            {
                Binding = binding,
                HorizontalSpan = GetSectionHorizontalSpan(_currentMesh, binding.SectionIndex)
            })
            .OrderByDescending(x => x.HorizontalSpan)
            .Select(x => x.Binding)
            .FirstOrDefault();

        if (handsBinding is null)
            return;

        handsBinding.IsVisible = true;
        handsBinding.AutoHidden = false;
        handsBinding.AutoRule = "VanSq hands preserved from default head texture";
    }

    private static double GetSectionHorizontalSpan(MeshData mesh, int sectionIndex)
    {
        MeshPart? part = mesh.Parts.FirstOrDefault(p => p.SectionIndex == sectionIndex);
        if (part is null || part.IndexCount <= 0)
            return 0;

        int start = Math.Clamp(part.IndexStart, 0, mesh.Indices.Count);
        int count = Math.Clamp(part.IndexCount, 0, mesh.Indices.Count - start);
        if (count <= 0)
            return 0;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        for (int i = start; i < start + count; i++)
        {
            int vertexIndex = mesh.Indices[i];
            if (vertexIndex < 0 || vertexIndex >= mesh.Positions.Count)
                continue;
            float x = mesh.Positions[vertexIndex].X;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
        }

        return float.IsFinite(minX) && float.IsFinite(maxX) ? maxX - minX : 0;
    }

    private static string GetHeadVariantFamily(string? textureReference, string? resolvedTexturePath)
    {
        string? name = textureReference;
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(resolvedTexturePath))
            name = Path.GetFileNameWithoutExtension(resolvedTexturePath);
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string stem = Path.GetFileNameWithoutExtension(name).Trim();
        if (!(stem.Contains("head", StringComparison.OrdinalIgnoreCase) || stem.Contains("face", StringComparison.OrdinalIgnoreCase)))
            return string.Empty;

        stem = System.Text.RegularExpressions.Regex.Replace(stem, @"(?:_|\s)(\d{2})$", string.Empty, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return stem;
    }

    private void ApplyMaterialBindingsToMesh(MeshData? mesh)
    {
        if (mesh is null || _currentMaterialAnalysis is null || mesh.Parts.Count == 0)
            return;

        var bySection = _currentMaterialAnalysis.Bindings.ToDictionary(x => x.SectionIndex);
        foreach (MeshPart part in mesh.Parts)
        {
            if (!bySection.TryGetValue(part.SectionIndex, out MshSectionMaterialBinding? binding))
                continue;
            part.RenderGroupName = binding.RenderGroupName;
            part.MaterialName = binding.MaterialName;
            part.TextureReference = binding.TextureReference;
            part.IsVisible = binding.IsVisible;
        }
    }

    private void ApplyMaterialVisibility_Click(object sender, RoutedEventArgs e)
    {
        MaterialsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        MaterialsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ApplyMaterialBindingsToMesh(_currentMesh);
        ApplyMaterialBindingsToMesh(_animatedMesh);
        RefreshMaterialsUi();
        RefreshScene(fitCamera: false);
    }

    private void AutoMaterialVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMaterialAnalysis is null)
            return;
        MshMaterialAnalyzer.ApplyDefaultVisibility(_currentMaterialAnalysis.Bindings);
        ApplyMaterialBindingsToMesh(_currentMesh);
        ApplyMaterialBindingsToMesh(_animatedMesh);
        RefreshMaterialsUi();
        RefreshScene(fitCamera: false);
        StatusText.Text = LocalizationService.T("Mode propre : variantes alternatives masquées automatiquement.");
    }

    private void ShowAllMaterials_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMaterialAnalysis is null)
            return;
        foreach (MshSectionMaterialBinding binding in _currentMaterialAnalysis.Bindings)
        {
            binding.IsVisible = true;
            binding.AutoHidden = false;
            binding.AutoRule = "";
        }
        ApplyMaterialBindingsToMesh(_currentMesh);
        ApplyMaterialBindingsToMesh(_animatedMesh);
        RefreshMaterialsUi();
        RefreshScene(fitCamera: false);
    }

    private void OpenSelectedMaterialTexture_Click(object sender, RoutedEventArgs e)
    {
        if (MaterialsGrid.SelectedItem is not MshSectionMaterialBinding binding || binding.ResolvedTexturePath is null)
            return;
        OpenInExplorerSafe(binding.ResolvedTexturePath);
    }
}
