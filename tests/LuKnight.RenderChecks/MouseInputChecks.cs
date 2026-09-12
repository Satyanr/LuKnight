using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LuKnight.Behaviors;
using LuKnight.Models;
using LuKnight.Physics;
using LuKnight.Services;
using LuKnight.Views;

internal static partial class Program
{
    // Explicit UI check: real WPF capture, routed input, no global input injection or AI calls.
    private static void CheckMouseInput()
    {
        var settings = new SettingsService();
        settings.Update(settings.Current with { Chat = new() { Provider = ChatProvider.Local } });
        var host = new LuKnight.MainWindow(new AppServices(settings, new FakeCredentials()));
        var view = Get<CharacterView>(host, "CharacterControl");
        var popup = Get<Popup>(host, "ChatPopup");
        host.Show();
        host.UpdateLayout();
        var behavior = Get<BehaviorController>(host, "_behaviorController");
        var physics = Get<CharacterPhysicsController>(host, "_physicsController");
        void Down()
        {
            // Capture synchronously emits a real move with the physical button released.
            // Suppress that move only while synthesizing Down; exercise cleanup separately below.
            var move = (MouseEventHandler)Delegate.CreateDelegate(typeof(MouseEventHandler), host,
                typeof(LuKnight.MainWindow).GetMethod("Character_PreviewMouseMove", Private)!);
            view.RemoveHandler(UIElement.PreviewMouseMoveEvent, move);
            try
            {
                view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            }
            finally { view.AddHandler(UIElement.PreviewMouseMoveEvent, move, true); }
            Require(ReferenceEquals(Mouse.Captured, view) && !host.IsMouseCaptured,
                "Mouse capture must belong to the character receiving the routed input.");
            Require(Get<bool>(host, "_leftMouseDown"), "Mouse down was lost.");
        }
        void Up() => view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        void Released()
        {
            Require(!view.IsMouseCaptured && !Get<bool>(host, "_leftMouseDown") && !Get<bool>(host, "_dragStarted"),
                "Mouse lifecycle left capture or drag flags behind.");
            Require((Get<BehaviorPauseReason>(behavior, "_pauseReasons") & BehaviorPauseReason.UserDrag) == 0,
                "UserDrag pause was not released.");
        }
        void BeginDrag()
        {
            Down();
            Require(physics.BeginGrab(Get<Point>(host, "_mouseDownScreenPosition"), Get<double>(host, "_mouseDownTime")),
                "Prepared grab did not start.");
            typeof(LuKnight.MainWindow).GetField("_dragStarted", Private)!.SetValue(host, true);
        }
        try
        {
            Down(); Up(); Released();
            Require(popup.IsOpen, "Quick click did not open chat.");
            Down(); Up(); Released();
            Require(!popup.IsOpen, "Second click did not close chat.");

            Down();
            view.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            Released();
            Require(!popup.IsOpen && !Get<bool>(physics, "_trackingPointer"), "Missing MouseUp became a click or retained pointer tracking.");

            BeginDrag(); Up(); Released();
            Require(physics.IsFalling && !physics.IsGrabbed && !popup.IsOpen, "Drag release did not hand ownership to physics.");
            host.ResetCharacterPosition();

            BeginDrag();
            view.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            Released();
            Require(physics.IsFalling && !physics.IsGrabbed, "Missing MouseUp left an active grab.");
            host.ResetCharacterPosition();

            BeginDrag(); view.ReleaseMouseCapture(); Released();
            Require(physics.IsFalling && !physics.IsGrabbed, "Capture loss did not end the grab.");
            host.ResetCharacterPosition();

            BeginDrag(); host.ResetCharacterPosition(); Released();
            Require(!physics.IsActive, "Reset left physics active.");
            Down(); host.HideToTray(); Released();
            Require(!host.IsVisible && !popup.IsOpen, "Hide left character or popup visible.");
        }
        finally { host.Close(); }
    }
}
