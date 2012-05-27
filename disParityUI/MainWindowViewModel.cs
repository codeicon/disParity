using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.ComponentModel;
using disParity;

namespace disParityUI
{

  class MainWindowViewModel : INotifyPropertyChanged
  {

    private ParitySet paritySet;
    private ObservableCollection<DataDriveViewModel> drives = new ObservableCollection<DataDriveViewModel>();

    public event PropertyChangedEventHandler PropertyChanged;

    public MainWindowViewModel()
    {
      paritySet = new ParitySet(@"C:\projects\disParity\bin\Debug\");
      foreach (DataDrive d in paritySet.Drives)
        drives.Add(new DataDriveViewModel(d));
    }

    public void AddDrive(string path)
    {
      drives.Add(new DataDriveViewModel(paritySet.AddDrive(path)));
    }

    public void ScanDrive(DataDriveViewModel drive)
    {
      drive.Scan();
    }

    public void ScanAll()
    {
      foreach (DataDriveViewModel vm in drives)
        ScanDrive(vm);
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

    private void FirePropertyChanged(string name)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(name));
    }

  }

}

