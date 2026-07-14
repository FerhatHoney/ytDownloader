namespace ytDownloader; // Kendi proje adın

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    // Uygulama açılırken pencere boyutunu (Genişlik x Yükseklik) ayarlıyoruz
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        window.Width = 1000;
        window.Height = 750;
        window.Title = "Youtube İndirici";
        return window;
    }
}