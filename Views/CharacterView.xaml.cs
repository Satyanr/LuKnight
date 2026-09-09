using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;

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
    private int _facingDirection = 1;

    private double _eyeOffsetX;
    private double _eyeOffsetY;

    private bool _cursorTracking;

    public CharacterState CurrentState { get; private set; }
        = CharacterState.Idle;

    public CharacterView()
    {
        InitializeComponent();
    }

    public void SetFacingDirection(int direction)
    {
        _facingDirection =
            direction < 0
                ? -1
                : 1;

        BodyScale.ScaleX =
            _facingDirection;
    }
    public void Blink()
    {
        if (CurrentState == CharacterState.Sleep)
            return;

        PlayTransientStoryboard("BlinkStoryboard");
    }


    public void TwitchEars()
    {
        if (CurrentState == CharacterState.Sleep)
            return;

        PlayTransientStoryboard("EarTwitchStoryboard");
    }


    public void LookSide(int direction)
    {
        if (CurrentState == CharacterState.Sleep)
            return;

        _cursorTracking = false;

        _eyeOffsetX = 0;
        _eyeOffsetY = 0;

        double screenTargetX =
            direction < 0
                ? -7
                : 7;

        double targetX =
            screenTargetX *
            _facingDirection;

        var animation =
            new DoubleAnimationUsingKeyFrames
            {
                FillBehavior = FillBehavior.Stop
            };

        // posisi awal
        animation.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.Zero)));

        // melirik
        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                targetX,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(140))));

        // tahan pandangan
        animation.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                targetX,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(750))));

        // kembali tengah
        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(950))));

        EyeLookTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            animation);
    }

    public void TrackCursor(
    double horizontal,
    double vertical)
    {
        if (CurrentState == CharacterState.Sleep)
            return;

        _cursorTracking = true;

        // Hentikan animasi LookSide jika sedang berjalan.
        EyeLookTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            null);

        EyeLookTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            null);

        double screenTargetX =
            Math.Clamp(
                horizontal,
                -1,
                1) * 7;

        double targetY =
            Math.Clamp(
                vertical,
                -1,
                1) * 5;

        // Karena seluruh karakter bisa di-mirror,
        // arah X lokal harus disesuaikan.
        double localTargetX =
            screenTargetX *
            _facingDirection;

        // Smooth following.
        _eyeOffsetX +=
            (localTargetX - _eyeOffsetX)
            * 0.28;

        _eyeOffsetY +=
            (targetY - _eyeOffsetY)
            * 0.28;

        EyeLookTranslate.X =
            _eyeOffsetX;

        EyeLookTranslate.Y =
            _eyeOffsetY;
    }

    public void RelaxCursorLook()
    {
        if (!_cursorTracking)
            return;

        if (CurrentState == CharacterState.Sleep)
        {
            _cursorTracking = false;
            return;
        }

        EyeLookTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            null);

        EyeLookTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            null);

        _eyeOffsetX +=
            (0 - _eyeOffsetX) * 0.20;

        _eyeOffsetY +=
            (0 - _eyeOffsetY) * 0.20;

        if (Math.Abs(_eyeOffsetX) < 0.08 &&
            Math.Abs(_eyeOffsetY) < 0.08)
        {
            _eyeOffsetX = 0;
            _eyeOffsetY = 0;
            _cursorTracking = false;
        }

        EyeLookTranslate.X =
            _eyeOffsetX;

        EyeLookTranslate.Y =
            _eyeOffsetY;
    }

    private void PlayTransientStoryboard(
        string resourceName)
    {
        if (FindResource(resourceName)
            is not Storyboard storyboard)
        {
            return;
        }

        storyboard.Begin(
            this,
            HandoffBehavior.Compose,
            false);
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

        EyeBlinkScale.ScaleX = 1;
        EyeBlinkScale.ScaleY = 1;

        EyeLookTranslate.X = 0;
        EyeLookTranslate.Y = 0;

        _eyeOffsetX = 0;
        _eyeOffsetY = 0;
        _cursorTracking = false;

        LeftEarRotate.Angle = -24;
        RightEarRotate.Angle = 27;

        LeftArmRotate.Angle = -18;
        RightArmRotate.Angle = 18;

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