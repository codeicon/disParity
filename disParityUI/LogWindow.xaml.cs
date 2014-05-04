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
using System.Threading;

namespace disParityUI
{

  public partial class LogWindow : Window
  {

    private LogWindowViewModel viewModel;
    private Thread monitor;

    public LogWindow()
    {
      InitializeComponent();
      viewModel = new LogWindowViewModel();
      DataContext = viewModel;
      monitor = new Thread(MonitorItems);
      monitor.IsBackground = true;
      monitor.Start();
      CopyToClipboardButton.IsEnabled = false;
      listbox.SelectionChanged += HandleListBoxSelectionChanged;
    }

    private void MonitorItems(object o)
    {
      int lastCount = 0;
      for (; ; )
      {
        if (listbox.Items.Count != lastCount)
         Application.Current.Dispatcher.Invoke(new Action(() => 
          {
            lastCount = listbox.Items.Count;
            listbox.ScrollIntoView(listbox.Items[lastCount - 1]);
          }));
        Thread.Sleep(500);
      }
    }

    private void HandleEntriesChanged(object sender, EventArgs args)
    {
      listbox.ScrollIntoView(listbox.Items[listbox.Items.Count - 1]);
    }

    private void HandleListBoxSelectionChanged(object sender, EventArgs args)
    {
      if (listbox.SelectedItems.Count > 0)
        CopyToClipboardButton.IsEnabled = true;
      else
        CopyToClipboardButton.IsEnabled = false;
    }

    public void HandleCopyToClipboardClick(object Sender, RoutedEventArgs args)
    {
      StringBuilder sb = new StringBuilder();
      foreach (var item in listbox.SelectedItems)
      {
        LogEntry e = item as LogEntry;
        sb.Append(e.Text);
        sb.Append("\n");
      }
      Clipboard.SetText(sb.ToString());
    }

    public void HandleSaveClick(object Sender, RoutedEventArgs args)
    {
      viewModel.Save();
    }

  }

}
