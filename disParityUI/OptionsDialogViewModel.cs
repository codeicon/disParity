using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using disParity;

namespace disParityUI
{

  public class OptionsDialogViewModel : NotifyPropertyChanged
  {

    private Config config;
    private ParitySet paritySet;
    private const string PARITY_NOT_SET = "<Not set>";

    public OptionsDialogViewModel(ParitySet paritySet)
    {
      this.paritySet = paritySet;
      config = paritySet.Config;
      SetProperties();
    }

    private void SetProperties()
    {
      if (String.IsNullOrEmpty(config.ParityDir))
        ParityDir = PARITY_NOT_SET;
      else
        ParityDir = config.ParityDir;
      CanSetLocation = true;
      // crash log shows a user crashing here with "Value must be greated than MinValue of 256" exception,
      // which I cannot reproduce no matter what I set MaxTempRAM to.  But, enforce the minimum anyway.
      if (config.MaxTempRAM < MaxTempRAMIncrement)
        MaxTempRAM = MaxTempRAMIncrement;
      else
        MaxTempRAM = (int)config.MaxTempRAM;
      IgnoreHidden = config.IgnoreHidden;
      TempDir = config.TempDir;
      MonitorDrives = config.MonitorDrives;
      UpdateDelay = config.UpdateDelay;
      updateHours = config.UpdateHours;
      DailyScanTime = config.UpdateDaily;
      switch (config.UpdateMode)
      {
        case UpdateMode.NoAction:
          NoAction = true;
          UpdateSoon = false;
          UpdateHourly = false;
          UpdateDaily = false;
          break;
        case UpdateMode.UpdateSoon:
          NoAction = false;
          UpdateSoon = true;
          UpdateHourly = false;
          UpdateDaily = false;
          break;
        case UpdateMode.UpdateHourly:
          NoAction = false;
          UpdateSoon = false;
          UpdateHourly = true;
          UpdateDaily = false;
          break;
        case UpdateMode.UpdateDaily:
          NoAction = false;
          UpdateSoon = false;
          UpdateHourly = false;
          UpdateDaily = true;
          break;
      }
      foreach (string i in config.Ignores)
        if (String.IsNullOrEmpty(ignores))
          ignores = i;
        else
          ignores += "\r\n" + i;
    }

    public void SetNewParityLocation(string path)
    {
      string root = Path.GetPathRoot(path);
      foreach (DataDrive d in paritySet.Drives)
        if (String.Compare(root, Path.GetPathRoot(d.Root), true) == 0)
        {
          bool? result = MessageWindow.Show(Owner, "Duplicate drives detected!", "The selected parity location appears to be on one of your data drives.\n\n" +
            "This is not recommended.  If the drive fails, disParity will not be able to recover any of your data.\n\n" +
            "Are you sure you want to store parity on this drive?", MessageWindowIcon.Error, MessageWindowButton.YesNo);
          if (result == false)
            return;
          else if (result == true)
            break;
        }

      DirectoryInfo dirInfo;
      bool empty;
      try
      {
        dirInfo = new DirectoryInfo(path);
        dirInfo.GetFiles("files*.dat");
        empty = (dirInfo.GetFiles("files*.dat").Length == 0) && (dirInfo.GetFiles("parity*.dat").Length == 0);
      }
      catch (Exception e)
      {
        MessageWindow.Show(Owner, "Error", "Could not access directory: " + e.Message, MessageWindowIcon.Error, MessageWindowButton.OK);
        return;
      }
      if (!empty)
      {
        if (MessageWindow.Show(Owner, "Directory not empty", "This directory appears to contain existing parity data.  Are you sure you want to change to this location?", MessageWindowIcon.Question, MessageWindowButton.YesNo) == false)
          return;
      }

      ParityDir = path;
    }

    private bool MoveParityData(string dest)
    {
      bool? result = MessageWindow.Show(Owner, "Move parity data?", "Would you like to move your existing parity data to the new location?", MessageWindowIcon.Question, MessageWindowButton.YesNoCancel);
      if (result == null)
        return false; // they cancelled

      // close open parity file (if any)
      paritySet.CloseParity();

      if (result == true)
      {
        Win32.SHFILEOPSTRUCT fileOpStruct = new Win32.SHFILEOPSTRUCT();

        fileOpStruct.wFunc = 1; // FO_MOVE
        fileOpStruct.pFrom = Marshal.StringToHGlobalUni(config.ParityDir + @"\files*.dat" + '\0' + config.ParityDir + @"\parity*.dat" + '\0' + '\0');
        fileOpStruct.pTo = Marshal.StringToHGlobalUni(dest + '\0' + '\0');
        fileOpStruct.fAnyOperationsAborted = false;
        fileOpStruct.lpszProgressTitle = "Relocating parity data";
        int moveResult = Win32.SHFileOperation(ref fileOpStruct);
        Marshal.FreeHGlobal(fileOpStruct.pFrom);
        Marshal.FreeHGlobal(fileOpStruct.pTo);

        if (moveResult != 0 || fileOpStruct.fAnyOperationsAborted)
        {
          MessageWindow.Show(Owner, "Could not move parity data", "Warning!  Not all parity files were moved.  Your backup is now in an indeterminate state.  It is strongly recommended that you rebuild your backup from scratch. ", MessageWindowIcon.Error, MessageWindowButton.OK);
          return false;
        }
      }

      return true;
    }

