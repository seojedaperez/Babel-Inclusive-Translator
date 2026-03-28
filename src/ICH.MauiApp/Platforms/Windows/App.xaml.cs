using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ICH.MauiApp.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		try {
			this.InitializeComponent();
		} catch (Exception ex) {
			System.IO.File.WriteAllText("maui-crash.log", ex.ToString());
			throw;
		}
	}

	protected override Microsoft.Maui.Hosting.MauiApp CreateMauiApp() {
		try {
			return MauiProgram.CreateMauiApp();
		} catch (Exception ex) {
			System.IO.File.WriteAllText("maui-crash.log", "CreateMauiApp crash: " + ex.ToString());
			throw;
		}
	}
}

