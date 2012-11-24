using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms; // for FolderBrowserDialog
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using disParity;
using System.Reflection;

namespace disParityUI
{

  public partial class MainWindow : Window
  {

    private MainWindowViewModel viewModel;
    private DispatcherTimer timer;

    public MainWindow()
    {
      viewModel = new MainWindowViewModel(this);
      DataContext = viewModel;
      Initialized += HandleInitialized;
      Loaded += HandleLoaded;
      Closed += HandleClosed;
      Closing += HandleClosing;

      InitializeComponent();
      timer = new DispatcherTimer();
      timer.Tick += HandleTimer;
      timer.Interval = new TimeSpan(0, 0, 1);
      timer.Start();
    }

    private void HandleInitialized(object sender, EventArgs args)
    {
      if (SingleInstance.AlreadyRunning()) {
        MessageWindow.Show(null, "Already running", "Another instance of disParity is already running", MessageWindowIcon.Error, MessageWindowButton.OK);
        Close();
      }
    }

    private void HandleTimer(object sender, EventArgs args)
    {
      CommandManager.InvalidateRequerySuggested();
    }

    private void HandleLoaded(object sender, EventArgs args)
    {
      viewModel.Loaded();
    }

    private void HandleClosing(object sender, CancelEventArgs args)
    {
      if (viewModel.Busy) 
        // FIXME: prompt user here?
        args.Cancel = true;
    }

    private void HandleClosed(object sender, EventArgs args)
    {
      viewModel.Shutdown();
    }

    #region Command CanExecute/Executed methods

    void AddDriveCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      e.CanExecute = !String.IsNullOrEmpty(viewModel.ParityLocation) && !viewModel.Busy;
    }

    void AddDriveExecuted(object sender, ExecutedRoutedEventArgs e)
    {
      FolderBrowserDialog d = new FolderBrowserDialog();
      d.Description = "Choose a drive or path to add:";
      DialogResult r = d.ShowDialog();
      if (r == System.Windows.Forms.DialogResult.OK)
        viewModel.AddDrive(d.SelectedPath);
    }

    void ScanDriveCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      e.CanExecute = !viewModel.Busy && (DriveList.SelectedItems.Count == 1);
    }

    void ScanDriveExecuted(object sender, ExecutedRoutedEventArgs e)
    {
      if (DriveList.SelectedItems.Count == 1)  // sanity check
        ((DataDriveViewModel)DriveList.SelectedItem).Scan();
    }

    void ScanAllCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      e.CanExecute = !viewModel.Busy && DriveList.HasItems;
    }

    void ScanAllExecuted(object sender, ExecutedRoutedEventArgs e)
    {
      viewModel.ScanAll();
    }

    void UpdateAllCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      if (viewModel.Busy || viewModel.Drives.Count == 0)
        e.CanExecute = false;
      else
        e.CanExecute = true;
    }

    void UpdateAllExecuted(object sender, ExecutedRoutedEventArgs e)
    {
      viewModel.UpdateAll();
    }

    void RecoverDriveCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      e.CanExecute = !viewModel.Busy && (DriveList.SelectedItems.Count == 1);
    }

    void RecoverDriveExecuted(object sender, ExecutedRoutedEventArgs e)
    {
      FolderBrowserDialog d = new FolderBrowserDialog();
      d.Description = "Choose a location to place recovered files:";
      DialogResult r = d.ShowDialog();
      if (r == System.Windows.Forms.DialogResult.OK)
        viewModel.RecoverDrive((DataDriveViewModel)DriveList.SelectedItem, d.SelectedPath);
    }

    void OptionsCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      e.CanExecute = !viewModel.Busy;
    }

    public static RoutedUICommand AddDrive;

    void OptionsExecuted(object sender, ExecutedRoutedEventArgs e)
    {
      OptionsDialog dialog = new OptionsDialog(viewModel.GetOptionsDialogViewModel());
      dialog.Owner = this;
      if (dialog.ShowDialog() == true)
        viewModel.OptionsChanged();
    }

    void AboutExecuted(object sender, ExecutedRoutedEventArgs e)
    {
      new AboutWindow(this, new AboutWindowViewModel()).ShowDialog();
    }

    #endregion

  }


}
