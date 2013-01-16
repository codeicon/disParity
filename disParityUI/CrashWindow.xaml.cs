using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Diagnostics;

namespace disParityUI
{

  public partial class CrashWindow : Window
  {

    private CrashWindowViewModel viewModel;

    public CrashWindow(Window owner, CrashWindowViewModel viewModel)
    {
      this.Owner = owner;
      this.viewModel = viewModel;
      InitializeComponent();
      DataContext = viewModel;
      Loaded += HandleLoaded;
    }

    private void HandleLoaded(object sender, EventArgs args)
    {
      WindowUtils.RemoveCloseButton(this);
    }

    public void HandleOKClick(object Sender, RoutedEventArgs args)
    {
      DialogResult = true;
    }

    public void HandleSupportClick(object Sender, RoutedEventArgs args)
    {
      Process.Start(new ProcessStartInfo(viewModel.ForumURL));
      args.Handled = true;
    }


  }

}

