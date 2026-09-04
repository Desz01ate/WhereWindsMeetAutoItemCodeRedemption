using System.Windows;
using WhereWindsMeetItemCodeRedeemer.ViewModels;

namespace WhereWindsMeetItemCodeRedeemer;

public partial class CalibrationWindow : Window
{
    public CalibrationWindow(CalibrationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += Close;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
