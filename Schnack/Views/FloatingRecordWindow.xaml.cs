using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Schnack.Interop;

namespace Schnack.Views;

public partial class FloatingRecordWindow : Window
{
    private bool _dragging;
    private Point _pressScreen;
    private bool _suppressToggle;
    private Storyboard? _recordingStoryboard;
    private Storyboard? _processingStoryboard;

    public event EventHandler? ToggleRecording;
    public event EventHandler? DragCompleted;
    public event EventHandler? HideRequested;

    public FloatingRecordWindow()
    {
        InitializeComponent();
        InitAnimations();
    }

    private void InitAnimations()
    {
        // Recording: Skalierungs-Puls (1.0 → 1.08 → 1.0, 0.7 s Halbwelle)
        _recordingStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var anim = new DoubleAnimation(1.0, 1.08, new Duration(TimeSpan.FromSeconds(0.7)))
            {
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTargetName(anim, "RootScale");
            Storyboard.SetTargetProperty(anim, new PropertyPath(prop));
            _recordingStoryboard.Children.Add(anim);
        }

        // Processing: Opacity-Puls (1.0 → 0.4 → 1.0, 0.9 s Halbwelle)
        _processingStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var opacityAnim = new DoubleAnimation(1.0, 0.4, new Duration(TimeSpan.FromSeconds(0.9)))
        {
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTargetName(opacityAnim, "RootBorder");
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(UIElement.OpacityProperty));
        _processingStoryboard.Children.Add(opacityAnim);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
            return;

        nint ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        uint style = unchecked((uint)(long)ex);
        style |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, unchecked((nint)(long)style));
    }

    public void SetRecordingVisual(bool isRecording, bool isProcessing)
    {
        if (isRecording)
        {
            RootBorder.Background = new SolidColorBrush(Color.FromRgb(220, 53, 69));
            RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(139, 0, 0));
        }
        else if (isProcessing)
        {
            RootBorder.Background = new SolidColorBrush(Color.FromRgb(255, 193, 7));
            RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(180, 120, 0));
        }
        else
        {
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF));
            RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x33, 0x33, 0x33));
        }

        _recordingStoryboard?.Stop(this);
        _processingStoryboard?.Stop(this);
        RootBorder.Opacity = 1.0;
        RootScale.ScaleX = 1.0;
        RootScale.ScaleY = 1.0;

        if (isRecording)
            _recordingStoryboard?.Begin(this, isControllable: true);
        else if (isProcessing)
            _processingStoryboard?.Begin(this, isControllable: true);
    }

    private void OnBorderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        _suppressToggle = false;
        _pressScreen = PointToScreen(e.GetPosition(this));
        ((UIElement)sender).CaptureMouse();
    }

    private void OnBorderMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !ReferenceEquals(Mouse.Captured, sender))
            return;

        var now = PointToScreen(e.GetPosition(this));
        var dx = now.X - _pressScreen.X;
        var dy = now.Y - _pressScreen.Y;
        if (!_dragging && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4))
            _dragging = true;

        if (_dragging)
        {
            _suppressToggle = true;
            // WS_EX_NOACTIVATE prevents window activation, so DragMove() fails silently.
            // Manual delta-based positioning works regardless of activation state.
            Left += now.X - _pressScreen.X;
            Top  += now.Y - _pressScreen.Y;
            _pressScreen = now;
        }
    }

    // Bestätigter Klick auf "Schließen" im Rechtsklick-Menü blendet den Button aus
    // (Wieder-Einblenden über das Tray-Häkchen).
    private void OnCloseMenuItemClick(object sender, RoutedEventArgs e) =>
        HideRequested?.Invoke(this, EventArgs.Empty);

    private void OnBorderMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(Mouse.Captured, sender))
            ((UIElement)sender).ReleaseMouseCapture();

        if (!_suppressToggle && !_dragging)
            ToggleRecording?.Invoke(this, EventArgs.Empty);

        var wasDragging = _dragging;
        _dragging = false;
        _suppressToggle = false;

        if (wasDragging)
            DragCompleted?.Invoke(this, EventArgs.Empty);
    }
}
