using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using disParity;

namespace disParityUI
{

  public class LogWindowViewModel
  {

    private static ObservableCollection<LogEntry> entries;
    private static MainWindowViewModel mainWindowViewModel;

    private const int MAX_ENTRIES = 1000;

    internal static void Initialize(MainWindowViewModel mainWindowViewModel)
    {
      LogFile.OnEntry += HandleNewLogEntry;
      entries = new ObservableCollection<LogEntry>();
      LogWindowViewModel.mainWindowViewModel = mainWindowViewModel;  
    }

    private static void HandleNewLogEntry(DateTime now, string entry, bool error)
    {
      Application.Current.Dispatcher.BeginInvoke(new Action(() => 
      {
        entries.Add(new LogEntry(now, entry, error));
        while (entries.Count > MAX_ENTRIES)
          entries.RemoveAt(0);
      }));
    }

    public void Save()
    {
      Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
      dlg.FileName = "disParity log"; // Default file name
      dlg.DefaultExt = ".txt"; // Default file extension
      dlg.Filter = "Text documents (.txt)|*.txt"; // Filter files by extension // Show save file dialog box
      if (dlg.ShowDialog() == true)
        using (StreamWriter fs = new StreamWriter(dlg.FileName, false))
          foreach (LogEntry e in entries)
            fs.WriteLine(e.Text);
    }

    public ObservableCollection<LogEntry> LogEntries
    {
      get
      {
        return entries;
      }
    }

    public int Top
    {
      get
      {
        return mainWindowViewModel.Config.LogWindowY;
      }
      set
      {
        mainWindowViewModel.Config.LogWindowY = value;
        mainWindowViewModel.Config.Save();
      }
    }

    public int Left
    {
      get
      {
        return mainWindowViewModel.Config.LogWindowX;
      }
      set
      {
        mainWindowViewModel.Config.LogWindowX = value;
        mainWindowViewModel.Config.Save();
      }
    }

    public int Width
    {
      get
      {
        return mainWindowViewModel.Config.LogWindowWidth;
      }
      set
      {
        mainWindowViewModel.Config.LogWindowWidth = value;
        mainWindowViewModel.Config.Save();
      }
    }

    public int Height
    {
      get
      {
        return mainWindowViewModel.Config.LogWindowHeight;
      }
      set
      {
        mainWindowViewModel.Config.LogWindowHeight = value;
        mainWindowViewModel.Config.Save();
      }
    }

  }

  public class LogEntry
  {

    public LogEntry(DateTime now, string text, bool error)
    {
      Text = now + " " + text;
      if (error)
        Color = Brushes.Red;
      else
        Color = Brushes.Black;
    }

    public Brush Color { get; private set; }


    public string Text { get; private set; }

  }

}
