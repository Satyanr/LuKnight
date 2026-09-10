using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LuKnight.Visuals;
using System.Windows.Media;

namespace LuKnight.Views;

public partial class CharacterView
{
    private CharacterRenderMode _renderMode = CharacterRenderMode.Vector;
    private CharacterRenderMode _preferredRenderMode = CharacterRenderMode.Vector;
    private Storyboard? _spriteMotionStoryboard;
    private readonly Dictionary<CharacterState, SpriteAnimationClip> _spriteStateClips = new();
    private SpriteAnimationPlayer? _spritePlayer;

    private double _spriteLookX;
    private double _spriteLookY;

    private bool _spriteCursorTracking;

    private double _spriteAirAngle;
    private double _spriteAirScaleX = 1;
    private double _spriteAirScaleY = 1;

    private void PlaySpriteLandingShadow(
    double impactSpeed)
    {
        if (_renderMode !=
            CharacterRenderMode.Sprite)
        {
            return;
        }


        double strength =
            Math.Clamp(
                impactSpeed / 1000.0,
                0.15,
                1.0);


        StopSpriteShadowAnimations();


        // Base state setelah animasi selesai.
        SpriteShadow.Opacity = 0.50;

        SpriteShadowScale.ScaleX = 1;
        SpriteShadowScale.ScaleY = 1;

        SpriteShadowTranslate.Y = 0;


        var opacityAnimation =
            new DoubleAnimationUsingKeyFrames
            {
                FillBehavior =
                    FillBehavior.Stop
            };


        opacityAnimation.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                0.68 + (0.12 * strength),

                KeyTime.FromTimeSpan(
                    TimeSpan.Zero)));


        opacityAnimation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0.50,

                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        300))));


        SpriteShadow.BeginAnimation(
            UIElement.OpacityProperty,
            opacityAnimation);


        var scaleX =
            new DoubleAnimationUsingKeyFrames
            {
                FillBehavior =
                    FillBehavior.Stop
            };


        scaleX.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                0.72 +
                (0.10 * strength),

                KeyTime.FromTimeSpan(
                    TimeSpan.Zero)));


        scaleX.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1.10 +
                (0.10 * strength),

                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        85))));


        scaleX.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1,

                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        300))));


        SpriteShadowScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            scaleX);


        var scaleY =
            new DoubleAnimationUsingKeyFrames
            {
                FillBehavior =
                    FillBehavior.Stop
            };


        scaleY.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                0.70,

                KeyTime.FromTimeSpan(
                    TimeSpan.Zero)));


        scaleY.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1,

                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        300))));


        SpriteShadowScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            scaleY);
    }

    private void ResetSpriteAirMotion()
    {
        _spriteAirAngle = 0;

        _spriteAirScaleX = 1;
        _spriteAirScaleY = 1;


        SpriteAirRotate.Angle = 0;

        SpriteAirScale.ScaleX = 1;
        SpriteAirScale.ScaleY = 1;
    }

    private void UpdateSpriteAirMotion(
    double velocityX,
    double velocityY)
    {
        if (_renderMode !=
                CharacterRenderMode.Sprite ||
            CurrentState !=
                CharacterState.Falling)
        {
            return;
        }


        double horizontal =
            Math.Clamp(
                velocityX / 900.0,
                -1,
                1);


        double vertical =
            Math.Clamp(
                velocityY / 900.0,
                -1,
                1);


        // Saat naik, tubuh sedikit lebih miring.
        // Saat mulai turun, tubuh perlahan tegak lagi.
        double directionalStrength =
            vertical < 0
                ? 13.0
                : 7.0;


        double targetAngle =
            horizontal *
            directionalStrength;


        double speed =
            Math.Sqrt(
                (velocityX * velocityX) +
                (velocityY * velocityY));


        double speedFactor =
            Math.Clamp(
                speed / 1450.0,
                0,
                1);


        double targetScaleX =
            1.0 -
            (0.025 * speedFactor);


        double targetScaleY =
            1.0 +
            (0.045 * speedFactor);


        // smoothing
        _spriteAirAngle +=
            (targetAngle -
             _spriteAirAngle) *
            0.16;


        _spriteAirScaleX +=
            (targetScaleX -
             _spriteAirScaleX) *
            0.14;


        _spriteAirScaleY +=
            (targetScaleY -
             _spriteAirScaleY) *
            0.14;


        SpriteAirRotate.Angle =
            _spriteAirAngle;


        SpriteAirScale.ScaleX =
            _spriteAirScaleX;

        SpriteAirScale.ScaleY =
            _spriteAirScaleY;
    }

    private void StopSpriteAttentionAnimations()
    {
        SpriteAttentionTranslate
            .BeginAnimation(
                TranslateTransform.XProperty,
                null);

        SpriteAttentionTranslate
            .BeginAnimation(
                TranslateTransform.YProperty,
                null);

        SpriteAttentionRotate
            .BeginAnimation(
                RotateTransform.AngleProperty,
                null);
    }

    private void ResetSpriteAttention()
    {
        StopSpriteAttentionAnimations();


        _spriteLookX = 0;
        _spriteLookY = 0;

        _spriteCursorTracking = false;


        SpriteAttentionTranslate.X = 0;
        SpriteAttentionTranslate.Y = 0;

        SpriteAttentionRotate.Angle = 0;
    }

    private void TrackSpriteCursor(
    double horizontal,
    double vertical)
    {
        StopSpriteAttentionAnimations();


        _spriteCursorTracking = true;


        double targetX =
            Math.Clamp(
                horizontal,
                -1,
                1) * 3.2;


        double targetY =
            Math.Clamp(
                vertical,
                -1,
                1) * 2.2;


        _spriteLookX +=
            (targetX -
             _spriteLookX) * 0.22;


        _spriteLookY +=
            (targetY -
             _spriteLookY) * 0.22;


        SpriteAttentionTranslate.X =
            _spriteLookX;

        SpriteAttentionTranslate.Y =
            _spriteLookY;


        SpriteAttentionRotate.Angle =
            Math.Clamp(
                horizontal,
                -1,
                1) * 1.1;
    }

    public void SetAirborneVelocity(
        double velocityX,
        double velocityY)
    {
        UpdateSpriteAirMotion(
            velocityX,
            velocityY);
    }
    private void StartSpriteStateMotion(
    CharacterState state)
    {
        switch (state)
        {
            case CharacterState.Idle:

                StartSpriteMotion(
                    "SpriteIdleStoryboard");

                break;


            case CharacterState.Walk:

                StartSpriteMotion(
                    "SpriteWalkStoryboard");

                break;


            case CharacterState.Sleep:

                StartSpriteMotion(
                    "SpriteSleepStoryboard");

                break;


            case CharacterState.Grabbed:

                StartSpriteMotion(
                    "SpriteGrabbedStoryboard");

                break;


            case CharacterState.Falling:

                StartSpriteMotion(
                    "SpriteFallingStoryboard");

                break;


            case CharacterState.Hanging:

                StartSpriteMotion(
                    "SpriteHangingStoryboard");

                break;


            case CharacterState.Climbing:

                StartSpriteMotion(
                    "SpriteClimbingStoryboard");

                break;


            default:

                StopSpriteMotion();

                break;
        }
    }

    private void PlaySpriteStateEntrance()
    {
        if (_renderMode !=
            CharacterRenderMode.Sprite)
        {
            return;
        }


        SpriteImpactRoot
            .BeginAnimation(
                UIElement.OpacityProperty,
                null);


        SpriteImpactRoot.Opacity =
            0.72;


        var animation =
            new DoubleAnimation
            {
                From = 0.72,
                To = 1.0,

                Duration =
                    TimeSpan.FromMilliseconds(
                        110),

                FillBehavior =
                    FillBehavior.Stop
            };


        SpriteImpactRoot
            .BeginAnimation(
                UIElement.OpacityProperty,
                animation);
    }

    private void RelaxSpriteCursor()
    {
        if (!_spriteCursorTracking)
            return;


        StopSpriteAttentionAnimations();


        _spriteLookX +=
            (0 - _spriteLookX) *
            0.18;


        _spriteLookY +=
            (0 - _spriteLookY) *
            0.18;


        SpriteAttentionTranslate.X =
            _spriteLookX;

        SpriteAttentionTranslate.Y =
            _spriteLookY;


        SpriteAttentionRotate.Angle *=
            0.82;


        if (Math.Abs(
                _spriteLookX) < 0.05 &&
            Math.Abs(
                _spriteLookY) < 0.05 &&
            Math.Abs(
                SpriteAttentionRotate.Angle)
                < 0.05)
        {
            ResetSpriteAttention();
        }
    }

    private void PlaySpriteLookSide(
        int direction)
    {
        ResetSpriteAttention();


        double targetX =
            direction < 0
                ? -3.5
                : 3.5;


        double targetAngle =
            direction < 0
                ? -1.4
                : 1.4;


        var move =
            new DoubleAnimationUsingKeyFrames
            {
                FillBehavior =
                    FillBehavior.Stop
            };


        move.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.Zero)));


        move.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                targetX,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        150))));


        move.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                targetX,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        700))));


        move.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        900))));


        SpriteAttentionTranslate
            .BeginAnimation(
                TranslateTransform.XProperty,
                move);


        var rotate =
            new DoubleAnimation
            {
                From = 0,
                To = targetAngle,

                Duration =
                    TimeSpan.FromMilliseconds(
                        180),

                AutoReverse = true,

                FillBehavior =
                    FillBehavior.Stop
            };


        SpriteAttentionRotate
            .BeginAnimation(
                RotateTransform.AngleProperty,
                rotate);
    }

    private void PlaySpriteLookDown()
    {
        ResetSpriteAttention();


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
                2.8,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        160))));


        animation.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                2.8,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        700))));


        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        900))));


        SpriteAttentionTranslate
            .BeginAnimation(
                TranslateTransform.YProperty,
                animation);
    }

    private void PlaySpriteEdgePeek(
        int direction)
    {
        ResetSpriteAttention();


        double targetX =
            direction < 0
                ? -4.5
                : 4.5;


        double targetAngle =
            direction < 0
                ? -2.0
                : 2.0;


        var xAnimation =
            new DoubleAnimation
            {
                From = 0,
                To = targetX,

                Duration =
                    TimeSpan.FromMilliseconds(
                        240),

                AutoReverse = true,

                FillBehavior =
                    FillBehavior.Stop
            };


        SpriteAttentionTranslate
            .BeginAnimation(
                TranslateTransform.XProperty,
                xAnimation);


        var yAnimation =
            new DoubleAnimation
            {
                From = 0,
                To = 2.2,

                Duration =
                    TimeSpan.FromMilliseconds(
                        240),

                AutoReverse = true,

                FillBehavior =
                    FillBehavior.Stop
            };


        SpriteAttentionTranslate
            .BeginAnimation(
                TranslateTransform.YProperty,
                yAnimation);


        var rotateAnimation =
            new DoubleAnimation
            {
                From = 0,
                To = targetAngle,

                Duration =
                    TimeSpan.FromMilliseconds(
                        240),

                AutoReverse = true,

                FillBehavior =
                    FillBehavior.Stop
            };


        SpriteAttentionRotate
            .BeginAnimation(
                RotateTransform.AngleProperty,
                rotateAnimation);
    }

    private void StopSpriteImpactAnimations()
    {
        SpriteImpactScale
            .BeginAnimation(
                ScaleTransform.ScaleXProperty,
                null);

        SpriteImpactRoot
            .BeginAnimation(
            UIElement.OpacityProperty,
            null);

        SpriteImpactScale
            .BeginAnimation(
                ScaleTransform.ScaleYProperty,
                null);


        SpriteImpactTranslate
            .BeginAnimation(
                TranslateTransform.YProperty,
                null);


        SpriteImpactRotate
            .BeginAnimation(
                RotateTransform.AngleProperty,
                null);
    }

    private void ResetSpriteImpact()
    {
        StopSpriteImpactAnimations();


        SpriteImpactScale.ScaleX = 1;
        SpriteImpactScale.ScaleY = 1;

        SpriteImpactTranslate.X = 0;
        SpriteImpactTranslate.Y = 0;

        SpriteImpactRotate.Angle = 0;

        SpriteImpactRoot.Opacity = 1;
    }

    private void PlaySpriteLandingReaction(
    double impactSpeed)
    {
        if (_renderMode !=
            CharacterRenderMode.Sprite)
        {
            return;
        }


        ResetSpriteImpact();

        PlaySpriteLandingShadow(
    impactSpeed);


        double strength =
            Math.Clamp(
                (impactSpeed - 120.0) /
                900.0,
                0.15,
                1.0);


        double squashY =
            0.94 -
            (0.14 * strength);


        double stretchX =
            1.03 +
            (0.09 * strength);


        double reboundY =
            1.02 +
            (0.04 * strength);


        double dropY =
            2.0 +
            (4.0 * strength);


        double reboundOffsetY =
            -1.5 -
            (3.5 * strength);


        // =========================
        // SCALE Y
        // =========================

        var scaleY =
            new DoubleAnimationUsingKeyFrames
            {
                FillBehavior =
                    FillBehavior.Stop
            };


        scaleY.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(
                    TimeSpan.Zero)));


        scaleY.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                squashY,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        70))));


        scaleY.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                reboundY,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        180))));


        scaleY.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        380))));


        SpriteImpactScale
            .BeginAnimation(
                ScaleTransform.ScaleYProperty,
                scaleY);


        // =========================
        // SCALE X
        // =========================

        var scaleX =
            new DoubleAnimationUsingKeyFrames
            {
                FillBehavior =
                    FillBehavior.Stop
            };


        scaleX.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(
                    TimeSpan.Zero)));


        scaleX.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                stretchX,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        70))));


        scaleX.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0.98,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        180))));


        scaleX.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        380))));


        SpriteImpactScale
            .BeginAnimation(
                ScaleTransform.ScaleXProperty,
                scaleX);


        // =========================
        // BOUNCE Y
        // =========================

        var translateY =
            new DoubleAnimationUsingKeyFrames
            {
                FillBehavior =
                    FillBehavior.Stop
            };


        translateY.KeyFrames.Add(
            new LinearDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.Zero)));


        translateY.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                dropY,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        70))));


        translateY.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                reboundOffsetY,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        180))));


        translateY.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        380))));


        SpriteImpactTranslate
            .BeginAnimation(
                TranslateTransform.YProperty,
                translateY);
    }

    private void PlaySpriteTwitch()
    {
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
                -1.4,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        70))));


        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1.1,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        145))));


        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(
                        230))));


        SpriteAttentionRotate
            .BeginAnimation(
                RotateTransform.AngleProperty,
                animation);
    }

    public CharacterRenderMode RenderMode => _renderMode;

    private void RegisterSpriteFolder(
    CharacterState state,
    string name,
    string relativeFolder,
    double framesPerSecond)
    {
        SpriteAnimationClip? clip =
            SpriteClipFactory.FromFolder(
                name,
                relativeFolder,
                framesPerSecond,
                loop: true);


        if (clip is null)
        {
            return;
        }


        RegisterSpriteClip(
            state,
            clip);
    }

    private void RegisterDefaultSpriteClips()
    {
        RegisterSpriteFolder(
            CharacterState.Idle,
            "idle",
            "Assets/Characters/LuKnight/Idle",
            5);


        RegisterSpriteFolder(
            CharacterState.Walk,
            "walk",
            "Assets/Characters/LuKnight/Walk",
            7);


        RegisterSpriteFolder(
            CharacterState.Sleep,
            "sleep",
            "Assets/Characters/LuKnight/Sleep",
            3);


        RegisterSpriteClip(
            CharacterState.Grabbed,
            new SpriteAnimationClip(
            "grabbed-dangling",
            new[]
            {
                "Assets/Characters/LuKnight/Grabbed/grabbed_000.png"
            },
            framesPerSecond: 1,
            loop: true));


        RegisterSpriteFolder(
            CharacterState.Falling,
            "falling",
            "Assets/Characters/LuKnight/Falling",
            7);


        RegisterSpriteFolder(
            CharacterState.Hanging,
            "hanging",
            "Assets/Characters/LuKnight/Hanging",
            5);


        RegisterSpriteFolder(
            CharacterState.Climbing,
            "climbing",
            "Assets/Characters/LuKnight/Climbing",
            7);
    }

    private void EnsureSpritePlayer()
    {
        if (_spritePlayer is not null)
            return;

        _spritePlayer = new SpriteAnimationPlayer(SpriteImage);
        _spritePlayer.FrameLoadFailed += SpritePlayer_FrameLoadFailed;
    }

    private void SpritePlayer_FrameLoadFailed()
    {
        ApplyRenderMode(CharacterRenderMode.Vector);
        PlayVectorForCurrentState();
    }

    public void SetRenderMode(CharacterRenderMode mode)
    {
        _preferredRenderMode = mode;
        SetState(CurrentState);
    }

    private void ApplyRenderMode(CharacterRenderMode mode)
    {
        bool changed = _renderMode != mode;
        _renderMode = mode;
        bool useSprite = mode == CharacterRenderMode.Sprite;

        SpriteLayer.Visibility = useSprite ? Visibility.Visible : Visibility.Collapsed;
        VectorLayer.Visibility = useSprite ? Visibility.Collapsed : Visibility.Visible;

        if (!useSprite)
        {
            _spritePlayer?.Stop();
            StopSpriteMotion();
            StopSpriteExpression();
            if (changed)
            {
                SetFacingDirection(_facingDirection);
            }
            return;
        }

        EnsureSpritePlayer();
        StopCurrentAnimation();
        StopMoodStoryboard();
        SetFacingDirection(_facingDirection);
    }

    public void RegisterSpriteClip(CharacterState state, SpriteAnimationClip clip)
    {
        _spriteStateClips[state] = clip;
        if (_preferredRenderMode == CharacterRenderMode.Sprite && CurrentState == state)
        {
            EnsureSpritePlayer();
            _spritePlayer?.Stop();
            SetState(CurrentState);
        }
    }

    private void PlaySpriteForCurrentState()
    {
        if (_renderMode != CharacterRenderMode.Sprite)
            return;

        EnsureSpritePlayer();
        if (!_spriteStateClips.TryGetValue(CurrentState, out SpriteAnimationClip? clip))
        {
            ApplyRenderMode(CharacterRenderMode.Vector);
            PlayVectorForCurrentState();
            return;
        }

        // Expression PNGs contain a whole body: physical action clips take priority.
        if (CurrentState == CharacterState.Idle &&
            _spriteMoodClips.TryGetValue(CurrentMood, out SpriteAnimationClip? expression))
        {
            clip = expression;
        }
        _spritePlayer?.Play(clip);
    }

    private void CharacterView_Unloaded(object sender, RoutedEventArgs e)
    {
        StopSpriteMotion();
        StopSpriteExpression();
        ResetSpriteImpact();
        ResetSpriteShadow();
        if (_spriteExpressionTimer is not null)
        {
            _spriteExpressionTimer.Tick -= SpriteExpressionTimer_Tick;
            _spriteExpressionTimer = null;
        }
        if (_spritePlayer is null)
            return;

        _spritePlayer.FrameLoadFailed -= SpritePlayer_FrameLoadFailed;
        _spritePlayer.Dispose();
        _spritePlayer = null;
    }

    private void StartSpriteMotion(string resourceName)
    {
        StopSpriteMotion();
        if (FindResource(resourceName) is not Storyboard storyboard)
            return;

        _spriteMotionStoryboard = storyboard;
        storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }

    private void StopSpriteMotion()
    {
        _spriteMotionStoryboard?.Remove(this);
        _spriteMotionStoryboard = null;
        SpriteTranslate.X = 0;
        SpriteTranslate.Y = 0;
        SpriteRotate.Angle = 0;
        SpriteScale.ScaleX = _facingDirection;
        SpriteScale.ScaleY = 1;
    }

    private readonly Dictionary<CharacterMood, SpriteAnimationClip> _spriteMoodClips = new();
    private DispatcherTimer? _spriteExpressionTimer;

    private void RegisterDefaultSpriteExpressions()
    {
        foreach (var (mood, file) in new[]
        {
            (CharacterMood.Happy, "happy"),
            (CharacterMood.Surprised, "surprised"),
            (CharacterMood.Dizzy, "dizzy"),
            (CharacterMood.Confused, "sad"),
            (CharacterMood.Thinking, "determined"),
            (CharacterMood.Sad, "sad"),
            (CharacterMood.Angry, "angry"),
            (CharacterMood.Determined, "determined"),
            (CharacterMood.Wink, "wink")
        })
        {
            string path = $"Assets/Characters/LuKnight/Expressions/{file}.png";
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, path)))
                _spriteMoodClips[mood] = new SpriteAnimationClip(
                    $"expression-{file}", new[] { path }, 1, true);
        }
    }

    private void PlaySpriteWink()
    {
        if (CurrentState != CharacterState.Idle ||
            !_spriteMoodClips.TryGetValue(CharacterMood.Wink, out var clip))
            return;

        StopSpriteExpression();
        _spritePlayer?.Play(clip);
        if (_renderMode != CharacterRenderMode.Sprite)
            return;

        if (_spriteExpressionTimer is null)
        {
            _spriteExpressionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _spriteExpressionTimer.Tick += SpriteExpressionTimer_Tick;
        }
        _spriteExpressionTimer.Start();
    }

    private void StopSpriteExpression()
    {
        _spriteExpressionTimer?.Stop();
    }

    private void SpriteExpressionTimer_Tick(object? sender, EventArgs e)
    {
        StopSpriteExpression();
        PlaySpriteForCurrentState();
    }
}
