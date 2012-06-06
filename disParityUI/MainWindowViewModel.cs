using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using disParity;

namespace disParityUI
{

  class MainWindowViewModel : INotifyPropertyChanged
  {

    private ParitySet paritySet;
    private ObservableCollection<DataDriveViewModel> drives = new ObservableCollection<DataDriveViewModel>();
    private int runningScans;
    private int runningUpdates;
    private DataDriveViewModel recoverDrive; // current drive being recovered, if any

    public event PropertyChangedEventHandler PropertyChanged;

    public MainWindowViewModel()
    {
      paritySet = new ParitySet(@".\");
      foreach (DataDrive d in paritySet.Drives) {
        d.ScanCompleted += HandleScanCompleted;
        drives.Add(new DataDriveViewModel(d));
      }
      paritySet.RecoverProgress += HandleRecoverProgress;
      paritySet.RecoverComplete += HandleRecoverComplete;
    }

    public void AddDrive(string path)
    {
      drives.Add(new DataDriveViewModel(paritySet.AddDrive(path)));
    }

    public void ScanAll()
    {
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
          Status = "All drives up to date.";
      }
    }

    public void UpdateAll()
    {
      Busy = true;
      Status = "Update In Progress...";
      Task.Factory.StartNew(() =>
      {
        paritySet.Update();
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
        paritySet.Recover(drive.DataDrive, path);
      }
      );
    }

    private void HandleRecoverProgress(object sender, RecoverProgressEventArgs args)
    {
      Progress = args.Progress;
      if (!String.IsNullOrEmpty(args.Filename))
        recoverDrive.Status = "Recovering " + args.Filename + "...";
    }

    private void HandleRecoverComplete(object sender, RecoverCompleteEventArgs args)
    {
      Status = "Recovery complete!";
      recoverDrive.Status = String.Format("{0} file{1} recovered ({2} failure{3})",
        args.Successes, args.Successes == 1 ? "" : "s", args.Failures, args.Failures == 1 ? "" : "s");
      Progress = 0;
      Busy = false;
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
        status = value;
        FirePropertyChanged("Status");
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
        progress = value;
        FirePropertyChanged("Progress");
      }
    }

    private void FirePropertyChanged(string name)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(name));
    }

  }

}

