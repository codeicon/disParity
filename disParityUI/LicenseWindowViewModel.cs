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
      //string exeFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase);
      //LicenseText = File.ReadAllText(Path.Combine(exeFolder, "License.txt"));
      // Assume working directory has been set to install dir
      LicenseText = File.ReadAllText("License.txt");

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
