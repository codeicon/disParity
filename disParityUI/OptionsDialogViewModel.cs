using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using disParity;

namespace disParityUI
{

  public class OptionsDialogViewModel : ViewModel
  {

    private Config config;
    private const string PARITY_NOT_SET = "<Not set>";

    public OptionsDialogViewModel(Config config)
    {
      this.config = config;
      if (String.IsNullOrEmpty(config.ParityDir)) {
        ParityDir = PARITY_NOT_SET;
        CanSetLocation = true;
      }
      else {
        ParityDir = config.ParityDir;
        CanSetLocation = false;
      }
    }

    public void SetNewParityLocation(string path)
    {
      // maybe do this someday if it's possible for non-existant paths to be passed in
      //if (!Directory.Exists(path)) {
      //  if (MessageBox.Show("Directory does not exist.  Create it?", "Directory does not exist", 
      //    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
      DirectoryInfo dirInfo;
      bool empty;
      try {
        dirInfo = new DirectoryInfo(path);
        empty = (dirInfo.GetDirectories().Length == 0) || (dirInfo.GetFiles().Length == 0);
      }
      catch (Exception e) {
        MessageBox.Show("Could not access directory: " + e.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }
      if (!empty) {
        MessageBox.Show("Directory is not empty.  Please choose a different location.", "Parity location not empty", MessageBoxButton.OK, MessageBoxImage.Exclamation);
        return;
      }
      ParityDir = path;
    }

    public void CommitChanges()
    {
      if (parityDir != PARITY_NOT_SET)
        config.ParityDir = parityDir;
      config.Save();
    }

    private bool canSetLocation;
    public bool CanSetLocation
    {
      get
      {
        return canSetLocation;
      }
      set
      {
        SetProperty(ref canSetLocation, "CanSetLocation", value);
      }
    }

    private string parityDir;
    public string ParityDir
    {
      get
      {
        return parityDir;
      }
      set
      {
        SetProperty(ref parityDir, "ParityDir", value);
      }

    }

  }
}
