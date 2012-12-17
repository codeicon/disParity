using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using disParity;

namespace disParityUI
{

  internal class ReportWindowViewModel : NotifyPropertyChanged
  {

    public ReportWindowViewModel(string text)
    {
      ReportText = text;
    }

    public void Save()
    {
      Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
      dlg.FileName = "Report"; // Default file name
      dlg.DefaultExt = ".txt"; // Default file extension
      dlg.Filter = "Text documents (.txt)|*.txt"; // Filter files by extension // Show save file dialog box
      if (dlg.ShowDialog() == true)
        File.WriteAllText(dlg.FileName, ReportText);
    }

    #region Properties
    private string reportText;
    public string ReportText
    {
      get
      {
        return reportText;
      }
      set
      {
        SetProperty(ref reportText, "ReportText", value);
      }
    }
    #endregion

  }

}
