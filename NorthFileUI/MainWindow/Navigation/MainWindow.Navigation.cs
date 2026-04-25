using NorthFileUI.Commands;
using NorthFileUI.Interop;
using NorthFileUI.Services;
using NorthFileUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private static string DescribeUsnCapability(RustUsnCapability c)
        {
            if (c.error_code != 0)
            {
                return SF("UsnCapabilityError", c.error_code);
            }
            if (c.available != 0)
            {
                return S("UsnCapabilityAvailable");
            }
            if (c.is_ntfs_local == 0)
            {
                return S("UsnCapabilityNotNtfs");
            }
            if (c.access_denied != 0)
            {
                return S("UsnCapabilityDenied");
            }
            return S("UsnCapabilityUnavailable");
        }

        private static string DescribeSourceDetail(byte sourceKind, RustUsnCapability c)
        {
            if (sourceKind != 1)
            {
                return string.Empty;
            }
            if (c.error_code != 0)
            {
                return SF("UsnFallbackProbeError", c.error_code);
            }
            if (c.is_ntfs_local == 0)
            {
                return S("UsnFallbackNotLocalNtfs");
            }
            if (c.access_denied != 0)
            {
                return S("UsnFallbackAccessDenied");
            }
            if (c.available != 0)
            {
                return S("UsnFallbackBatchUnavailable");
            }
            return S("UsnFallbackUnavailable");
        }

        private void UpdateNavButtonsState()
        {
            BackButton.IsEnabled = _backStack.Count > 0;
            ForwardButton.IsEnabled = _forwardStack.Count > 0;
        }

    }
}
