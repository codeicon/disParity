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
using System.Windows.Shapes;
using Xceed.Wpf.Toolkit;

namespace disParityUI
{

  public partial class OptionsDialog : Window
  {

    private OptionsDialogViewModel viewModel;

    public OptionsDialog(OptionsDialogViewModel viewModel)
    {
      this.viewModel = viewModel;
      viewModel.Owner = this;
      DataContext = viewModel;
      InitializeComponent();

      Loaded += HandleLoaded;
    }

    private void HandleLoaded(object sender, EventArgs args)
    {
      WindowUtils.RemoveCloseButton(this);
    }

    public void HandleSetLocationClick(object Sender, RoutedEventArgs args)
    {
      FolderBrowserDialog d = new FolderBrowserDialog();
      d.Description = "Choose a location to store parity data:";
      DialogResult r = d.ShowDialog();
      if (r == System.Windows.Forms.DialogResult.OK)
        viewModel.SetNewParityLocation(d.SelectedPath);
    }

    public void HandleImportClick(object sender, RoutedEventArgs args)
    {
      Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
      dlg.Title = "Choose a configuration file to import";
      dlg.DefaultExt = ".txt";
      dlg.FileName = "config.txt";
      dlg.Filter = "Config files (config.txt)|*.txt";
      dlg.CheckFileExists = true;
      if (dlg.ShowDialog() == true)
        viewModel.ImportOldConfiguration(dlg.FileName);
    }

    public void HandleChangeTempDirClick(object Sender, RoutedEventArgs args)
    {
      FolderBrowserDialog d = new FolderBrowserDialog();
      d.Description = "Choose a location to store temporary data during updates:";
      DialogResult r = d.ShowDialog();
      if (r == System.Windows.Forms.DialogResult.OK)
        viewModel.SetNewTempDir(d.SelectedPath);
    }

    public void HandleOKClick(object Sender, RoutedEventArgs args)
    {
      if (viewModel.CommitChanges())
        DialogResult = true;
    }

  }

}
