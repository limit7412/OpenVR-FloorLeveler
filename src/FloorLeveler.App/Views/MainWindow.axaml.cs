using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using FloorLeveler.App.ViewModels;

namespace FloorLeveler.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    // 記録 = Space (仕様 §6)。ただしフォーカスが入力部品にある場合はその操作を
    // 優先し、床サンプルを誤って追加しない (チェックボックス/ラジオの切替など)。
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.Handled)
        {
            return;
        }

        if (FocusedElementConsumesSpace())
        {
            return;
        }

        if (DataContext is MainViewModel vm && vm.RecordPointCommand.CanExecute(null))
        {
            vm.RecordPointCommand.Execute(null);
            e.Handled = true;
        }
    }

    private bool FocusedElementConsumesSpace()
    {
        var focused = FocusManager?.GetFocusedElement();
        return focused is Button or ToggleButton or TextBox or ComboBox;
    }
}
