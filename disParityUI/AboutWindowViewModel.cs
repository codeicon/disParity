using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;

namespace disParityUI
{

  public class AboutWindowViewModel : ViewModel
  {

    public AboutWindowViewModel()
    {
      versionString = disParity.Version.VersionString;
    }

    private string versionString;
    public string VersionString
    {
      get
      {
        return versionString;
      }
      set
      {
        SetProperty(ref versionString, "VersionString", value);
      }
    }

    public string ForumURL
    {
      get
      {
        return @"http://www.vilett.com/disParity/forum/";
      }
      set
      {
      }
    }

  }

}
