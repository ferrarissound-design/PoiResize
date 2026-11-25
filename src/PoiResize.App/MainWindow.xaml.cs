using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PoiResize.App.ViewModels;

namespace PoiResize.App;

public partial class MainWindow : Window
{
    private const string InternalDragFormat = "PoiResize.ImageItem";
    private readonly MainViewModel _vm = new();
    private Point _dragStart;
    private ImageItem? _dragItem;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm.IsBusy)
        {
            if (MessageBox.Show("処理中です。キャンセルして終了しますか？", "PoiResize", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _vm.CancelIfRunning();
        }
        _vm.SaveSettings(); // 前回の設定として復元される
        base.OnClosing(e);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- エクスプローラーからのドロップ ----------

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && !_vm.IsBusy) e.Effects = DragDropEffects.Copy;
        else if (e.Data.GetDataPresent(InternalDragFormat)) e.Effects = DragDropEffects.Move;
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(InternalDragFormat) is ImageItem moving)
        {
            var target = FindItemUnder(e.OriginalSource as DependencyObject);
            if (target is not null && !ReferenceEquals(target, moving))
                _vm.MoveItem(moving, _vm.Items.IndexOf(target));
            e.Handled = true;
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            _vm.AddPaths(paths);
            e.Handled = true;
        }
    }

    // ---------- 一覧内のドラッグ並べ替え ----------

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = FindItemUnder(e.OriginalSource as DependencyObject);
    }

    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null || _vm.IsBusy) return;
        if (Keyboard.Modifiers != ModifierKeys.None) return; // Shift / Ctrl は複数選択に使う
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(List, new DataObject(InternalDragFormat, item), DragDropEffects.Move);
        List.SelectedItem = item;
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _vm.RemoveSelectedCommand.CanExecute(List.SelectedItems))
        {
            _vm.RemoveSelectedCommand.Execute(List.SelectedItems);
            e.Handled = true;
        }
    }

    private static ImageItem? FindItemUnder(DependencyObject? d)
    {
        while (d is not null and not ListViewItem)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return (d as ListViewItem)?.DataContext as ImageItem;
    }
}
