using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;

namespace PcOptimizer.Core.Tests.Fakes;

internal sealed class FakeTweak(string id, params SettingChange[] changes) : ITweak
{
    public string Id => id;
    public string Title => $"Test {id}";
    public TweakCategory Category => TweakCategory.System;
    public string WhatChanges => "";
    public string Why => "";
    public string ExpectedBenefit => "";
    public Risk Risk => Risk.Low;
    public TierMask AppliesTo => TierMask.All;
    public bool RequiresReboot => false;
    public bool Applicable { get; init; } = true;

    public bool IsApplicable(TweakContext context, out string? reason)
    {
        reason = Applicable ? null : "Non supportato (test).";
        return Applicable;
    }

    public IReadOnlyList<SettingChange> PlanChanges(TweakContext context) => changes;
}

