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

namespace disParityUI
{
  /// <summary>
  /// Interaction logic for UndeleteWindow.xaml
  /// </summary>
  public partial class UndeleteWindow : Window
  {
    public UndeleteWindow()
    {
      InitializeComponent();
    }

    public void HandleOKClick(object Sender, RoutedEventArgs args)
    {
      DialogResult = true;
    }

  }
}
