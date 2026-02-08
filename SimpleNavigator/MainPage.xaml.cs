
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using SimpleNavigator.Localization;

namespace SimpleNavigator
{
    public class LocationData
    {
        public string? Name { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public class SavedLocation : INotifyPropertyChanged
    {
        private string? _name;
        private double _latitude;
        private double _longitude;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public double Latitude
        {
            get => _latitude;
            set
            {
                _latitude = value;
                OnPropertyChanged(nameof(Latitude));
            }
        }

        public double Longitude
        {
            get => _longitude;
            set
            {
                _longitude = value;
                OnPropertyChanged(nameof(Longitude));
            }
        }

        public string DisplayText => $"{Name} ({Latitude:F6}, {Longitude:F6})";

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MainPage : ContentPage
    {
        private sealed record LanguageOption(string Name, string CultureCode);

        private Location targetLocation = null;
        private bool isCompassActive = false;
        private IDispatcherTimer gpsTimer;
        private bool isUpdatingLocation = false;
        private CancellationTokenSource gpsCancellationTokenSource;
        private Location lastKnownLocation;
        private ObservableCollection<SavedLocation> savedLocations;
        private SavedLocation selectedLocation;
        private bool isUpdatingLanguage;
        private List<LanguageOption> languageOptions = [];
        private Picker LanguagePickerControl => this.FindByName<Picker>("LanguagePicker");

        public MainPage()
        {
            InitializeComponent();
            Compass.ReadingChanged += OnCompassChanged;

            savedLocations = new ObservableCollection<SavedLocation>();
            SavedLocationsPicker.ItemsSource = savedLocations;

            languageOptions = BuildLanguageOptions();
            RefreshLanguagePicker();

            LoadSavedLocations();
            UpdateLocationListVisibility();
            ApplyCulture();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            StartGpsUpdates();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopGpsUpdates();
            if (isCompassActive)
            {
                Compass.Stop();
                isCompassActive = false;
            }

            SaveLocationsToStorage();
        }

        private static string L(string key) => LocalizationResourceManager.Instance[key];

        private List<LanguageOption> BuildLanguageOptions() =>
        [
            new LanguageOption(L("LanguageEnglish"), "en-US"),
            new LanguageOption(L("LanguageSlovak"), "sk-SK")
        ];

        private void RefreshLanguagePicker()
        {
            isUpdatingLanguage = true;
            try
            {
                var current = (LanguagePickerControl.SelectedItem as LanguageOption)?.CultureCode
                              ?? LocalizationResourceManager.Instance.CurrentCulture.Name;

                languageOptions = BuildLanguageOptions();
                LanguagePickerControl.ItemsSource = languageOptions;
                LanguagePickerControl.SelectedItem = languageOptions.FirstOrDefault(l => l.CultureCode == current)
                                                     ?? languageOptions[0];
            }
            finally
            {
                isUpdatingLanguage = false;
            }
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (isUpdatingLanguage)
            {
                return;
            }

            if (LanguagePickerControl.SelectedItem is LanguageOption option)
            {
                LocalizationResourceManager.Instance.SetCulture(CultureInfo.GetCultureInfo(option.CultureCode));
                Preferences.Default.Set("AppCulture", option.CultureCode);
                RefreshLanguagePicker();
                ApplyCulture();
            }
        }

        private void ApplyCulture()
        {
            UpdateCompassStatusLabel();
            UpdateTargetLabel();
            UpdateDistanceDisplay();
            UpdateGpsLabel();
        }

        private void UpdateCompassStatusLabel()
        {
            if (isCompassActive)
            {
                CompassStatusLabel.Text = L("CompassOn");
                CompassStatusLabel.TextColor = Colors.Green;
            }
            else
            {
                CompassStatusLabel.Text = L("CompassOff");
                CompassStatusLabel.TextColor = Colors.Red;
            }
        }

        private void UpdateTargetLabel()
        {
            if (targetLocation == null)
            {
                TargetLocationLabel.Text = L("TargetGpsNotAvailable");
                return;
            }

            TargetLocationLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                L("TargetLocationFormat"),
                targetLocation.Latitude,
                targetLocation.Longitude);
        }

        private void UpdateGpsLabel()
        {
            if (lastKnownLocation != null)
            {
                GpsLabel.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    L("GpsCurrentFormat"),
                    lastKnownLocation.Latitude,
                    lastKnownLocation.Longitude);
            }
            else
            {
                GpsLabel.Text = L("GpsRetrieving");
            }
        }

        private async Task UpdateGpsLocation()
        {
            if (isUpdatingLocation) return;

            isUpdatingLocation = true;
            try
            {
                gpsCancellationTokenSource = new CancellationTokenSource();
                var request = new GeolocationRequest(GeolocationAccuracy.Medium);

                var location = await Geolocation.Default.GetLocationAsync(request, gpsCancellationTokenSource.Token);

                if (location != null)
                {
                    double distance = 0;
                    if (lastKnownLocation != null)
                    {
                        distance = location.CalculateDistance(lastKnownLocation, DistanceUnits.Kilometers);
                    }

                    lastKnownLocation = location;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        GpsLabel.Text = string.Format(
                            CultureInfo.CurrentCulture,
                            L("GpsCurrentFormat"),
                            location.Latitude,
                            location.Longitude);
                        UpdateDistanceDisplay();
                    });

                    if (distance > 0.005)
                    {
                        gpsTimer.Interval = TimeSpan.FromSeconds(1);
                    }
                    else
                    {
                        gpsTimer.Interval = TimeSpan.FromSeconds(5);
                    }
                }
                else
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        GpsLabel.Text = L("GpsUnavailableWait");
                    });
                }
            }
            catch (OperationCanceledException)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    GpsLabel.Text = L("GpsUpdateCanceled");
                });
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    GpsLabel.Text = string.Format(CultureInfo.CurrentCulture, L("GpsUnavailableFormat"), ex.Message);
                });
            }
            finally
            {
                isUpdatingLocation = false;
                gpsCancellationTokenSource?.Dispose();
                gpsCancellationTokenSource = null;
            }
        }

        private void StopGpsUpdates()
        {
            if (gpsTimer != null)
            {
                gpsCancellationTokenSource?.Cancel();
                gpsTimer.Stop();
            }
        }

        private void StartGpsUpdates()
        {
            if (gpsTimer == null)
            {
                gpsTimer = Dispatcher.CreateTimer();
                gpsTimer.Interval = TimeSpan.FromSeconds(5);
                gpsTimer.Tick += async (s, e) => await UpdateGpsLocation();
            }

            if (!gpsTimer.IsRunning)
            {
                gpsTimer.Start();
            }
        }

        private async void OnSaveLocationClicked(object sender, EventArgs e)
        {
            try
            {
                var request = new GeolocationRequest(GeolocationAccuracy.High);
                var location = await Geolocation.Default.GetLocationAsync(request);

                if (location != null)
                {
                    string name = await DisplayPromptAsync(
                        L("SaveLocationTitle"),
                        L("EnterNamePrompt"),
                        L("SaveButton"),
                        L("CancelButton"),
                        L("MyLocationDefault"));

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        var savedLocation = new SavedLocation
                        {
                            Name = name.Trim(),
                            Latitude = location.Latitude,
                            Longitude = location.Longitude
                        };

                        savedLocations.Add(savedLocation);
                        SaveLocationsToStorage();
                        UpdateLocationListVisibility();

                        await DisplayAlert(
                            L("SuccessTitle"),
                            string.Format(CultureInfo.CurrentCulture, L("LocationSavedFormat"), name),
                            L("OkButton"));
                    }
                }
                else
                {
                    await DisplayAlert(L("ErrorTitle"), L("UnableGetGps"), L("OkButton"));
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert(
                    L("ErrorTitle"),
                    string.Format(CultureInfo.CurrentCulture, L("SaveLocationErrorFormat"), ex.Message),
                    L("OkButton"));
            }
        }

        private async void OnAddManualLocationClicked(object sender, EventArgs e)
        {
            try
            {
                string name = await DisplayPromptAsync(
                    L("AddLocationTitle"),
                    L("EnterNamePrompt"),
                    L("NextButton"),
                    L("CancelButton"));

                if (string.IsNullOrWhiteSpace(name)) return;

                string latString = await DisplayPromptAsync(
                    L("AddLocationTitle"),
                    L("EnterLatitudePrompt"),
                    L("NextButton"),
                    L("CancelButton"),
                    "",
                    -1,
                    Keyboard.Default);

                if (string.IsNullOrWhiteSpace(latString)) return;

                string lonString = await DisplayPromptAsync(
                    L("AddLocationTitle"),
                    L("EnterLongitudePrompt"),
                    L("SaveButton"),
                    L("CancelButton"),
                    "",
                    -1,
                    Keyboard.Default);

                if (string.IsNullOrWhiteSpace(lonString)) return;

                if (TryParseCoordinate(latString, out double lat) && TryParseCoordinate(lonString, out double lon))
                {
                    if (lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180)
                    {
                        var savedLocation = new SavedLocation
                        {
                            Name = name.Trim(),
                            Latitude = lat,
                            Longitude = lon
                        };

                        savedLocations.Add(savedLocation);
                        SaveLocationsToStorage();
                        UpdateLocationListVisibility();

                        await DisplayAlert(
                            L("SuccessTitle"),
                            string.Format(CultureInfo.CurrentCulture, L("LocationAddedFormat"), name),
                            L("OkButton"));
                    }
                    else
                    {
                        await DisplayAlert(L("ErrorTitle"), L("InvalidCoordinatesRange"), L("OkButton"));
                    }
                }
                else
                {
                    await DisplayAlert(L("ErrorTitle"), L("InvalidNumberFormat"), L("OkButton"));
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert(
                    L("ErrorTitle"),
                    string.Format(CultureInfo.CurrentCulture, L("AddLocationErrorFormat"), ex.Message),
                    L("OkButton"));
            }
        }

        private void OnLocationSelected(object sender, EventArgs e)
        {
            var picker = sender as Picker;
            if (picker.SelectedItem is SavedLocation location)
            {
                selectedLocation = location;
                targetLocation = new Location(location.Latitude, location.Longitude);
                UpdateTargetLabel();
                UpdateDistanceDisplay();
            }
        }

        private async void OnEditLocationClicked(object sender, EventArgs e)
        {
            if (selectedLocation == null)
            {
                await DisplayAlert(L("ErrorTitle"), L("SelectLocationFirstError"), L("OkButton"));
                return;
            }

            var renameAction = L("ActionRename");
            var changeCoordinatesAction = L("ActionChangeCoordinates");
            var deleteAction = L("ActionDelete");

            string action = await DisplayActionSheet(
                string.Format(CultureInfo.CurrentCulture, L("EditLocationActionSheetTitle"), selectedLocation.Name),
                L("CancelButton"),
                deleteAction,
                renameAction,
                changeCoordinatesAction);

            switch (action)
            {
                case var _ when action == renameAction:
                    await RenameLocation(selectedLocation);
                    break;
                case var _ when action == changeCoordinatesAction:
                    await ChangeCoordinates(selectedLocation);
                    break;
                case var _ when action == deleteAction:
                    await DeleteLocation(selectedLocation);
                    break;
            }
        }

        private async Task RenameLocation(SavedLocation location)
        {
            string newName = await DisplayPromptAsync(
                L("RenameLocationTitle"),
                L("EnterNewNamePrompt"),
                L("SaveButton"),
                L("CancelButton"),
                location.Name);

            if (!string.IsNullOrWhiteSpace(newName) && newName.Trim() != location.Name)
            {
                location.Name = newName.Trim();
                SaveLocationsToStorage();
                await DisplayAlert(L("SuccessTitle"), L("LocationRenamedSuccess"), L("OkButton"));
            }
        }

        private async Task ChangeCoordinates(SavedLocation location)
        {
            string latString = await DisplayPromptAsync(
                L("ChangeCoordinatesTitle"),
                L("EnterNewLatitudePrompt"),
                L("NextButton"),
                L("CancelButton"),
                location.Latitude.ToString("F6"),
                -1,
                Keyboard.Default);

            if (string.IsNullOrWhiteSpace(latString)) return;

            string lonString = await DisplayPromptAsync(
                L("ChangeCoordinatesTitle"),
                L("EnterNewLongitudePrompt"),
                L("SaveButton"),
                L("CancelButton"),
                location.Longitude.ToString("F6"),
                -1,
                Keyboard.Default);

            if (string.IsNullOrWhiteSpace(lonString)) return;

            if (TryParseCoordinate(latString, out double lat) && TryParseCoordinate(lonString, out double lon))
            {
                if (lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180)
                {
                    location.Latitude = lat;
                    location.Longitude = lon;

                    if (selectedLocation == location)
                    {
                        targetLocation = new Location(lat, lon);
                        UpdateTargetLabel();
                        UpdateDistanceDisplay();
                    }

                    await DisplayAlert(L("SuccessTitle"), L("CoordinatesUpdatedSuccess"), L("OkButton"));
                }
                else
                {
                    await DisplayAlert(L("ErrorTitle"), L("InvalidCoordinates"), L("OkButton"));
                }
            }
            else
            {
                await DisplayAlert(L("ErrorTitle"), L("InvalidNumberFormat"), L("OkButton"));
            }
        }

        private async Task DeleteLocation(SavedLocation location)
        {
            bool confirm = await DisplayAlert(
                L("DeleteLocationTitle"),
                string.Format(CultureInfo.CurrentCulture, L("DeleteLocationConfirmFormat"), location.Name),
                L("ActionDelete"),
                L("CancelButton"));

            if (confirm)
            {
                savedLocations.Remove(location);

                if (selectedLocation == location)
                {
                    selectedLocation = null;
                    targetLocation = null;
                    TargetLocationLabel.Text = L("TargetGpsNotAvailable");
                    DistanceLabel.Text = L("DistanceNotAvailable");
                    DistanceLabel.TextColor = Colors.White;
                    SavedLocationsPicker.SelectedItem = null;
                }
                UpdateLocationListVisibility();
                SaveLocationsToStorage();
                await DisplayAlert(L("SuccessTitle"), L("LocationDeletedSuccess"), L("OkButton"));
            }
        }

        private void UpdateLocationListVisibility()
        {
            bool hasLocations = savedLocations.Count > 0;
            SavedLocationsPicker.IsVisible = hasLocations;
            EditLocationButton.IsVisible = hasLocations;

            if (!hasLocations)
            {
                SavedLocationsPicker.SelectedItem = null;
                selectedLocation = null;
            }
        }

        private void OnShowDirectionClicked(object sender, EventArgs e)
        {
            if (targetLocation == null)
            {
                DisplayAlert(L("ErrorTitle"), L("SelectTargetFirstError"), L("OkButton"));
                return;
            }

            isCompassActive = !isCompassActive;

            if (isCompassActive)
            {
                Compass.Start(SensorSpeed.UI);
            }
            else
            {
                Compass.Stop();
            }

            UpdateCompassStatusLabel();
        }

        private void OnCompassChanged(object sender, CompassChangedEventArgs e)
        {
            if (targetLocation == null || lastKnownLocation == null) return;

            double bearingToTarget = GetBearing(lastKnownLocation, targetLocation);
            double heading = e.Reading.HeadingMagneticNorth;
            double rotation = (bearingToTarget - heading + 360) % 360;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ArrowContainer.Rotation = rotation;
                CompassRose.Rotation = -heading;
                HeadingLabel.Text = $"{(int)heading}°";
                UpdateDistanceDisplay();
            });
        }

        private double GetBearing(Location currentLocation, Location targetLocation)
        {
            double lat1 = DegreesToRadians(currentLocation.Latitude);
            double lon1 = DegreesToRadians(currentLocation.Longitude);
            double lat2 = DegreesToRadians(targetLocation.Latitude);
            double lon2 = DegreesToRadians(targetLocation.Longitude);

            double deltaLon = lon2 - lon1;
            double x = Math.Sin(deltaLon) * Math.Cos(lat2);
            double y = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(deltaLon);
            double bearing = Math.Atan2(x, y);

            return (RadiansToDegrees(bearing) + 360) % 360;
        }

        private double DegreesToRadians(double degrees) => degrees * (Math.PI / 180);
        private double RadiansToDegrees(double radians) => radians * (180 / Math.PI);

        private bool TryParseCoordinate(string input, out double result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            if (double.TryParse(input, out result))
                return true;

            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return true;

            string normalizedInput = input.Replace(',', '.');
            if (double.TryParse(normalizedInput, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return true;

            return false;
        }

        private void SaveLocationsToStorage()
        {
            try
            {
                var locationList = savedLocations.Select(loc => new LocationData
                {
                    Name = loc.Name,
                    Latitude = loc.Latitude,
                    Longitude = loc.Longitude
                }).ToList();

                string json = JsonSerializer.Serialize(locationList);
                Preferences.Default.Set("SavedLocations", json);

                string verification = Preferences.Default.Get("SavedLocations", "");
                bool success = verification == json;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving locations: {ex.Message}");
            }
        }

        private void LoadSavedLocations()
        {
            try
            {
                string json = Preferences.Default.Get("SavedLocations", "");

                if (!string.IsNullOrEmpty(json))
                {
                    var locationList = JsonSerializer.Deserialize<List<LocationData>>(json);
                    if (locationList != null)
                    {
                        savedLocations.Clear();
                        foreach (var item in locationList)
                        {
                            var savedLocation = new SavedLocation
                            {
                                Name = item.Name,
                                Latitude = item.Latitude,
                                Longitude = item.Longitude
                            };
                            savedLocations.Add(savedLocation);
                        }
                    }
                }
            }
            catch (Exception)
            {
                Preferences.Default.Remove("SavedLocations");
            }
        }

        private void UpdateDistanceDisplay()
        {
            if (targetLocation == null || lastKnownLocation == null)
            {
                DistanceLabel.Text = L("DistanceNotAvailable");
                DistanceLabel.TextColor = Colors.White;
                return;
            }

            double distanceKm = lastKnownLocation.CalculateDistance(targetLocation, DistanceUnits.Kilometers);

            if (distanceKm < 1.0)
            {
                double distanceM = distanceKm * 1000;
                DistanceLabel.Text = string.Format(CultureInfo.CurrentCulture, L("DistanceMetersFormat"), distanceM);

                if (distanceM < 10)
                    DistanceLabel.TextColor = Colors.LimeGreen;
                else if (distanceM < 50)
                    DistanceLabel.TextColor = Colors.Yellow;
                else
                    DistanceLabel.TextColor = Colors.Orange;
            }
            else
            {
                DistanceLabel.Text = string.Format(CultureInfo.CurrentCulture, L("DistanceKilometersFormat"), distanceKm);
                DistanceLabel.TextColor = Colors.Red;
            }
        }
    }
}