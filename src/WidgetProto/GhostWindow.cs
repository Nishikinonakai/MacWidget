using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace WidgetProto;

/// <summary>
/// 落点虚影：拖拽时显示在预测吸附位置的半透明占位（macOS 同款交互）。
/// 单例复用；WS_EX_TRANSPARENT 全程不吃鼠标；贴底（在被拖组件之下）。
/// </summary>
public sealed class GhostWindow : Window
{
    static GhostWindow? _inst;
    public static GhostWindow Instance => _inst ??= new GhostWindow();

    GhostWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        Title = "MacWidget Ghost";
        // macOS 实测虚影形态：细白描边圆角矩形、几乎无填充（exp1/exp2-mid 截图）；
        // 与卡同几何：帧内衬 8、圆角 20
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(20),
            Margin = new Thickness(8),
        };
        SourceInitialized += (_, _) =>
        {
            var src = (HwndSource)PresentationSource.FromVisual(this)!;
            src.CompositionTarget.BackgroundColor = Colors.Transparent;
            var h = src.Handle;
            var ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
            ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex));
            Dwm.ExtendIntoClient(h);   // 无 backdrop 的顶层窗必须 extend，否则透明面呈黑底
            BottomPin.Install(src);
        };
    }

    /// <summary>以虚拟桌面物理 px 显示；和被拖组件共用跨屏坐标系。</summary>
    public void ShowAt(Rect rect)
    {
        var display = DisplayTopology.ForRect(rect);
        Width = rect.Width / display.Scale;
        Height = rect.Height / display.Scale;
        Left = 0; Top = 0;
        if (!IsVisible) Show();
        if (PresentationSource.FromVisual(this) is HwndSource src)
            Native.MoveCompositedWindow(src.Handle, (int)Math.Round(rect.Left), (int)Math.Round(rect.Top),
                (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
    }

    public void HideGhost() { if (IsVisible) Hide(); }
}
