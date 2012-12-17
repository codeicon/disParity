using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using disParity;

namespace disParityUI
{

  public class LicenseWindowViewModel : NotifyPropertyChanged
  {

    public LicenseWindowViewModel()
    {
      string exeFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      string licenseFile = Path.Combine(exeFolder, "License.txt");

#if DEBUG
      if (!File.Exists(licenseFile))
        licenseFile = @"C:\projects\disParity\license.txt";
#endif

      try {
        LicenseText = File.ReadAllText(licenseFile);
      } catch (Exception e) {
        LogFile.Log("Error reading {0}: {1}", licenseFile, e.Message);
        LicenseText = "Error reading license file: " + e.Message;
      }

    }

    private string licenseText;
    public string LicenseText
    {
      get
      {
        return licenseText;
      }
      set
      {
        SetProperty(ref licenseText, "LicenseText", value);
      }
    }

  }

}
