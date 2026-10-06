using Microsoft.Extensions.DependencyInjection;

namespace BingoApp;

public partial class App : Application
{
	public App()
	{
		// El idioma se resuelve UNA vez, antes de inflar cualquier pantalla: lo que la
		// persona eligio, o lo que dice el aparato. Todo lo demas lo lee de Texts.
		AppLanguage.Start();
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}