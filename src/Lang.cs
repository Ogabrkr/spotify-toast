using System.Runtime.InteropServices;

namespace SpotifyToast;

/// <summary>Textos do app em português, inglês e espanhol.</summary>
internal sealed class Lang
{
    public required string Style, Glass, Solid;
    public required string Theme, Dark, Light, FollowWindows;
    public required string Position, TopLeft, TopRight;
    public required string Size, Small, Medium, Large;
    public required string Duration, SecondsFormat;
    public required string Language, Automatic;
    public required string ShowOverFullscreen, StartWithWindows, ShowNotification, Quit;
    public required string NowPlaying, NoTrackYet, PressPlay, MediaError;

    public static readonly Lang Pt = new()
    {
        Style = "Estilo", Glass = "Liquid Glass", Solid = "Sólido",
        Theme = "Tema", Dark = "Escuro", Light = "Claro", FollowWindows = "Seguir o Windows",
        Position = "Posição", TopLeft = "Canto superior esquerdo", TopRight = "Canto superior direito",
        Size = "Tamanho", Small = "Pequeno", Medium = "Médio", Large = "Grande",
        Duration = "Duração", SecondsFormat = "{0} segundos",
        Language = "Idioma", Automatic = "Automático (Windows)",
        ShowOverFullscreen = "Aparecer sobre tela cheia", StartWithWindows = "Iniciar com o Windows",
        ShowNotification = "Mostrar notificação", Quit = "Fechar o app",
        NowPlaying = "TOCANDO AGORA", NoTrackYet = "Nenhuma música ainda", PressPlay = "Dê play no Spotify",
        MediaError = "Não consegui acessar o controle de mídia do Windows: ",
    };

    public static readonly Lang En = new()
    {
        Style = "Style", Glass = "Liquid Glass", Solid = "Solid",
        Theme = "Theme", Dark = "Dark", Light = "Light", FollowWindows = "Follow Windows",
        Position = "Position", TopLeft = "Top left corner", TopRight = "Top right corner",
        Size = "Size", Small = "Small", Medium = "Medium", Large = "Large",
        Duration = "Duration", SecondsFormat = "{0} seconds",
        Language = "Language", Automatic = "Automatic (Windows)",
        ShowOverFullscreen = "Show over fullscreen", StartWithWindows = "Start with Windows",
        ShowNotification = "Show notification", Quit = "Quit",
        NowPlaying = "NOW PLAYING", NoTrackYet = "No song yet", PressPlay = "Press play on Spotify",
        MediaError = "Couldn't access Windows media controls: ",
    };

    public static readonly Lang Es = new()
    {
        Style = "Estilo", Glass = "Liquid Glass", Solid = "Sólido",
        Theme = "Tema", Dark = "Oscuro", Light = "Claro", FollowWindows = "Seguir a Windows",
        Position = "Posición", TopLeft = "Esquina superior izquierda", TopRight = "Esquina superior derecha",
        Size = "Tamaño", Small = "Pequeño", Medium = "Mediano", Large = "Grande",
        Duration = "Duración", SecondsFormat = "{0} segundos",
        Language = "Idioma", Automatic = "Automático (Windows)",
        ShowOverFullscreen = "Mostrar sobre pantalla completa", StartWithWindows = "Iniciar con Windows",
        ShowNotification = "Mostrar notificación", Quit = "Cerrar la app",
        NowPlaying = "REPRODUCIENDO", NoTrackYet = "Aún no hay canción", PressPlay = "Dale play en Spotify",
        MediaError = "No pude acceder a los controles multimedia de Windows: ",
    };

    /// <summary>"pt" | "en" | "es" | "system" (idioma da interface do Windows; inglês se não for pt/es).</summary>
    public static Lang For(string code) => (code == "system" ? SystemCode() : code) switch
    {
        "pt" => Pt,
        "es" => Es,
        _ => En,
    };

    // o app usa InvariantGlobalization, então o idioma vem direto do Windows
    [DllImport("kernel32.dll")] static extern ushort GetUserDefaultUILanguage();

    static string SystemCode() => (GetUserDefaultUILanguage() & 0x3FF) switch
    {
        0x16 => "pt",
        0x0A => "es",
        _ => "en",
    };
}
