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
    private void LoadGat(string path, bool selectTab)
    {
        try
        {
            _currentGat = GatReader.Read(path);
            GatSummaryText.Text =
                $"GAT : {_currentGat.FileName} — {_currentGat.Attachments.Count} attachment(s).\n" +
                "Le GAT décrit des points d'attache nommés relatifs à un os (translation + quaternion).";
            GatGrid.ItemsSource = _currentGat.Attachments.Select(a => new
            {
                a.Name,
                Os = a.BoneName,
                Tx = a.Translation.X.ToString("0.######"),
                Ty = a.Translation.Y.ToString("0.######"),
                Tz = a.Translation.Z.ToString("0.######"),
                Qx = a.Rotation.X.ToString("0.######"),
                Qy = a.Rotation.Y.ToString("0.######"),
                Qz = a.Rotation.Z.ToString("0.######"),
                Qw = a.Rotation.W.ToString("0.######")
            });
            StatusText.Text = LocalizationService.F("GAT : {0} — {1:N0} attachment(s)", _currentGat.FileName, _currentGat.Attachments.Count);
            if (selectTab) MainTabs.SelectedItem = GatTab;
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "GAT non reconnu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadBan(string path, bool selectTab, bool reportErrors = true)
    {
        try
        {
            _currentBan = BanReader.Read(path);
            BanSummaryText.Text =
                $"BAN : {_currentBan.FileName} — {_currentBan.SectionCount} sections\n" +
                $"Taille déclarée : {_currentBan.DeclaredFileSize:N0} — réelle : {_currentBan.ActualFileSize:N0}\n" +
                $"Marker : 0x{_currentBan.Marker:X4} — table offsets : 0x{_currentBan.OffsetTableOffset:X}.\n" +
                "La structure des sections est inspectée, mais leur rôle sémantique n'est pas encore attribué.";
            BanGrid.ItemsSource = _currentBan.Sections.Select(x => new
            {
                x.Index,
                Offset = $"0x{x.Offset:X}",
                x.Size,
                AperçuHex = x.PreviewHex
            });
            StatusText.Text = LocalizationService.F("BAN : {0} — {1:N0} sections", _currentBan.FileName, _currentBan.SectionCount);
            if (selectTab) MainTabs.SelectedItem = BanTab;
        }
        catch (Exception ex)
        {
            
            
            
            if (reportErrors)
                LocalizationService.Show(ex.Message, "BAN non reconnu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TryAutoLoadCompanions(string mshPath)
    {
        if (AutoCompanionsCheck.IsChecked == false)
            return;

        string? directory = Path.GetDirectoryName(mshPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        string stem = Path.GetFileNameWithoutExtension(mshPath);
        string? st = FindCompanion(directory, stem, ".st") ?? FindCompanionInLibrary(stem, ".st", mshPath);
        string? gat = FindCompanion(directory, stem, ".gat") ?? FindCompanionInLibrary(stem, ".gat", mshPath);
        string? ban = FindCompanion(directory, stem, ".ban") ?? FindCompanionInLibrary(stem, ".ban", mshPath);

        if (st is not null) LoadSt(st, selectTab: false, addRecent: false);
        if (gat is not null) LoadGat(gat, selectTab: false);
        if (ban is not null) LoadBan(ban, selectTab: false, reportErrors: false);
    }

    private static string? FindCompanion(string directory, string stem, string extension)
    {
        return Directory.EnumerateFiles(directory)
            .FirstOrDefault(path =>
                string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileNameWithoutExtension(path), stem, StringComparison.OrdinalIgnoreCase));
    }

    private string? FindCompanionInLibrary(string stem, string extension, string sourcePath)
    {
        string sourceDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        return _allGameAssets
            .Where(a => string.Equals(a.Extension, extension, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(Path.GetFileNameWithoutExtension(a.Name), stem, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => CommonPathPrefixLength(sourceDirectory, Path.GetDirectoryName(a.FullPath) ?? string.Empty))
            .Select(a => a.FullPath)
            .FirstOrDefault();
    }

    private static int CommonPathPrefixLength(string left, string right)
    {
        int length = Math.Min(left.Length, right.Length);
        int i = 0;
        while (i < length && char.ToUpperInvariant(left[i]) == char.ToUpperInvariant(right[i])) i++;
        return i;
    }

    private void OpenMtm_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.T("Ouvrir un fichier MTM"),
            Filter = LocalizationService.T("Getting Up MTM (*.mtm)|*.mtm|Tous les fichiers (*.*)|*.*"),
            InitialDirectory = GetPreferredOpenDirectory() ?? string.Empty
        };
        if (dialog.ShowDialog() != true) return;
        LoadMtm(dialog.FileName);
    }

    private void LoadMtm(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        HexTextBox.Text = HexDumper.Dump(data);
        var analysis = MtmAnalyzer.Analyze(path);
        MtmStringsGrid.ItemsSource = analysis.Strings.Select(x => new
        {
            Offset = $"0x{x.Offset:X8}",
            x.Value
        });
        MtmNumbersGrid.ItemsSource = analysis.NumericValues.Select(x => new
        {
            Offset = $"0x{x.Offset:X8}",
            x.Hex,
            x.UInt32,
            x.Int32,
            Float32 = float.IsFinite(x.Float32) ? x.Float32.ToString("G9") : x.Float32.ToString()
        });
        StatusText.Text = LocalizationService.F("MTM : {0} — {1:N0} octets — {2:N0} strings", analysis.FileName, analysis.Size, analysis.Strings.Count);
        MainTabs.SelectedItem = MtmStringsTab;
    }

    private void CompareMtm_Click(object sender, RoutedEventArgs e)
    {
        var left = new OpenFileDialog
        {
            Title = LocalizationService.T("Choisir le premier MTM"),
            Filter = LocalizationService.T("Getting Up MTM (*.mtm)|*.mtm|Tous les fichiers (*.*)|*.*")
        };
        if (left.ShowDialog() != true) return;

        var right = new OpenFileDialog
        {
            Title = LocalizationService.T("Choisir le second MTM"),
            Filter = LocalizationService.T("Getting Up MTM (*.mtm)|*.mtm|Tous les fichiers (*.*)|*.*")
        };
        if (right.ShowDialog() != true) return;

        var diffs = MtmAnalyzer.Compare(left.FileName, right.FileName);
        MtmDiffGrid.ItemsSource = diffs.Select(x => new
        {
            Offset = $"0x{x.Offset:X8}",
            Left = $"0x{x.Left:X2}",
            Right = $"0x{x.Right:X2}"
        });
        StatusText.Text = LocalizationService.F("Diff MTM : {0} ↔ {1} — {2:N0} différences affichées", Path.GetFileName(left.FileName), Path.GetFileName(right.FileName), diffs.Count);
        MainTabs.SelectedItem = MtmDiffTab;
    }
}
