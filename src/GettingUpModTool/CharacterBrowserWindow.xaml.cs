using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using GettingUpModTool.Core.Formats.MSH;
using GettingUpModTool.Core.Formats.ST;
using GettingUpModTool.Core.Game;
using GettingUpModTool.Core.IO;

namespace GettingUpModTool;

public partial class CharacterBrowserWindow : Window
{
    private readonly List<GameAsset> _allAssets;
    private readonly List<GameAsset> _allCharacterMeshes;
    private MshMaterialAnalysis? _selectedMaterialAnalysis;
    private int _meshRequestId;
    private int _texturePreviewRequestId;
    private bool _ready;

    public string? SelectedMeshPath { get; private set; }
    public int? SelectedHeadVariantSectionIndex { get; private set; }
    public string? SelectedHeadVariantTexturePath { get; private set; }

    public CharacterBrowserWindow(IEnumerable<GameAsset> allAssets, string? initialSearch = null)
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => LocalizationService.Apply(this), System.Windows.Threading.DispatcherPriority.Loaded);

        _allAssets = allAssets.ToList();
        _allCharacterMeshes = _allAssets
            .Where(FullGameDatabase.IsCharacterMesh)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(initialSearch))
            CharacterSearchBox.Text = initialSearch;

        _ready = true;
        RefreshMeshList(selectFirst: true);
    }

    private void CharacterSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready)
            RefreshMeshList(selectFirst: false);
    }

    private void RefreshMeshList(bool selectFirst)
    {
        string search = CharacterSearchBox.Text.Trim();
        IEnumerable<GameAsset> query = _allCharacterMeshes;
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(a =>
                a.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                a.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                a.DirectoryRelativePath.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        List<GameAsset> rows = query.ToList();
        GameAsset? previous = CharacterMeshList.SelectedItem as GameAsset;
        CharacterMeshList.ItemsSource = rows;
        CharacterCountText.Text = LocalizationService.F("{0:N0} mesh(es)", rows.Count);

        if (previous is not null)
        {
            GameAsset? replacement = rows.FirstOrDefault(a =>
                string.Equals(a.FullPath, previous.FullPath, StringComparison.OrdinalIgnoreCase));
            if (replacement is not null)
            {
                CharacterMeshList.SelectedItem = replacement;
                CharacterMeshList.ScrollIntoView(replacement);
                return;
            }
        }

        if (selectFirst && rows.Count > 0)
            CharacterMeshList.SelectedIndex = 0;
        else if (rows.Count == 0)
            ClearCharacterSelection("Aucun mesh personnage ne correspond à la recherche.");
    }

    private async void CharacterMeshList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CharacterMeshList.SelectedItem is not GameAsset mesh)
        {
            ClearCharacterSelection("Choisis un mesh personnage.");
            return;
        }

        int requestId = ++_meshRequestId;
        SelectedHeadVariantSectionIndex = null;
        SelectedHeadVariantTexturePath = null;
        CharacterTitleText.Text = mesh.Name;
        CharacterInfoText.Text = LocalizationService.T("Analyse des sections et des textures…");
        CharacterStatusText.Text = mesh.RelativePath;
        CharacterTextureList.ItemsSource = null;
        ClearTexturePreview("Chargement des textures…");
        LoadCharacterMeshButton.IsEnabled = File.Exists(mesh.FullPath);
        OpenMeshFolderButton.IsEnabled = File.Exists(mesh.FullPath);
        OpenCharacterTexturesButton.IsEnabled = false;
        ExportCharacterPngButton.IsEnabled = false;

        try
        {
            CharacterAnalysisResult analysis = await Task.Run(() => AnalyzeMesh(mesh));
            if (requestId != _meshRequestId)
                return;

            _selectedMaterialAnalysis = analysis.MaterialAnalysis;
            CharacterTextureList.ItemsSource = analysis.Textures;
            CharacterInfoText.Text = LocalizationService.T(
                $"{analysis.SectionCount} section(s) · {analysis.DrawBatchCount} lot(s) de rendu · {analysis.BoneCountLabel} · {analysis.Textures.Count} texture(s) trouvée(s). " +
                "Cette page n'utilise aucune animation.");
            CharacterStatusText.Text = mesh.RelativePath;
            OpenCharacterTexturesButton.IsEnabled = true;
            ExportCharacterPngButton.IsEnabled = analysis.Textures.Count > 0;

            if (analysis.Textures.Count > 0)
            {
                CharacterTextureItem? defaultHead = analysis.Textures.FirstOrDefault(x =>
                    x.IsHeadVariant && !x.SourceLabel.Contains("masquée", StringComparison.OrdinalIgnoreCase));
                if (defaultHead is not null)
                {
                    CharacterTextureList.SelectedItem = defaultHead;
                    SelectedHeadVariantSectionIndex = defaultHead.PrimarySectionIndex;
                    SelectedHeadVariantTexturePath = defaultHead.FullPath;
                }
                else
                {
                    CharacterTextureList.SelectedIndex = 0;
                }
            }
            else
                ClearTexturePreview("Aucune texture ST trouvée pour ce mesh.");
        }
        catch (Exception ex)
        {
            if (requestId != _meshRequestId)
                return;

            _selectedMaterialAnalysis = null;
            List<CharacterTextureItem> fallbackTextures = BuildSameFolderTextures(mesh);
            CharacterTextureList.ItemsSource = fallbackTextures;
            CharacterInfoText.Text = LocalizationService.F("Analyse avancée indisponible : {0}", ex.Message);
            OpenCharacterTexturesButton.IsEnabled = true;
            ExportCharacterPngButton.IsEnabled = fallbackTextures.Count > 0;
        }
    }

    private CharacterAnalysisResult AnalyzeMesh(GameAsset mesh)
    {
        byte[] data = File.ReadAllBytes(mesh.FullPath);
        var skeleton = MshSkeletonDetector.Detect(data);
        MshStructureAnalysis structure = MshStructureAnalyzer.Analyze(data, skeleton);
        MshMaterialAnalysis materials = MshMaterialAnalyzer.Analyze(data, structure.Sections, mesh.FullPath, _allAssets);

        var byPath = new Dictionary<string, CharacterTextureItem>(StringComparer.OrdinalIgnoreCase);
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

            bool allHidden = group.All(x => !x.IsVisible);
            string displayName = Path.GetFileName(fullPath);
            bool isVanSq = string.Equals(mesh.Name, "VanSq_01.msh", StringComparison.OrdinalIgnoreCase);
            bool isVanSqAltHead = isVanSq && (string.Equals(displayName, "VanSq_Head_02.st", StringComparison.OrdinalIgnoreCase)
                || string.Equals(displayName, "VanSq_Head_03.st", StringComparison.OrdinalIgnoreCase)
                || string.Equals(displayName, "VanSq_Head_04.st", StringComparison.OrdinalIgnoreCase));

            string sectionLabel = LocalizationService.T(sections.Length == 1
                ? $"Section {sections[0]}"
                : $"Sections {string.Join(", ", sections)}");

            string detailLine = (materialNames.Length == 0
                    ? LocalizationService.T("Texture liée au mesh")
                    : string.Join(" · ", materialNames)) +
                    (allHidden ? " · " + LocalizationService.T("variante alternative masquée en 3D") : "");
            if (isVanSqAltHead)
                detailLine += " · " + LocalizationService.T("tête uniquement (mains conservées depuis VanSq_Head_01.st)");

            byPath[fullPath] = new CharacterTextureItem
            {
                FullPath = fullPath,
                DisplayName = displayName,
                SourceLabel = allHidden ? sectionLabel + " · masquée" : sectionLabel,
                DetailLine = detailLine,
                PrimarySectionIndex = sections.Length == 1 ? sections[0] : null,
                IsHeadVariant = IsHeadTextureName(displayName)
            };
        }

        
        
        
        
        if (byPath.Count == 0)
        {
            foreach (CharacterTextureItem item in BuildSameFolderTextures(mesh))
            {
                string fullPath = Path.GetFullPath(item.FullPath);
                if (!byPath.ContainsKey(fullPath))
                    byPath[fullPath] = item;
            }
        }

        return new CharacterAnalysisResult
        {
            SectionCount = structure.Sections.Count,
            DrawBatchCount = structure.TotalDrawBatches,
            BoneCountLabel = skeleton is null ? LocalizationService.T("squelette non détecté") : LocalizationService.F("{0:N0} os", skeleton.Bones.Count),
            MaterialAnalysis = materials,
            Textures = byPath.Values
                .OrderBy(x => x.SourceLabel.StartsWith("Section", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static bool IsHeadTextureName(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.Contains("head", StringComparison.OrdinalIgnoreCase) ||
               stem.Contains("face", StringComparison.OrdinalIgnoreCase);
    }

    private List<CharacterTextureItem> BuildSameFolderTextures(GameAsset mesh)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(mesh.FullPath));
        if (directory is null)
            return [];

        return _allAssets
            .Where(a => a.Kind == GameAssetKind.Texture || a.Extension.Equals(".st", StringComparison.OrdinalIgnoreCase))
            .Where(a => string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(a.FullPath)) ?? ""),
                Path.TrimEndingDirectorySeparator(directory),
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new CharacterTextureItem
            {
                FullPath = a.FullPath,
                DisplayName = a.Name,
                SourceLabel = "Dossier",
                DetailLine = "Texture située à côté du mesh"
            })
            .ToList();
    }

    private async void CharacterTextureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CharacterTextureList.SelectedItem is not CharacterTextureItem item)
        {
            ClearTexturePreview("Sélectionne une texture");
            return;
        }

        if (item.IsHeadVariant)
        {
            SelectedHeadVariantTexturePath = item.FullPath;
            if (item.PrimarySectionIndex is int headSection)
                SelectedHeadVariantSectionIndex = headSection;
        }

        int requestId = ++_texturePreviewRequestId;
        CharacterTexturePreviewMessage.Text = LocalizationService.T("Chargement…");
        CharacterTexturePreviewMessage.Visibility = Visibility.Visible;
        CharacterTexturePreviewImage.Source = null;

        try
        {
            StTexture texture = await Task.Run(() => StReader.Read(item.FullPath));
            BitmapSource bitmap = CreateBitmapSource(texture);
            if (requestId != _texturePreviewRequestId)
                return;

            CharacterTexturePreviewImage.Source = bitmap;
            CharacterTexturePreviewMessage.Visibility = Visibility.Collapsed;
            CharacterStatusText.Text = item.IsHeadVariant && item.PrimarySectionIndex is int
                ? $"{item.DisplayName} · {texture.Width} × {texture.Height} · {texture.Codec} · {LocalizationService.T("Cette tête sera utilisée en 3D.")}"
                : $"{item.DisplayName} · {texture.Width} × {texture.Height} · {texture.Codec}";
        }
        catch (Exception ex)
        {
            if (requestId != _texturePreviewRequestId)
                return;

            CharacterTexturePreviewImage.Source = null;
            CharacterTexturePreviewMessage.Text = LocalizationService.F("Aperçu impossible\n{0}", ex.Message);
            CharacterTexturePreviewMessage.Visibility = Visibility.Visible;
        }
    }

    private static BitmapSource CreateBitmapSource(StTexture texture)
    {
        BitmapSource bitmap = BitmapSource.Create(
            texture.Width,
            texture.Height,
            96,
            96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            texture.Bgra32,
            texture.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private void ClearCharacterSelection(string message)
    {
        ++_meshRequestId;
        _selectedMaterialAnalysis = null;
        SelectedHeadVariantSectionIndex = null;
        SelectedHeadVariantTexturePath = null;
        CharacterTitleText.Text = LocalizationService.T("Aucun mesh sélectionné");
        CharacterInfoText.Text = LocalizationService.T(message);
        CharacterStatusText.Text = LocalizationService.T(message);
        CharacterTextureList.ItemsSource = null;
        LoadCharacterMeshButton.IsEnabled = false;
        OpenMeshFolderButton.IsEnabled = false;
        OpenCharacterTexturesButton.IsEnabled = false;
        ExportCharacterPngButton.IsEnabled = false;
        ClearTexturePreview("Sélectionne un personnage");
    }

    private void ClearTexturePreview(string message)
    {
        ++_texturePreviewRequestId;
        CharacterTexturePreviewImage.Source = null;
        CharacterTexturePreviewMessage.Text = LocalizationService.T(message);
        CharacterTexturePreviewMessage.Visibility = Visibility.Visible;
    }

    private void CharacterMeshList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        LoadSelectedMeshAndClose();
    }

    private void LoadCharacterMesh_Click(object sender, RoutedEventArgs e)
    {
        LoadSelectedMeshAndClose();
    }

    private void LoadSelectedMeshAndClose()
    {
        if (CharacterMeshList.SelectedItem is not GameAsset mesh || !File.Exists(mesh.FullPath))
            return;

        SelectedMeshPath = mesh.FullPath;
        DialogResult = true;
    }

    private async void ExportCharacterPng_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterMeshList.SelectedItem is not GameAsset mesh)
            return;

        List<CharacterTextureItem> textures = (CharacterTextureList.ItemsSource as IEnumerable<CharacterTextureItem>)?.ToList() ?? [];
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
            CharacterStatusText.Text = LocalizationService.F("{0:N0} texture(s) PNG exportée(s) dans {1}", exported.Count, folder);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Export PNG", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenCharacterTextures_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterMeshList.SelectedItem is not GameAsset mesh)
            return;

        var dialog = new TextureBrowserWindow(_allAssets, mesh.FullPath, _selectedMaterialAnalysis)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void OpenMeshFolder_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterMeshList.SelectedItem is not GameAsset mesh || !File.Exists(mesh.FullPath))
            return;
        OpenInExplorerSafe(mesh.FullPath);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private sealed class CharacterAnalysisResult
    {
        public int SectionCount { get; init; }
        public int DrawBatchCount { get; init; }
        public string BoneCountLabel { get; init; } = "squelette inconnu";
        public MshMaterialAnalysis? MaterialAnalysis { get; init; }
        public List<CharacterTextureItem> Textures { get; init; } = [];
    }

    public sealed class CharacterTextureItem
    {
        public required string FullPath { get; init; }
        public required string DisplayName { get; init; }
        public string SourceLabel { get; init; } = "Dossier";
        public string DetailLine { get; init; } = "";
        public int? PrimarySectionIndex { get; init; }
        public bool IsHeadVariant { get; init; }
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
