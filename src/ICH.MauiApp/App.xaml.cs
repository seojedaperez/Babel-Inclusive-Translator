namespace ICH.MauiApp;

public partial class App : Application
{
    private readonly MainPage _mainPage;

    public App(MainPage mainPage)
    {
        InitializeComponent();
        _mainPage = mainPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_mainPage)
        {
            Title = "ICH - Inclusive Communication Hub",
            Width = 1100,
            Height = 800
        };
    }
}
