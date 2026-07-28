using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    // --- Motion toolkit -----------------------------------------------------------------
    // Attribution: the animation/drawing IDEAS in this file and in this branch's other
    // changes (EndCard's shadow+highlight, DrawBadge's pill, DrawBackButton's chevron, the
    // Home icon-tile grid) were inspired by reviewing Aetherphone (github.com/Aetherment/
    // Aetherphone, AGPL-3.0), a much more visually sophisticated Dalamud plugin, for portable
    // ImGui techniques. No source from that project was copied - everything here is an
    // original implementation, written from scratch against FroggePlugin's own existing
    // conventions, of generally-known techniques (a critically-damped spring is a standard,
    // widely-published game-programming formula predating and unrelated to Aetherphone
    // specifically; drawing a pill/chevron/tinted-fade via ImGui's own public draw-list API
    // is a common technique, not anyone's proprietary code). Called out explicitly here since
    // that's the honest thing to do even where not legally required, and because Aetherphone's
    // AGPL-3.0 license would require this branch to also be AGPL-3.0 (FroggePlugin is MIT) if
    // any of its copyrighted code/expression had actually been reused rather than just its
    // ideas. Kept deliberately minimal - just what this polish pass actually consumes (a
    // back-button press-squeeze, Home tile hover-scale, and a breathing loading indicator),
    // not a general-purpose animation library.

    // Critically-damped spring (the Unity SmoothDamp / Game Programming Gems 4 formula), not
    // naive Euler integration of F=-kx-cv - unconditionally stable for any deltaTime, so a
    // frame hitch can't make it explode or oscillate.
    private struct Spring
    {
        private float velocity;
        public float Value;

        public float Update(float deltaTime, float target, float smoothTime)
        {
            smoothTime = MathF.Max(0.0001f, smoothTime); // guards the only division below
            var omega = 2f / smoothTime;
            var x = omega * deltaTime;
            var exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            var change = Value - target;
            var temp = (velocity + omega * change) * deltaTime;
            velocity = (velocity - omega * temp) * exp;
            var output = target + (change + temp) * exp;

            // Overshoot clamp - without this, output can cross target and keep going.
            if ((target - Value > 0f) == (output > target))
            {
                output = target;
                velocity = deltaTime > 0f ? (output - target) / deltaTime : 0f;
            }

            Value = output;
            return Value;
        }
    }

    // Dictionary<string, Spring> indexing returns a COPY (Spring is a struct) - mutating that
    // copy in place would do nothing, so every read/mutate/write-back goes through StepSpring
    // rather than callers touching `springs` directly. Bounded to the 6 fixed Home-tile ids for
    // the process lifetime (never keyed on API/user data) - tighter than, and consistent with,
    // this file's own remoteImageCache's already-accepted "no LRU bound, v1" precedent.
    private static readonly Dictionary<string, Spring> springs = new();

    private static float StepSpring(string id, float target, float smoothTime)
    {
        var spring = springs.TryGetValue(id, out var existing) ? existing : new Spring { Value = target };
        var value = spring.Update(ImGui.GetIO().DeltaTime, target, smoothTime);
        springs[id] = spring;
        return value;
    }

    // Draw() dispatches exactly one Page per frame, so only one Back button ever exists at a
    // time - a plain static field needs no dictionary/id and has zero growth.
    private static Spring backButtonSpring;

    private static class Pulse
    {
        // 0..1 sine wave off ImGui.GetTime(), with an optional phase offset so multiple dots/
        // indicators can ripple instead of breathing in perfect unison.
        public static float Wave(double periodSeconds, double phaseOffset = 0.0) =>
            (float)((Math.Sin((ImGui.GetTime() / periodSeconds + phaseOffset) * Math.PI * 2) + 1.0) / 2.0);
    }
}
