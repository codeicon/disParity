using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;

namespace disParityUI
{

  internal static class Icons
  {

    public static ImageSource Good = new ImageSourceConverter().ConvertFromString("pack://application:,,,/Icons/StatusGood.ico") as ImageSource;

    public static ImageSource Caution = new ImageSourceConverter().ConvertFromString("pack://application:,,,/Icons/StatusCaution.ico") as ImageSource;

    public static ImageSource Unknown = new ImageSourceConverter().ConvertFromString("pack://application:,,,/Icons/StatusUnknown.ico") as ImageSource;

    public static ImageSource Urgent = new ImageSourceConverter().ConvertFromString("pack://application:,,,/Icons/StatusUrgent.ico") as ImageSource;

  }

}
