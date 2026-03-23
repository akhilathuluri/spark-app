using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Spark.Services;

public static class WindowGpuAnimationService
{
    public static void ResetOpenState(FrameworkElement root, ScaleTransform scale, double fromScale, TranslateTransform? translate = null, double fromY = 0)
    {
        root.BeginAnimation(UIElement.OpacityProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        root.Opacity = 0;
        scale.ScaleX = fromScale;
        scale.ScaleY = fromScale;

        if (translate != null)
        {
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.Y = fromY;
        }
    }

    public static void AnimateOpen(FrameworkElement root, ScaleTransform scale, double fromScale, TranslateTransform? translate = null, double fromY = 0, int durationMs = 200)
    {
        var dur = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        root.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, dur) { EasingFunction = ease });

        var scaleAnim = new DoubleAnimation(fromScale, 1.0, dur) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

        if (translate != null)
        {
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(fromY, 0, dur) { EasingFunction = ease });
        }
    }
}
