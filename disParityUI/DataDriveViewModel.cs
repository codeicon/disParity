using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;
using System.Windows.Input;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace disParityUI
{

  class DataDriveViewModel : INotifyPropertyChanged
  {

    private DispatcherTimer updateStatusTimer;

    private static Brush cautionBrush = new SolidColorBrush(Color.FromRgb(255, 210, 0));
    private static ImageSource statusGood = new ImageSourceConverter().ConvertFromString("pack://application:,,,/StatusGood.ico") as ImageSource;
    private static ImageSource statusCaution = new ImageSourceConverter().ConvertFromString("pack://application:,,,/StatusCaution.ico") as ImageSource;
    private static ImageSource statusUnknown = new ImageSourceConverter().ConvertFromString("pack://application:,,,/StatusUnknown.ico") as ImageSource;
    private static ImageSource statusUrgent = new ImageSourceConverter().ConvertFromString("pack://application:,,,/StatusUrgent.ico") as ImageSource;

    public event PropertyChangedEventHandler PropertyChanged;

    public DataDriveViewModel(DataDrive dataDrive)
    {
      DataDrive = dataDrive;
      DataDrive.ProgressReport += HandleProgressReport;
      DataDrive.StatusChanged += HandleStatusChanged;
      DataDrive.UpdateProgress += HandleUpdateProgress;
      DataDrive.ReadingFile += HandleReadingFile;
      UpdateStatus();
      FileCount = String.Format("{0} ({1})", DataDrive.FileCount, Utils.SmartSize(DataDrive.TotalFileSize));
      updateStatusTimer = new DispatcherTimer();
      updateStatusTimer.Interval = TimeSpan.FromSeconds(1);
      updateStatusTimer.Tick += HandleUpdateStatusTimerTick;
      updateStatusTimer.Stop();
    }

    public void Scan()
    {
      Task.Factory.StartNew(() =>
      {
        try {
          DataDrive.Scan();
        }
        catch {
          // TODO: log exception here?
        }
      }
      );      
    }

    public DataDrive DataDrive { get; private set; }

    private void HandleUpdateStatusTimerTick(object sender, EventArgs args)
    {
      UpdateStatus();
      updateStatusTimer.Stop();
    }

    private void HandleReadingFile(object sender, ReadingFileEventArgs args)
    {
      Status = "Reading " + args.Filename;
      updateStatusTimer.Start();
    }

    private void HandleProgressReport(object sender, ProgressReportEventArgs args)
    {
      if (!String.IsNullOrEmpty(args.Status))
        Status = args.Status;
      Progress = args.Progress;
    }

    private void HandleStatusChanged(object sender, StatusChangedEventArgs args)
    {
      if (args.Status == DriveStatus.UpdateRequired) {
        Status = String.Format("Update Required ({0} new, {1} deleted, {2} moved)",
          args.AddCount, args.DeleteCount, args.MoveCount);
        if (args.DeleteCount > 0 || args.MoveCount > 0)
          StatusIcon = statusUrgent;
        else
          StatusIcon = statusCaution;
      } 
      else
        UpdateStatus();
      FileCount = String.Format("{0} ({1})", DataDrive.FileCount,
        Utils.SmartSize(DataDrive.TotalFileSize));
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
        string volumeLabel = DataDrive.VolumeLabel;
        if (String.IsNullOrEmpty(volumeLabel))
          return DataDrive.Root;
        else
          return String.Format("{0} ({1})", DataDrive.Root, volumeLabel);
      }
    }

    private ImageSource statusIcon;
    public ImageSource StatusIcon
    {
      get 
      { 
        return statusIcon; 
      }
      set
      {
        if (statusIcon != value) 
        {
          statusIcon = value;
          FirePropertyChanged("StatusIcon");
        }
      }
    }

    public string AdditionalInfo
    {
      get
      {
        if (DataDrive.DriveType == DriveType.Network)
          return "Network drive";
        else
          return String.Format("{0} used {1} free",
            Utils.SmartSize(DataDrive.TotalSpace - DataDrive.FreeSpace),
            Utils.SmartSize(DataDrive.FreeSpace));
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
        if (fileCount != value) {
          fileCount = value;
          FirePropertyChanged("FileCount");
        }
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


    private Brush statusColor = Brushes.Black;
    public Brush StatusColor
    {
      get
      {
        return statusColor;
      }
      set
      {
        if (statusColor != value) {
          statusColor = value;
          FirePropertyChanged("StatusColor");
        }
      }
    }

    public bool NeedsUpdate
    {
      get
      {
        return (DataDrive.Status == DriveStatus.UpdateRequired);
      }
    }

    public void UpdateStatus()
    {
      switch (DataDrive.Status) {
        case DriveStatus.ScanRequired:
          Status = "Unknown (scan required)";
          //StatusColor = Brushes.Red;
          StatusIcon = statusUnknown;
          break;
        case DriveStatus.UpdateRequired:
          Status = "Update required";
          //StatusColor = cautionBrush;
          StatusIcon = statusCaution;
          break;
        case DriveStatus.UpToDate:
          Status = "Up to date";
          //StatusColor = Brushes.Green;
          StatusIcon = statusGood;
          break;
        case DriveStatus.AccessError:
          Status = "Error: " + DataDrive.LastError;
          //StatusColor = Brushes.Red;
          StatusIcon = statusUrgent;
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