    public void ImportOldConfiguration(string path)
    {
      try
      {
        config.ImportOld(path);
        SetProperties();
        ConfigImported = true;
      }
      catch (Exception e)
      {
        App.LogCrash(e);
        MessageWindow.ShowError(Owner, "Import error", "Sorry, an error occurred while importing the configuration: " + e.Message);
        return;
      }
    }

    public void SetNewTempDir(string path)
    {
      if (ParityDir != PARITY_NOT_SET)
        if (Utils.PathsAreOnSameDrive(path, ParityDir))
          if (MessageWindow.Show(Owner, "Bad temp folder", "The path you selected appears to be on the same drive as your parity.\n\n" +
            "It is not recommended to have the temp folder on the same drive as your parity data.\n\n" +
            "Are you sure you want to set it to this location?", MessageWindowIcon.Caution, MessageWindowButton.YesNo) == false)
            return;
      TempDir = path;
    }

    public bool CommitChanges()
    {
      bool parityDirMoved = false;
      if (parityDir != PARITY_NOT_SET)
      {
        if ((String.Compare(parityDir, config.ParityDir, true) != 0) && !paritySet.Empty)
        {
          if (!MoveParityData(parityDir))
            return false;
          parityDirMoved = true;
        }
        config.ParityDir = parityDir;
      }
      config.MaxTempRAM = (uint)MaxTempRAM;
      if (config.IgnoreHidden != IgnoreHidden)
      {
        IgnoresChanged = true;
        config.IgnoreHidden = IgnoreHidden;
      }
      config.MonitorDrives = MonitorDrives;
      config.UpdateDelay = UpdateDelay;
      config.UpdateHours = UpdateHours;
      config.UpdateDaily = DailyScanTime;
      if (NoAction)
        config.UpdateMode = UpdateMode.NoAction;
      else if (UpdateSoon)
        config.UpdateMode = UpdateMode.UpdateSoon;
      else if (UpdateHourly)
        config.UpdateMode = UpdateMode.UpdateHourly;
      else if (UpdateDaily)
        config.UpdateMode = UpdateMode.UpdateDaily;

      config.TempDir = TempDir;
      config.Ignores.Clear();
      if (!String.IsNullOrEmpty(ignores))
      {
        string[] sep = { "\r\n" };
        string[] s = ignores.Split(sep, StringSplitOptions.RemoveEmptyEntries);
        foreach (string i in s)
          config.Ignores.Add(i);
      }
      config.Save();
      if (parityDirMoved)
        paritySet.Reset();
      return true;
    }

    #region Properties

    public bool ConfigImported { get; set; }

    public bool IgnoresChanged { get; set; }

    public Window Owner { get; set; }

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

        // don't let them set the max higher than 80% of physical RAM, rounded
        // to the next multiple of the increment
        int max = (int)(0.8 * systemRAMInMB);
        max = (max / MaxTempRAMIncrement) * MaxTempRAMIncrement;

        if (System.Environment.Is64BitProcess)
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
        IgnoresChanged = true;
        SetProperty(ref ignores, "Ignores", value);
      }
    }

    private bool monitorDrives;
    public bool MonitorDrives
    {
      get
      {
        return monitorDrives;
      }
      set
      {
        SetProperty(ref monitorDrives, "MonitorDrives", value);
      }
    }

    private bool noAction;
    public bool NoAction
    {
      get
      {
        return noAction;
      }
      set
      {
        SetProperty(ref noAction, "NoAction", value);
      }
    }

    private bool updateSoon;
    public bool UpdateSoon
    {
      get
      {
        return updateSoon;
      }
      set
      {
        SetProperty(ref updateSoon, "UpdateSoon", value);
      }
    }

    private bool updateHourly;
    public bool UpdateHourly
    {
      get
      {
        return updateHourly;
      }
      set
      {
        SetProperty(ref updateHourly, "UpdateHourly", value);
      }
    }

    private bool updateDaily;
    public bool UpdateDaily
    {
      get
      {
        return updateDaily;
      }
      set
      {
        SetProperty(ref updateDaily, "UpdateDaily", value);
      }
    }

    private UInt32 updateDelay;
    public UInt32 UpdateDelay
    {
      get
      {
        return updateDelay;
      }
      set
      {
        SetProperty(ref updateDelay, "UpdateDelay", value);
      }
    }

    private UInt32 updateHours;
    public UInt32 UpdateHours
    {
      get
      {
        return updateHours;
      }
      set
      {
        SetProperty(ref updateHours, "UpdateHours", value);
      }
    }

    private DateTime dailyScanTime;
    public DateTime DailyScanTime
    {
      get
      {
        return dailyScanTime;
      }
      set
      {
        SetProperty(ref dailyScanTime, "DailyScanTime", value);
      }
    }

    #endregion

  }
}
