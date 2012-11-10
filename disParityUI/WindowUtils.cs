using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;
using System.Windows;
using System.Windows.Interop;

namespace disParityUI
{

  internal static class WindowUtils
  {

    public static void RemoveCloseButton(Window window)
    {
      var hwnd = new WindowInteropHelper(window).Handle;
      Win32.SetWindowLong(hwnd, Win32.GWL_STYLE, Win32.GetWindowLong(hwnd, Win32.GWL_STYLE) & ~Win32.WS_SYSMENU);
    }

  }

}
