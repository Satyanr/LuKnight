using System;
using LuKnight.Visuals;
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
    Sleep,

    Grabbed,
    Falling,

    Hanging,
    Climbing
}

public enum CharacterMood
{
    Neutral,
    Curious,
    Happy,
    Surprised,
    Thinking,
    Confused,
    Dizzy,
    Sad,
    Angry,
    Determined,
    Wink
}

public partial class CharacterView : UserControl
{


    private Storyboard? _currentStoryboard;
    private int _facingDirection = 1;

    private double _eyeOffsetX;
    private double _eyeOffsetY;

    private bool _cursorTracking;

    private Storyboard? _moodStoryboard;

    private void StartMoodStoryboard(
    string resourceName)
    {
        if (FindResource(resourceName)
            is not Storyboard storyboard)
        {
            return;
        }

        _moodStoryboard = storyboard;

        storyboard.Begin(
            this,
            HandoffBehavior.SnapshotAndReplace,
            true);
    }


    private void StopMoodStoryboard()
    {
        if (_moodStoryboard is null)
            return;

        _moodStoryboard.Remove(this);

        _moodStoryboard = null;
    }

    public CharacterMood CurrentMood { get; private set; }
        = CharacterMood.Neutral;

    public CharacterState CurrentState { get; private set; }
        = CharacterState.Idle;

    public CharacterView()
    {
        InitializeComponent();

        EnsureSpritePlayer();

        RegisterDefaultSpriteClips();
        RegisterDefaultSpriteExpressions();

        SetRenderMode(
            CharacterRenderMode.Sprite);
    }

    public void PlayLandingReaction(
        double impactSpeed = 0)
    {
        if (_renderMode ==
            CharacterRenderMode.Sprite)
        {
            PlaySpriteLandingReaction(
                impactSpeed);

            return;
        }


        PlayTransientStoryboard(
            "LandingReactionStoryboard");
    }

    public void SetFacingDirection(int direction)
    {
        _facingDirection =
            direction < 0
                ? -1
                : 1;

        if (_renderMode == CharacterRenderMode.Sprite)
        {
            SpriteScale.ScaleX = _facingDirection;
            return;
        }

        BodyScale.ScaleX =
            _facingDirection;
    }
    public void Blink()
    {
        if (_renderMode == CharacterRenderMode.Sprite)
        {
            if (CurrentMood == CharacterMood.Neutral || CurrentMood == CharacterMood.Happy)
                PlaySpriteWink();
            return;
        }

        if (CurrentState == CharacterState.Sleep)
            return;

        PlayTransientStoryboard("BlinkStoryboard");
    }


    public void TwitchEars()
    {
        if (_renderMode ==
    CharacterRenderMode.Sprite)
        {
            PlaySpriteTwitch();

            return;
        }

        if (CurrentState == CharacterState.Sleep)
            return;

        PlayTransientStoryboard("EarTwitchStoryboard");
    }


