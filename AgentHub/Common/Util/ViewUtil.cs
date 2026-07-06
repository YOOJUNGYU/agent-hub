using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AgentHub.Common.Util
{
    public static class ViewUtil
    {
        public static bool IsLocationInWorkingArea(Point location, int width, int height)
        {
            const int tolerance = 10;
            var leftX = location.X + tolerance;
            var topY = location.Y + tolerance;
            var rightX = location.X + width - tolerance;
            var bottomY = location.Y + height - tolerance;

            var points = new List<Point>
            {
                new Point(leftX, topY),
                new Point(rightX, topY),
                new Point(leftX, bottomY),
                new Point(rightX,bottomY)
            };

            var isLocationInWorkingArea = true;
            foreach (var screen in Screen.AllScreens)
            {
                isLocationInWorkingArea = true;
                foreach (var point in points)
                {
                    if (!screen.WorkingArea.Contains(point))
                        isLocationInWorkingArea = false;
                }
                if (isLocationInWorkingArea) break;
            }

            return isLocationInWorkingArea;
        }
    }
}
