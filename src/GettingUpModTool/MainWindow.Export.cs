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
    private async void ExportSelectedFormat_Click(object sender, RoutedEventArgs e)
    {
        if (_exportInProgress)
            return;

        _exportInProgress = true;
        Button? exportButton = sender as Button;
        if (exportButton is not null)
            exportButton.IsEnabled = false;
        string previousStatus = StatusText.Text;
        StatusText.Text = LocalizationService.T("Export en cours…");
        try
        {
            string format = (ViewportExportFormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "GLB";
            switch (format.ToUpperInvariant())
            {
                case "PNG": CaptureViewportPng_Click(sender, e); break;
                case "OBJ": await ExportObjAsync(); break;
                case "GLTF": await ExportGltfAsync(); break;
                default: await ExportGlbAsync(); break;
            }
        }
        finally
        {
            _exportInProgress = false;
            if (exportButton is not null)
                exportButton.IsEnabled = true;
            if (StatusText.Text == LocalizationService.T("Export en cours…"))
                StatusText.Text = previousStatus;
        }
    }

    private void CaptureViewportPng_Click(object sender, RoutedEventArgs e)
    {
        ModelViewport.UpdateLayout();
        int width = Math.Max(1, (int)Math.Round(ModelViewport.ActualWidth));
        int height = Math.Max(1, (int)Math.Round(ModelViewport.ActualHeight));
        if (width <= 1 || height <= 1)
        {
            LocalizationService.Show(LocalizationService.T("Le viewport n'est pas encore prêt pour une capture."), "Capture PNG", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("Capturer le viewport en PNG"),
            Filter = LocalizationService.T("Image PNG (*.png)|*.png"),
            FileName = _currentMshPath is null ? "viewport.png" : Path.GetFileNameWithoutExtension(_currentMshPath) + "_preview.png"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(ModelViewport);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);
            StatusText.Text = LocalizationService.F("Capture PNG : {0}", dialog.FileName);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Capture PNG", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExportObj_Click(object sender, RoutedEventArgs e) => await ExportObjAsync();

    private async Task ExportObjAsync()
    {
        if (_currentMesh is null)
        {
            LocalizationService.Show("Crée d'abord un aperçu MSH.", "Getting Up Mod Tool", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("Exporter en OBJ"),
            Filter = LocalizationService.T("Wavefront OBJ (*.obj)|*.obj"),
            FileName = _currentMshPath is null ? "mesh.obj" : Path.GetFileNameWithoutExtension(_currentMshPath) + ".obj"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            MeshData exportMesh = CloneMeshForExport(_currentMesh);
            Dictionary<int, byte[]> textures = BuildSectionTexturePngs(out List<string> textureWarnings);
            await Task.Run(() => ObjExporter.Export(exportMesh, dialog.FileName, textures));
            string objDetails = LocalizationService.T("état normal");
            StatusText.Text = textureWarnings.Count == 0
                ? $"OBJ + MTL + textures exportés ({objDetails}) : {dialog.FileName}"
                : $"OBJ exporté ({objDetails}) avec {textureWarnings.Count} avertissement(s) texture : {dialog.FileName}";
            ShowTextureExportWarnings("OBJ", textureWarnings);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Export OBJ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


    private static MeshData CloneMeshForExport(MeshData source)
    {
        var clone = new MeshData();
        clone.Positions.AddRange(source.Positions);
        clone.Normals.AddRange(source.Normals);
        clone.UVs.AddRange(source.UVs);
        clone.Indices.AddRange(source.Indices);
        clone.Skinning.AddRange(source.Skinning);
        foreach (MeshPart part in source.Parts)
        {
            clone.Parts.Add(new MeshPart
            {
                SectionIndex = part.SectionIndex,
                IndexStart = part.IndexStart,
                IndexCount = part.IndexCount,
                RenderGroupName = part.RenderGroupName,
                MaterialName = part.MaterialName,
                TextureReference = part.TextureReference,
                IsVisible = part.IsVisible
            });
        }
        return clone;
    }

    private static byte[] EncodePng(ImageSource source)
    {
        if (source is not BitmapSource bitmap)
            throw new InvalidOperationException("Texture non convertible en PNG.");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private Dictionary<int, byte[]> BuildSectionTexturePngs(out List<string> warnings)
    {
        var result = new Dictionary<int, byte[]>();
        warnings = [];
        foreach (var pair in _sectionTextures.OrderBy(x => x.Key))
        {
            try
            {
                result[pair.Key] = EncodePng(pair.Value);
            }
            catch (Exception ex)
            {
                string textureName = GetSectionTextureDisplayName(pair.Key);
                warnings.Add($"{textureName} (section {pair.Key}) : {ex.Message}");
            }
        }
        return result;
    }

    private string GetSectionTextureDisplayName(int sectionIndex)
    {
        MshSectionMaterialBinding? binding = _currentMaterialAnalysis?.Bindings
            .Where(b => b.SectionIndex == sectionIndex)
            .OrderByDescending(b => b.IsVisible)
            .ThenByDescending(b => !string.IsNullOrWhiteSpace(b.ResolvedTexturePath))
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(binding?.ResolvedTexturePath))
            return Path.GetFileName(binding.ResolvedTexturePath);
        if (!string.IsNullOrWhiteSpace(binding?.TextureReference))
            return binding.TextureReference;
        return $"Section {sectionIndex}";
    }

    private void ShowTextureExportWarnings(string format, IReadOnlyList<string> warnings)
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

    private async void ExportGlb_Click(object sender, RoutedEventArgs e) => await ExportGlbAsync();

    private async Task ExportGlbAsync()
    {
        if (_currentMesh is null)
        {
            LocalizationService.Show("Crée d'abord un aperçu MSH.", "Getting Up Mod Tool", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("Exporter en GLB pour Blender"),
            Filter = LocalizationService.T("glTF Binary (*.glb)|*.glb"),
            FileName = _currentMshPath is null ? "mesh.glb" : Path.GetFileNameWithoutExtension(_currentMshPath) + ".glb"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            Dictionary<int, byte[]> textures = BuildSectionTexturePngs(out List<string> textureWarnings);
            MeshData mesh = CloneMeshForExport(_currentMesh);
            SkeletonData? skeleton = _currentSkeleton;
            await Task.Run(() => GltfExporter.ExportGlb(mesh, skeleton, dialog.FileName, textures));
            StatusText.Text = textureWarnings.Count == 0
                ? $"GLB exporté ({LocalizationService.T("état normal")}) : {dialog.FileName}"
                : $"GLB exporté ({LocalizationService.T("état normal")}) avec {textureWarnings.Count} avertissement(s) texture : {dialog.FileName}";
            ShowTextureExportWarnings("GLB", textureWarnings);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Export GLB", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExportGltf_Click(object sender, RoutedEventArgs e) => await ExportGltfAsync();

    private async Task ExportGltfAsync()
    {
        if (_currentMesh is null)
        {
            LocalizationService.Show("Crée d'abord un aperçu MSH.", "Getting Up Mod Tool", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("Exporter en glTF 2.0"),
            Filter = LocalizationService.T("glTF 2.0 (*.gltf)|*.gltf"),
            FileName = _currentMshPath is null ? "mesh.gltf" : Path.GetFileNameWithoutExtension(_currentMshPath) + ".gltf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            Dictionary<int, byte[]> textures = BuildSectionTexturePngs(out List<string> textureWarnings);
            MeshData mesh = CloneMeshForExport(_currentMesh);
            SkeletonData? skeleton = _currentSkeleton;
            await Task.Run(() => GltfExporter.Export(mesh, skeleton, dialog.FileName, textures));
            string details = _currentSkeleton is not null && _currentMesh.HasSkinning
                ? $"mesh + skin + squelette + {textures.Count} texture(s)"
                : $"mesh + {textures.Count} texture(s)";
            if (textureWarnings.Count > 0)
                details += $" · {textureWarnings.Count} avertissement(s) texture";
            StatusText.Text = $"glTF exporté ({details}, {LocalizationService.T("état normal")}) : {dialog.FileName}";
            ShowTextureExportWarnings("glTF", textureWarnings);
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Export glTF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
