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
    private void DiagnoseCurrentMsh_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMshBytes is null)
        {
            LocalizationService.Show("Ouvre d'abord un fichier .msh.", "Diagnostic MSH", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        AnalyzeCurrentMshStructure(selectTab: false);
        AnalyzeCurrentMaterials();
        MainTabs.SelectedItem = MeshDiagnosticTab;
    }

    private void UseAllSections_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMshPath is not null)
            PreviewCurrentMsh();
    }

    private void AnalyzeCurrentMshStructure(bool selectTab)
    {
        if (_currentMshBytes is null)
            return;

        _currentMshAnalysis = MshGenericStructureAnalyzer.Analyze(_currentMshBytes, _currentSkeleton);
        MshSectionsGrid.ItemsSource = _currentMshAnalysis.Sections;
        MshIssuesGrid.ItemsSource = _currentMshAnalysis.Issues;

        string formats = _currentMshAnalysis.Sections.Count == 0
            ? "aucune"
            : string.Join(", ", _currentMshAnalysis.Sections
                .GroupBy(x => x.FormatLabel)
                .Select(g => $"{g.Key} ×{g.Count()}"));

        MshDiagnosticSummaryText.Text =
            $"{Path.GetFileName(_currentMshPath ?? "MSH")} — {_currentMshAnalysis.Sections.Count} section(s) / {_currentMshAnalysis.TotalDrawBatches} lot(s) de rendu — " +
            $"{_currentMshAnalysis.TotalVertices:N0} vertices — {_currentMshAnalysis.TotalTriangles:N0} triangles — formats : {formats}.\n" +
            $"Squelette : {(_currentSkeleton is null ? "non détecté" : _currentSkeleton.Bones.Count + " os")} — " +
            $"diagnostic : {_currentMshAnalysis.ErrorCount} erreur(s), {_currentMshAnalysis.WarningCount} avertissement(s).";

        if (_currentMshAnalysis.Sections.Count > 1)
            UseAllSectionsCheck.IsChecked = true;

        if (selectTab)
            MainTabs.SelectedItem = MeshDiagnosticTab;
    }

    private void DetectCandidates_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMshBytes is null)
        {
            LocalizationService.Show("Ouvre d'abord un fichier .msh.", "Getting Up Mod Tool", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var candidates = MshHeuristicAnalyzer.FindCandidates(_currentMshBytes);
            CandidatesGrid.ItemsSource = candidates;
            StatusText.Text = LocalizationService.F("{0:N0} candidat(s) trouvés. Double-clique pour appliquer.", candidates.Count);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void CandidatesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CandidatesGrid.SelectedItem is not MshLayoutCandidate c) return;
        VertexOffsetBox.Text = $"0x{c.VertexOffset:X}";
        StrideBox.Text = c.VertexStride.ToString();
        VertexCountBox.Text = c.SuggestedVertexCount.ToString();
        PositionOffsetBox.Text = LocalizationService.T("0");
        NormalOffsetBox.Text = LocalizationService.T("");
        UvOffsetBox.Text = LocalizationService.T("");
        BoneWeightsOffsetBox.Text = LocalizationService.T("");
        BoneIndicesOffsetBox.Text = LocalizationService.T("");
        BoneIndexDivisorBox.Text = LocalizationService.T("1");
        IndexOffsetBox.Text = LocalizationService.T("");
        IndexCountBox.Text = LocalizationService.T("0");
        NormalFormatBox.SelectedIndex = 0;
        TryDetectSkeleton();
        PreviewCurrentMsh();
    }

    private void AutoAnalyzeMsh_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMshBytes is null)
        {
            LocalizationService.Show("Ouvre d'abord un fichier .msh.", "Getting Up Mod Tool", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool ok = TryAutoAnalyze(preview: false);
        TryDetectSkeleton();
        if (ok)
            PreviewCurrentMsh();
    }

    private bool TryAutoAnalyze(bool preview)
    {
        if (_currentMshBytes is null)
            return false;

        var result = MshAutoDetector.Detect(_currentMshBytes);
        if (result is null)
        {
            AutoDetectText.Text = LocalizationService.T("Aucune section MSH skinnée ou statique reconnue automatiquement. Consulte Diagnostic MSH puis utilise le mode manuel si nécessaire.");
            StatusText.Text = LocalizationService.T("Analyse automatique : layout non reconnu.");
            return false;
        }

        ApplyLayoutToUi(result.Layout);
        int sectionCount = _currentMshAnalysis?.Sections.Count ?? 0;
        string skinningLine = result.Layout.BoneWeightsOffset is int w && result.Layout.BoneIndicesOffset is int b
            ? $"Skinning : weights +{w}, indices +{b}, diviseur {result.Layout.BoneIndexDivisor}"
            : "Mesh statique : aucun skinning requis";
        AutoDetectText.Text =
            $"Confiance mesh : {result.Confidence:P0}\n" +
            $"Sections structurelles : {sectionCount} — mode assemblage {(UseAllSectionsCheck.IsChecked != false ? "ACTIF" : "désactivé")}\n" +
            $"Section principale : 0x{result.Layout.VertexOffset:X} — {result.Layout.VertexCount} vertices — stride {result.Layout.VertexStride}\n" +
            skinningLine + "\n" +
            $"Index buffer principal : {(result.Layout.IndexOffset is int io ? $"0x{io:X}" : "non détecté")} — {result.Layout.IndexCount} indices\n" +
            result.Notes;
        StatusText.Text = LocalizationService.F("MSH reconnu automatiquement ({0:P0}).", result.Confidence);

        if (preview)
            PreviewCurrentMsh();
        return true;
    }

    private void TryDetectSkeleton()
    {
        if (_currentMshBytes is null)
        {
            _currentSkeleton = null;
            return;
        }

        _currentSkeleton = MshSkeletonDetector.Detect(_currentMshBytes);
        if (_currentSkeleton is null)
        {
            SkeletonSummaryText.Text = LocalizationService.T("Aucun bloc squelette 82 octets/record reconnu dans ce MSH.");
            SkeletonGrid.ItemsSource = null;
            RefreshBnmTables();
            return;
        }

        SkeletonSummaryText.Text =
            $"Squelette détecté : {_currentSkeleton.Bones.Count} os — confiance {_currentSkeleton.Confidence:P0}\n" +
            $"Bone count @ 0x{_currentSkeleton.CountOffset:X}, records @ 0x{_currentSkeleton.RecordsOffset:X}, record size {_currentSkeleton.RecordSize}.\n" +
            "La matrice stockée est interprétée comme inverse-bind ; sa matrice inverse sert à placer les os dans la vue 3D.";

        UpdateSkeletonAndSkinningTables();
        RefreshBnmTables();
    }

    private void ApplyLayoutToUi(MshLayout layout)
    {
        VertexOffsetBox.Text = $"0x{layout.VertexOffset:X}";
        VertexCountBox.Text = layout.VertexCount.ToString();
        StrideBox.Text = layout.VertexStride.ToString();
        PositionOffsetBox.Text = layout.PositionOffset.ToString();
        NormalOffsetBox.Text = layout.NormalOffset?.ToString() ?? "";
        NormalFormatBox.SelectedIndex = layout.NormalEncoding == NormalEncoding.PackedUnorm8 ? 1 : 0;
        UvOffsetBox.Text = layout.UVOffset?.ToString() ?? "";
        BoneWeightsOffsetBox.Text = layout.BoneWeightsOffset?.ToString() ?? "";
        BoneIndicesOffsetBox.Text = layout.BoneIndicesOffset?.ToString() ?? "";
        BoneIndexDivisorBox.Text = layout.BoneIndexDivisor.ToString();
        IndexOffsetBox.Text = layout.IndexOffset is int io ? $"0x{io:X}" : "";
        IndexCountBox.Text = layout.IndexCount.ToString();
        IndexSizeBox.SelectedIndex = layout.IndexSize == IndexElementSize.UInt32 ? 1 : 0;
        PrimitiveBox.SelectedIndex = layout.PrimitiveMode == PrimitiveMode.TriangleStrip ? 1 : 0;
    }

    private MshLayout ReadLayoutFromUi()
    {
        return new MshLayout
        {
            VertexOffset = NumberParser.ParseInt(VertexOffsetBox.Text),
            VertexCount = NumberParser.ParseInt(VertexCountBox.Text, 100),
            VertexStride = NumberParser.ParseInt(StrideBox.Text, 32),
            PositionOffset = NumberParser.ParseInt(PositionOffsetBox.Text),
            NormalOffset = NumberParser.ParseNullableInt(NormalOffsetBox.Text),
            NormalEncoding = NormalFormatBox.SelectedIndex == 1 ? NormalEncoding.PackedUnorm8 : NormalEncoding.Float3,
            UVOffset = NumberParser.ParseNullableInt(UvOffsetBox.Text),
            BoneWeightsOffset = NumberParser.ParseNullableInt(BoneWeightsOffsetBox.Text),
            BoneIndicesOffset = NumberParser.ParseNullableInt(BoneIndicesOffsetBox.Text),
            BoneIndexDivisor = Math.Max(1, NumberParser.ParseInt(BoneIndexDivisorBox.Text, 1)),
            IndexOffset = NumberParser.ParseNullableInt(IndexOffsetBox.Text),
            IndexCount = NumberParser.ParseInt(IndexCountBox.Text),
            IndexSize = IndexSizeBox.SelectedIndex == 1 ? IndexElementSize.UInt32 : IndexElementSize.UInt16,
            PrimitiveMode = PrimitiveBox.SelectedIndex == 1 ? PrimitiveMode.TriangleStrip : PrimitiveMode.TriangleList
        };
    }

    private void UpdateSkeletonAndSkinningTables()
    {
        if (_currentSkeleton is not null)
        {
            int n = _currentSkeleton.Bones.Count;
            var vertexUsage = new int[n];
            var totalWeight = new float[n];

            if (_currentMesh is not null && _currentMesh.HasSkinning)
            {
                foreach (var skin in _currentMesh.Skinning)
                {
                    foreach (var (joint, weight) in skin.Influences())
                    {
                        if (joint >= 0 && joint < n && weight > 0)
                        {
                            vertexUsage[joint]++;
                            totalWeight[joint] += weight;
                        }
                    }
                }
            }

            SkeletonGrid.ItemsSource = _currentSkeleton.Bones.Select(b => new
            {
                b.Index,
                b.Name,
                Parent = b.ParentIndex < 0 ? "<root>" : $"{b.ParentIndex}: {_currentSkeleton.Bones[b.ParentIndex].Name}",
                BindX = b.BindPosition.X.ToString("0.###"),
                BindY = b.BindPosition.Y.ToString("0.###"),
                BindZ = b.BindPosition.Z.ToString("0.###"),
                VertexInfluences = vertexUsage[b.Index],
                TotalWeight = totalWeight[b.Index].ToString("0.###")
            });
        }
        else
        {
            SkeletonGrid.ItemsSource = null;
        }

        if (_currentMesh is not null && _currentMesh.HasSkinning)
        {
            SkinningGrid.ItemsSource = _currentMesh.Skinning.Select((s, i) => new
            {
                Vertex = i,
                Joint0 = BoneLabel(s.Joint0),
                W0 = s.Weight0.ToString("0.000"),
                Joint1 = BoneLabel(s.Joint1),
                W1 = s.Weight1.ToString("0.000"),
                Joint2 = BoneLabel(s.Joint2),
                W2 = s.Weight2.ToString("0.000"),
                Joint3 = BoneLabel(s.Joint3),
                W3 = s.Weight3.ToString("0.000")
            });
        }
        else
        {
            SkinningGrid.ItemsSource = null;
        }
    }

    private string BoneLabel(int index)
    {
        if (_currentSkeleton is not null && index >= 0 && index < _currentSkeleton.Bones.Count)
            return $"{index}: {_currentSkeleton.Bones[index].Name}";
        return index.ToString();
    }
}
