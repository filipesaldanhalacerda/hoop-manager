using System.Collections.Specialized;
using HoopConnectionManager.ViewModels;

namespace HoopConnectionManager.Views;

public partial class LogsView : System.Windows.Controls.UserControl
{
    public LogsView()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachAutoScroll();
    }

    private void AttachAutoScroll()
    {
        if (DataContext is not LogsViewModel viewModel)
        {
            return;
        }

        viewModel.Entries.CollectionChanged -= OnEntriesChanged;
        viewModel.Entries.CollectionChanged += OnEntriesChanged;
        if (viewModel.Entries.Count > 0)
        {
            LogList.ScrollIntoView(viewModel.Entries[^1]);
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is LogsViewModel { FollowLatest: true } viewModel && viewModel.Entries.Count > 0)
        {
            LogList.ScrollIntoView(viewModel.Entries[^1]);
        }
    }
}
