﻿namespace Shallow

open System
open System.Drawing

open MonoTouch.UIKit

/// Sets up a Tinder-style swipe gesture for a targetView within a referenceView.
/// Ported from http://stackoverflow.com/questions/21325057/implement-uikitdynamics-for-dragging-view-off-screen
type SwipeGestureHandler(referenceView: UIView, targetView: UIView) =
    let mutable attachment = None
    let mutable startCenter = PointF.Empty
    let mutable lastTime = DateTime.MinValue 
    let mutable lastAngle = 0.0f
    let mutable angularVelocity = 0.0f

    let targetViewRemoved = Event<_>()
    let animator = UIDynamicAnimator(referenceView)

    let angleOfView (view: #UIView) =
        // http://stackoverflow.com/a/2051861/1271826 and
        // https://github.com/mono/maccore/blob/master/src/CoreGraphics/CGAffineTransform.cs#L39
        atan2 view.Transform.yx view.Transform.xx

    let handlePan (gesture: UIPanGestureRecognizer) =
        match gesture.State, attachment with
        | UIGestureRecognizerState.Began, _ ->
            animator.RemoveAllBehaviors()

            let anchor = gesture.LocationInView(referenceView)
            let offset =
                let size = targetView.Bounds.Size
                // calculate the center offset and anchor point
                let pointWithinAnimatedView = gesture.LocationInView(targetView)
                UIOffset(pointWithinAnimatedView.X - size.Width / 2.0f, pointWithinAnimatedView.Y - size.Height / 2.0f)

            startCenter <- targetView.Center
            lastTime <- DateTime.Now
            lastAngle <- angleOfView targetView
            attachment <-
                let attach = UIAttachmentBehavior(targetView, offset, anchor)
                attach.Action <- fun () ->
                    let time = DateTime.Now
                    let angle = angleOfView targetView
                    if time > lastTime then
                        let seconds = float32 (time - lastTime).TotalSeconds
                        angularVelocity <- (angle - lastAngle) / seconds
                        lastTime <- time
                        lastAngle <- angle
                animator.Add(attach)
                Some attach

        | UIGestureRecognizerState.Changed, Some attachment ->
            // During pan, update the attachment behavior's anchor point for drag-n-rotate effect.
            attachment.AnchorPoint <- gesture.LocationInView(referenceView)

        | UIGestureRecognizerState.Ended, _ ->
            animator.RemoveAllBehaviors()
            let velocity = gesture.VelocityInView(referenceView)
            let magnitude = sqrt (velocity.Y ** 2.0f + velocity.X ** 2.0f)
            if magnitude < 800.0f then
                // If we aren't flinging fast, snap back.
                animator.AddBehavior(UISnapBehavior(targetView, startCenter))
            else
                // Otherwise, continue the fling until the view leaves the referenceView.
                let dynamic = UIDynamicItemBehavior(targetView, AngularResistance = 2.0f)
                dynamic.AddLinearVelocityForItem(velocity, targetView)
                dynamic.AddAngularVelocityForItem(angularVelocity, targetView)

                // Remove the view when it leaves the referenceView
                dynamic.Action <- fun () ->
                    if not (referenceView.Bounds.IntersectsWith(targetView.Frame)) then
                        animator.RemoveAllBehaviors()
                        targetView.RemoveFromSuperview()
                        targetViewRemoved.Trigger(targetView)
                
                animator.AddBehavior(dynamic)
                targetView.UserInteractionEnabled <- false
        | _ -> ()

    do targetView.AddGestureRecognizer(UIPanGestureRecognizer(handlePan))

    member this.TargetViewRemoved = targetViewRemoved.Publish