    public void LookSide(int direction)
    {
        if (_renderMode ==
    CharacterRenderMode.Sprite)
        {
            PlaySpriteLookSide(
                direction);

            return;
        }

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
        if (_renderMode ==
    CharacterRenderMode.Sprite)
        {
            TrackSpriteCursor(
                horizontal,
                vertical);

            return;
        }

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
        if (_renderMode ==
    CharacterRenderMode.Sprite)
        {
            RelaxSpriteCursor();

            return;
        }

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

    public void SetMood(CharacterMood mood)
    {
        if (CurrentState == CharacterState.Sleep &&
            mood != CharacterMood.Neutral)
        {
            return;
        }

        StopMoodStoryboard();

        CurrentMood = mood;

        if (_renderMode == CharacterRenderMode.Sprite)
        {
            StopSpriteExpression();
            PlaySpriteForCurrentState();
            return;
        }

        ApplyMoodVisuals(mood);

        switch (mood)
        {
            case CharacterMood.Happy:

                PlayTransientStoryboard(
                    "HappyReactionStoryboard");

                break;


            case CharacterMood.Surprised:

                PlayTransientStoryboard(
                    "SurpriseReactionStoryboard");

                break;


            case CharacterMood.Thinking:

                StartMoodStoryboard(
                    "ThinkingStoryboard");

                break;

            case CharacterMood.Dizzy:

                StartMoodStoryboard(
                    "DizzyStoryboard");

                break;
        }
    }

    private void ApplyMoodVisuals(
        CharacterMood mood)
    {
        if (_renderMode == CharacterRenderMode.Sprite)
        {
            return;
        }

        ResetMoodVisuals();

        switch (mood)
        {
            case CharacterMood.Neutral:
                break;


            case CharacterMood.Curious:

                MoodRotate.Angle = 4;

                LeftEarRotate.Angle = -15;
                RightEarRotate.Angle = 31;

                LeftCheek.Opacity = 0.70;
                RightCheek.Opacity = 0.70;

                EmblemGlow.Opacity = 0.20;

                break;


            case CharacterMood.Happy:

                MoodScale.ScaleX = 1.03;
                MoodScale.ScaleY = 1.03;

                LeftEarRotate.Angle = -13;
                RightEarRotate.Angle = 15;

                LeftArmRotate.Angle = -38;
                RightArmRotate.Angle = 38;

                LeftCheek.Opacity = 1;
                RightCheek.Opacity = 1;

                MouthScale.ScaleX = 1.12;
                MouthScale.ScaleY = 1.08;

                EmblemGlow.Opacity = 0.55;

                break;


            case CharacterMood.Surprised:

                LeftEarRotate.Angle = -8;
                RightEarRotate.Angle = 10;

                LeftArmRotate.Angle = -48;
                RightArmRotate.Angle = 48;

                MouthScale.ScaleX = 0.72;
                MouthScale.ScaleY = 1.35;

                EmblemGlow.Opacity = 0.75;

                break;


            case CharacterMood.Thinking:

                MoodRotate.Angle = -4;

                LeftEarRotate.Angle = -17;
                RightEarRotate.Angle = 20;

                LeftArmRotate.Angle = -12;
                RightArmRotate.Angle = 12;

                MouthScale.ScaleX = 0.88;
                MouthScale.ScaleY = 0.82;

                EmblemScale.ScaleX = 1.05;
                EmblemScale.ScaleY = 1.05;

                EmblemGlow.Opacity = 0.25;

                break;


            case CharacterMood.Confused:

                MoodRotate.Angle = 6;

                LeftEarRotate.Angle = -7;
                RightEarRotate.Angle = 34;

                LeftArmRotate.Angle = -10;
                RightArmRotate.Angle = 22;

                MouthScale.ScaleX = 0.82;
                MouthScale.ScaleY = 0.75;

                LeftCheek.Opacity = 0.35;
                RightCheek.Opacity = 0.35;

                EmblemGlow.Opacity = 0.12;

                break;

            case CharacterMood.Dizzy:

                DizzyEyes.Visibility =
                    Visibility.Visible;

                MoodRotate.Angle = 5;

                LeftEarRotate.Angle = -5;
                RightEarRotate.Angle = 7;

                LeftArmRotate.Angle = 28;
                RightArmRotate.Angle = -28;

                LeftCheek.Opacity = 0.25;
                RightCheek.Opacity = 0.25;

                MouthScale.ScaleX = 0.75;
                MouthScale.ScaleY = 0.65;

                EmblemGlow.Opacity = 0.18;

                break;
        }
    }

    private void ResetMoodVisuals()
    {
        if (_renderMode == CharacterRenderMode.Sprite)
        {
            return;
        }

        DizzyEyes.Visibility =
        Visibility.Collapsed;

        DizzyEyeRotate.Angle = 0;

        DizzyEyeTranslate.X = 0;
        DizzyEyeTranslate.Y = 0;

        MoodScale.ScaleX = 1;
        MoodScale.ScaleY = 1;

        MoodRotate.Angle = 0;

        MoodTranslate.X = 0;
        MoodTranslate.Y = 0;

        LeftEarRotate.Angle = -24;
        RightEarRotate.Angle = 27;

        LeftArmRotate.Angle = -18;
        RightArmRotate.Angle = 18;

        LeftCheek.Opacity = 0.55;
        RightCheek.Opacity = 0.55;

        MouthScale.ScaleX = 1;
        MouthScale.ScaleY = 1;

        EmblemScale.ScaleX = 1;
        EmblemScale.ScaleY = 1;

        EmblemGlow.Opacity = 0;
    }

    private void CharacterView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        EnsureSpritePlayer();
        SetState(CurrentState);
        SetFacingDirection(_facingDirection);
    }

