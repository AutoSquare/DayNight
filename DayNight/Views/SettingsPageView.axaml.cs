using Avalonia.Controls;

namespace DayNight.Views;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();
    }

    private void ShowHelp(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        HelpDialogWindow.Show(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("no window"), "设置说明",
            ("外观", "晨昏线自身的界面配色，可选跟随系统、亮色或暗色。"),
            ("启动", "开机自动启动、静默启动（不弹主窗口只驻托盘）、最小化到托盘（开启时点最小化进托盘；关闭时最小化到任务栏，点关闭退出程序）。"),
            ("调度", "全屏时暂缓切换、启动和唤醒时立即对齐当前时段。"),
            ("切换提示", "切换前屏幕中央的提示卡及显示时长、切换后的托盘通知。"));
    }
}
