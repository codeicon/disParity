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
  /// Interaction logic for MessageWindow.xaml
  /// </summary>
  public partial class MessageWindow : Window
  {

    public MessageWindow()
    {
      InitializeComponent();
      Loaded += HandleLoaded;
    }

    private void HandleLoaded(object sender, EventArgs args)
    {
      WindowUtils.RemoveCloseButton(this);
    }

    public void HandleYesClick(object Sender, RoutedEventArgs args)
    {
      DialogResult = true;
    }

    public void HandleOKClick(object Sender, RoutedEventArgs args)
    {
      DialogResult = true;
    }

    public void HandleCancelClick(object Sender, RoutedEventArgs args)
    {
      DialogResult = null;
      Close();
    }

    public void HandleNoClick(object Sender, RoutedEventArgs args)
    {
      DialogResult = false;
    }

    internal static bool? Show(Window owner, string caption, string message, MessageWindowIcon icon = MessageWindowIcon.OK, MessageWindowButton buttons = MessageWindowButton.OK)
    {
      bool? result = null;
      Application.Current.Dispatcher.Invoke(new Action(() =>
        {
          MessageWindow window = new MessageWindow();
          window.DataContext = new MessageWindowViewModel(caption, message, icon, buttons);
          window.Owner = owner;
          result = window.ShowDialog();
        }));
      return result;
    }

  }


}
