namespace SyxxUI.Avalonia.ViewModels;

public static class ShellLaunchPlan
{
    public const string InitialDestination = "What's New";

    public static IReadOnlyList<string> NavigationItems { get; } = new[]
    {
        InitialDestination,
        "Dashboard",
        "Prophet",
        "Mirror",
        "Portal",
        "Glutton",
        "Cage",
        "Witness",
        "Conclusions",
        "Settings",
        "License",
    };
}
