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
      MaxTempRAM = (int)config.MaxTempRAM;
      IgnoreHidden = config.IgnoreHidden;
      TempDir = config.TempDir;
      foreach (string i in config.Ignores)
        if (String.IsNullOrEmpty(ignores))
          ignores = i;
        else
          ignores += "\r\n" + i;
    }

    public void SetNewParityLocation(string path)
    {

      // FIXME: What to do if there already is parity data here!?!

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

    public void SetNewTempDir(string path)
    {
      TempDir = path;
    }

    public void CommitChanges()
    {
      if (parityDir != PARITY_NOT_SET)
        config.ParityDir = parityDir;
      config.MaxTempRAM = (uint)MaxTempRAM;
      config.IgnoreHidden = IgnoreHidden;
      config.TempDir = TempDir;
      config.Ignores.Clear();
      if (!String.IsNullOrEmpty(ignores)) {
        string[] sep = { "\r\n" };
        string[] s = ignores.Split(sep, StringSplitOptions.RemoveEmptyEntries);
        foreach (string i in s)
          config.Ignores.Add(i);
      }
      config.Save();
    }

    #region Properties

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

    private string tempDir;
    public string TempDir
    {
      get
      {
        return tempDir;
      }
      set
      {
        SetProperty(ref tempDir, "TempDir", value);
      }

    }

    private int maxTempRAM;
    public int MaxTempRAM
    {
      get
      {
        return maxTempRAM;
      }
      set
      {
        SetProperty(ref maxTempRAM, "MaxTempRAM", value);
      }
    }

    public int MaxTempRAMIncrement { get { return 256; } }

    // TODO: Need to test out this value on an actual 32 bit OS
    const int MAXIMUM_MAX_TEMP_RAM_32BIT = 1536;
    public int MaximumMaxTempRam
    {
      get
      {
        int systemRAMInMB = (int)(Utils.TotalSystemRAM() / (1024 * 1024));

        // don't let the set the max higher than 80% of physical RAM, rounded
        // to the next multiple of the increment
        int max = (int)(0.8 * systemRAMInMB);
        max = (max / MaxTempRAMIncrement) * MaxTempRAMIncrement;

        if (Environment.Is64BitProcess)
          return max;
        else
          // 32 bit process
          if (max > MAXIMUM_MAX_TEMP_RAM_32BIT)
            return MAXIMUM_MAX_TEMP_RAM_32BIT;
          else
            return max;
      }
    }

    private bool ignoreHidden;
    public bool IgnoreHidden
    {
      get
      {
        return ignoreHidden;
      }
      set
      {
        SetProperty(ref ignoreHidden, "IgnoreHidden", value);
      }
    }

    private string ignores;
    public string Ignores
    {
      get
      {
        return ignores;
      }
      set
      {
        SetProperty(ref ignores, "Ignores", value);
      }
    }

    #endregion

  }
}
