using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace GettingUpModTool;

/// <summary>One startup stage: overall progress (0–1), a localized message and an optional detail line.</summary>
public sealed record StartupStep(double Fraction, string Message, string? Detail = null);

public partial class SplashWindow : Window
{
    // Long enough to read the logo on fast machines, short enough to never feel like a delay.
    private static readonly TimeSpan MinimumDisplayTime = TimeSpan.FromMilliseconds(1200);

    private readonly Stopwatch _shownFor = new();
    private double _target;

    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = $"MOD TOOL · v{AppInfo.Version}";
        LogoImage.Source = LoadLargestIconFrame();
        Progress = new Progress<StartupStep>(Report);
        Loaded += (_, _) => _shownFor.Start();
    }

    /// <summary>Progress sink for startup code; created on the UI thread, so reports arrive there.</summary>
    public IProgress<StartupStep> Progress { get; }

    private void Report(StartupStep step)
    {
        StepText.Text = step.Message;
        DetailText.Text = string.IsNullOrEmpty(step.Detail) ? " " : step.Detail;
        AnimateTo(step.Fraction);
    }

    /// <summary>Fills the bar, then keeps the splash up for the minimum display time.</summary>
    public async Task CompleteAsync(string message)
    {
        Report(new StartupStep(1, message));
        TimeSpan remaining = MinimumDisplayTime - _shownFor.Elapsed;
        await Task.Delay(remaining > TimeSpan.FromMilliseconds(350) ? remaining : TimeSpan.FromMilliseconds(350));
    }

    private void AnimateTo(double fraction)
    {
        // The bar never moves backwards, even if a later step reports a smaller estimate.
        _target = Math.Clamp(Math.Max(_target, fraction), 0, 1);
        PercentText.Text = _target.ToString("P0");
        LoadingBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            new DoubleAnimation(_target, TimeSpan.FromMilliseconds(280)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private static BitmapSource? LoadLargestIconFrame()
    {
        try
        {
            var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Resources/AppIcon.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();
        }
        catch
        {
            return null;
        }
    }
}
