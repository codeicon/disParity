using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows;
using System.Windows.Shell; // for TaskbarItem stuff
using disParity;

namespace disParityUI
{

  class MainWindowViewModel : ViewModel
  {

    private ParitySet paritySet;
    private ObservableCollection<DataDriveViewModel> drives = new ObservableCollection<DataDriveViewModel>();
    private int runningScans;
    private DataDriveViewModel recoverDrive; // current drive being recovered, if any
    private bool updateAfterScan = false;

    public MainWindowViewModel()
    {

      // Set up application data and log folders
      string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "disParity");
      if (!Directory.Exists(appDataPath))
        Directory.CreateDirectory(appDataPath);
      string logPath = Path.Combine(appDataPath, "logs");
      if (!Directory.Exists(logPath))
        Directory.CreateDirectory(logPath);
      LogFile.LogPath = logPath;

      paritySet = new ParitySet(appDataPath);
      foreach (DataDrive d in paritySet.Drives)
        AddDrive(d);
      paritySet.RecoverProgress += HandleRecoverProgress;
      paritySet.UpdateProgress += HandleUpdateProgress;

      Left = paritySet.Config.MainWindowX;
      Top = paritySet.Config.MainWindowY;
      Height = paritySet.Config.MainWindowHeight;
      Width = paritySet.Config.MainWindowWidth;

      UpdateStartupMessage();
      ParityLocation = paritySet.Config.ParityDir;

    }

    public void OptionsChanged()
    {
      UpdateStartupMessage();
      ParityLocation = paritySet.Config.ParityDir;
    }

    private void UpdateStartupMessage()
    {
      if (String.IsNullOrEmpty(paritySet.Config.ParityDir))
        StartupMessage = "Welcome to disParity!\r\n\r\n" +
          "To use disParity you must first specify a location where the parity data will be stored.\r\n\r\n" +
          "Press the 'Options...' button on the right.";
      else if (drives.Count == 0)
        StartupMessage = "Add one or more drives to be backed up by pressing the 'Add Drive' button.";
      else
        StartupMessage = "";
    }

    public OptionsDialogViewModel GetOptionsDialogViewModel()
    {
      return new OptionsDialogViewModel(paritySet.Config);
    }

    /// <summary>
    /// Called from the view when the app is closing
    /// </summary>
    public void Shutdown()
    {
      // save the main window position and size so it can be restored on next run
      paritySet.Config.MainWindowX = (int)left;
      paritySet.Config.MainWindowY = (int)top;
      paritySet.Config.MainWindowWidth = (int)Width;
      paritySet.Config.MainWindowHeight = (int)Height;
      paritySet.Close();
    }

    /// <summary>
    /// Adds a new drive with the given path to the parity set
    /// </summary>
    public void AddDrive(string path)
    {
      AddDrive(paritySet.AddDrive(path));
      UpdateStartupMessage();
    }

    private void AddDrive(DataDrive drive)
    {
      drive.ScanCompleted += HandleScanCompleted;
      drives.Add(new DataDriveViewModel(drive));
    }

    public void ScanAll()
    {
      if (drives.Count == 0)
        return;
      Busy = true;
      Status = "Scanning drives...";
      runningScans = drives.Count;
      foreach (DataDriveViewModel vm in drives)
        vm.Scan();
    }

    private void HandleUpdateProgress(object sender, UpdateProgressEventArgs args)
    {
      ProgressState = TaskbarItemProgressState.Normal;
      Progress = args.Progress;
    }

    private void HandleScanCompleted(object sender, EventArgs args)
    {
      runningScans--;
      if (runningScans == 0) {
        bool anyDriveNeedsUpdate = false;
        Busy = false;
        foreach (DataDrive d in paritySet.Drives)
          if (d.Status == DriveStatus.AccessError) {
            Status = "Error(s) encountered during scan!";
            return;
          } else if (d.Status == DriveStatus.UpdateRequired)
            anyDriveNeedsUpdate = true;
        if (anyDriveNeedsUpdate)
          if (updateAfterScan) {
            updateAfterScan = false;
            Update();
          } 
          else
            Status = "Changes detected.  Update required.";
        else
          DisplayUpToDateStatus();
      }
    }

