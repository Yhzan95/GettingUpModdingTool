using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using GettingUpModTool.Core.Formats.MSH;
using GettingUpModTool.Core.Formats.ST;
using GettingUpModTool.Core.Game;
using GettingUpModTool.Core.IO;

namespace GettingUpModTool;

public partial class TextureBrowserWindow : Window
{
    private readonly List<GameAsset> _allTextures;
    private readonly string? _currentMshPath;
    private readonly MshMaterialAnalysis? _materialAnalysis;
    private readonly List<TextureBrowserItem> _items = [];
    private bool _ready;
    private int _previewRequestId;
    private bool _galleryMode;
    private bool _syncingSelection;
    private int _galleryLoadGeneration;

    public string? SelectedTexturePath { get; private set; }

    public TextureBrowserWindow(
        IEnumerable<GameAsset> allAssets,
        string? currentMshPath,
        MshMaterialAnalysis? materialAnalysis)
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => LocalizationService.Apply(this), System.Windows.Threading.DispatcherPriority.Loaded);

        _allTextures = allAssets
            .Where(a => a.Kind == GameAssetKind.Texture || a.Extension.Equals(".st", StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _currentMshPath = currentMshPath;
        _materialAnalysis = materialAnalysis;

        BuildItems();
        ScopeBox.SelectedIndex = _currentMshPath is null ? 2 : 0;
        _ready = true;
        RefreshList(selectFirst: true);
        UpdateContextText();
    }

    private void BuildItems()
    {
        _items.Clear();
        var byPath = new Dictionary<string, TextureBrowserItem>(StringComparer.OrdinalIgnoreCase);
        string? currentDirectory = _currentMshPath is null ? null : Path.GetDirectoryName(_currentMshPath);

        if (_materialAnalysis is not null)
        {
            foreach (MshSectionMaterialBinding binding in _materialAnalysis.Bindings.OrderBy(b => b.SectionIndex))
            {
                if (string.IsNullOrWhiteSpace(binding.ResolvedTexturePath))
                    continue;

                string fullPath = Path.GetFullPath(binding.ResolvedTexturePath);
                string detail = $"Section {binding.SectionIndex} · {binding.MaterialName}";
                if (!string.IsNullOrWhiteSpace(binding.RenderGroupName) &&
                    !binding.RenderGroupName.Equals("Base", StringComparison.OrdinalIgnoreCase))
                    detail += $" · {binding.RenderGroupName}";

                byPath[fullPath] = new TextureBrowserItem
                {
                    FullPath = fullPath,
                    DisplayName = Path.GetFileName(fullPath),
                    RelativePath = FindRelativePath(fullPath),
                    SourceLabel = $"Section {binding.SectionIndex}",
                    DetailLine = detail,
                    IsMappedToCurrentMesh = true,
                    IsInCurrentMeshFolder = currentDirectory is not null && SameDirectory(fullPath, currentDirectory),
                    SortRank = 0
                };
            }
        }

        if (currentDirectory is not null && Directory.Exists(currentDirectory))
        {
            try
            {
                foreach (string localTexturePath in Directory.EnumerateFiles(currentDirectory, "*.st", SearchOption.TopDirectoryOnly))
                {
                    string fullPath = Path.GetFullPath(localTexturePath);
                    if (byPath.ContainsKey(fullPath))
                    {
                        byPath[fullPath].IsInCurrentMeshFolder = true;
                        continue;
                    }

                    byPath[fullPath] = new TextureBrowserItem
                    {
                        FullPath = fullPath,
                        DisplayName = Path.GetFileName(fullPath),
                        RelativePath = FindRelativePath(fullPath),
                        SourceLabel = "Même dossier",
                        DetailLine = "Texture située à côté du mesh",
                        IsMappedToCurrentMesh = false,
                        IsInCurrentMeshFolder = true,
                        SortRank = 1
                    };
                }
            }
            catch (IOException)
            {
                
            }
            catch (UnauthorizedAccessException)
            {
                
            }
        }

        foreach (GameAsset asset in _allTextures)
        {
            string fullPath = Path.GetFullPath(asset.FullPath);
            bool sameFolder = currentDirectory is not null && SameDirectory(fullPath, currentDirectory);

            if (byPath.TryGetValue(fullPath, out TextureBrowserItem? existing))
            {
                existing.RelativePath = asset.RelativePath;
                existing.IsInCurrentMeshFolder |= sameFolder;
                continue;
            }

            byPath[fullPath] = new TextureBrowserItem
            {
                FullPath = fullPath,
                DisplayName = asset.Name,
                RelativePath = asset.RelativePath,
                SourceLabel = sameFolder ? "Même dossier" : "Jeu",
                DetailLine = sameFolder ? "Texture située à côté du mesh" : asset.DirectoryRelativePath,
                IsMappedToCurrentMesh = false,
                IsInCurrentMeshFolder = sameFolder,
                SortRank = sameFolder ? 1 : 2
            };
        }

        
        
        _items.AddRange(byPath.Values
            .OrderBy(x => x.SortRank)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase));
    }

    private string FindRelativePath(string fullPath)
    {
        GameAsset? asset = _allTextures.FirstOrDefault(a =>
            string.Equals(Path.GetFullPath(a.FullPath), fullPath, StringComparison.OrdinalIgnoreCase));
        return asset?.RelativePath ?? fullPath;
    }

    private static bool SameDirectory(string filePath, string directory)
    {
        string? fileDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        return fileDirectory is not null &&
               string.Equals(
                   Path.TrimEndingDirectorySeparator(fileDirectory),
                   Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
                   StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateContextText()
    {
        if (_currentMshPath is null)
        {
            ContextText.Text = LocalizationService.T("Aucun mesh actif : toutes les textures ST indexées sont disponibles. Sélectionne une texture pour la voir immédiatement.");
            return;
        }

        int mapped = _items.Count(x => x.IsMappedToCurrentMesh);
        int folder = _items.Count(x => x.IsInCurrentMeshFolder);
        ContextText.Text = LocalizationService.F("{0} · {1:N0} texture(s) liée(s) au mesh · {2:N0} texture(s) dans son dossier.", Path.GetFileName(_currentMshPath), mapped, folder);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
            return;
        RefreshList(selectFirst: false);
    }

    private void ScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;
        RefreshList(selectFirst: false);
    }

    private void RefreshList(bool selectFirst)
    {
        string search = SearchBox.Text.Trim();
        string scope = (ScopeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        IEnumerable<TextureBrowserItem> query = _items;
        query = scope switch
        {
            "current" => query.Where(x => x.IsMappedToCurrentMesh || x.IsInCurrentMeshFolder),
            "folder" => query.Where(x => x.IsInCurrentMeshFolder),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.DetailLine.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        List<TextureBrowserItem> rows = query
            .OrderBy(x => x.SortRank)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TextureBrowserItem? previous = TextureList.SelectedItem as TextureBrowserItem
            ?? TextureGalleryList.SelectedItem as TextureBrowserItem;

        TextureList.ItemsSource = rows;
        TextureGalleryList.ItemsSource = rows;
        TextureCountText.Text = LocalizationService.F("{0:N0} texture(s)", rows.Count);

        if (previous is not null)
        {
            TextureBrowserItem? replacement = rows.FirstOrDefault(x =>
                string.Equals(x.FullPath, previous.FullPath, StringComparison.OrdinalIgnoreCase));
            if (replacement is not null)
            {
                _syncingSelection = true;
                TextureList.SelectedItem = replacement;
                TextureGalleryList.SelectedItem = replacement;
                _syncingSelection = false;
                TextureList.ScrollIntoView(replacement);
                if (_galleryMode)
                    _ = LoadGalleryThumbnailsAsync(rows);
                return;
            }
        }

        if ((selectFirst || rows.Count == 1) && rows.Count > 0)
        {
            _syncingSelection = true;
            TextureList.SelectedIndex = 0;
            TextureGalleryList.SelectedIndex = 0;
            _syncingSelection = false;
            _ = PreviewTextureAsync(rows[0]);
        }
        else if (rows.Count == 0)
        {
            ClearPreview("Aucune texture ne correspond au filtre.");
        }

        if (_galleryMode)
            _ = LoadGalleryThumbnailsAsync(rows);
    }

    private async void TextureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
            return;

        if (TextureList.SelectedItem is not TextureBrowserItem item)
        {
            ClearPreview("Choisis une texture dans la liste");
            return;
        }

        _syncingSelection = true;
        TextureGalleryList.SelectedItem = item;
        _syncingSelection = false;
        await PreviewTextureAsync(item);
    }

    private async void TextureGalleryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || TextureGalleryList.SelectedItem is not TextureBrowserItem item)
            return;

        _syncingSelection = true;
        TextureList.SelectedItem = item;
        _syncingSelection = false;
        await PreviewTextureAsync(item);
    }

    private async Task PreviewTextureAsync(TextureBrowserItem item)
    {
        int requestId = ++_previewRequestId;
        PreviewTitleText.Text = item.DisplayName;
        PreviewInfoText.Text = LocalizationService.T("Chargement…");
        PreviewPathText.Text = item.RelativePath;
        PreviewMessageText.Text = LocalizationService.T("Chargement de la texture…");
        PreviewMessageText.Visibility = Visibility.Visible;
        TexturePreviewImage.Source = null;
        TexturePreviewActualImage.Source = null;
        UseTextureButton.IsEnabled = false;
        ExportPngButton.IsEnabled = File.Exists(item.FullPath);
        OpenFolderButton.IsEnabled = File.Exists(item.FullPath);

        try
        {
            StTexture texture = await Task.Run(() => StReader.Read(item.FullPath));
            BitmapSource bitmap = CreateBitmapSource(texture);
            if (requestId != _previewRequestId)
                return;

            TexturePreviewImage.Source = bitmap;
            TexturePreviewActualImage.Source = bitmap;
            ShowFitPreview();
            PreviewMessageText.Visibility = Visibility.Collapsed;
            PreviewInfoText.Text = LocalizationService.F("{0} × {1} · {2} · nom interne : {3}", texture.Width, texture.Height, texture.Codec, texture.Name);
            PreviewPathText.Text = item.RelativePath;
            UseTextureButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            if (requestId != _previewRequestId)
                return;

            TexturePreviewImage.Source = null;
            TexturePreviewActualImage.Source = null;
            PreviewMessageText.Text = LocalizationService.T("Aperçu impossible");
            PreviewMessageText.Visibility = Visibility.Visible;
            PreviewInfoText.Text = ex.Message;
            UseTextureButton.IsEnabled = File.Exists(item.FullPath);
        }
    }

    private void ListView_Click(object sender, RoutedEventArgs e)
    {
        _galleryMode = false;
        TextureGalleryList.Visibility = Visibility.Collapsed;
        TextureList.Visibility = Visibility.Visible;
        GalleryStatusText.Text = LocalizationService.T("Liste");
    }

    private async void GalleryView_Click(object sender, RoutedEventArgs e)
    {
        _galleryMode = true;
        TextureList.Visibility = Visibility.Collapsed;
        TextureGalleryList.Visibility = Visibility.Visible;

        if (TextureGalleryList.ItemsSource is IEnumerable<TextureBrowserItem> rows)
            await LoadGalleryThumbnailsAsync(rows.ToList());
    }

    private async Task LoadGalleryThumbnailsAsync(IReadOnlyList<TextureBrowserItem> rows)
    {
        int generation = ++_galleryLoadGeneration;
        const int limit = 60;
        List<TextureBrowserItem> targets = rows.Take(limit).ToList();
        int ready = targets.Count(x => x.Thumbnail is not null || x.ThumbnailTried);
        GalleryStatusText.Text = rows.Count > limit
            ? $"Miniatures {ready}/{targets.Count} · recherche pour affiner"
            : $"Miniatures {ready}/{targets.Count}";

        foreach (TextureBrowserItem item in targets)
        {
            if (generation != _galleryLoadGeneration || !_galleryMode)
                return;
            if (item.Thumbnail is not null || item.ThumbnailTried)
                continue;

            item.ThumbnailTried = true;
            try
            {
                BitmapSource thumb = await Task.Run(() =>
                {
                    StTexture texture = StReader.Read(item.FullPath);
                    return CreateThumbnailSource(texture, 128);
                });

                if (generation != _galleryLoadGeneration)
                    return;
                item.Thumbnail = thumb;
            }
            catch (Exception ex)
            {
                item.ThumbnailError = "⚠ " + ex.Message;
            }

            ready++;
            GalleryStatusText.Text = rows.Count > limit
                ? $"Miniatures {ready}/{targets.Count} · recherche pour affiner"
                : $"Miniatures {ready}/{targets.Count}";
        }
    }

    private static BitmapSource CreateThumbnailSource(StTexture texture, int maxSize)
    {
        int width = texture.Width;
        int height = texture.Height;
        double scale = Math.Min(1.0, Math.Min((double)maxSize / width, (double)maxSize / height));
        int tw = Math.Max(1, (int)Math.Round(width * scale));
        int th = Math.Max(1, (int)Math.Round(height * scale));
        byte[] pixels = new byte[tw * th * 4];

        for (int y = 0; y < th; y++)
        {
            int sy = Math.Min(height - 1, (int)((long)y * height / th));
            for (int x = 0; x < tw; x++)
            {
                int sx = Math.Min(width - 1, (int)((long)x * width / tw));
                int src = (sy * width + sx) * 4;
                int dst = (y * tw + x) * 4;
                pixels[dst] = texture.Bgra32[src];
                pixels[dst + 1] = texture.Bgra32[src + 1];
                pixels[dst + 2] = texture.Bgra32[src + 2];
                pixels[dst + 3] = texture.Bgra32[src + 3];
            }
        }

        BitmapSource bitmap = BitmapSource.Create(
            tw, th, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, tw * 4);
        bitmap.Freeze();
        return bitmap;
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

    private void FitPreview_Click(object sender, RoutedEventArgs e)
    {
        ShowFitPreview();
    }

    private void ActualSizePreview_Click(object sender, RoutedEventArgs e)
    {
        if (TexturePreviewActualImage.Source is null)
            return;

        FitPreviewHost.Visibility = Visibility.Collapsed;
        ActualPreviewHost.Visibility = Visibility.Visible;
        PreviewModeText.Text = LocalizationService.T("100% · utilise les barres de défilement");
    }

    private void ShowFitPreview()
    {
        ActualPreviewHost.Visibility = Visibility.Collapsed;
        FitPreviewHost.Visibility = Visibility.Visible;
        PreviewModeText.Text = LocalizationService.T("Image entière · aucun recadrage");
    }

    private void ClearPreview(string message)
    {
        ++_previewRequestId;
        TexturePreviewImage.Source = null;
        TexturePreviewActualImage.Source = null;
        ShowFitPreview();
        PreviewMessageText.Text = message;
        PreviewMessageText.Visibility = Visibility.Visible;
        PreviewTitleText.Text = LocalizationService.T("Aucune texture sélectionnée");
        PreviewInfoText.Text = LocalizationService.T("");
        PreviewPathText.Text = LocalizationService.T("");
        UseTextureButton.IsEnabled = false;
        ExportPngButton.IsEnabled = false;
        OpenFolderButton.IsEnabled = false;
    }

    private TextureBrowserItem? GetSelectedItem()
    {
        return TextureList.SelectedItem as TextureBrowserItem
            ?? TextureGalleryList.SelectedItem as TextureBrowserItem;
    }

    private async void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedItem() is not TextureBrowserItem item || !File.Exists(item.FullPath))
            return;

        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("Exporter la texture en PNG"),
            Filter = "PNG (*.png)|*.png",
            FileName = Path.GetFileNameWithoutExtension(item.FullPath) + ".png"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await Task.Run(() => StPngExporter.Export(item.FullPath, dialog.FileName));
            PreviewInfoText.Text = LocalizationService.F("PNG exporté : {0}", dialog.FileName);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Export PNG", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedItem() is not TextureBrowserItem item || !File.Exists(item.FullPath))
            return;
        OpenInExplorerSafe(item.FullPath);
    }

    private void UseTexture_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedItem() is not TextureBrowserItem item)
            return;

        SelectedTexturePath = item.FullPath;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public sealed class TextureBrowserItem : INotifyPropertyChanged
    {
        private BitmapSource? _thumbnail;
        private string _thumbnailError = "";

        public required string FullPath { get; init; }
        public required string DisplayName { get; init; }
        public string RelativePath { get; set; } = "";
        public string SourceLabel { get; init; } = "Jeu";
        public string DetailLine { get; init; } = "";
        public bool IsMappedToCurrentMesh { get; init; }
        public bool IsInCurrentMeshFolder { get; set; }
        public int SortRank { get; init; }
        public bool ThumbnailTried { get; set; }

        public string ThumbnailError
        {
            get => _thumbnailError;
            set
            {
                if (_thumbnailError == value) return;
                _thumbnailError = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailError)));
            }
        }

        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (ReferenceEquals(_thumbnail, value))
                    return;
                _thumbnail = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
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
