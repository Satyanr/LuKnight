using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace LuKnight.Views;

public enum CharacterState
{
    Idle,
    Walk,
    Sleep
}

public partial class CharacterView : UserControl
{
    private Storyboard? _currentStoryboard;

    public CharacterState CurrentState { get; private set; }
        = CharacterState.Idle;

    public CharacterView()
    {
        InitializeComponent();
    }

    public void SetFacingDirection(int direction)
    {
        BodyScale.ScaleX =
            direction < 0
                ? -1
                : 1;
    }

    private void CharacterView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        SetState(CharacterState.Idle);
    }

    public void SetState(CharacterState state)
    {
        StopCurrentAnimation();

        CurrentState = state;

        ResetVisualState();

        switch (state)
        {
            case CharacterState.Idle:
                StartStoryboard("IdleStoryboard");
                break;

            case CharacterState.Walk:
                StartStoryboard("WalkStoryboard");
                break;

            case CharacterState.Sleep:
                AwakeEyes.Visibility = Visibility.Collapsed;
                SleepEyes.Visibility = Visibility.Visible;
                SleepText.Visibility = Visibility.Visible;

                StartStoryboard("SleepStoryboard");
                break;
        }
    }

    private void StartStoryboard(string resourceName)
    {
        if (FindResource(resourceName) is Storyboard storyboard)
        {
            _currentStoryboard = storyboard;

            storyboard.Begin(
                this,
                HandoffBehavior.SnapshotAndReplace,
                true);
        }
    }

    private void StopCurrentAnimation()
    {
        if (_currentStoryboard is null)
            return;

        _currentStoryboard.Remove(this);
        _currentStoryboard = null;
    }

    private void ResetVisualState()
    {
        AwakeEyes.Visibility = Visibility.Visible;
        SleepEyes.Visibility = Visibility.Collapsed;

        SleepText.Visibility = Visibility.Collapsed;
        SleepText.Opacity = 0;

        BodyScale.ScaleX = 1;
        BodyScale.ScaleY = 1;

        BodyTranslate.X = 0;
        BodyTranslate.Y = 0;

        BodyRotate.Angle = 0;

        LeftFootRotate.Angle = 0;
        RightFootRotate.Angle = 0;
    }

    private void CharacterView_MouseRightButtonDown(
    object sender,
    MouseButtonEventArgs e)
    {
        // Reserved untuk context menu Lu-Knight.
        e.Handled = true;
    }
}