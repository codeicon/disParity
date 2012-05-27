using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;
using System.ComponentModel;
using System.Threading.Tasks;

namespace disParityUI
{

  class DataDriveViewModel : INotifyPropertyChanged
  {

    private DataDrive dataDrive;

    public event PropertyChangedEventHandler PropertyChanged;

    public DataDriveViewModel(DataDrive dataDrive)
    {
      this.dataDrive = dataDrive;
      dataDrive.ScanProgress += HandleScanProgress;
      dataDrive.ScanCompleted += HandleScanCompleted;
      UpdateStatus();
    }

    public void Scan()
    {
      Task.Factory.StartNew(() =>
      {
        dataDrive.Scan();
      }
      );
    }

    private void HandleScanProgress(object sender, ScanProgressEventArgs args)
    {
      if (!String.IsNullOrEmpty(args.Status))
        Status = args.Status;
      Progress = args.Progress;
    }

    private void HandleScanCompleted(object sender, ScanCompletedEventArgs args)
    {
      if (dataDrive.Status == DriveStatus.UpdateRequired) {
        Status = String.Format("Update Required ({0} new, {1} deleted, {2} moved, {3} edited)",
          args.AddCount, args.DeleteCount, args.MoveCount, args.EditCount);
        WarningLevel = "Medium";
      } 
      else
        UpdateStatus();
      Progress = 0;
    }

    public string Root
    {
      get
      {
        return dataDrive.Root;
      }
    }

    public int FileCount
    {
      get
      {
        if (dataDrive.Files == null)
          return 0;
        else
          return dataDrive.Files.Count();
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

    private void UpdateStatus()
    {
      switch (dataDrive.Status) {
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
