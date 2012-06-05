using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;
using System.Windows.Input;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace disParityUI
{

  class DataDriveViewModel : INotifyPropertyChanged
  {

    public event PropertyChangedEventHandler PropertyChanged;

    public DataDriveViewModel(DataDrive dataDrive)
    {
      DataDrive = dataDrive;
      DataDrive.ScanProgress += HandleScanProgress;
      DataDrive.StatusChanged += HandleStatusChanged;
      DataDrive.UpdateProgress += HandleUpdateProgress;
      UpdateStatus();
      FileCount = String.Format("{0} ({1})", DataDrive.FileCount, Utils.SmartSize(DataDrive.TotalSize));
    }

    public void Scan()
    {
      Task.Factory.StartNew(() =>
      {
        DataDrive.Scan();
      }
      );
    }

    public DataDrive DataDrive { get; private set; }
    
    private void HandleScanProgress(object sender, ScanProgressEventArgs args)
    {
      if (!String.IsNullOrEmpty(args.Status))
        Status = args.Status;
      Progress = args.Progress;
    }

    private void HandleStatusChanged(object sender, StatusChangedEventArgs args)
    {
      if (args.Status == DriveStatus.UpdateRequired) {
        Status = String.Format("Update Required ({0} new, {1} deleted, {2} moved",
          args.AddCount, args.DeleteCount, args.MoveCount);
        WarningLevel = "Medium";
      } 
      else
        UpdateStatus();
      FileCount = String.Format("{0} ({1})", DataDrive.FileCount,
        Utils.SmartSize(DataDrive.TotalSize));
      Progress = 0;
    }

    private void HandleUpdateProgress(object sender, UpdateProgressEventArgs args)
    {
      if (!String.IsNullOrEmpty(args.Status))
        Status = args.Status;
      FileCount = String.Format("{0} ({1})", args.Files, Utils.SmartSize(args.Size));
      Progress = args.Progress;
    }

    public string Root
    {
      get
      {
        return DataDrive.Root;
      }
    }

    private string fileCount;
    public string FileCount
    {
      get
      {
        return fileCount;
      }
      set
      {
        fileCount = value;
        FirePropertyChanged("FileCount");
      }
    }

    private string status = "Unknown";
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

    private string warningLevel;
    public string WarningLevel
    {
      get
      {
        return warningLevel;
      }
      set
      {
        warningLevel = value;
        FirePropertyChanged("WarningLevel");
      }
    }

    public bool NeedsUpdate
    {
      get
      {
        return (DataDrive.Status == DriveStatus.UpdateRequired);
      }
    }

    private void UpdateStatus()
    {
      switch (DataDrive.Status) {
        case DriveStatus.ScanRequired:
          Status = "Unknown (Scan Required)";
          WarningLevel = "High";
          break;
        case DriveStatus.UpdateRequired:
          Status = "Update Required";
          WarningLevel = "Medium";
          break;
        case DriveStatus.UpToDate:
          Status = "Up To Date";
          WarningLevel = "Low";
          break;
      }
    }

    private void FirePropertyChanged(string name)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(name));
    }
  }

}
