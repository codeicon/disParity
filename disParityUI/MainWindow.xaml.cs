using System;
using System.Collections.Generic;
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
using disParity;

namespace disParityUI
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window
  {

    private MainWindowViewModel viewModel;

    public MainWindow()
    {
      InitializeComponent();

      viewModel = new MainWindowViewModel();
      DataContext = viewModel;
    }

    void AddDriveCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      e.CanExecute = true;
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
      e.CanExecute = DriveList.SelectedItems.Count == 1;
    }

    void ScanDriveExecuted(object sender, ExecutedRoutedEventArgs e)
    {
      if (DriveList.SelectedItems.Count == 1)  // sanity check
        viewModel.ScanDrive((DataDriveViewModel)DriveList.SelectedItem);
    }

    void ScanAllCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      e.CanExecute = DriveList.HasItems;
    }

    void ScanAllExecuted(object sender, ExecutedRoutedEventArgs e)
    {
      viewModel.ScanAll();
    }

  }

  public class Commands
  {
    public static RoutedUICommand AddDrive;
    public static RoutedUICommand ScanDrive;
    public static RoutedUICommand ScanAll;

    static Commands()
    {
      AddDrive = new RoutedUICommand("Add Drive...", "AddDrive", typeof(MainWindow));
      ScanDrive = new RoutedUICommand("Scan Drive", "ScanDrive", typeof(MainWindow));
      ScanAll = new RoutedUICommand("Scan All", "ScanAll", typeof(MainWindow));
    }
  }

}
