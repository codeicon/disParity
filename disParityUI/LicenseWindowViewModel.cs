using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;


namespace disParityUI
{

  public class LicenseWindowViewModel : ViewModel
  {

    public LicenseWindowViewModel()
    {
      string exeFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase);
      string licenseFile = Path.Combine(exeFolder, "License.txt");
      if (!File.Exists(licenseFile))
        licenseFile = @"C:\projects\disParity\license.txt";
      LicenseText = File.ReadAllText(licenseFile);

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
