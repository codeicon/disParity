using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;
using disParity;

namespace disParityUI
{

  public class CrashWindowViewModel : NotifyPropertyChanged
  {

    public CrashWindowViewModel()
    {
    }

    public ImageSource Icon
    {
      get
      {
        return Icons.Urgent;
      }
    }

    public string ForumURL
    {
      get
      {
        return @"http://www.vilett.com/disParity/forum/";
      }
    }

  }

}
