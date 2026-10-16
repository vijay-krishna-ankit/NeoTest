using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.VisualStudio.PlatformUI;

namespace CodeCoverage
{
    internal static class VisualStudioTheme
    {
        // Dynamic resources are refreshed by Visual Studio's color service when the user changes
        // the IDE theme or enables High Contrast.
        public static void ApplyToolWindowSurface(Panel panel)
        {
            panel.SetResourceReference(Panel.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        }

        public static void ApplyToolWindowControl(Control control)
        {
            control.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            control.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            control.SetResourceReference(Control.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
        }

        public static void ApplyToolWindowText(TextBlock text)
            => text.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
    }
}
