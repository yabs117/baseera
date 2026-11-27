using System.Windows.Media.Animation;

namespace Baseera.App;

internal sealed class BlinkingEye : FrameworkElement
{
    private static readonly DependencyProperty OpennessProperty = DependencyProperty.Register(
        nameof(Openness), typeof(double), typeof(BlinkingEye), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    internal double Openness => (double)GetValue(OpennessProperty);
    internal bool IsBlinking { get; private set; }

    internal BlinkingEye()
    {
        Width = Height = 33;
        FlowDirection = FlowDirection.LeftToRight;
        IsHitTestVisible = false;
        Unloaded += (_, _) => Reset();
    }

    internal void Blink(bool allowMotion)
    {
        if (!allowMotion || IsBlinking) return;
        var motion = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        motion.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, TimeSpan.Zero));
        motion.KeyFrames.Add(new SplineDoubleKeyFrame(.09, TimeSpan.FromMilliseconds(155), new KeySpline(.37, 0, .63, 1)));
        motion.KeyFrames.Add(new DiscreteDoubleKeyFrame(.09, TimeSpan.FromMilliseconds(180)));
        motion.KeyFrames.Add(new SplineDoubleKeyFrame(1, TimeSpan.FromMilliseconds(380), new KeySpline(.37, 0, .63, 1)));
        motion.Completed += (_, _) => Reset();
        IsBlinking = true;
        BeginAnimation(OpennessProperty, motion);
    }

    private void Reset()
    {
        IsBlinking = false;
        BeginAnimation(OpennessProperty, null);
    }

    protected override void OnRender(DrawingContext context)
    {
        context.PushTransform(new ScaleTransform(RenderSize.Width / 32, RenderSize.Height / 32));
        Brand.DrawEye(context, Openness);
        context.Pop();
    }
}
