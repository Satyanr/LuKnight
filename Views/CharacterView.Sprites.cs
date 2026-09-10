using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LuKnight.Visuals;

namespace LuKnight.Views;

public partial class CharacterView
{
    private CharacterRenderMode _renderMode = CharacterRenderMode.Vector;
    private CharacterRenderMode _preferredRenderMode = CharacterRenderMode.Vector;
    private Storyboard? _spriteMotionStoryboard;
    private readonly Dictionary<CharacterState, SpriteAnimationClip> _spriteStateClips = new();
    private SpriteAnimationPlayer? _spritePlayer;

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


        RegisterSpriteFolder(
            CharacterState.Grabbed,
            "grabbed",
            "Assets/Characters/LuKnight/Grabbed",
            6);


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
