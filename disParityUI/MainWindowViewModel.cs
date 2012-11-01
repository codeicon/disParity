using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows.Shell; // for TaskbarItem stuff
using disParity;

namespace disParityUI
{

  class MainWindowViewModel : INotifyPropertyChanged
  {

    private ParitySet paritySet;
    private ObservableCollection<DataDriveViewModel> drives = new ObservableCollection<DataDriveViewModel>();
    private int runningScans;
    private DataDriveViewModel recoverDrive; // current drive being recovered, if any

    public event PropertyChangedEventHandler PropertyChanged;

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
      foreach (DataDrive d in paritySet.Drives) {
        d.ScanCompleted += HandleScanCompleted;
        drives.Add(new DataDriveViewModel(d));
      }
      paritySet.RecoverProgress += HandleRecoverProgress;
      paritySet.UpdateProgress += HandleUpdateProgress;
    }

    public void AddDrive(string path)
    {
      drives.Add(new DataDriveViewModel(paritySet.AddDrive(path)));
    }

    public void ScanAll()
    {
      if (drives.Count == 0) {
        Status = "No drives added.  Add a new data drive by pressing 'Add Drive...'";
        return;
      }
      Busy = true;
      Status = "Scanning drives...";
      runningScans = drives.Count;
      foreach (DataDriveViewModel vm in drives)
        vm.Scan();
    }

    public void ScanDrive(DataDriveViewModel drive)
    {
      Task.Factory.StartNew(() =>
      {
        drive.Scan();
      }
      );
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
          Status = "Changes detected.  Update required.";
        else
          DisplayUpToDateStatus();
      }
    }

    private void DisplayUpToDateStatus()
    {
      long totalSize = 0;
      int totalFiles = 1000;
      foreach (DataDriveViewModel vm in drives) {
        totalSize += vm.DataDrive.TotalFileSize;
        totalFiles += vm.DataDrive.FileCount;
      }
      Status = String.Format("{1:N0} files ({0}) protected.  All drives up to date.",
        Utils.SmartSize(totalSize), totalFiles);
    }

    public void UpdateAll()
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

    public string ParityPath
    {
      get
      {
        return paritySet.ParityPath;
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
        busy = value;
        FirePropertyChanged("Busy");
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
        if (status != value) {
          status = value;
          FirePropertyChanged("Status");
        }
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
        if (progress != value) {
          progress = value;
          FirePropertyChanged("Progress");
        }
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
        if (value != progressState) {
          progressState = value;
          FirePropertyChanged("ProgressState");
        }
      }
    }

    private void FirePropertyChanged(string name)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(name));
    }

  }

}

