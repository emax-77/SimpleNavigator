using System.Globalization;
using SimpleNavigator.Localization;

namespace SimpleNavigator
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            ApplySavedCulture();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        private static void ApplySavedCulture()
        {
            var cultureCode = Preferences.Default.Get("AppCulture", string.Empty);
            if (!string.IsNullOrWhiteSpace(cultureCode))
            {
                LocalizationResourceManager.Instance.SetCulture(CultureInfo.GetCultureInfo(cultureCode));
            }
        }
    }
}
