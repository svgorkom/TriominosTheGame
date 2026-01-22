using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Triominos.UI.Controls;
using Triominos.UI.ViewModels;

namespace Triominos.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Subscribe to ViewModel property changes to update drag piece
        if (DataContext is GameViewModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
        
        DataContextChanged += MainWindow_DataContextChanged;
        
        // Handle right-click at window level for dropping pieces to rack
        PreviewMouseRightButtonDown += Window_PreviewMouseRightButtonDown;
    }

    private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is GameViewModel oldVm)
        {
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
        }
        
        if (e.NewValue is GameViewModel newVm)
        {
            newVm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameViewModel.SelectedPiece))
        {
            UpdateDragPiece();
        }
    }

    private void UpdateDragPiece()
    {
        if (DataContext is not GameViewModel viewModel)
            return;

        if (viewModel.SelectedPiece != null)
        {
            DragPiece.Piece = viewModel.SelectedPiece.Piece;
            DragPiece.Visibility = Visibility.Visible;

            // Position at current mouse location
            Point mousePos = Mouse.GetPosition(DragCanvas);
            Canvas.SetLeft(DragPiece, mousePos.X - DragPiece.PieceSize / 2);
            Canvas.SetTop(DragPiece, mousePos.Y - DragPiece.PieceSize / 2);
        }
        else
        {
            DragPiece.Visibility = Visibility.Collapsed;
            DragPiece.Piece = null;
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (DragPiece.Visibility == Visibility.Visible)
        {
            Point mousePos = e.GetPosition(DragCanvas);
            Canvas.SetLeft(DragPiece, mousePos.X - DragPiece.PieceSize / 2);
            Canvas.SetTop(DragPiece, mousePos.Y - DragPiece.PieceSize / 2);
        }
    }

    private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not GameViewModel viewModel)
            return;

        // Check if we can add to rack (must have a pool piece selected)
        if (!viewModel.IsSelectingFromPool || viewModel.SelectedPiece == null)
            return;

        // Check if the mouse is over a PieceSelectorControl (the rack)
        var hitElement = e.OriginalSource as DependencyObject;
        while (hitElement != null)
        {
            if (hitElement is PieceSelectorControl rackControl)
            {
                // Check if this rack has a DropToRackCommand (player's rack, not pool)
                if (rackControl.DropToRackCommand != null && 
                    rackControl.DropToRackCommand.CanExecute(null))
                {
                    rackControl.DropToRackCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }
            hitElement = VisualTreeHelper.GetParent(hitElement);
        }
    }
}