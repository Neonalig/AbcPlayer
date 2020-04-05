using System;
using System.Windows;

namespace AbcPlayerApp {
    public static class Extensions {
        public static Window Restart(this Window window) {
            dynamic newWindow = Activator.CreateInstance(window.GetType());
            window.Close();
            newWindow.Show();
            return newWindow;
        }
    }
}
