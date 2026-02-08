using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace SimpleNavigator.Localization;

public sealed class LocalizationResourceManager : INotifyPropertyChanged
{
    private readonly ResourceManager resourceManager = new("SimpleNavigator.Resources.Strings", typeof(LocalizationResourceManager).Assembly);

    public static LocalizationResourceManager Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.CurrentUICulture;

    public string this[string key] => resourceManager.GetString(key, CurrentCulture) ?? key;

    public void SetCulture(CultureInfo culture)
    {
        if (Equals(CurrentCulture, culture))
        {
            return;
        }

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CurrentCulture = culture;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
