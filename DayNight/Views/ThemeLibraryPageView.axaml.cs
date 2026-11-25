using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DayNight.Views;

public partial class ThemeLibraryPageView : UserControl
{
    public ThemeLibraryPageView()
    {
        InitializeComponent();
    }

    private void ShowHelp(object? sender, RoutedEventArgs e)
    {
        HelpDialogWindow.Show(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("no window"), "主题库说明",
            ("主题来源", "这里列出 Windows「个性化 → 选择要应用的主题」中已安装的全部主题，预览图取自各主题的壁纸。"),
            ("点击切换", "点击任意主题卡片可以立即切换到该主题（需确认），保持到当前时段结束，之后按计划自动切换，不影响时段规则。"),
            ("当前使用", "带标记的是正在生效的主题。在系统里新增或删除主题后，点「重新扫描」刷新列表。"));
    }

    private void ThemeCardTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is not Border card) return;
        if (card.DataContext is not ViewModels.ThemeItemViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not MainWindow owner) return;
        owner.ConfirmTheme(vm.Name, vm.CountText,
            "应用整套主题，包括壁纸、系统与应用明暗、强调色、声音和光标；不改动时段规则，保持到当前时段结束，之后按计划自动切换。",
            () => vm.ApplyNow());
    }
}
