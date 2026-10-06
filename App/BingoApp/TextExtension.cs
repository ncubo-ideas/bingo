using System.ComponentModel;
using Microsoft.Maui.Controls.Xaml;

namespace BingoApp;

/// <summary>
/// Una frase del catalogo desde el XAML: <c>Text="{local:Text portada.organizador}"</c>.
///
/// No entrega el texto, entrega un BINDING al catalogo vivo: asi, cuando la persona
/// cambia el idioma con la pantalla abierta, cada rotulo se repinta solo. La primera
/// version devolvia el string resuelto al inflar la pagina, y en el emulador se vio la
/// pantalla mezclada que esta guia existe para evitar: la portada seguia en ingles con
/// el selector ya en espanol, y solo cambiaba lo que el code-behind volvia a poner.
/// </summary>
[ContentProperty(nameof(Key))]
public sealed class TextExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; }

    // El punto de la clave se cambia por una barra: el parser de rutas de binding parte
    // por puntos antes de mirar los corchetes, y "[portada.titulo]" se le rompia en dos.
    public BindingBase ProvideValue(IServiceProvider serviceProvider) =>
        new Binding("[" + (Key ?? string.Empty).Replace('.', '/') + "]", BindingMode.OneWay, source: LiveTexts.Instance);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}

/// <summary>
/// El catalogo como fuente de binding: un indexador por clave que avisa "cambio todo"
/// cuando cambia el idioma. Es el unico objeto al que se enlazan los XAML, y por eso
/// basta con un solo PropertyChanged para repintar todas las pantallas nativas abiertas.
/// </summary>
public sealed class LiveTexts : INotifyPropertyChanged
{
    public static readonly LiveTexts Instance = new();

    // El nombre vacio es "cambio todo": es lo que un binding a un indexador si escucha.
    // "Item[]" no le llega, y se vio en el emulador: el selector cambiaba y los rotulos no.
    private LiveTexts() => AppLanguage.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public event PropertyChangedEventHandler PropertyChanged;

    public string this[string clave] => Texts.T((clave ?? string.Empty).Replace('/', '.'));
}
