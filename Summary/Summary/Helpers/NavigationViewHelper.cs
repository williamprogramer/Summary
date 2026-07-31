using Summary.Views;
using System;
using System.Collections.Generic;

namespace Summary.Helpers
{
    internal static class NavigationViewHelper
    {
        public static readonly Dictionary<string, Type> Views = new()
        {
            {
                "DefaultView", typeof(DefaultView)
            },
            {
                "SettingsView", typeof(SettingsView)
            }
        };
    }
}