    public void SetState(CharacterState state)
    {
        StopCurrentAnimation();
        StopSpriteMotion();
        StopSpriteExpression();
        ResetSpriteAttention();
        ResetSpriteImpact();
        CurrentState = state;

        if (_preferredRenderMode == CharacterRenderMode.Sprite &&
            _spriteStateClips.ContainsKey(state))
        {
            ApplyRenderMode(CharacterRenderMode.Sprite);
            PlaySpriteForCurrentState();


            if (_renderMode ==
                CharacterRenderMode.Sprite)
            {
                PlaySpriteStateEntrance();

                StartSpriteStateMotion(
                    state);
            }


            return;
        }

        ApplyRenderMode(CharacterRenderMode.Vector);
        PlayVectorForCurrentState();
    }

    private void PlayVectorForCurrentState()
    {
        ResetVisualState();

        switch (CurrentState)
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

            case CharacterState.Grabbed:

                StartStoryboard(
                    "GrabbedStoryboard");

                break;


            case CharacterState.Falling:

                StartStoryboard(
                    "FallingStoryboard");

                break;

            case CharacterState.Hanging:

                StartStoryboard(
                    "HangingStoryboard");

                break;


            case CharacterState.Climbing:

                StartStoryboard(
                    "ClimbingStoryboard");

                break;
        }
    }

    public void LookDown()
    {
        if (_renderMode ==
            CharacterRenderMode.Sprite)
        {
            PlaySpriteLookDown();

            return;
        }

        if (CurrentState ==
            CharacterState.Sleep)
        {
            return;
        }


        _cursorTracking = false;

        _eyeOffsetX = 0;
        _eyeOffsetY = 0;


        EyeLookTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            null);

        EyeLookTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            null);


        var animation =
            new DoubleAnimationUsingKeyFrames
            {
                FillBehavior =
                    FillBehavior.Stop
            };


        animation.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.Zero)));


        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                5,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        160))));


        animation.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                5,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        700))));


        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        900))));


        EyeLookTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            animation);
    }

    public void PlayEdgePeek(
        int direction)
    {
        if (_renderMode ==
            CharacterRenderMode.Sprite)
        {
            PlaySpriteEdgePeek(
                direction);

            return;
        }

        if (CurrentState ==
            CharacterState.Sleep)
        {
            return;
        }


        LookDown();

        TwitchEars();


        double targetX =
            direction < 0
                ? -5
                : 5;


        var animation =
            new DoubleAnimation
            {
                From = 0,
                To = targetX,

                Duration =
                    TimeSpan.FromMilliseconds(
                        220),

                AutoReverse = true,

                FillBehavior =
                    FillBehavior.Stop
            };


        BodyTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            animation);
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

        BodyScale.ScaleX =
            _facingDirection;
        BodyScale.ScaleY = 1;

        BodyTranslate.X = 0;
        BodyTranslate.Y = 0;

        BodyRotate.Angle = 0;

        LeftFootRotate.Angle = 0;
        RightFootRotate.Angle = 0;

        ApplyMoodVisuals(CurrentMood);
    }

    private void CharacterView_MouseRightButtonDown(
    object sender,
    MouseButtonEventArgs e)
    {
        // Reserved untuk context menu Lu-Knight.
        e.Handled = true;
    }
}