    private void DisplayUpToDateStatus()
    {
      long totalSize = 0;
      int totalFiles = 0;
      foreach (DataDriveViewModel vm in drives) {
        totalSize += vm.DataDrive.TotalFileSize;
        totalFiles += vm.DataDrive.FileCount;
      }
      Status = String.Format("{1:N0} files ({0}) protected.  All drives up to date.",
        Utils.SmartSize(totalSize), totalFiles);
    }

    public void UpdateAll()
    {
      updateAfterScan = true;
      ScanAll();
    }

    private void Update()
    {
      Busy = true;
      Status = "Update In Progress...";
      Task.Factory.StartNew(() =>
      {
        try {
          paritySet.Update();
          DisplayUpToDateStatus();
        }
        catch (Exception e) {
          Status = "Update failed: " + e.Message;
        }
        finally {
          Progress = 0;
          ProgressState = TaskbarItemProgressState.None;
          Busy = false;
        }
      }
      );
    }

    public void RecoverDrive(DataDriveViewModel drive, string path)
    {
      Busy = true;
      Status = "Recovering " + drive.Root + " to " + path + "...";
      recoverDrive = drive;
      Task.Factory.StartNew(() =>
      {
        try {
          int successes;
          int failures;
          paritySet.Recover(drive.DataDrive, path, out successes, out failures);
          Status = String.Format("{0} file{1} recovered ({2} failure{3})",
            successes, successes == 1 ? "" : "s", failures, failures == 1 ? "" : "s");
        }
        catch (Exception e) {
          Status = "Recover failed: " + e.Message;
        }
        finally {
          Progress = 0;
          recoverDrive.UpdateStatus();
          ProgressState = TaskbarItemProgressState.None;
          Busy = false;
        }
      }
      );
    }

    private void HandleRecoverProgress(object sender, RecoverProgressEventArgs args)
    {
      Progress = args.Progress;
      if (!String.IsNullOrEmpty(args.Filename))
        recoverDrive.Status = "Recovering " + args.Filename + "...";
    }

    public ObservableCollection<DataDriveViewModel> Drives
    {
      get
      {
        return drives;
      }
    }

    private string parityLocation;
    public string ParityLocation
    {
      get
      {
        return parityLocation;
      }
      set
      {
        SetProperty(ref parityLocation, "ParityLocation", value);
      }
    }

    private string startupMessage;
    public string StartupMessage
    {
      get
      {
        return startupMessage;
      }
      set
      {
        SetProperty(ref startupMessage, "StartupMessage", value);
        if (startupMessage != "")
          StartupMessageVisibility = Visibility.Visible;
        else
          StartupMessageVisibility = Visibility.Hidden;
      }
    }

    private Visibility startupMessageVisibility;
    public Visibility StartupMessageVisibility
    {
      get
      {
        return startupMessageVisibility;
      }
      set
      {
        SetProperty(ref startupMessageVisibility, "StartupMessageVisibility", value);
      }
    }

    private bool busy;
    public bool Busy
    {
      get
      {
        return busy;
      }
      set
      {
        SetProperty(ref busy, "Busy", value);
      }
    }

    private string status = "";
    public string Status
    {
      get
      {
        return status;
      }
      set
      {
        SetProperty(ref status, "Status", value);
      }
    }

    private double progress = 0.0;
    public double Progress
    {
      get
      {
        return progress;
      }
      set
      {
        SetProperty(ref progress, "Progress", value);
      }
    }

    private double top;
    public double Top
    {
      get
      {
        return top;
      }
      set
      {
        SetProperty(ref top, "Top", value);
      }
    }

    private double left;
    public double Left
    {
      get
      {
        return left;
      }
      set
      {
        SetProperty(ref left, "Left", value);
      }
    }

    private double height;
    public double Height
    {
      get
      {
        return height;
      }
      set
      {
        SetProperty(ref height, "Height", value);
      }
    }

    private double width;
    public double Width
    {
      get
      {
        return width;
      }
      set
      {
        SetProperty(ref width, "Width", value);
      }
    }

    /// <summary>
    /// This is for the taskbar icon's progress indicator
    /// </summary>
    private TaskbarItemProgressState progressState = TaskbarItemProgressState.None;
    public TaskbarItemProgressState ProgressState
    {
      get
      {
        return progressState;
      }
      set
      {
        SetProperty(ref progressState, "ProgressState", value);
      }
    }

  }

}

