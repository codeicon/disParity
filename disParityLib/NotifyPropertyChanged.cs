using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;

namespace disParity
{

  public abstract class NotifyPropertyChanged : INotifyPropertyChanged
  {

    public event PropertyChangedEventHandler PropertyChanged;

    protected bool SetProperty<T>(ref T property, string name, T value)
    {
      if ((property == null && value != null) || (property != null && !property.Equals(value)))
      {
        property = value;
        FirePropertyChanged(name);
        return true;
      }
      return false;
    }

    protected void FirePropertyChanged(string name)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(name));
    }

  }

}
