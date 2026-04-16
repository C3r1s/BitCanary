using Avalonia.Controls;
using Avalonia.Input;
using Messenger.Client.Avalonia.ViewModels;

namespace Messenger.Client.Avalonia.Views;

public partial class GroupInfoView : UserControl
{
    public GroupInfoView()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is GroupInfoViewModel vm)
        {
            vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }
}
