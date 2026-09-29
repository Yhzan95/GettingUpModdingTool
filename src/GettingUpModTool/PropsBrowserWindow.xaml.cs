using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using GettingUpModTool.Core.IO;
using GettingUpModTool.Core.Models;
using GettingUpModTool.Core.Formats.MSH;
using GettingUpModTool.Core.Formats.ST;
using GettingUpModTool.Core.Game;

namespace GettingUpModTool;

public partial class PropsBrowserWindow : Window
{
    private readonly List<GameAsset> _allAssets;
    private readonly List<GameAsset> _allPropMeshes;
    private readonly List<PropCategoryRow> _categories;
    private MshMaterialAnalysis? _selectedMaterialAnalysis;
    private int _meshRequestId;
    private int _texturePreviewRequestId;
    private bool _ready;

    public string? SelectedMeshPath { get; private set; }

    public PropsBrowserWindow(IEnumerable<GameAsset> allAssets, string? initialSearch = null)
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => LocalizationService.Apply(this), System.Windows.Threading.DispatcherPriority.Loaded);

        _allAssets = allAssets.ToList();
        _allPropMeshes = _allAssets
            .Where(PropCatalog.IsPropMesh)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _categories = PropCatalog.Categories.Select(def => new PropCategoryRow
        {
            Key = def.Key,
            Name = LocalizationService.T(def.Name),
            Icon = def.Icon,
            Description = LocalizationService.T(def.Description),
            Count = def.Key == "all"
                ? _allPropMeshes.Count
                : _allPropMeshes.Count(a => PropCatalog.GetCategoryKey(a) == def.Key)
        }).ToList();

        PropCategoryList.ItemsSource = _categories;
        PropCategoryList.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(initialSearch))
            PropSearchBox.Text = initialSearch;

        _ready = true;
        RefreshMeshList(selectFirst: true);
    }

    private void PropCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready)
            RefreshMeshList(selectFirst: true);
    }

    private void PropSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready)
            RefreshMeshList(selectFirst: false);
    }

    private void RefreshMeshList(bool selectFirst)
    {
        string category = (PropCategoryList.SelectedItem as PropCategoryRow)?.Key ?? "all";
        string search = PropSearchBox.Text.Trim();

        IEnumerable<GameAsset> query = _allPropMeshes;
        if (!category.Equals("all", StringComparison.OrdinalIgnoreCase))
            query = query.Where(a => PropCatalog.GetCategoryKey(a).Equals(category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(a =>
                a.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                a.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                a.DirectoryRelativePath.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        List<GameAsset> rows = query.ToList();
        GameAsset? previous = PropMeshList.SelectedItem as GameAsset;
        PropMeshList.ItemsSource = rows;
        string categoryName = LocalizationService.T(PropCatalog.CategoryName(category));
        PropCountText.Text = category == "all"
            ? LocalizationService.F("{0:N0} mesh(es) objet", rows.Count)
            : $"{rows.Count:N0} · {categoryName}";

        if (previous is not null)
        {
            GameAsset? replacement = rows.FirstOrDefault(a => string.Equals(a.FullPath, previous.FullPath, StringComparison.OrdinalIgnoreCase));
            if (replacement is not null)
            {
                PropMeshList.SelectedItem = replacement;
                PropMeshList.ScrollIntoView(replacement);
                return;
            }
        }

        if (selectFirst && rows.Count > 0)
            PropMeshList.SelectedIndex = 0;
        else if (rows.Count == 0)
            ClearPropSelection("Aucun objet ne correspond au filtre.");
    }

    private async void PropMeshList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PropMeshList.SelectedItem is not GameAsset mesh)
        {
            ClearPropSelection("Choisis un objet.");
            return;
        }

        int requestId = ++_meshRequestId;
        PropTitleText.Text = mesh.Name;
        PropInfoText.Text = LocalizationService.T("Analyse du mesh et des textures…");
        PropStatusText.Text = mesh.RelativePath;
        PropTextureList.ItemsSource = null;
        ClearTexturePreview("Chargement des textures…");
        LoadPropMeshButton.IsEnabled = File.Exists(mesh.FullPath);
        OpenPropFolderButton.IsEnabled = File.Exists(mesh.FullPath);
        ExportPropObjButton.IsEnabled = File.Exists(mesh.FullPath);
        ExportPropGltfButton.IsEnabled = File.Exists(mesh.FullPath);
        ExportPropPngButton.IsEnabled = false;
        OpenPropTexturesButton.IsEnabled = false;

        try
        {
            PropAnalysisResult analysis = await Task.Run(() => AnalyzeMesh(mesh));
            if (requestId != _meshRequestId)
                return;

            _selectedMaterialAnalysis = analysis.MaterialAnalysis;
            PropTextureList.ItemsSource = analysis.Textures;
            PropInfoText.Text = LocalizationService.T(
                $"{analysis.LayoutLabel} · {analysis.SectionCount} section(s) · {analysis.DrawBatchCount} lot(s) · " +
                $"{analysis.VertexCount:N0} vertices · {analysis.TriangleCount:N0} triangles · {analysis.Textures.Count} texture(s).");
            PropStatusText.Text = mesh.RelativePath;
            OpenPropTexturesButton.IsEnabled = true;
            ExportPropPngButton.IsEnabled = analysis.Textures.Count > 0;

            if (analysis.Textures.Count > 0)
                PropTextureList.SelectedIndex = 0;
            else
                ClearTexturePreview("Aucune texture ST liée ou située à côté de ce mesh.");
        }
        catch (Exception ex)
        {
            if (requestId != _meshRequestId)
                return;

            _selectedMaterialAnalysis = null;
            List<PropTextureItem> fallback = BuildSameFolderTextures(mesh);
            PropTextureList.ItemsSource = fallback;
            PropInfoText.Text = LocalizationService.F("Analyse géométrique indisponible : {0}. Les textures du dossier restent accessibles.", ex.Message);
            OpenPropTexturesButton.IsEnabled = true;
            ExportPropPngButton.IsEnabled = fallback.Count > 0;
            if (fallback.Count > 0)
                PropTextureList.SelectedIndex = 0;
        }
    }

    private PropAnalysisResult AnalyzeMesh(GameAsset mesh)
    {
        byte[] data = File.ReadAllBytes(mesh.FullPath);
        var skeleton = MshSkeletonDetector.Detect(data);
        MshStructureAnalysis structure = MshGenericStructureAnalyzer.Analyze(data, skeleton);
        MshMaterialAnalysis materials = MshMaterialAnalyzer.Analyze(data, structure.Sections, mesh.FullPath, _allAssets);

        var byPath = new Dictionary<string, PropTextureItem>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, MshSectionMaterialBinding> group in materials.Bindings
                     .Where(b => !string.IsNullOrWhiteSpace(b.ResolvedTexturePath) && File.Exists(b.ResolvedTexturePath))
                     .GroupBy(b => Path.GetFullPath(b.ResolvedTexturePath!), StringComparer.OrdinalIgnoreCase))
        {
            string fullPath = group.Key;
            int[] sections = group.Select(x => x.SectionIndex).Distinct().OrderBy(x => x).ToArray();
            string[] materialNames = group.Select(x => x.MaterialName)
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != "<inconnu>")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            byPath[fullPath] = new PropTextureItem
            {
                FullPath = fullPath,
                DisplayName = Path.GetFileName(fullPath),
                SourceLabel = sections.Length == 1 ? LocalizationService.F("Section {0:0}", sections[0]) : LocalizationService.F("Sections {0:0}", string.Join(", ", sections)),
                DetailLine = materialNames.Length == 0 ? LocalizationService.T("Texture liée au mesh") : string.Join(" · ", materialNames)
            };
        }

        
        
        foreach (PropTextureItem item in BuildSameFolderTextures(mesh))
        {
            string full = Path.GetFullPath(item.FullPath);
            if (!byPath.ContainsKey(full))
                byPath[full] = item;
        }

        bool isStatic = structure.Sections.Count > 0 && structure.Sections.All(x => !x.IsSkinned);
        string layoutLabel = structure.Sections.Count == 0
            ? "layout non reconnu"
            : isStatic
                ? "mesh statique"
                : structure.Sections.All(x => x.IsSkinned) ? "mesh skinné/animable" : "mesh hybride";

        return new PropAnalysisResult
        {
            SectionCount = structure.Sections.Count,
            DrawBatchCount = structure.TotalDrawBatches,
            VertexCount = structure.TotalVertices,
            TriangleCount = structure.TotalTriangles,
            LayoutLabel = layoutLabel,
            MaterialAnalysis = materials,
            Textures = byPath.Values
                .OrderBy(x => x.SourceLabel.StartsWith("Section", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private List<PropTextureItem> BuildSameFolderTextures(GameAsset mesh)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(mesh.FullPath));
        if (directory is null)
            return [];

        string normalizedDirectory = Path.TrimEndingDirectorySeparator(directory);
        return _allAssets
            .Where(a => a.Kind == GameAssetKind.Texture || a.Extension.Equals(".st", StringComparison.OrdinalIgnoreCase))
            .Where(a => string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(a.FullPath)) ?? ""),
                normalizedDirectory,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new PropTextureItem
            {
                FullPath = a.FullPath,
                DisplayName = a.Name,
                SourceLabel = "Dossier",
                DetailLine = "Texture située à côté du mesh"
            })
            .ToList();
    }

    private async void PropTextureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PropTextureList.SelectedItem is not PropTextureItem item)
        {
            ClearTexturePreview("Sélectionne une texture");
            return;
        }

        int requestId = ++_texturePreviewRequestId;
        PropTexturePreviewMessage.Text = LocalizationService.T("Chargement…");
        PropTexturePreviewMessage.Visibility = Visibility.Visible;
        PropTexturePreviewImage.Source = null;

        try
        {
            StTexture texture = await Task.Run(() => StReader.Read(item.FullPath));
            BitmapSource bitmap = CreateBitmapSource(texture);
            if (requestId != _texturePreviewRequestId)
                return;

            PropTexturePreviewImage.Source = bitmap;
            PropTexturePreviewMessage.Visibility = Visibility.Collapsed;
            PropStatusText.Text = $"{item.DisplayName} · {texture.Width} × {texture.Height} · {texture.Codec}";
        }
        catch (Exception ex)
        {
            if (requestId != _texturePreviewRequestId)
                return;
            PropTexturePreviewImage.Source = null;
            PropTexturePreviewMessage.Text = LocalizationService.F("Aperçu impossible\n{0}", ex.Message);
            PropTexturePreviewMessage.Visibility = Visibility.Visible;
        }
    }

    private static BitmapSource CreateBitmapSource(StTexture texture)
    {
        BitmapSource bitmap = BitmapSource.Create(
            texture.Width, texture.Height, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null,
            texture.Bgra32, texture.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private void ClearPropSelection(string message)
    {
        ++_meshRequestId;
        _selectedMaterialAnalysis = null;
        PropTitleText.Text = LocalizationService.T("Aucun objet sélectionné");
        PropInfoText.Text = LocalizationService.T(message);
        PropStatusText.Text = LocalizationService.T(message);
        PropTextureList.ItemsSource = null;
        LoadPropMeshButton.IsEnabled = false;
        OpenPropFolderButton.IsEnabled = false;
        ExportPropObjButton.IsEnabled = false;
        ExportPropGltfButton.IsEnabled = false;
        ExportPropPngButton.IsEnabled = false;
        OpenPropTexturesButton.IsEnabled = false;
        ClearTexturePreview("Sélectionne un objet");
    }

    private void ClearTexturePreview(string message)
    {
        ++_texturePreviewRequestId;
        PropTexturePreviewImage.Source = null;
        PropTexturePreviewMessage.Text = LocalizationService.T(message);
        PropTexturePreviewMessage.Visibility = Visibility.Visible;
    }

    private void PropMeshList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LoadSelectedMeshAndClose();
    private void LoadPropMesh_Click(object sender, RoutedEventArgs e) => LoadSelectedMeshAndClose();

    private void LoadSelectedMeshAndClose()
    {
        if (PropMeshList.SelectedItem is not GameAsset mesh || !File.Exists(mesh.FullPath))
            return;
        SelectedMeshPath = mesh.FullPath;
        DialogResult = true;
    }

    private (MeshData Mesh, SkeletonData? Skeleton, MshMaterialAnalysis Materials, Dictionary<int, byte[]> TexturePngs, List<string> Warnings) BuildPropExportData(GameAsset asset)
    {
        byte[] data = File.ReadAllBytes(asset.FullPath);
        SkeletonData? skeleton = MshSkeletonDetector.Detect(data);
        MshStructureAnalysis structure = MshGenericStructureAnalyzer.Analyze(data, skeleton);
        if (structure.Sections.Count == 0)
            throw new InvalidDataException("Aucune section MSH exploitable n'a été détectée pour cet objet.");

        MeshData mesh = MshMultiSectionReader.Read(data, structure.Sections);
        MshMaterialAnalysis materials = MshMaterialAnalyzer.Analyze(data, structure.Sections, asset.FullPath, _allAssets);

        
        
        
        var bySection = materials.Bindings
            .GroupBy(x => x.SectionIndex)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(b => b.IsVisible)
                      .ThenByDescending(b => !string.IsNullOrWhiteSpace(b.ResolvedTexturePath) && File.Exists(b.ResolvedTexturePath))
                      .ThenByDescending(b => !string.IsNullOrWhiteSpace(b.MaterialName))
                      .First());

        foreach (MeshPart part in mesh.Parts)
        {
            if (!bySection.TryGetValue(part.SectionIndex, out MshSectionMaterialBinding? binding))
                continue;
            part.RenderGroupName = binding.RenderGroupName;
            part.MaterialName = binding.MaterialName;
            part.TextureReference = binding.TextureReference;
            part.IsVisible = binding.IsVisible;
        }

        var textures = new Dictionary<int, byte[]>();
        var warnings = new List<string>();
        foreach (MshSectionMaterialBinding binding in bySection.Values.OrderBy(b => b.SectionIndex))
        {
            if (binding.ResolvedTexturePath is null || !File.Exists(binding.ResolvedTexturePath))
            {
                if (!string.IsNullOrWhiteSpace(binding.TextureReference))
                    warnings.Add($"{binding.TextureReference} (section {binding.SectionIndex}) : texture introuvable");
                continue;
            }
            try
            {
                StTexture st = StReader.Read(binding.ResolvedTexturePath);
                BitmapSource bitmap = CreateBitmapSource(st);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                textures[binding.SectionIndex] = stream.ToArray();
            }
            catch (Exception ex)
            {
                warnings.Add($"{Path.GetFileName(binding.ResolvedTexturePath)} (section {binding.SectionIndex}) : {ex.Message}");
            }
        }

        return (mesh, skeleton, materials, textures, warnings);
    }

    private async void ExportPropPng_Click(object sender, RoutedEventArgs e)
    {
        if (PropMeshList.SelectedItem is not GameAsset mesh)
            return;

        List<PropTextureItem> textures = (PropTextureList.ItemsSource as IEnumerable<PropTextureItem>)?.ToList() ?? [];
        if (textures.Count == 0)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.T("Choisir le dossier pour les textures PNG")
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            string folder = Path.Combine(dialog.FolderName, Path.GetFileNameWithoutExtension(mesh.Name) + "_Textures");
            IReadOnlyList<string> exported = await Task.Run(() => StPngExporter.ExportMany(textures.Select(x => x.FullPath), folder));
            PropStatusText.Text = LocalizationService.F("{0:N0} texture(s) PNG exportée(s) dans {1}", exported.Count, folder);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Export PNG", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExportPropObj_Click(object sender, RoutedEventArgs e)
    {
        if (PropMeshList.SelectedItem is not GameAsset asset || !File.Exists(asset.FullPath))
            return;

        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("Exporter l'objet en OBJ"),
            Filter = LocalizationService.T("Wavefront OBJ (*.obj)|*.obj"),
            FileName = Path.GetFileNameWithoutExtension(asset.FullPath) + ".obj"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var export = await Task.Run(() => BuildPropExportData(asset));
            await Task.Run(() => ObjExporter.Export(export.Mesh, dialog.FileName, export.TexturePngs));
            PropStatusText.Text = export.Warnings.Count == 0 ? LocalizationService.F("OBJ + MTL + textures exportés : {0}", dialog.FileName) : LocalizationService.F("OBJ exporté avec {0:N0} avertissement(s) texture : {1}", export.Warnings.Count, dialog.FileName);
            ShowTextureExportWarnings("OBJ", export.Warnings);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Export OBJ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExportPropGltf_Click(object sender, RoutedEventArgs e)
    {
        if (PropMeshList.SelectedItem is not GameAsset asset || !File.Exists(asset.FullPath))
            return;

        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("Exporter l'objet pour Blender (glTF 2.0)"),
            Filter = LocalizationService.T("glTF 2.0 (*.gltf)|*.gltf"),
            FileName = Path.GetFileNameWithoutExtension(asset.FullPath) + ".gltf"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var export = await Task.Run(() => BuildPropExportData(asset));
            await Task.Run(() => GltfExporter.Export(export.Mesh, export.Skeleton, dialog.FileName, export.TexturePngs));
            string details = export.Skeleton is not null && export.Mesh.HasSkinning
                ? $"mesh + skin + squelette + {export.TexturePngs.Count} texture(s)"
                : $"mesh + {export.TexturePngs.Count} texture(s)";
            PropStatusText.Text = export.Warnings.Count == 0 ? LocalizationService.F("glTF exporté ({0}) : {1}", details, dialog.FileName) : LocalizationService.F("glTF exporté ({0}) avec {1:N0} avertissement(s) texture : {2}", details, export.Warnings.Count, dialog.FileName);
            ShowTextureExportWarnings("glTF", export.Warnings);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Export glTF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


    private static void ShowTextureExportWarnings(string format, IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
            return;

        ExportLog.WriteTextureWarnings(format, warnings);

        const int maxVisible = 20;
        var lines = warnings.Take(maxVisible).Select(w => "• " + w).ToList();
        if (warnings.Count > maxVisible)
            lines.Add(LocalizationService.T("… et d’autres avertissements sont disponibles dans le journal."));

        string message = LocalizationService.T("Certaines textures n’ont pas pu être exportées correctement :")
            + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine, lines);
        LocalizationService.Show(
            message,
            $"{LocalizationService.T("Avertissements de textures")} — {format}",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OpenPropTextures_Click(object sender, RoutedEventArgs e)
    {
        if (PropMeshList.SelectedItem is not GameAsset mesh)
            return;
        var dialog = new TextureBrowserWindow(_allAssets, mesh.FullPath, _selectedMaterialAnalysis) { Owner = this };
        dialog.ShowDialog();
    }

    private void OpenPropFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PropMeshList.SelectedItem is not GameAsset mesh || !File.Exists(mesh.FullPath))
            return;
        OpenInExplorerSafe(mesh.FullPath);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class PropAnalysisResult
    {
        public int SectionCount { get; init; }
        public int DrawBatchCount { get; init; }
        public int VertexCount { get; init; }
        public int TriangleCount { get; init; }
        public string LayoutLabel { get; init; } = "layout inconnu";
        public MshMaterialAnalysis? MaterialAnalysis { get; init; }
        public List<PropTextureItem> Textures { get; init; } = [];
    }

    public sealed class PropTextureItem
    {
        public required string FullPath { get; init; }
        public required string DisplayName { get; init; }
        public string SourceLabel { get; init; } = "Dossier";
        public string DetailLine { get; init; } = "";
    }

    public sealed class PropCategoryRow
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public required string Icon { get; init; }
        public required string Description { get; init; }
        public int Count { get; init; }
        public string CountLabel => Count.ToString("N0");
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
