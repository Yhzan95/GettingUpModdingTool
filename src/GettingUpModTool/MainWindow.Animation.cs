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
    private void OpenBnm_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.T("Ouvrir une animation BNM"),
            Filter = LocalizationService.T("Getting Up animation (*.bnm)|*.bnm|Tous les fichiers (*.*)|*.*"),
            InitialDirectory = GetPreferredOpenDirectory(Path.Combine("engine", "Animation")) ?? string.Empty
        };
        if (dialog.ShowDialog() != true) return;
        LoadBnm(dialog.FileName);
    }

    private void LoadBnm(string path)
    {
        try
        {
            AddRecentAsset(path);
            _currentBnmPath = path;
            byte[] data = File.ReadAllBytes(path);
            _currentBnm = BnmReader.Read(data, Path.GetFileName(path));
            _currentAnimationBinding = null;
            HexTextBox.Text = HexDumper.Dump(data);
            StopPlayback(resetFrame: false);
            _currentAnimationFrame = 0;
            ResolveCurrentAnimationBinding();
            ConfigureAnimationUi();
            RefreshBnmTables();
            ApplyAnimationFrame(0, fitCamera: false);
            StatusText.Text = LocalizationService.F("BNM décodé : {0} — {1:N0} frames @ {2} FPS — {3:N0} tracks", _currentBnm.Name, _currentBnm.FrameCount, _currentBnm.FramesPerSecond, _currentBnm.TrackCount);
            MainTabs.SelectedItem = BnmSummaryTab;
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "BNM non reconnu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CompareBnm_Click(object sender, RoutedEventArgs e)
    {
        var left = new OpenFileDialog
        {
            Title = LocalizationService.T("Choisir le premier BNM"),
            Filter = LocalizationService.T("Getting Up animation (*.bnm)|*.bnm|Tous les fichiers (*.*)|*.*")
        };
        if (left.ShowDialog() != true) return;

        var right = new OpenFileDialog
        {
            Title = LocalizationService.T("Choisir le second BNM"),
            Filter = LocalizationService.T("Getting Up animation (*.bnm)|*.bnm|Tous les fichiers (*.*)|*.*")
        };
        if (right.ShowDialog() != true) return;

        try
        {
            var diffs = BnmReader.Compare(left.FileName, right.FileName);
            BnmDiffGrid.ItemsSource = diffs.Select(x => new
            {
                x.Field,
                x.Left,
                x.Right,
                Identique = x.Same ? "oui" : "non"
            });
            StatusText.Text = LocalizationService.F("Diff BNM : {0} ↔ {1}", Path.GetFileName(left.FileName), Path.GetFileName(right.FileName));
            MainTabs.SelectedItem = BnmDiffTab;
        }
        catch (Exception ex)
        {
            LocalizationService.Show(ex.Message, "Comparaison BNM", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshBnmTables()
    {
        if (_currentBnm is null)
        {
            BnmSummaryText.Text = LocalizationService.T("Aucune animation BNM chargée.");
            BnmTracksGrid.ItemsSource = null;
            BnmKeysGrid.ItemsSource = null;
            return;
        }

        BnmAnimation bnm = _currentBnm;
        bool skeletonMatches = CanAnimateCurrentPair();
        string mapping;
        if (skeletonMatches && _currentAnimationBinding is not null)
        {
            mapping = _currentAnimationBinding.IsPartial
                ? $"Mapping partiel confirmé par TrackId : {_currentAnimationBinding.MappedBoneCount}/{_currentAnimationBinding.BoneCount} os — {_currentAnimationBinding.Reason}"
                : $"Mapping confirmé : {_currentAnimationBinding.Mode} — {_currentAnimationBinding.Reason}";
        }
        else if (_currentSkeleton is not null && _currentBnm is not null)
        {
            mapping = _currentAnimationBinding?.Reason ?? "Le BNM n'a pas pu être relié de façon fiable au squelette chargé.";
        }
        else
        {
            mapping = "Charge le MSH correspondant pour analyser le mapping des tracks BNM vers les os.";
        }

        int decodedRot = bnm.Tracks.Count(t => t.RotationDecoded || t.RotationKeyCount == 0);
        int decodedTrans = bnm.Tracks.Count(t => t.TranslationDecoded || t.TranslationKeyCount == 0);

        BnmSummaryText.Text =
            $"Animation : {bnm.Name}\n" +
            $"Bloc animation : 0x{bnm.DataBaseOffset:X} — table tracks : 0x{bnm.OffsetTableOffset:X}\n" +
            $"Frames : {bnm.FrameCount} @ {bnm.FramesPerSecond} FPS — durée ≈ {bnm.DurationSeconds:0.###} s\n" +
            $"Tracks : {bnm.TrackCount} — rotation décodable : {decodedRot}/{bnm.TrackCount} — translation décodable : {decodedTrans}/{bnm.TrackCount}\n" +
            $"Bornes translation EntityRoot/root : {bnm.TranslationBoundsRoot}\n" +
            $"Bornes translation os : {bnm.TranslationBoundsBones}\n" +
            mapping + "\n" +
            "v0.5.0 : table de tracks BNM dynamique (formats +0x7C et +0x98), offsets de streams, codecs, quantification XYZ, remap quaternion et translations sont décodés à partir des routines du GettingUp.exe PC fourni. " +
            "Le runtime compose local = Anim(R,T) × Translate(restOffset), puis world = local × parentWorld.";

        BnmTracksGrid.ItemsSource = bnm.Tracks.Select(t => new
        {
            t.Index,
            Cible = BnmTrackLabel(t.Index),
            TrackId = $"0x{t.TrackId:X4}",
            Header = $"0x{t.AbsoluteOffset:X}",
            RuntimeBase = $"0x{t.RuntimeBaseOffset:X}",
            RotKeys = t.RotationKeyCount,
            RotCodec = t.RotationCodec,
            RotEntry = t.StreamAEntryBytes?.ToString() ?? "?",
            RotStream = $"0x{t.RotationStreamAbsoluteOffset:X}",
            RotDecoded = t.RotationDecoded || t.RotationKeyCount == 0 ? "oui" : "non",
            TransKeys = t.TranslationKeyCount,
            TransCodec = t.TranslationCodec,
            TransEntry = t.StreamBBytesPerEntryApprox?.ToString() ?? "?",
            TransStream = $"0x{t.TranslationStreamAbsoluteOffset:X}",
            TransDecoded = t.TranslationDecoded || t.TranslationKeyCount == 0 ? "oui" : "non",
            RotRange = t.CodecDescriptorHex
        });

        var keyRows = new List<object>();
        foreach (BnmTrack t in bnm.Tracks)
        {
            foreach (BnmRotationKey k in t.RotationKeys)
            {
                keyRows.Add(new
                {
                    Track = t.Index,
                    Cible = BnmTrackLabel(t.Index),
                    Type = "Rotation",
                    Codec = t.RotationCodec,
                    k.Frame,
                    Temps = bnm.FramesPerSecond > 0 ? (k.Frame / (double)bnm.FramesPerSecond).ToString("0.###") : "0",
                    X = k.Rotation.X.ToString("0.######"),
                    Y = k.Rotation.Y.ToString("0.######"),
                    Z = k.Rotation.Z.ToString("0.######"),
                    W = k.Rotation.W.ToString("0.######"),
                    Offset = $"0x{k.AbsoluteOffset:X}",
                    Raw = k.RawHex
                });
            }

            foreach (BnmTranslationKey k in t.TranslationKeys)
            {
                keyRows.Add(new
                {
                    Track = t.Index,
                    Cible = BnmTrackLabel(t.Index),
                    Type = "Translation",
                    Codec = t.TranslationCodec,
                    k.Frame,
                    Temps = bnm.FramesPerSecond > 0 ? (k.Frame / (double)bnm.FramesPerSecond).ToString("0.###") : "0",
                    X = k.Translation.X.ToString("0.######"),
                    Y = k.Translation.Y.ToString("0.######"),
                    Z = k.Translation.Z.ToString("0.######"),
                    W = "",
                    Offset = $"0x{k.AbsoluteOffset:X}",
                    Raw = k.RawHex
                });
            }
        }
        BnmKeysGrid.ItemsSource = keyRows;
    }

    private string BnmTrackLabel(int index)
    {
        if (_currentAnimationBinding is not null)
        {
            if (index == _currentAnimationBinding.EntityRootTrackIndex)
                return "EntityRoot";
            int mappedBone = _currentAnimationBinding.BoneIndexForTrack(index);
            if (_currentSkeleton is not null && mappedBone >= 0 && mappedBone < _currentSkeleton.Bones.Count)
                return $"{mappedBone}: {_currentSkeleton.Bones[mappedBone].Name}";
            return "<track non utilisé>";
        }

        if (index == 0)
            return "EntityRoot (?)";
        if (_currentSkeleton is not null && index - 1 >= 0 && index - 1 < _currentSkeleton.Bones.Count)
            return $"{index - 1}: {_currentSkeleton.Bones[index - 1].Name}";
        return "<inconnu>";
    }

    private void ResolveCurrentAnimationBinding()
    {
        _currentAnimationBinding = null;
        if (_currentBnm is null || _currentSkeleton is null)
            return;

        try
        {
            _currentAnimationBinding = AnimationSkeletonBindingResolver.Resolve(
                _currentBnm,
                _currentSkeleton,
                _currentBnmPath,
                _allGameAssets);
        }
        catch
        {
            _currentAnimationBinding = null;
        }
    }

    private bool CanAnimateCurrentPair()
        => _currentMesh is not null &&
           _currentSkeleton is not null &&
           _currentBnm is not null &&
           _currentMesh.HasSkinning &&
           _currentAnimationBinding?.IsUsable == true;

    private void ConfigureAnimationUi()
    {
        _updatingAnimationUi = true;
        try
        {
            if (_currentBnm is null)
            {
                AnimationSlider.Minimum = 0;
                AnimationSlider.Maximum = 1;
                AnimationSlider.Value = 0;
                AnimationTimeText.Text = LocalizationService.T("Aucune animation");
                return;
            }

            double max = Math.Max(0, _currentBnm.FrameCount - 1);
            AnimationSlider.Minimum = 0;
            AnimationSlider.Maximum = Math.Max(1, max);
            AnimationSlider.Value = Math.Clamp(_currentAnimationFrame, 0, Math.Max(1, max));
            UpdateAnimationTimeText();
        }
        finally
        {
            _updatingAnimationUi = false;
        }
    }

    private void PlayAnimation_Click(object sender, RoutedEventArgs e)
    {
        if (!CanAnimateCurrentPair())
        {
            string detail = _currentAnimationBinding?.Reason ?? "Aucun mapping fiable entre les tracks BNM et le squelette.";
            LocalizationService.Show($"Cette animation ne peut pas encore être appliquée de façon fiable à ce mesh.\n\n{detail}", "Animation BNM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isPlaying = true;
        _lastAnimationTick = DateTime.UtcNow;
        _animationTimer.Start();
        StatusText.Text = LocalizationService.F("Lecture : {0}", _currentBnm!.Name);
    }

    private void PauseAnimation_Click(object sender, RoutedEventArgs e)
        => StopPlayback(resetFrame: false);

    private void StopAnimation_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback(resetFrame: false);
        _currentAnimationFrame = 0;
        ApplyAnimationFrame(0, fitCamera: false);
    }

    private void StopPlayback(bool resetFrame)
    {
        _isPlaying = false;
        _animationTimer.Stop();
        if (resetFrame)
            _currentAnimationFrame = 0;
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isPlaying || _currentBnm is null || !CanAnimateCurrentPair())
        {
            StopPlayback(resetFrame: false);
            return;
        }

        DateTime now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _lastAnimationTick).TotalSeconds, 0, 0.15);
        _lastAnimationTick = now;
        double next = _currentAnimationFrame + dt * _currentBnm.FramesPerSecond * AnimationSpeed();
        double maxFrame = Math.Max(0, _currentBnm.FrameCount - 1);

        if (next > maxFrame)
        {
            if (AnimationLoopCheck.IsChecked != false && maxFrame > 0)
                next %= maxFrame;
            else
            {
                next = maxFrame;
                StopPlayback(resetFrame: false);
            }
        }

        ApplyAnimationFrame(next, fitCamera: false);
    }

    private double AnimationSpeed()
        => AnimationSpeedBox.SelectedIndex switch
        {
            0 => 0.25,
            1 => 0.5,
            3 => 2.0,
            _ => 1.0
        };

    private void AnimationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingAnimationUi || _currentBnm is null)
            return;
        ApplyAnimationFrame(e.NewValue, fitCamera: false);
    }

    private void AnimationOption_Click(object sender, RoutedEventArgs e)
    {
        if (CanAnimateCurrentPair())
            ApplyAnimationFrame(_currentAnimationFrame, fitCamera: false);
        else
            RefreshScene(fitCamera: false);
    }

    private void ApplyAnimationFrame(double frame, bool fitCamera)
    {
        if (!CanAnimateCurrentPair())
        {
            ClearAnimationPose();
            UpdateAnimationTimeText();
            return;
        }

        double max = Math.Max(0, _currentBnm!.FrameCount - 1);
        _currentAnimationFrame = Math.Clamp(frame, 0, max);

        AnimationPose pose = AnimationPoseEvaluator.Evaluate(
            _currentMesh!,
            _currentSkeleton!,
            _currentBnm,
            (float)_currentAnimationFrame,
            skinMesh: ApplySkinningCheck.IsChecked != false,
            applyEntityRoot: RootMotionCheck.IsChecked == true,
            binding: _currentAnimationBinding);

        _animatedMesh = pose.SkinnedMesh;
        _animatedBoneWorlds = pose.BoneWorldMatrices;
        RefreshScene(fitCamera);
        UpdateAnimationUiFromFrame();
        RefreshAnimationDebug();
    }

    private void UpdateAnimationUiFromFrame()
    {
        _updatingAnimationUi = true;
        try
        {
            if (_currentBnm is not null)
                AnimationSlider.Value = Math.Clamp(_currentAnimationFrame, AnimationSlider.Minimum, AnimationSlider.Maximum);
            UpdateAnimationTimeText();
        }
        finally
        {
            _updatingAnimationUi = false;
        }
    }

    private void UpdateAnimationTimeText()
    {
        if (_currentBnm is null)
        {
            AnimationTimeText.Text = LocalizationService.T("Aucune animation");
            return;
        }

        double seconds = _currentBnm.FramesPerSecond > 0
            ? _currentAnimationFrame / _currentBnm.FramesPerSecond
            : 0;
        AnimationTimeText.Text = LocalizationService.F("{0} — frame {1:0.0}/{2:0} — {3:0.00}s", _currentBnm.Name, _currentAnimationFrame, Math.Max(0, _currentBnm.FrameCount - 1), seconds);
    }

    private void ClearAnimationPose()
    {
        _animatedMesh = null;
        _animatedBoneWorlds = null;
    }

    private void RefreshAnimationDebug()
    {
        if (_currentBnm is null || _currentSkeleton is null || _animatedBoneWorlds is null)
        {
            AnimationDebugGrid.ItemsSource = null;
            return;
        }

        var rows = new List<object>();
        for (int i = 0; i < _currentSkeleton.Bones.Count; i++)
        {
            BoneData bone = _currentSkeleton.Bones[i];
            int mappedTrackIndex = _currentAnimationBinding?.TrackIndexForBone(i) ?? (i + 1);
            BnmTrack? track = mappedTrackIndex >= 0 && mappedTrackIndex < _currentBnm.Tracks.Count ? _currentBnm.Tracks[mappedTrackIndex] : null;
            Quaternion? q = track is null ? null : BnmSampler.SampleRotation(track, (float)_currentAnimationFrame);
            Vector3? t = track is null ? null : BnmSampler.SampleTranslation(track, (float)_currentAnimationFrame);
            Matrix4x4 w = _animatedBoneWorlds[i];

            rows.Add(new
            {
                bone.Index,
                bone.Name,
                Parent = bone.ParentIndex < 0 ? "<root>" : _currentSkeleton.Bones[bone.ParentIndex].Name,
                Track = mappedTrackIndex,
                RotCodec = track?.RotationCodec ?? -1,
                TxDelta = t?.X.ToString("0.###") ?? "-",
                TyDelta = t?.Y.ToString("0.###") ?? "-",
                TzDelta = t?.Z.ToString("0.###") ?? "-",
                Qx = q?.X.ToString("0.###") ?? "-",
                Qy = q?.Y.ToString("0.###") ?? "-",
                Qz = q?.Z.ToString("0.###") ?? "-",
                Qw = q?.W.ToString("0.###") ?? "-",
                WorldX = w.M41.ToString("0.###"),
                WorldY = w.M42.ToString("0.###"),
                WorldZ = w.M43.ToString("0.###")
            });
        }

        AnimationDebugGrid.ItemsSource = rows;
        AnimationDebugSummaryText.Text =
            $"{_currentBnm.Name} — frame {_currentAnimationFrame:0.00} — " +
            $"skinning={(ApplySkinningCheck.IsChecked != false ? "ON" : "OFF")} — EntityRoot={(RootMotionCheck.IsChecked == true ? "ON" : "OFF")}. " +
            "Rotations = locales absolues; translations enfants = delta + restOffset, conformément à la composition observée dans GettingUp.exe.";
    }
}
