using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;

namespace disParityUI
{

  internal static class Icons
  {

    public static ImageSource Good = new ImageSourceConverter().ConvertFromString("pack://application:,,,/StatusGood.ico") as ImageSource;

    public static ImageSource Caution = new ImageSourceConverter().ConvertFromString("pack://application:,,,/StatusCaution.ico") as ImageSource;

    public static ImageSource Unknown = new ImageSourceConverter().ConvertFromString("pack://application:,,,/StatusUnknown.ico") as ImageSource;

    public static ImageSource Urgent = new ImageSourceConverter().ConvertFromString("pack://application:,,,/StatusUrgent.ico") as ImageSource;

  }

}
