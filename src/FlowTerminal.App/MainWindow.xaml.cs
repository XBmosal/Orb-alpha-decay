using System.Windows;

namespace FlowTerminal.App;

/// <summary>
/// The main shell window. It binds read-only state from <see cref="ShellViewModel"/>.
/// There are deliberately no buy/sell/cancel/flatten/reverse controls anywhere in
/// this window or its code-behind.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        ApplyViewModel();
    }

    private void ApplyViewModel()
    {
        InstrumentText.Text = _viewModel.SelectedInstrument;
        ContractText.Text = "(discovering…)";
        SessionText.Text = _viewModel.SessionLabel;
        SimulatedBanner.Text = _viewModel.SimulatedDataBanner;
        ReadOnlyBanner.Text = _viewModel.ReadOnlyBanner;
        RithmicStatusText.Text = _viewModel.RithmicStatus;
    }
}
