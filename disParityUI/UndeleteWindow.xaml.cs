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

  public partial class UndeleteWindow : Window
  {

    UndeleteWindowViewModel vm;

    public UndeleteWindow()
    {
      InitializeComponent();
      Loaded += HandleLoaded;
    }

    private void HandleLoaded(object sender, EventArgs args)
    {
      WindowUtils.RemoveCloseButton(this);
      vm = (UndeleteWindowViewModel)DataContext;
      SelectAll();
    }

    private void SelectAll()
    {
      listBox.SelectedItems.Clear();
      foreach (var s in vm.Files)
        listBox.SelectedItems.Add(s);
    }

    public void HandleSelectAllClick(object Sender, RoutedEventArgs args)
    {
      SelectAll();
    }

    public void HandleUnselectAllClick(object Sender, RoutedEventArgs args)
    {
      listBox.SelectedItems.Clear();
    }

    public void HandleOKClick(object Sender, RoutedEventArgs args)
    {
      foreach (var s in listBox.SelectedItems)
        vm.SelectedFiles.Add((string)s);
      DialogResult = true;
    }

  }
}
